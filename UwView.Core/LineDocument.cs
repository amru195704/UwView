using System.Text;

namespace UwView.Core;

/// <summary>
/// バイト位置/行番号 → テキスト をオンデマンドで返すドキュメント層（§4.4）。
/// ページモード（GetPage: バイトオフセット基準）と行モード（GetLine: 行番号基準）の両 API を持つ。
/// エンコーディング差し替え時は索引再構築不要（オフセットはバイト位置＝エンコーディング非依存）。
/// </summary>
public sealed class LineDocument : IAsyncDisposable
{
    private const int MaxLineScanBytes = 64 * 1024;   // 1 行として走査する上限バイト
    private const int MaxLineDisplayChars = 8192;      // 表示用にクランプする文字数
    private const string Ellipsis = "…（省略）";

    private readonly IByteSource _src;
    private readonly int _blockLines;
    private readonly LruCache<long, string> _cache = new(4096);

    private Encoding _encoding;

    public int BomLength { get; }
    public NewlineStyle Newline { get; }
    public SparseLineIndex? Index { get; private set; }
    public long Length => _src.Length;
    public int BlockLines => _blockLines;
    public bool IsIndexed => Index is not null;
    public long? TotalLines => Index?.TotalLines;

    public Encoding Encoding
    {
        get => _encoding;
        set
        {
            _encoding = value;
            _cache.Clear(); // §4.6 索引再構築なしで即反映
        }
    }

    /// <summary>
    /// 行の区切り（文字コードと改行種別から決まる）。UTF-16 の改行は <c>0A 00</c> のように
    /// <b>文字1個ぶんの幅</b>を持つので、1バイトの <c>0x0A</c> だけを見ると行境界も文字境界もずれる
    /// （ソースレビュー 2026-09-19 の指摘2）。索引・行頭探索・本文切り出しで同じものを使う。
    /// </summary>
    private LineSeparator Separator => LineSeparator.For(_encoding, Newline);

    public LineDocument(IByteSource src, DetectedEncoding enc, NewlineStyle newline, int blockLines = 256)
    {
        _src = src;
        _encoding = enc.Encoding;
        BomLength = enc.BomLength;
        Newline = newline;
        _blockLines = blockLines;
    }

    /// <summary>すでに出来ている索引をそのまま使う（CLI から渡された索引。読み直さない）。</summary>
    public void AdoptIndex(SparseLineIndex index) => Index = index;

    /// <summary>裏で索引を構築し、完了後 行モードへ昇格可能にする（§3.1-2）。</summary>
    /// <param name="scanSource">通し読み用の読み取り元（省略時は表示と同じもの）。</param>
    public async Task BuildIndexAsync(IProgress<double>? progress = null, CancellationToken ct = default,
                                      IByteSource? scanSource = null)
        => Index = await SparseLineIndex.BuildAsync(scanSource ?? _src, BomLength, Newline, _blockLines, progress, ct,
                                                   LineSeparator.For(Encoding, Newline));

    // ── 行モード ─────────────────────────────────────────────

    public string GetLine(long lineIndex)
    {
        var index = Index ?? throw new InvalidOperationException("索引未構築です。BuildIndexAsync を先に呼んでください。");
        if (lineIndex < 0 || lineIndex >= index.TotalLines)
            throw new ArgumentOutOfRangeException(nameof(lineIndex));

        if (_cache.TryGet(lineIndex, out var cached))
            return cached;

        _dataMissing = false;
        long lineStart = LineStartOffset(lineIndex);
        string text = ReadLineAt(lineStart, out _);
        if (!_dataMissing) _cache.Set(lineIndex, text); // 未到着の不完全な結果は焼き付けない
        return text;
    }

    /// <summary>行番号 → その行の開始バイトオフセット。</summary>
    public long LineStartOffset(long lineIndex)
    {
        var index = Index ?? throw new InvalidOperationException("索引未構築です。");
        int k = (int)(lineIndex / _blockLines);
        long start = index.GetCheckpoint(k);
        int target = (int)(lineIndex % _blockLines);
        return ScanForwardNewlines(start, target);
    }

    /// <summary>バイトオフセット（行頭想定）→ 最寄り行番号。モード昇格時の位置継続に使う（§3.1-3）。</summary>
    public long OffsetToLineIndex(long byteOffset)
    {
        var index = Index ?? throw new InvalidOperationException("索引未構築です。");
        long off = Math.Clamp(byteOffset, BomLength, Length);

        // データ未到着だと改行を数え落として行番号がズレる。呼び出し側が
        // LastReadIncomplete で判定できるようにフラグを初期化しておく（WASM 用）。
        _dataMissing = false;

        int lo = 0, hi = index.CheckpointCount - 1, k = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (index.GetCheckpoint(mid) <= off) { k = mid; lo = mid + 1; }
            else hi = mid - 1;
        }

        long cp = index.GetCheckpoint(k);
        long lineIndex = (long)k * _blockLines + CountNewlines(cp, off);
        return Math.Min(lineIndex, Math.Max(0, index.TotalLines - 1));
    }

    /// <summary>
    /// <see cref="OffsetToLineIndex"/> の非同期版。読み取りを ReadAsync で行うため、
    /// WASM の未取得チャンクも await で取得でき、改行の数え落ちが起きない。
    /// 多数のヒットを一括で行番号へ写像する用途（フィルタ結果の前後±N）向け。
    /// </summary>
    public async ValueTask<long> OffsetToLineIndexAsync(long byteOffset, CancellationToken ct = default)
    {
        var index = Index ?? throw new InvalidOperationException("索引未構築です。");
        long off = Math.Clamp(byteOffset, BomLength, Length);

        int lo = 0, hi = index.CheckpointCount - 1, k = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (index.GetCheckpoint(mid) <= off) { k = mid; lo = mid + 1; }
            else hi = mid - 1;
        }

        long cp = index.GetCheckpoint(k);
        long lineIndex = (long)k * _blockLines + await CountNewlinesAsync(cp, off, ct);
        return Math.Min(lineIndex, Math.Max(0, index.TotalLines - 1));
    }

    private async ValueTask<long> CountNewlinesAsync(long from, long to, CancellationToken ct)
    {
        long count = 0, pos = from;
        var buf = new byte[8192];
        while (pos < to)
        {
            ct.ThrowIfCancellationRequested();
            int want = (int)Math.Min(buf.Length, to - pos);
            int got = TrimToUnits(pos, await _src.ReadAsync(pos, buf.AsMemory(0, want), ct));
            if (got <= 0) break;
            var span = buf.AsSpan(0, got);
            for (int i = 0; i < got; i++)
                if (IsSeparatorAt(span, i, pos)) count++;
            pos += got;
        }
        return count;
    }

    // ── ページモード ─────────────────────────────────────────

    /// <summary>指定バイトオフセットから rows 行ぶんを取得（索引不要）。行境界に揃えて読む（§3.1-1）。</summary>
    public IReadOnlyList<string> GetPage(long byteOffset, int rows)
    {
        var list = new List<string>(rows);
        long pos = AlignToLineStart(byteOffset);
        for (int r = 0; r < rows && pos < Length; r++)
        {
            list.Add(ReadLineAt(pos, out long nextStart));
            pos = nextStart;
        }
        return list;
    }

    /// <summary>GetPage の行頭オフセット付き版（ブックマーク描画などオフセットが要る描画用）。</summary>
    public IReadOnlyList<(long Offset, string Text)> GetPageWithOffsets(long byteOffset, int rows)
    {
        var list = new List<(long, string)>(rows);
        long pos = AlignToLineStart(byteOffset);
        for (int r = 0; r < rows && pos < Length; r++)
        {
            long start = pos;
            list.Add((start, ReadLineAt(pos, out long nextStart)));
            pos = nextStart;
        }
        return list;
    }

    /// <summary>
    /// 行の本文を<b>データの到着を待って</b>読む（保存・コピー用。読めなければ null）。
    ///
    /// ブラウザー版の読み取り元は、同期 <c>Read</c> では未取得のチャンクを 0 バイトとして返す。
    /// 画面は後から描き直せばよいが、保存は<b>そのとき書いた内容が最終結果</b>なので、
    /// 未取得のまま書くと本文が空行になってしまう（ソースレビュー 2026-09-19 の指摘4）。
    /// ファイル版は最初の同期読みで必ず完結するので、余計な読み直しは起きない。
    /// </summary>
    public async ValueTask<string?> GetLineAtOffsetAsync(long lineStart, CancellationToken ct = default)
    {
        long key = -lineStart - 1;
        if (_cache.TryGet(key, out var cached)) return cached;

        var sep = Separator;
        long limit = Math.Min(Length, lineStart + MaxLineScanBytes);
        int want = (int)(limit - lineStart);
        if (want <= 0) return "";

        byte[] bytes = new byte[want];
        int filled = await ReadAtAsync(lineStart, bytes, ct);
        if (filled < want) return null;   // 取得できなかった。空行として保存しない

        int end = filled;
        bool foundSep = false;
        for (int i = 0; i < filled; i++)
            if (IsSeparatorAt(bytes, i, lineStart)) { end = i - sep.ByteInUnit; foundSep = true; break; }

        bool truncated = !foundSep && limit < Length;
        string text = DecodeBytes(bytes.AsSpan(0, Math.Max(end, 0)), truncated);
        _cache.Set(key, text);
        return text;
    }

    /// <summary>行番号指定の同上（保存・コピー用。読めなければ null）。</summary>
    public async ValueTask<string?> GetLineAsync(long lineIndex, CancellationToken ct = default)
    {
        var index = Index ?? throw new InvalidOperationException("索引未構築です。");
        if (lineIndex < 0 || lineIndex >= index.TotalLines)
            throw new ArgumentOutOfRangeException(nameof(lineIndex));
        if (_cache.TryGet(lineIndex, out var cached)) return cached;

        long lineStart = await ScanForwardNewlinesAsync(
            index.GetCheckpoint((int)(lineIndex / _blockLines)), (int)(lineIndex % _blockLines), ct);
        if (lineStart < 0) return null;

        string? text = await GetLineAtOffsetAsync(lineStart, ct);
        if (text is not null) _cache.Set(lineIndex, text);
        return text;
    }

    /// <summary>非同期で buffer を埋める（読めた実バイト数を返す）。</summary>
    private async ValueTask<int> ReadAtAsync(long start, Memory<byte> buffer, CancellationToken ct)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int got = await _src.ReadAsync(start + total, buffer[total..], ct);
            if (got <= 0) break;
            total += got;
        }
        return total;
    }

    /// <summary><see cref="ScanForwardNewlines"/> の非同期版（取得できなければ -1）。</summary>
    private async ValueTask<long> ScanForwardNewlinesAsync(long from, int count, CancellationToken ct)
    {
        if (count <= 0) return from;
        var sep = Separator;
        long pos = from;
        int remaining = count;
        byte[] buf = new byte[8192];
        while (pos < Length)
        {
            int want = (int)Math.Min(buf.Length, Length - pos);
            int got = TrimToUnits(pos, await ReadAtAsync(pos, buf.AsMemory(0, want), ct));
            if (got <= 0) return -1;
            var span = buf.AsSpan(0, got);
            for (int i = 0; i < got; i++)
                if (IsSeparatorAt(span, i, pos) && --remaining == 0)
                    return pos + i - sep.ByteInUnit + sep.UnitSize;
            pos += got;
        }
        return Length;
    }

    /// <summary>行頭オフセット指定で 1 行取得（索引不要・フィルタ表示 §11-⑤ 用）。</summary>
    public string GetLineAtOffset(long lineStart)
    {
        long key = -lineStart - 1; // 行番号キー（非負）と衝突しない負キーで LRU を共用
        if (_cache.TryGet(key, out var cached))
            return cached;
        _dataMissing = false;
        string text = ReadLineAt(lineStart, out _);
        if (!_dataMissing) _cache.Set(key, text); // 未到着の不完全な結果は焼き付けない
        return text;
    }

    /// <summary>Tail（§11-③）: ソースが伸びた後に呼ぶ。末尾行の内容が変わりうるため LRU を破棄。</summary>
    public void OnSourceExtended() => _cache.Clear();

    /// <summary>指定オフセットを行頭に揃える。行の途中なら次の区切りの直後まで飛ばす。</summary>
    public long AlignToLineStart(long byteOffset)
    {
        if (byteOffset <= BomLength) return BomLength;
        if (byteOffset >= Length) return Length;

        var sep = Separator;
        long unitStart = byteOffset - sep.UnitSize;             // 直前の1文字
        Span<byte> unit = stackalloc byte[sep.UnitSize];
        if (unitStart >= BomLength && ReadAt(unitStart, unit) == sep.UnitSize
            && IsSeparatorAt(unit, sep.ByteInUnit, unitStart))
            return byteOffset; // 既に行頭

        return ScanForwardNewlines(byteOffset, 1);
    }

    /// <summary>行頭オフセット → 次の行の開始オフセット（EOF で Length）。ページモードのスクロール用。</summary>
    public long NextLineStart(long lineStart) => ScanForwardNewlines(lineStart, 1);

    /// <summary>行頭オフセット → 直前の行の開始オフセット（ページアップ用・後方スキャン）。</summary>
    public long PreviousLineStart(long lineStart)
    {
        if (lineStart <= BomLength) return BomLength;

        var sep = Separator;
        long i = lineStart - sep.UnitSize - 1; // 直前の1文字は前行を終端する区切り。その手前から遡る
        Span<byte> buf = stackalloc byte[8192];
        while (i >= BomLength)
        {
            int chunk = (int)Math.Min(buf.Length, i - BomLength + 1);
            long from = i - chunk + 1;
            int got = ReadAt(from, buf[..chunk]);
            var span = buf[..got];
            for (int j = got - 1; j >= 0; j--)
                if (IsSeparatorAt(span, j, from))
                    return from + j - sep.ByteInUnit + sep.UnitSize;
            i = from - 1;
        }
        return BomLength;
    }

    // ── 内部 ─────────────────────────────────────────────────

    /// <summary>start から始まる 1 行を読み・デコードして返す。nextStart に次行の開始オフセットを返す。</summary>
    private string ReadLineAt(long start, out long nextStart)
    {
        // 呼び出し元（行頭の走査など）で既に未到着が起きていたら、その事実を捨てない
        bool missingBefore = _dataMissing;
        _dataMissing = false;
        var (contentEnd, foundNl) = FindLineContentEnd(start, MaxLineScanBytes);
        bool missingHere = _dataMissing;
        // データ未到着（WASM の未取得チャンク）は「長大行の省略」ではないので省略記号を付けない
        bool truncated = !foundNl && contentEnd < Length && !missingHere;

        string text = DecodeRange(start, contentEnd, truncated);
        _dataMissing |= missingBefore || missingHere;

        if (foundNl)
            nextStart = contentEnd + Separator.UnitSize;
        else if (truncated)
            nextStart = ScanForwardNewlines(contentEnd, 1); // 長大行の残りを飛ばす
        else
            nextStart = Length; // EOF（末尾改行なしの最終行）

        return text;
    }

    private string DecodeRange(long start, long contentEnd, bool truncated)
    {
        int len = (int)(contentEnd - start);
        if (len <= 0)
            return truncated ? Ellipsis : string.Empty;

        byte[] bytes = new byte[len];
        int filled = FillRange(start, bytes);
        return DecodeBytes(bytes.AsSpan(0, filled), truncated);
    }

    /// <summary>読み終えた1行ぶんのバイトを、末尾の '\r' を落として文字列にする。</summary>
    private string DecodeBytes(ReadOnlySpan<byte> span, bool truncated)
    {
        // CRLF / 混在対策: 末尾の '\r' を1文字ぶん除去（UTF-16 なら 0D 00 の2バイト）
        var sep = Separator;
        if (sep.EndsWithCarriageReturn(span)) span = span[..^sep.UnitSize];

        string s = _encoding.GetString(span);

        if (s.Length > MaxLineDisplayChars)
        {
            s = s[..MaxLineDisplayChars];
            truncated = true;
        }
        return truncated ? s + Ellipsis : s;
    }

    /// <summary>
    /// 絶対位置 <paramref name="abs"/> のバイトが、行の区切りとして数えてよいものか。
    /// UTF-16/32 では文字の一部にも同じ値が現れるので、文字の境界に乗っているかまで見る。
    /// </summary>
    private bool IsSeparatorAt(ReadOnlySpan<byte> buf, int i, long bufBase)
    {
        var sep = Separator;
        if (buf[i] != sep.Value) return false;
        if (sep.UnitSize == 1) return true;
        long rel = bufBase + i - BomLength - sep.ByteInUnit;
        if (rel < 0 || rel % sep.UnitSize != 0) return false;
        // 文字の境界に 0A があっても改行とは限らない（UTF-16LE の Ċ は 0A 01）。
        // 文字ぜんぶを見て決める（再レビュー 2026-09-19 の指摘B）
        return sep.IsSeparatorUnit(buf, i - sep.ByteInUnit);
    }

    /// <summary>読んだバイト数を、文字の切れ目までに丸める（文字の途中で切って誤判定しないため）。</summary>
    private int TrimToUnits(long bufBase, int got)
    {
        int u = Separator.UnitSize;
        if (u == 1 || got <= 0) return got;
        int phase = (int)(((bufBase - BomLength) % u + u) % u);
        return Math.Max(0, got - (got + phase) % u);
    }

    /// <summary>from から count 個の区切りを飛ばし、その直後のオフセットを返す。EOF で Length。</summary>
    private long ScanForwardNewlines(long from, int count)
    {
        if (count <= 0) return from;
        var sep = Separator;
        long pos = from;
        int remaining = count;
        Span<byte> buf = stackalloc byte[8192];
        while (pos < Length)
        {
            int want = (int)Math.Min(buf.Length, Length - pos);
            int got = TrimToUnits(pos, ReadAt(pos, buf[..want]));
            if (got <= 0) break;
            var span = buf[..got];
            for (int i = 0; i < got; i++)
            {
                if (IsSeparatorAt(span, i, pos) && --remaining == 0)
                    return pos + i - sep.ByteInUnit + sep.UnitSize;
            }
            pos += got;
        }
        return Length;
    }

    /// <summary>from から maxBytes まで区切りを探す。見つかれば (本文の終わり, true)、なければ (打ち切り位置, false)。</summary>
    private (long end, bool foundNl) FindLineContentEnd(long from, int maxBytes)
    {
        var sep = Separator;
        long limit = Math.Min(Length, from + maxBytes);
        long pos = from;
        Span<byte> buf = stackalloc byte[8192];
        while (pos < limit)
        {
            int want = (int)Math.Min(buf.Length, limit - pos);
            int got = TrimToUnits(pos, ReadAt(pos, buf[..want]));
            if (got <= 0) break;
            var span = buf[..got];
            for (int i = 0; i < got; i++)
                if (IsSeparatorAt(span, i, pos))
                    return (pos + i - sep.ByteInUnit, true);
            pos += got;
        }
        return (limit, false);
    }

    private long CountNewlines(long from, long to)
    {
        long count = 0, pos = from;
        Span<byte> buf = stackalloc byte[8192];
        while (pos < to)
        {
            int want = (int)Math.Min(buf.Length, to - pos);
            int got = TrimToUnits(pos, ReadAt(pos, buf[..want]));
            if (got <= 0) break;
            var span = buf[..got];
            for (int i = 0; i < got; i++)
                if (IsSeparatorAt(span, i, pos)) count++;
            pos += got;
        }
        return count;
    }

    /// <summary>start から buffer を可能な限り埋め、実際に読めたバイト数を返す。</summary>
    /// <summary>
    /// 直近の読み取りで「データ未到着」が起きたか（WASM の Blob 未取得チャンク）。
    /// EOF と区別するために使う。true の結果は不完全なのでキャッシュしない。
    /// </summary>
    private bool _dataMissing;

    /// <summary>
    /// 直近の GetLine / GetLineAtOffset が「データ未到着」で不完全だったか。
    /// true の間は呼び出し側もその文字列をキャッシュしてはいけない（WASM 用）。
    /// </summary>
    public bool LastReadIncomplete => _dataMissing;

    /// <summary>
    /// I/O 読み取り。EOF ではないのに 0 バイトしか返らない場合＝データ未到着とみなす
    /// （Browser の BlobByteSource は未取得チャンクで 0 を返し、裏で取得を開始する）。
    /// </summary>
    private int ReadAt(long offset, Span<byte> buffer)
    {
        int got = _src.Read(offset, buffer);
        if (got <= 0 && offset >= 0 && offset < Length && buffer.Length > 0)
            _dataMissing = true;
        return got;
    }

    private int FillRange(long start, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int got = ReadAt(start + total, buffer[total..]);
            if (got <= 0) break;
            total += got;
        }
        return total;
    }

    public ValueTask DisposeAsync() => _src.DisposeAsync();
}
