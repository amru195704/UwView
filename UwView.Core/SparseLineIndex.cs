using System.Buffers;

namespace UwView.Core;

public enum NewlineStyle { Lf, CrLf, Cr }

/// <summary>
/// スパース行インデックス（§4.3）。N 行（既定 256）ごとに 1 つだけ
/// バイトオフセットを記録するため、2 億行でも索引は約 6MB。
/// checkpoints[k] = (k*BlockLines) 行目の開始バイトオフセット。
/// </summary>
public sealed class SparseLineIndex
{
    private readonly List<long> _checkpoints;

    public long FileLength { get; }
    public int BlockLines { get; }
    public long TotalLines { get; }
    public int BomLength { get; }
    public NewlineStyle Newline { get; }
    public IReadOnlyList<long> Checkpoints => _checkpoints;

    private SparseLineIndex(long fileLength, int blockLines, List<long> checkpoints,
        long totalLines, int bomLength, NewlineStyle newline)
    {
        FileLength = fileLength;
        BlockLines = blockLines;
        _checkpoints = checkpoints;
        TotalLines = totalLines;
        BomLength = bomLength;
        Newline = newline;
    }

    /// <summary>
    /// 背景タスクで 1MB ずつ順次読みしながら '\n' を数え、索引を構築する。
    /// 数 GB でも 1 回の順次読みで完了。IProgress と CancellationToken 対応。
    /// </summary>
    public static Task<SparseLineIndex> BuildAsync(
        IByteSource src, int bomLength, NewlineStyle newline, int blockLines = 256,
        IProgress<double>? progress = null, CancellationToken ct = default)
        => Task.Run(() => Build(src, bomLength, newline, blockLines, progress, ct), ct);

    internal static SparseLineIndex Build(
        IByteSource src, int bomLength, NewlineStyle newline, int blockLines,
        IProgress<double>? progress, CancellationToken ct)
    {
        if (blockLines <= 0) throw new ArgumentOutOfRangeException(nameof(blockLines));

        long fileLength = src.Length;
        var checkpoints = new List<long> { bomLength };
        long lineNo = 0;              // これまでに見た '\n' の数 ＝ 次に始まる行の 0 始まり index
        long pos = bomLength;
        long contentBytes = fileLength - bomLength;
        byte lastByte = 0;

        const int BufSize = 1 << 20; // 1MB
        byte[] buf = ArrayPool<byte>.Shared.Rent(BufSize);
        try
        {
            long lastReport = 0;
            while (pos < fileLength)
            {
                ct.ThrowIfCancellationRequested();
                int want = (int)Math.Min(BufSize, fileLength - pos);
                int got = src.Read(pos, buf.AsSpan(0, want));
                if (got <= 0) break;

                for (int i = 0; i < got; i++)
                {
                    if (buf[i] == (byte)'\n')
                    {
                        lineNo++;
                        if (lineNo % blockLines == 0)
                            checkpoints.Add(pos + i + 1);
                    }
                }
                lastByte = buf[got - 1];
                pos += got;

                if (progress is not null && contentBytes > 0 && pos - lastReport >= (16 << 20))
                {
                    lastReport = pos;
                    progress.Report((double)(pos - bomLength) / contentBytes);
                }
            }

            long totalLines;
            if (contentBytes <= 0) totalLines = 0;
            else if (lastByte == (byte)'\n') totalLines = lineNo; // 末尾改行あり
            else totalLines = lineNo + 1;                          // 末尾に改行なしの最終行

            progress?.Report(1.0);
            return new SparseLineIndex(fileLength, blockLines, checkpoints, totalLines, bomLength, newline);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }
}
