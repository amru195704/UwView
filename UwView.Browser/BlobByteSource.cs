using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using UwView.Core;

namespace UwView.Browser;

[SupportedOSPlatform("browser")]
internal static partial class BlobInterop
{
    [JSImport("pickFiles", "blobRead")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> PickFilesAsync();

    // JSImport は Promise<byte[]> 非対応のため、トークン発行(async) → 取り出し(sync) の2段方式
    [JSImport("readSliceBegin", "blobRead")]
    [return: JSMarshalAs<JSType.Promise<JSType.Number>>]
    internal static partial Task<int> ReadSliceBeginAsync(int id, double offset, int length);

    [JSImport("readSliceTake", "blobRead")]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    internal static partial byte[] ReadSliceTake(int token);

    [JSImport("closeFile", "blobRead")]
    internal static partial void CloseFile(int id);

    internal static async Task<byte[]> ReadSliceAsync(int id, double offset, int length)
        => ReadSliceTake(await ReadSliceBeginAsync(id, offset, length));
}

/// <summary>
/// Browser(WASM) 用 IByteSource（§4.1 BlobByteSource）。
/// JS 側の File(Blob) を blob.slice でランダム読みする。全体はメモリに載せない。
/// - ReadAsync: async 経路（索引構築・文字コード判定用）。不足チャンクを await で取得。
/// - Read: 同期経路（描画用）。キャッシュ済み範囲のみ返し、未取得チャンクは
///   裏で取得を発火して DataArrived で再描画を促す（§4.1「結果をキャッシュしてから描画」）。
/// </summary>
[SupportedOSPlatform("browser")]
public sealed class BlobByteSource : IByteSource, INotifyDataArrived
{
    private const int ChunkSize = 256 * 1024;
    private const int MaxChunks = 64; // 合計 16MB（WASM は本体非常駐を厳守 §9）

    private readonly int _jsId;
    private readonly LruCache<long, byte[]> _chunks = new(MaxChunks);
    private readonly Dictionary<long, Task<byte[]>> _inflight = [];
    private bool _disposed;

    public long Length { get; }

    public event EventHandler? DataArrived;

    public BlobByteSource(int jsFileId, long length)
    {
        _jsId = jsFileId;
        Length = length;
    }

    public int Read(long offset, Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (offset >= Length || buffer.Length == 0) return 0;

        int want = (int)Math.Min(Length - offset, buffer.Length);
        int copied = 0;
        while (copied < want)
        {
            long pos = offset + copied;
            long chunkIdx = pos / ChunkSize;
            if (!_chunks.TryGet(chunkIdx, out var chunk))
            {
                // 未取得: 裏で取得を発火し、揃った分だけ返す（描画側は DataArrived で再描画）
                _ = FetchChunkAsync(chunkIdx, notify: true);
                break;
            }
            int within = (int)(pos - chunkIdx * ChunkSize);
            int n = Math.Min(chunk.Length - within, want - copied);
            if (n <= 0) break;
            chunk.AsSpan(within, n).CopyTo(buffer.Slice(copied, n));
            copied += n;
        }
        return copied;
    }

    public async ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (offset >= Length || buffer.Length == 0) return 0;

        int want = (int)Math.Min(Length - offset, buffer.Length);
        int copied = 0;
        while (copied < want)
        {
            ct.ThrowIfCancellationRequested();
            long pos = offset + copied;
            long chunkIdx = pos / ChunkSize;
            if (!_chunks.TryGet(chunkIdx, out var chunk))
                chunk = await FetchChunkAsync(chunkIdx, notify: false);

            int within = (int)(pos - chunkIdx * ChunkSize);
            int n = Math.Min(chunk.Length - within, want - copied);
            if (n <= 0) break; // 末尾の短チャンク
            chunk.AsMemory(within, n).CopyTo(buffer.Slice(copied, n));
            copied += n;
        }
        return copied;
    }

    private Task<byte[]> FetchChunkAsync(long chunkIdx, bool notify)
    {
        if (_inflight.TryGetValue(chunkIdx, out var existing))
            return existing;

        var task = FetchCoreAsync(chunkIdx, notify);
        _inflight[chunkIdx] = task;
        return task;
    }

    private async Task<byte[]> FetchCoreAsync(long chunkIdx, bool notify)
    {
        try
        {
            long start = chunkIdx * ChunkSize;
            int len = (int)Math.Min(ChunkSize, Length - start);
            byte[] data = await BlobInterop.ReadSliceAsync(_jsId, start, len);
            _chunks.Set(chunkIdx, data);
            if (notify)
                DataArrived?.Invoke(this, EventArgs.Empty);
            return data;
        }
        finally
        {
            _inflight.Remove(chunkIdx);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _chunks.Clear();
        BlobInterop.CloseFile(_jsId); // JS 側の File 参照を解放
        return ValueTask.CompletedTask;
    }
}
