using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Compression;

namespace UwView.Core.Cli;

/// <summary>
/// 圧縮ファイルを展開しながら先頭から順に読ませる <see cref="IByteSource"/>（CLI の直接検索用）。
/// gz（オーナー指示 2026-09-19）に加え、v1.7.1 で bzip2・xz・lzma・zstd も同じ口で読む。
///
/// 検索（<see cref="RawGrep"/>）は前へ前へと読むので、展開したものを直近 <see cref="WindowBytes"/> だけ手元に残せば足りる。
/// 少し戻る読み（文字コードの判定・長い行の出力）もその範囲なら応える。範囲より前は読めない（<see cref="InvalidDataException"/>）。
///
/// 手元は<b>展開したかたまりを並べたまま</b>持ち、読みはそこから直接写す。以前は 64MB の窓に写し直し、
/// 窓が一杯になるたびに後ろ半分を詰め直していたので、展開したものを実質 2.5 回余計に写していた。
/// Mac の実機では目立たないが、Linux の VM（2 vCPU）では lz4 で rg -z に 1/1.54 と負ける原因になった
///（展開が速い形式ほど、写す手間がそのまま表に出る。2026-09-25 の切り分け）。
///
/// 展開後の長さは読み終わるまで分からないので、それまで <see cref="Length"/> は <see cref="long.MaxValue"/>。
/// 読み終わったら gz の末尾の記録（CRC32・長さ）と照らす。.NET の展開は途中で切れた gz でも黙って終わるため
/// （<see cref="CompressedInput.VerifyGzipOutput"/>）。
///
/// 展開と CRC は別スレッドで先回りして進め、検索と重ねる（3GB の gz で 2.2 秒 → 1.9 秒。gzip -dc | rg は 1.2 秒）。
/// 展開は <see cref="CompressedFormats.Open(string, CompressedKind)"/>（gz は macOS なら OS 標準の zlib）。
/// gz 以外は形式そのものが切れ目を知っている（xz はフッター、bzip2 はブロックの CRC、zstd はフレームの終端）ので、
/// 末尾の照合はライブラリに任せる。
/// </summary>
internal sealed class CompressedStreamByteSource : IByteSource
{
    public const int WindowBytes = 64 << 20;
    // 展開のかたまり。小さめにして、展開したものがキャッシュに残っているうちに探す
    private const int Chunk = 1 << 20;

    private readonly string _path;
    private readonly CompressedInput.GzipTrailer? _trailer;
    // 展開済みのかたまり（null の Buffer は終わり。Error があれば展開の失敗）
    private readonly BlockingCollection<(byte[]? Buffer, int Length, Exception? Error)> _ready = new(boundedCapacity: 8);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _producer;
    // 手元のかたまり（古い順）。合計が WindowBytes を超えたら古いものから返す
    private readonly List<(long Start, byte[] Buffer, int Length)> _held = [];
    private long _heldBytes;
    private long _windowStart;   // 手元の最初のかたまりの位置
    private long _total;         // 展開したバイト数（＝手元の終わりの位置）
    private uint _crc = Crc32.Initial;   // 展開側のスレッドだけが触る
    private long _produced;              // 同上（展開したバイト数）
    private bool _eof;

    private readonly CompressedKind _kind;

    private readonly int _threads;

    /// <param name="threads">展開に使うスレッド数（0 なら既定＝段階1の設定）。</param>
    public CompressedStreamByteSource(string path, CompressedKind kind = CompressedKind.Gzip, int threads = 0)
    {
        _path = path;
        _kind = kind;
        _threads = threads;
        _trailer = kind == CompressedKind.Gzip ? CompressedInput.ReadTrailer(path) : null;
        var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                                  FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
        _producer = Task.Run(() => Produce(file), CancellationToken.None);
    }

    private void Produce(FileStream file)
    {
        var ct = _stop.Token;
        try
        {
            bool verified = true;    // gz 以外はライブラリが照らす
            using (var gz = _kind == CompressedKind.Gzip
                       ? GzipDecoder.Open(file, out verified)
                       : CompressedFormats.Open(file, _kind, _path, _threads))
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
                    $"{CompressedFormats.Name(_kind)}: cannot re-read data more than {WindowBytes >> 20} MB back (a line may be too long)");
            if (at < _total)
            {
                // 後ろから探す（検索はほぼ最後のかたまりを読んでいる）
                int i = _held.Count - 1;
                while (_held[i].Start > at) i--;
                var (start, held, length) = _held[i];
                int from = (int)(at - start);
                int n = Math.Min(buffer.Length - written, length - from);
                held.AsSpan(from, n).CopyTo(buffer[written..]);
                written += n;
                continue;
            }
            if (_eof || !Pull()) break;
        }
        return written;
    }

    /// <summary>1かたまり展開して手元に足す。手元が一杯なら古いかたまりから返す。終わりなら照合して false。</summary>
    private bool Pull()
    {
        var (buffer, length, error) = _ready.Take();
        if (buffer is not null)
        {
            _held.Add((_total, buffer, length));
            _heldBytes += length;
            _total += length;
            while (_held.Count > 1 && _heldBytes - _held[0].Length >= WindowBytes)
            {
                _heldBytes -= _held[0].Length;
                ArrayPool<byte>.Shared.Return(_held[0].Buffer);
                _held.RemoveAt(0);
            }
            _windowStart = _held[0].Start;
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
        foreach (var (_, buffer, _) in _held) ArrayPool<byte>.Shared.Return(buffer);
        _held.Clear();
    }
}
