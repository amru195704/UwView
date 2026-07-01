using System.IO.MemoryMappedFiles;
using Microsoft.Win32.SafeHandles;

namespace UwView.Core;

/// <summary>
/// Desktop 用 mmap 実装（★優先）。ファイル全体を読み取り専用ビューにマップし、
/// AcquirePointer の unsafe ポインタで高速コピーする。
/// 読み取り専用のためマップ中はファイルをロックする（ビューアなので許容）。
/// 既知リスク: 外部からファイルが切り詰められるとアクセス時に落ちうる（Unix では SIGBUS）。
/// </summary>
public sealed unsafe class MmapByteSource : IByteSource
{
    private readonly MemoryMappedFile? _mmf;
    private readonly MemoryMappedViewAccessor? _view;
    private readonly SafeMemoryMappedViewHandle? _handle;
    private byte* _ptr;
    private bool _disposed;

    public long Length { get; }

    public MmapByteSource(string path)
    {
        Length = new FileInfo(path).Length;

        if (Length == 0)
            return; // 空ファイルは mmap 不可（ビューも張れない）

        // mapName は必ず null（Unix 非対応のため）。
        _mmf = MemoryMappedFile.CreateFromFile(
            path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);

        _view = _mmf.CreateViewAccessor(0, Length, MemoryMappedFileAccess.Read);
        _handle = _view.SafeMemoryMappedViewHandle;
        byte* p = null;
        _handle.AcquirePointer(ref p);
        _ptr = p + _view.PointerOffset;
    }

    public int Read(long offset, Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (offset >= Length || buffer.Length == 0)
            return 0;

        int count = (int)Math.Min(Length - offset, buffer.Length);
        new ReadOnlySpan<byte>(_ptr + offset, count).CopyTo(buffer);
        return count;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;
        _disposed = true;

        if (_handle is not null && _ptr is not null)
        {
            _handle.ReleasePointer();
            _ptr = null;
        }
        _view?.Dispose();
        _mmf?.Dispose();
        return ValueTask.CompletedTask;
    }
}
