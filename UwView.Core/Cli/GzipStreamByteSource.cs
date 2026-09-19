using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Compression;

namespace UwView.Core.Cli;

/// <summary>
/// gz を展開しながら先頭から順に読ませる <see cref="IByteSource"/>（CLI の gz 直接検索用。オーナー指示 2026-09-19）。
///
/// 検索（<see cref="RawGrep"/>）は前へ前へと読むので、展開したものを直近 <see cref="WindowBytes"/> だけ手元に残せば足りる。
/// 少し戻る読み（文字コードの判定・長い行の出力）もその範囲なら応える。範囲より前は読めない（<see cref="InvalidDataException"/>）。
///
/// 展開後の長さは読み終わるまで分からないので、それまで <see cref="Length"/> は <see cref="long.MaxValue"/>。
/// 読み終わったら gz の末尾の記録（CRC32・長さ）と照らす。.NET の展開は途中で切れた gz でも黙って終わるため
/// （<see cref="CompressedInput.VerifyGzipOutput"/>）。
///
/// 展開と CRC は別スレッドで先回りして進め、検索と重ねる（3GB の gz で 2.2 秒 → 1.9 秒。gzip -dc | rg は 1.2 秒）。
/// 展開は <see cref="GzipDecoder"/>（macOS は OS 標準の zlib）。
/// </summary>
internal sealed class GzipStreamByteSource : IByteSource
{
    public const int WindowBytes = 64 << 20;
    private const int Chunk = 4 << 20;

    private readonly string _path;
    private readonly CompressedInput.GzipTrailer? _trailer;
    // 展開済みのかたまり（null の Buffer は終わり。Error があれば展開の失敗）
    private readonly BlockingCollection<(byte[]? Buffer, int Length, Exception? Error)> _ready = new(boundedCapacity: 4);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _producer;
    private readonly byte[] _window = new byte[WindowBytes];
    private long _windowStart;   // _window[0] の位置
    private long _total;         // 展開したバイト数（＝手元の終わりの位置）
    private uint _crc = Crc32.Initial;   // 展開側のスレッドだけが触る
    private long _produced;              // 同上（展開したバイト数）
    private bool _eof;

    public GzipStreamByteSource(string path)
    {
        _path = path;
        _trailer = CompressedInput.ReadTrailer(path);
        var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                                  FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
        _producer = Task.Run(() => Produce(file), CancellationToken.None);
    }

    private void Produce(FileStream file)
    {
        var ct = _stop.Token;
        try
        {
            using (var gz = GzipDecoder.Open(file, out bool verified))
            {
                while (true)
                {
                    byte[] buf = ArrayPool<byte>.Shared.Rent(Chunk);
                    int got = 0;
                    while (got < Chunk)
                    {
                        int n = gz.Read(buf, got, Chunk - got);
                        if (n <= 0) break;
                        got += n;
                    }
                    if (got == 0) { ArrayPool<byte>.Shared.Return(buf); break; }
                    if (!verified) _crc = Crc32.Update(_crc, buf.AsSpan(0, got));
                    _produced += got;
                    _ready.Add((buf, got, null), ct);
                }
                // OS の zlib は読みの中で末尾を照らしている（切れていれば例外）。.NET の展開だけ自分で照らす
                if (!verified && _trailer is { } trailer)
                    CompressedInput.VerifyGzipOutput(trailer, _produced, Crc32.Finish(_crc), SuffixCrc);
            }
            _ready.Add((null, 0, null), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception e)
        {
            try { _ready.Add((null, 0, e), ct); } catch (OperationCanceledException) { }
        }
    }

    public long Length => _eof ? _total : long.MaxValue;

    public int Read(long offset, Span<byte> buffer)
    {
        int written = 0;
        while (written < buffer.Length)
        {
            long at = offset + written;
            if (at < _windowStart)
                throw new InvalidDataException(
                    $"gzip: cannot re-read data more than {WindowBytes >> 20} MB back (a line may be too long)");
            if (at < _total)
            {
                int n = (int)Math.Min(buffer.Length - written, _total - at);
                _window.AsSpan((int)(at - _windowStart), n).CopyTo(buffer[written..]);
                written += n;
                continue;
            }
            if (_eof || !Pull()) break;
        }
        return written;
    }

    /// <summary>1かたまり展開して手元に足す。手元が一杯なら古い半分を捨てる。終わりなら照合して false。</summary>
    private bool Pull()
    {
        int kept = (int)(_total - _windowStart);
        if (kept + Chunk > WindowBytes)
        {
            int keep = WindowBytes / 2;
            _window.AsSpan(kept - keep, keep).CopyTo(_window);
            _windowStart = _total - keep;
            kept = keep;
        }

        var (buffer, length, error) = _ready.Take();
        if (buffer is not null)
        {
            buffer.AsSpan(0, length).CopyTo(_window.AsSpan(kept));
            ArrayPool<byte>.Shared.Return(buffer);
            _total += length;
            return true;
        }

        _eof = true;
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error);
        return false;
    }

    /// <summary>
    /// 展開結果の末尾 length バイトの CRC32（連結 gz の照合用。展開側のスレッドで呼ばれる）。
    /// もう手元に無いので、もう一度展開して数える（連結 gz のときだけ通る、まれな経路）。
    /// </summary>
    private uint SuffixCrc(long length)
    {
        long skip = _produced - length;
        using var file = new FileStream(_path, FileMode.Open, FileAccess.Read,
                                        FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
        using var gz = new GZipStream(file, CompressionMode.Decompress);
        var buf = new byte[1 << 20];
        long pos = 0;
        uint state = Crc32.Initial;
        int n;
        while ((n = gz.Read(buf, 0, buf.Length)) > 0)
        {
            long from = Math.Max(pos, skip);
            if (from < pos + n) state = Crc32.Update(state, buf.AsSpan((int)(from - pos), (int)(pos + n - from)));
            pos += n;
        }
        return Crc32.Finish(state);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        while (_ready.TryTake(out var item))
            if (item.Buffer is not null) ArrayPool<byte>.Shared.Return(item.Buffer);
        try { await _producer; } catch (OperationCanceledException) { }
        while (_ready.TryTake(out var item))
            if (item.Buffer is not null) ArrayPool<byte>.Shared.Return(item.Buffer);
        _ready.Dispose();
        _stop.Dispose();
    }
}
