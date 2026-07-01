using UwView.Core;

namespace UwView.Core.Tests;

/// <summary>テスト用のインメモリ IByteSource。</summary>
internal sealed class MemoryByteSource(byte[] data) : IByteSource
{
    public long Length => data.Length;

    public int Read(long offset, Span<byte> buffer)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (offset >= data.Length || buffer.Length == 0) return 0;
        int count = (int)Math.Min(data.Length - offset, buffer.Length);
        data.AsSpan((int)offset, count).CopyTo(buffer);
        return count;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
