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

    public LineDocument(IByteSource src, DetectedEncoding enc, NewlineStyle newline, int blockLines = 256)
    {
        _src = src;
        _encoding = enc.Encoding;
        BomLength = enc.BomLength;
        Newline = newline;
        _blockLines = blockLines;
    }

    /// <summary>裏で索引を構築し、完了後 行モードへ昇格可能にする（§3.1-2）。</summary>
    public async Task BuildIndexAsync(IProgress<double>? progress = null, CancellationToken ct = default)
        => Index = await SparseLineIndex.BuildAsync(_src, BomLength, Newline, _blockLines, progress, ct);

    // ── 行モード ─────────────────────────────────────────────

    public string GetLine(long lineIndex)
    {
        var index = Index ?? throw new InvalidOperationException("索引未構築です。BuildIndexAsync を先に呼んでください。");
        if (lineIndex < 0 || lineIndex >= index.TotalLines)
            throw new ArgumentOutOfRangeException(nameof(lineIndex));

        if (_cache.TryGet(lineIndex, out var cached))
            return cached;

        long lineStart = LineStartOffset(lineIndex);
        string text = ReadLineAt(lineStart, out _);
        _cache.Set(lineIndex, text);
        return text;
    }

    /// <summary>行番号 → その行の開始バイトオフセット。</summary>
    public long LineStartOffset(long lineIndex)
    {
        var index = Index ?? throw new InvalidOperationException("索引未構築です。");
        int k = (int)(lineIndex / _blockLines);
        long start = index.Checkpoints[k];
        int target = (int)(lineIndex % _blockLines);
        return ScanForwardNewlines(start, target);
    }

    /// <summary>バイトオフセット（行頭想定）→ 最寄り行番号。モード昇格時の位置継続に使う（§3.1-3）。</summary>
    public long OffsetToLineIndex(long byteOffset)
    {
        var index = Index ?? throw new InvalidOperationException("索引未構築です。");
        long off = Math.Clamp(byteOffset, BomLength, Length);

        var cps = index.Checkpoints;
        int lo = 0, hi = cps.Count - 1, k = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (cps[mid] <= off) { k = mid; lo = mid + 1; }
            else hi = mid - 1;
        }

        long lineIndex = (long)k * _blockLines + CountNewlines(cps[k], off);
        return Math.Min(lineIndex, Math.Max(0, index.TotalLines - 1));
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

    /// <summary>指定オフセットを行頭に揃える。行の途中なら次の '\n' の直後まで飛ばす。</summary>
    public long AlignToLineStart(long byteOffset)
    {
        if (byteOffset <= BomLength) return BomLength;
        if (byteOffset >= Length) return Length;

        Span<byte> one = stackalloc byte[1];
        if (_src.Read(byteOffset - 1, one) == 1 && one[0] == (byte)'\n')
            return byteOffset; // 既に行頭

        return ScanForwardNewlines(byteOffset, 1);
    }

    /// <summary>行頭オフセット → 次の行の開始オフセット（EOF で Length）。ページモードのスクロール用。</summary>
    public long NextLineStart(long lineStart) => ScanForwardNewlines(lineStart, 1);

    /// <summary>行頭オフセット → 直前の行の開始オフセット（ページアップ用・後方スキャン）。</summary>
    public long PreviousLineStart(long lineStart)
    {
        if (lineStart <= BomLength) return BomLength;

        long i = lineStart - 2; // lineStart-1 は前行を終端する '\n' 想定。その手前から遡る
        Span<byte> buf = stackalloc byte[8192];
        while (i >= BomLength)
        {
            int chunk = (int)Math.Min(buf.Length, i - BomLength + 1);
            long from = i - chunk + 1;
            int got = _src.Read(from, buf[..chunk]);
            for (int j = got - 1; j >= 0; j--)
                if (buf[j] == (byte)'\n')
                    return from + j + 1;
            i = from - 1;
        }
        return BomLength;
    }

    // ── 内部 ─────────────────────────────────────────────────

    /// <summary>start から始まる 1 行を読み・デコードして返す。nextStart に次行の開始オフセットを返す。</summary>
    private string ReadLineAt(long start, out long nextStart)
    {
        var (contentEnd, foundNl) = FindLineContentEnd(start, MaxLineScanBytes);
        bool truncated = !foundNl && contentEnd < Length;

        string text = DecodeRange(start, contentEnd, truncated);

        if (foundNl)
            nextStart = contentEnd + 1;
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
        var span = bytes.AsSpan(0, filled);

        // CRLF / 混在対策: 末尾 '\r' を除去
        if (span.Length > 0 && span[^1] == (byte)'\r')
            span = span[..^1];

        string s = _encoding.GetString(span);

        if (s.Length > MaxLineDisplayChars)
        {
            s = s[..MaxLineDisplayChars];
            truncated = true;
        }
        return truncated ? s + Ellipsis : s;
    }

    /// <summary>from から count 個の '\n' を飛ばし、その直後のオフセットを返す。EOF で Length。</summary>
    private long ScanForwardNewlines(long from, int count)
    {
        if (count <= 0) return from;
        long pos = from;
        int remaining = count;
        Span<byte> buf = stackalloc byte[8192];
        while (pos < Length)
        {
            int want = (int)Math.Min(buf.Length, Length - pos);
            int got = _src.Read(pos, buf[..want]);
            if (got <= 0) break;
            for (int i = 0; i < got; i++)
            {
                if (buf[i] == (byte)'\n' && --remaining == 0)
                    return pos + i + 1;
            }
            pos += got;
        }
        return Length;
    }

    /// <summary>from から maxBytes まで '\n' を探す。見つかれば (その位置, true)、なければ (打ち切り位置, false)。</summary>
    private (long end, bool foundNl) FindLineContentEnd(long from, int maxBytes)
    {
        long limit = Math.Min(Length, from + maxBytes);
        long pos = from;
        Span<byte> buf = stackalloc byte[8192];
        while (pos < limit)
        {
            int want = (int)Math.Min(buf.Length, limit - pos);
            int got = _src.Read(pos, buf[..want]);
            if (got <= 0) break;
            for (int i = 0; i < got; i++)
                if (buf[i] == (byte)'\n')
                    return (pos + i, true);
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
            int got = _src.Read(pos, buf[..want]);
            if (got <= 0) break;
            for (int i = 0; i < got; i++)
                if (buf[i] == (byte)'\n') count++;
            pos += got;
        }
        return count;
    }

    /// <summary>start から buffer を可能な限り埋め、実際に読めたバイト数を返す。</summary>
    private int FillRange(long start, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int got = _src.Read(start + total, buffer[total..]);
            if (got <= 0) break;
            total += got;
        }
        return total;
    }

    public ValueTask DisposeAsync() => _src.DisposeAsync();
}
