using Microsoft.Win32.SafeHandles;

namespace UwView.Core;

/// <summary>開いているファイルに、外で何が起きたか（v1.8.2 extFS E-0）。</summary>
public enum FileChange
{
    /// <summary>変わっていない。</summary>
    None,
    /// <summary>後ろに足された（今までの中身はそのまま）。</summary>
    Grew,
    /// <summary>切り詰められた（<c>: &gt; app.log</c>・<c>truncate</c>）。長さが今の長さより短い。</summary>
    Truncated,
    /// <summary>同じファイルの中身が書き換えられた（<c>cp small.log app.log</c> など）。先頭か末尾の 4KB が違う。</summary>
    Rewritten,
    /// <summary>同じ名前に別のファイルができた（ローテーション・別名で書いてから置き換える保存）。開いている方はそのまま。</summary>
    Replaced,
    /// <summary>その名前のファイルが無くなった（開いている方はそのまま読める）。</summary>
    Deleted,
}

/// <summary>
/// 画面の表示に使う読み取り元（v1.8.2 extFS E-0）。位置を指定して読む（pread）。
///
/// 1.8.1 までは mmap（<see cref="MmapByteSource"/>）だった。mmap は外からファイルを切り詰められると、
/// 無くなった所を読んだ瞬間に SIGBUS で落ちる（.NET の例外にならないので捕まえられない）。
/// 読む前に長さを確かめても、確かめた直後に切り詰められれば防げない。
/// pread なら無くなった所は短く（0 バイトで）読めるだけなので、落ちようがない。
/// Windows でも、マップしていると他のプロセスが切り詰められなかった（ERROR_USER_MAPPED_FILE）のが無くなる。
///
/// <see cref="Length"/> は、開いたとき（伸ばしたとき）の長さのまま。切り詰められても縮めない
///（索引の行の位置と食い違わないように）。無くなった所は空として読める。
/// </summary>
public sealed class LiveFileByteSource : IByteSource
{
    /// <summary>中身が変わっていないかを見る範囲（先頭と末尾それぞれ）。</summary>
    internal const int ProbeBytes = 4096;

    private readonly string _path;
    private readonly SafeFileHandle _handle;
    private long _length;
    private byte[] _head = [];
    private byte[] _tail = [];

    public string Path => _path;
    public long Length => Volatile.Read(ref _length);

    public LiveFileByteSource(string path)
    {
        _path = path;
        // 書き込み中のプロセスと共存でき、開いたままでも名前を変えたり消したりできるように開く
        _handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
                                  FileShare.ReadWrite | FileShare.Delete);
        _length = RandomAccess.GetLength(_handle);
        _head = ReadProbe(_handle, 0, (int)Math.Min(ProbeBytes, _length));
        _tail = ReadTail(_handle, _length);
    }

    public int Read(long offset, Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        long len = Length;
        if (offset >= len || buffer.Length == 0) return 0;

        int want = (int)Math.Min(len - offset, buffer.Length);
        int total = 0;
        while (total < want)
        {
            int got;
            try { got = RandomAccess.Read(_handle, buffer.Slice(total, want - total), offset + total); }
            catch (Exception e) when (e is IOException or ObjectDisposedException) { break; }
            if (got <= 0) break;   // 切り詰められた所（短く読めたぶんだけ返す）
            total += got;
        }
        return total;
    }

    /// <summary>
    /// 伸びていれば、その長さまで読めるようにして true（末尾追従の1秒ごとの見張りから呼ぶ）。
    /// 縮んだ・書き換えられたかは <see cref="Check"/> で先に確かめること。
    /// </summary>
    public bool TryExpand()
    {
        if (_handle.IsClosed) return false;
        long newLen;
        try { newLen = RandomAccess.GetLength(_handle); }
        catch (IOException) { return false; }
        if (newLen <= Length) return false;

        if (_head.Length < ProbeBytes) _head = ReadProbe(_handle, 0, (int)Math.Min(ProbeBytes, newLen));
        _tail = ReadTail(_handle, newLen);
        Volatile.Write(ref _length, newLen);
        return true;
    }

    /// <summary>
    /// 開いたとき（伸ばしたとき）から、外で何が起きたか。
    /// 先に開いている方（ハンドル）を見て、変わっていなければ同じ名前のファイルと見比べる。
    /// 見るのは長さと、先頭・末尾の 4KB だけ（1秒ごとに呼んでも軽い）。
    /// </summary>
    public FileChange Check()
    {
        if (_handle.IsClosed) return FileChange.None;
        long len = Length;
        long handleLen;
        try { handleLen = RandomAccess.GetLength(_handle); }
        catch (IOException) { return FileChange.None; }

        if (handleLen < len) return FileChange.Truncated;
        if (!Same(_handle, 0, _head) || !Same(_handle, len - _tail.Length, _tail)) return FileChange.Rewritten;

        switch (SameNameDiffers())
        {
            case null: return FileChange.Deleted;
            case true: return FileChange.Replaced;
        }
        return handleLen > len ? FileChange.Grew : FileChange.None;
    }

    /// <summary>同じ名前のファイルが、開いている方と別物か（無ければ null）。読めなければ「同じ」とみなす。</summary>
    private bool? SameNameDiffers()
    {
        try
        {
            using var other = File.OpenHandle(_path, FileMode.Open, FileAccess.Read,
                                              FileShare.ReadWrite | FileShare.Delete);
            long len = Length;
            long otherLen = RandomAccess.GetLength(other);
            // 同じファイルなら、開いている方もその長さ以上になっている（先に名前の方を測るので、追記の途中でも逆転しない）。
            // 空で開いたときは先頭・末尾で見比べられないので、これが無いと置き換わりに気づかない
            if (otherLen > RandomAccess.GetLength(_handle)) return true;
            return otherLen < len
                   || !Same(other, 0, _head) || !Same(other, len - _tail.Length, _tail);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool Same(SafeFileHandle handle, long offset, byte[] expected)
    {
        if (expected.Length == 0) return true;
        var now = ReadProbe(handle, offset, expected.Length);
        return now.AsSpan().SequenceEqual(expected);
    }

    private static byte[] ReadTail(SafeFileHandle handle, long length)
    {
        int n = (int)Math.Min(ProbeBytes, length);
        return ReadProbe(handle, length - n, n);
    }

    private static byte[] ReadProbe(SafeFileHandle handle, long offset, int count)
    {
        if (count <= 0) return [];
        var buf = new byte[count];
        int total = 0;
        try
        {
            while (total < count)
            {
                int got = RandomAccess.Read(handle, buf.AsSpan(total), offset + total);
                if (got <= 0) break;
                total += got;
            }
        }
        catch (IOException) { }
        return total == count ? buf : buf[..total];
    }

    public ValueTask DisposeAsync()
    {
        _handle.Dispose();
        return ValueTask.CompletedTask;
    }
}
