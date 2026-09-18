using System.Buffers;

namespace UwView.Core;

public enum NewlineStyle { Lf, CrLf, Cr }

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

    public long FileLength => Volatile.Read(ref _fileLength);
    public int BlockLines { get; }
    public long TotalLines => Volatile.Read(ref _totalLines);
    public int BomLength { get; }
    public NewlineStyle Newline { get; }

    public int CheckpointCount => _checkpoints.Count;
    public long GetCheckpoint(int k) => _checkpoints[k];

    private SparseLineIndex(int blockLines, int bomLength, NewlineStyle newline)
    {
        BlockLines = blockLines;
        BomLength = bomLength;
        Newline = newline;
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
    public static SparseLineIndex FromCheckpoints(
        int bomLength, NewlineStyle newline, int blockLines,
        IReadOnlyList<long> checkpoints, long newlineCount, byte lastByte, long fileLength)
    {
        var index = new SparseLineIndex(blockLines, bomLength, newline);
        foreach (long at in checkpoints) index._checkpoints.Add(at);   // 先頭の BOM 位置は ctor が入れている
        index._newlineCount = newlineCount;
        index._lastByte = lastByte;
        Volatile.Write(ref index._scannedTo, fileLength);
        index.UpdateTotals(fileLength, newlineCount, lastByte);
        return index;
    }

    /// <summary>
    /// 背景タスクで 1MB ずつ順次読みしながら '\n' を数え、索引を構築する。
    /// 数 GB でも 1 回の順次読みで完了。IProgress と CancellationToken 対応。
    /// I/O は ReadAsync 経由（Desktop=同期の薄いラッパ / WASM=Blob の async 経路）。
    /// </summary>
    public static Task<SparseLineIndex> BuildAsync(
        IByteSource src, int bomLength, NewlineStyle newline, int blockLines = 256,
        IProgress<double>? progress = null, CancellationToken ct = default)
        => Task.Run(() => Build(src, bomLength, newline, blockLines, progress, ct), ct);

    internal static async Task<SparseLineIndex> Build(
        IByteSource src, int bomLength, NewlineStyle newline, int blockLines,
        IProgress<double>? progress, CancellationToken ct)
    {
        if (blockLines <= 0) throw new ArgumentOutOfRangeException(nameof(blockLines));

        var index = new SparseLineIndex(blockLines, bomLength, newline);
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
                for (int i = 0; i < span.Length; )
                {
                    int nl = span[i..].IndexOf((byte)'\n');
                    if (nl < 0) break;
                    i += nl + 1;
                    lineNo++;
                    if (lineNo % BlockLines == 0) _checkpoints.Add(pos + i);
                }
                lastByte = buf[got - 1];
                pos += got;

                // 読み手が矛盾を見ないよう、走査の進みに合わせて公開値を随時更新
                _newlineCount = lineNo;
                _lastByte = lastByte;
                Volatile.Write(ref _scannedTo, pos);
                UpdateTotals(pos, lineNo, lastByte);

                if (progress is not null && contentBytes > 0 && pos - lastReport >= (16 << 20))
                {
                    lastReport = pos;
                    progress.Report((double)(pos - BomLength) / contentBytes);
                }
            }

            UpdateTotals(pos, lineNo, lastByte);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    private void UpdateTotals(long scannedTo, long newlineCount, byte lastByte)
    {
        long total;
        if (scannedTo <= BomLength) total = 0;
        else if (lastByte == (byte)'\n') total = newlineCount; // 末尾改行あり
        else total = newlineCount + 1;                          // 末尾に改行なしの最終行
        Volatile.Write(ref _totalLines, total);
        Volatile.Write(ref _fileLength, scannedTo);
    }
}
