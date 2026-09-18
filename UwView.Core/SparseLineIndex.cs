using System.Buffers;
using System.Text;

namespace UwView.Core;

public enum NewlineStyle { Lf, CrLf, Cr }

/// <summary>
/// 行の区切りをバイト列としてどう探すか（ソースレビュー 2026-09-19 の指摘2）。
///
/// これまでは一律に <c>0x0A</c> の1バイトを区切りとみなしていたため、
/// <b>CR だけで改行するファイルは1行</b>になり、<b>UTF-16 は行数がずれて</b>いた
/// （UTF-16LE の改行は <c>0A 00</c> で、1バイトだけ進めると次の文字境界もずれる）。
/// </summary>
/// <param name="Value">区切りのバイト（LF なら 0x0A・CR だけなら 0x0D）。</param>
/// <param name="UnitSize">1文字のバイト数（UTF-16 なら 2・UTF-32 なら 4・それ以外 1）。</param>
/// <param name="ByteInUnit">その文字の中で <paramref name="Value"/> が現れる位置（UTF-16BE なら 1）。</param>
public readonly record struct LineSeparator(byte Value, int UnitSize, int ByteInUnit)
{
    /// <summary>文字コードと改行種別から決める。</summary>
    public static LineSeparator For(Encoding encoding, NewlineStyle newline)
    {
        byte value = newline == NewlineStyle.Cr ? (byte)'\r' : (byte)'\n';
        return encoding.CodePage switch
        {
            1200  => new(value, 2, 0),   // UTF-16LE: 0A 00
            1201  => new(value, 2, 1),   // UTF-16BE: 00 0A
            12000 => new(value, 4, 0),   // UTF-32LE
            12001 => new(value, 4, 3),   // UTF-32BE
            _     => new(value, 1, 0),
        };
    }

    /// <summary>
    /// <paramref name="unitStart"/> から始まる1文字が、ちょうど改行そのものか。
    ///
    /// 文字の境界に <see cref="Value"/> があっても、改行とは限らない。
    /// UTF-16LE の <c>Ċ</c>（U+010A）は <c>0A 01</c> で、LF の <c>0A 00</c> とは別の文字
    ///（再レビュー 2026-09-19 の指摘B）。<b>文字ぜんぶ</b>を見て決める。
    /// </summary>
    public bool IsSeparatorUnit(ReadOnlySpan<byte> span, int unitStart)
    {
        if (unitStart < 0 || unitStart + UnitSize > span.Length) return false;
        for (int k = 0; k < UnitSize; k++)
            if (span[unitStart + k] != (k == ByteInUnit ? Value : (byte)0)) return false;
        return true;
    }

    /// <summary>1バイトの LF 区切りか（＝これまでどおりの単純なバイト走査でよいか）。</summary>
    public bool IsSingleByteLf => UnitSize == 1 && Value == (byte)'\n';

    /// <summary>from 以降で次の区切りの<b>先頭バイト</b>の位置（＝その行の本文の終わり）。無ければ -1。</summary>
    public int IndexOfSeparator(ReadOnlySpan<byte> span, int from, int unitPhase = 0)
    {
        if (UnitSize == 1) { int rel = span[from..].IndexOf(Value); return rel < 0 ? -1 : from + rel; }
        int next = NextLineStart(span, from, unitPhase);
        return next < 0 ? -1 : next - UnitSize;
    }

    /// <summary><paramref name="before"/> より前にある最後の区切りの<b>直後</b>（無ければ 0＝span の先頭）。</summary>
    public int LineStartBefore(ReadOnlySpan<byte> span, int before, int unitPhase = 0)
    {
        if (UnitSize == 1) return span[..before].LastIndexOf(Value) + 1;
        for (int at = before - 1; at >= 0; at--)
        {
            if (span[at] != Value) continue;
            int unitStart = at - ByteInUnit;
            if ((unitStart + unitPhase) % UnitSize == 0 && IsSeparatorUnit(span, unitStart))
                return unitStart + UnitSize;
        }
        return 0;
    }

    /// <summary>span に含まれる区切りの数。</summary>
    public long CountSeparators(ReadOnlySpan<byte> span, int unitPhase = 0)
    {
        if (UnitSize == 1) return span.Count(Value);
        long n = 0;
        for (int i = 0; i < span.Length; )
        {
            int next = NextLineStart(span, i, unitPhase);
            if (next < 0) break;
            i = next;
            n++;
        }
        return n;
    }

    /// <summary>from 以降で次の区切りの<b>直後</b>の位置（見つからなければ -1）。</summary>
    /// <param name="unitPhase">span の先頭が、文字の何バイト目から始まっているか（通常 0）。</param>
    public int NextLineStart(ReadOnlySpan<byte> span, int from, int unitPhase = 0)
    {
        for (int i = from; i < span.Length; )
        {
            int rel = span[i..].IndexOf(Value);
            if (rel < 0) return -1;
            int at = i + rel;
            if (UnitSize == 1) return at + 1;

            // 文字の途中に現れた同じ値は区切りではない（UTF-16 の上位バイト等）
            int unitStart = at - ByteInUnit;
            if ((unitStart + unitPhase) % UnitSize == 0 && IsSeparatorUnit(span, unitStart))
            {
                int end = unitStart + UnitSize;
                return end <= span.Length ? end : -1;
            }
            i = at + 1;
        }
        return -1;
    }
}

/// <summary>
/// 追記専用の long 配列（単一書き手・複数読み手セーフ）。
/// 配列は伸びるだけ・要素は不変なので、count → array の順で読めばロック不要。
/// </summary>
internal sealed class AppendOnlyLongList
{
    private long[] _items;
    private int _count;

    public AppendOnlyLongList(int capacity = 1024) => _items = new long[capacity];

    public int Count => Volatile.Read(ref _count);

    public long this[int index]
    {
        get
        {
            // count を先に読む → その時点の配列は必ず count 個の有効要素を含む
            var items = Volatile.Read(ref _items);
            return items[index];
        }
    }

    public void Add(long value)
    {
        var items = _items;
        int count = _count;
        if (count == items.Length)
        {
            var bigger = new long[items.Length * 2];
            Array.Copy(items, bigger, count);
            Volatile.Write(ref _items, bigger);
            items = bigger;
        }
        items[count] = value;
        Volatile.Write(ref _count, count + 1);
    }
}

/// <summary>
/// スパース行インデックス（§4.3）。N 行（既定 256）ごとに 1 つだけ
/// バイトオフセットを記録するため、2 億行でも索引は約 6MB。
/// checkpoints[k] = (k*BlockLines) 行目の開始バイトオフセット。
/// Tail（§11-③）用に <see cref="ExtendAsync"/> で追記分を増分スキャンできる。
/// </summary>
public sealed class SparseLineIndex
{
    private readonly AppendOnlyLongList _checkpoints;
    private long _fileLength;
    private long _totalLines;
    private long _newlineCount; // これまでに見た '\n' の総数
    private long _scannedTo;    // スキャン済みバイト位置
    private byte _lastByte;     // スキャン済み範囲の最終バイト
    private bool _endsWithSeparator;  // 最後が行の区切りで終わっているか

    public long FileLength => Volatile.Read(ref _fileLength);
    public int BlockLines { get; }
    public long TotalLines => Volatile.Read(ref _totalLines);
    public int BomLength { get; }
    public NewlineStyle Newline { get; }

    public int CheckpointCount => _checkpoints.Count;
    public long GetCheckpoint(int k) => _checkpoints[k];

    /// <summary>行の区切りの探し方（文字コードと改行種別から決まる）。</summary>
    public LineSeparator Separator { get; }

    private SparseLineIndex(int blockLines, int bomLength, NewlineStyle newline, LineSeparator? separator = null)
    {
        BlockLines = blockLines;
        BomLength = bomLength;
        Newline = newline;
        Separator = separator ?? new LineSeparator((byte)'\n', 1, 0);
        _checkpoints = new AppendOnlyLongList();
        _checkpoints.Add(bomLength);
        _scannedTo = bomLength;
    }

    /// <summary>
    /// <b>すでに数えてある改行から索引を組み立てる</b>（<c>uvf ファイル 語 -open</c> の受け渡し用）。
    ///
    /// CLI は検索でどのみちファイルを通しで読むので、そのついでに N 行ごとの位置を控えておける。
    /// それを渡してもらえば、画面はもう一度読み直さずに済む（10GB なら丸ごと1回ぶんが浮く。
    /// オーナー指示 2026-09-18「-open が最後にある場合、検索時に index も同時に作成する」）。
    /// </summary>
    /// <param name="checkpoints">先頭（BOM の直後）から <paramref name="blockLines"/> 行ごとの行頭位置。</param>
    /// <param name="newlineCount">ファイル全体の '\n' の数。</param>
    /// <param name="lastByte">ファイルの最後のバイト（末尾に改行があるかの判定に使う）。</param>
    /// <remarks>CLI の走査は <c>0x0A</c> を区切りとするので、<b>LF/CRLF の1バイト文字コード専用</b>。
    /// それ以外は渡さず、画面側で索引を作ること（ソースレビュー 2026-09-19 の指摘2）。</remarks>
    public static SparseLineIndex FromCheckpoints(
        int bomLength, NewlineStyle newline, int blockLines,
        IReadOnlyList<long> checkpoints, long newlineCount, byte lastByte, long fileLength)
    {
        var index = new SparseLineIndex(blockLines, bomLength, newline);
        foreach (long at in checkpoints) index._checkpoints.Add(at);   // 先頭の BOM 位置は ctor が入れている
        index._newlineCount = newlineCount;
        index._lastByte = lastByte;
        index._endsWithSeparator = lastByte == (byte)'\n';
        Volatile.Write(ref index._scannedTo, fileLength);
        index.UpdateTotals(fileLength, newlineCount, index._endsWithSeparator);
        return index;
    }

    /// <summary>
    /// 背景タスクで 1MB ずつ順次読みしながら '\n' を数え、索引を構築する。
    /// 数 GB でも 1 回の順次読みで完了。IProgress と CancellationToken 対応。
    /// I/O は ReadAsync 経由（Desktop=同期の薄いラッパ / WASM=Blob の async 経路）。
    /// </summary>
    public static Task<SparseLineIndex> BuildAsync(
        IByteSource src, int bomLength, NewlineStyle newline, int blockLines = 256,
        IProgress<double>? progress = null, CancellationToken ct = default, LineSeparator? separator = null)
        => Task.Run(() => Build(src, bomLength, newline, blockLines, progress, ct, separator), ct);

    internal static async Task<SparseLineIndex> Build(
        IByteSource src, int bomLength, NewlineStyle newline, int blockLines,
        IProgress<double>? progress, CancellationToken ct, LineSeparator? separator = null)
    {
        if (blockLines <= 0) throw new ArgumentOutOfRangeException(nameof(blockLines));

        var index = new SparseLineIndex(blockLines, bomLength, newline, separator);
        await index.ScanAsync(src, src.Length, progress, ct);
        progress?.Report(1.0);
        return index;
    }

    /// <summary>
    /// Tail（§11-③）: 前回スキャン位置から追記分だけ増分スキャンして索引を伸ばす。
    /// 単一書き手（Tail ループ）から呼ぶこと。読み手（UI/検索）はロック無しで整合する。
    /// </summary>
    public Task ExtendAsync(IByteSource src, CancellationToken ct = default)
        => Task.Run(() => ScanAsync(src, src.Length, null, ct), ct);

    private async Task ScanAsync(IByteSource src, long targetLength, IProgress<double>? progress, CancellationToken ct)
    {
        long pos = _scannedTo;
        long lineNo = _newlineCount;
        byte lastByte = _lastByte;
        bool endsWithSeparator = _endsWithSeparator;
        long contentBytes = targetLength - BomLength;

        const int BufSize = 4 << 20; // 4MB（1MB より 1 割ほど速い。pread の実測 889→966 MB/s）
        byte[] buf = ArrayPool<byte>.Shared.Rent(BufSize);
        try
        {
            long lastReport = 0;
            while (pos < targetLength)
            {
                ct.ThrowIfCancellationRequested();
                int want = (int)Math.Min(BufSize, targetLength - pos);
                int got = await src.ReadAsync(pos, buf.AsMemory(0, want), ct);
                if (got <= 0) break;

                // 改行は1バイトずつ比べず、SIMD の IndexOf で跳ぶ。
                // 258GB の実測で索引作成だけ 696MB/s と、検索（880MB/s）や媒体（942MB/s）に負けていた
                //（1バイトずつ＝約1GB/s が頭打ち。IndexOf なら 3.4GB/s 出る。オーナー計測 2026-09-18）
                var span = buf.AsSpan(0, got);
                int phase = (int)((pos - BomLength) % Separator.UnitSize);   // 文字の途中から読み始めた場合
                endsWithSeparator = false;   // このひと塊の中身だけで決める
                for (int i = 0; i < span.Length; )
                {
                    int next = Separator.NextLineStart(span, i, phase);
                    if (next < 0) break;
                    i = next;
                    lineNo++;
                    // ここまでで読んだ分が、ちょうど区切りで終わったか。
                    // 前回の走査結果を引き継ぐと、改行なしの追記（Tail）で古い true が残り、
                    // 増えた未完行が行数に入らない（再レビュー 2026-09-19 の退行C）
                    endsWithSeparator = i == span.Length;
                    if (lineNo % BlockLines == 0) _checkpoints.Add(pos + i);
                }
                lastByte = buf[got - 1];
                pos += got;

                // 読み手が矛盾を見ないよう、走査の進みに合わせて公開値を随時更新
                _newlineCount = lineNo;
                _lastByte = lastByte;
                _endsWithSeparator = endsWithSeparator;
                Volatile.Write(ref _scannedTo, pos);
                UpdateTotals(pos, lineNo, endsWithSeparator);

                if (progress is not null && contentBytes > 0 && pos - lastReport >= (16 << 20))
                {
                    lastReport = pos;
                    progress.Report((double)(pos - BomLength) / contentBytes);
                }
            }

            UpdateTotals(pos, lineNo, endsWithSeparator);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    private void UpdateTotals(long scannedTo, long newlineCount, bool endsWithSeparator)
    {
        long total;
        if (scannedTo <= BomLength) total = 0;
        else if (endsWithSeparator) total = newlineCount;       // 末尾が改行で終わっている
        else total = newlineCount + 1;                          // 末尾に改行なしの最終行
        Volatile.Write(ref _totalLines, total);
        Volatile.Write(ref _fileLength, scannedTo);
    }
}
