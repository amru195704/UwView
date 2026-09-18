using Microsoft.Win32.SafeHandles;

namespace UwView.Core;

/// <summary>
/// 先頭から順に読むための IByteSource（RandomAccess＝pread 直読み）。
///
/// 画面は <see cref="MmapByteSource"/>（好きな位置をすぐ覗ける・Tail で伸ばせる）を使うが、
/// CLI の全文走査のように<b>大きなファイルを1回通しで読む</b>用途では mmap が不利になる。
/// 実測（外付け USB SSD・50GB の OSM・8GB ずつ計測 2026-09-17）:
/// <code>
///   dd（1MB ブロック）            942 MB/s   ← 媒体の素の帯域
///   mmap ＋ 1MB コピー            575 MB/s
///   RandomAccess ＋ 1MB           889 MB/s
///   RandomAccess ＋ 4MB           966 MB/s
/// </code>
/// mmap はページフォルト＋マップからのコピーのぶん帯域の6割しか引けない（マップがメモリより
/// 大きいと更に落ちる）。通しで読むだけなら pread のほうが素直に速い。
///
/// 並列に読んでも 960 MB/s 止まり（媒体が飽和している）ので、ここは単線でよい。
/// </summary>
internal sealed class SequentialFileByteSource : IByteSource
{
    private readonly SafeFileHandle _handle;

    public SequentialFileByteSource(string path)
    {
        // 書き込み中のファイルでも開けるよう mmap 版と同じ共有指定にする
        _handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
                                  FileShare.ReadWrite | FileShare.Delete, FileOptions.SequentialScan);
        _length = RandomAccess.GetLength(_handle);
    }

    private long _length;

    /// <summary>
    /// いま見えているファイルの長さ。<see cref="Refresh"/> を呼ぶまで変わらない
    /// （走査の途中で伸びると範囲の辻褄が合わなくなるため、読みの最中は固定する）。
    /// </summary>
    public long Length => Volatile.Read(ref _length);

    /// <summary>
    /// 長さを取り直す（<b>走査を始める前に呼ぶ</b>）。
    /// Tail で追記されたぶんは、これを呼ばないと検索・索引の対象に入らない
    /// （表示用 mmap は <see cref="MmapByteSource.TryExpand"/> で伸びるので、見えるのに探せない状態になる。
    /// ソースレビュー 2026-09-19 の指摘1）。
    /// </summary>
    public long Refresh()
    {
        if (_handle.IsClosed) return Length;
        try { Volatile.Write(ref _length, RandomAccess.GetLength(_handle)); }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { /* 取れなければ前の値のまま */ }
        return Length;
    }

    public int Read(long offset, Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (offset >= Length || buffer.Length == 0) return 0;

        int want = (int)Math.Min(Length - offset, buffer.Length);
        return RandomAccess.Read(_handle, buffer[..want], offset);
    }

    public ValueTask DisposeAsync()
    {
        _handle.Dispose();
        return ValueTask.CompletedTask;
    }
}
