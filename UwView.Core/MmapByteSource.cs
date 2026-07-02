using System.IO.MemoryMappedFiles;
using Microsoft.Win32.SafeHandles;

namespace UwView.Core;

/// <summary>
/// Desktop 用 mmap 実装（★優先）。ファイル全体を読み取り専用ビューにマップし、
/// AcquirePointer の unsafe ポインタで高速コピーする。
/// FileShare.ReadWrite で開くため、他プロセスが書き込み中のログでも開ける（Tail §11-③）。
/// <see cref="TryExpand"/> でファイル成長分を再マップする（旧ビューは write ロック下で解放）。
/// 既知リスク: 外部からファイルが切り詰められるとアクセス時に落ちうる（Unix では SIGBUS）。
/// </summary>
public sealed unsafe class MmapByteSource : IByteSource
{
    private readonly string _path;
    private readonly ReaderWriterLockSlim _lock = new();

    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _view;
    private SafeMemoryMappedViewHandle? _handle;
    private byte* _ptr;
    private long _length;
    private bool _disposed;

    public long Length => Volatile.Read(ref _length);

    public MmapByteSource(string path)
    {
        _path = path;
        long len = new FileInfo(path).Length;
        if (len > 0)
            Map(len);
        _length = len;
    }

    /// <summary>len バイトぶんの新しいマップを張り、旧マップを解放する（呼び出し側でロック管理）。</summary>
    private void Map(long len)
    {
        // 書き込み中のプロセスと共存できるよう FileShare.ReadWrite で開く
        var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        MemoryMappedFile mmf;
        try
        {
            // mapName は必ず null（Unix 非対応のため）。leaveOpen:false で FileStream も mmf が破棄
            mmf = MemoryMappedFile.CreateFromFile(
                fs, null, len, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: false);
        }
        catch
        {
            fs.Dispose();
            throw;
        }

        var view = mmf.CreateViewAccessor(0, len, MemoryMappedFileAccess.Read);
        var handle = view.SafeMemoryMappedViewHandle;
        byte* p = null;
        handle.AcquirePointer(ref p);

        Unmap();
        _mmf = mmf;
        _view = view;
        _handle = handle;
        _ptr = p + view.PointerOffset;
    }

    private void Unmap()
    {
        if (_handle is not null && _ptr is not null)
        {
            _handle.ReleasePointer();
            _ptr = null;
        }
        _view?.Dispose();
        _mmf?.Dispose();
        _view = null;
        _mmf = null;
        _handle = null;
    }

    /// <summary>
    /// ファイルが伸びていれば再マップして true を返す（Tail 用・ポーリングから呼ぶ）。
    /// 縮んだ場合は何もしない（切り詰めは非対応）。
    /// </summary>
    public bool TryExpand()
    {
        if (_disposed) return false;
        long newLen;
        try { newLen = new FileInfo(_path).Length; }
        catch { return false; }
        if (newLen <= Length) return false;

        _lock.EnterWriteLock();
        try
        {
            if (_disposed) return false;
            Map(newLen);
            Volatile.Write(ref _length, newLen);
            return true;
        }
        finally { _lock.ExitWriteLock(); }
    }

    public int Read(long offset, Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));

        _lock.EnterReadLock();
        try
        {
            long len = _length;
            if (offset >= len || buffer.Length == 0 || _ptr is null)
                return 0;

            int count = (int)Math.Min(len - offset, buffer.Length);
            new ReadOnlySpan<byte>(_ptr + offset, count).CopyTo(buffer);
            return count;
        }
        finally { _lock.ExitReadLock(); }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _lock.EnterWriteLock();
        try
        {
            _disposed = true;
            Unmap();
        }
        finally { _lock.ExitWriteLock(); }
        _lock.Dispose();
        return ValueTask.CompletedTask;
    }
}
