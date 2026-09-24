using System.IO.Compression;

namespace UwView.Core;

/// <summary>
/// 圧縮形式の見分けと、展開しながら読む口（v1.7.1・指示書_開発部_圧縮形式サポート_2026-09-24）。
///
/// <b>外部コマンドは呼ばない。</b>ripgrep は <c>-z</c> で <c>gzip</c>・<c>bzip2</c> などの
/// コマンドへパイプするが、PATH に無ければ<b>黙って飛ばす</b>。こちらは自分で展開するので、
/// 入っている環境かどうかに左右されないし、平文を一時ファイルに書かない。
///
/// 見分けは<b>中身の先頭（マジック）</b>で行う。名前は当てにならない——ログの回転で
/// <c>app.log.1</c> のような名前の gz や bz2 が普通にある。
/// ただし <b>生の lzma と brotli にはマジックが無い</b>ので、この2つだけは拡張子も見る。
///
/// 対応は第1弾の bzip2・xz・lzma・zstd・lz4・brotli（オーナー決定 2026-09-24）。
/// 実測（50MB）で lz4・brotli は外部コマンドより速く、bzip2・xz は 1/2.4〜1/2.8。
/// </summary>
public static class CompressedFormats
{
    /// <summary>先頭を見て形式を決める（分からなければ <see cref="CompressedKind.None"/>）。</summary>
    public static CompressedKind Sniff(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            Span<byte> head = stackalloc byte[16];
            int got = file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            return Sniff(head[..got], path);
        }
        catch (IOException) { return CompressedKind.None; }
        catch (UnauthorizedAccessException) { return CompressedKind.None; }
    }

    /// <summary>読み込み済みの先頭から形式を決める。</summary>
    public static CompressedKind Sniff(ReadOnlySpan<byte> head, string path)
    {
        if (head.Length >= 2 && head[0] == 0x1F && head[1] == 0x8B) return CompressedKind.Gzip;
        if (head.Length >= 4 && head[0] == 'P' && head[1] == 'K' && head[2] == 3 && head[3] == 4)
            return CompressedKind.Zip;
        if (head.Length >= 4 && head[0] == 'B' && head[1] == 'Z' && head[2] == 'h'
            && head[3] is >= (byte)'1' and <= (byte)'9')
            return CompressedKind.Bzip2;
        if (head.Length >= 6 && head[0] == 0xFD && head[1] == '7' && head[2] == 'z'
            && head[3] == 'X' && head[4] == 'Z' && head[5] == 0x00)
            return CompressedKind.Xz;
        if (head.Length >= 4 && head[0] == 0x28 && head[1] == 0xB5 && head[2] == 0x2F && head[3] == 0xFD)
            return CompressedKind.Zstd;
        if (head.Length >= 4 && head[0] == 0x04 && head[1] == 0x22 && head[2] == 0x4D && head[3] == 0x18)
            return CompressedKind.Lz4;

        // 生の lzma にはマジックが無い。名前が .lzma で、ヘッダーとして筋が通るときだけ受ける
        if (Extension(path, ".lzma") && LooksLikeLzma(head)) return CompressedKind.Lzma;
        // brotli にもマジックが無い。名前が .br なら brotli として試す（展開できなければ断る）
        if (Extension(path, ".br") && head.Length > 0) return CompressedKind.Brotli;
        return CompressedKind.None;
    }

    /// <summary>
    /// 生 lzma のヘッダーらしいか（プロパティ1B＋辞書サイズ4B＋展開後サイズ8B）。
    ///
    /// プロパティは <c>(pb*5 + lp)*9 + lc</c> で最大 224。辞書サイズは 4KiB〜1.5GiB の範囲。
    /// 展開後サイズは「不明（全部 0xFF）」か、現実的な大きさ。
    /// </summary>
    private static bool LooksLikeLzma(ReadOnlySpan<byte> head)
    {
        if (head.Length < 13 || head[0] > 224) return false;
        uint dictionary = (uint)(head[1] | (head[2] << 8) | (head[3] << 16) | (head[4] << 24));
        if (dictionary is < (1 << 12) or > (1 << 30)) return false;
        ulong size = BitConverter.ToUInt64(head[5..13]);
        return size == ulong.MaxValue || size < (1UL << 48);
    }

    private static bool Extension(string path, string extension)
        => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 展開に使ってよいスレッド数（xz の並列展開が使う）。段階1の設定（UVF_MAX_THREADS / UVP_MAX_THREADS・
    /// 設定の MaxThreads）をそのまま入れる——この機能のための別の設定は作らない（指示書 §4.2）。
    /// 入口（uvf / uvp の CLI・画面）が起動時に入れる。入れなければ既定（8、論理プロセッサ数まで）。
    /// </summary>
    public static int DecodeThreads { get; set; } = ThreadBudget.Resolve(null, 0);

    /// <summary>展開に複数スレッドを使う形式か（いまは xz だけ。ほかは展開スレッド数を見ない）。</summary>
    public static bool UsesDecodeThreads(CompressedKind kind) => kind == CompressedKind.Xz;

    /// <summary>
    /// 何本かを同時に展開するときの、xz の部品1本あたりのスレッド数（全体の本数を xz の部品の数で割る）。
    /// ほかの形式は1スレッドで展開するので数に入れない。
    /// </summary>
    public static int DecodeThreadsFor(IEnumerable<CompressedKind> kinds, int threads)
    {
        int parallelDecoders = kinds.Count(UsesDecodeThreads);
        return Math.Max(1, threads / Math.Max(1, Math.Min(threads, parallelDecoders)));
    }

    /// <summary>展開しながら読むストリーム（前方専用）。</summary>
    public static Stream Open(string path, CompressedKind kind)
    {
        var file = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
        try { return Open(file, kind, path); }
        catch { file.Dispose(); throw; }
    }

    /// <summary>
    /// 開いてあるストリームから展開して読む（<paramref name="path"/> は文言用）。
    /// gz 以外は、展開ライブラリの失敗を <see cref="InvalidDataException"/> にそろえて返す
    /// （ライブラリごとに例外の型がまちまちで、呼び出し側が「切れている」と言えなくなるため）。
    /// </summary>
    /// <param name="threads">展開に使うスレッド数（0 なら <see cref="DecodeThreads"/>）。複数ファイルを同時に
    /// 探すときは、ファイルの数で割った本数を渡す（掛け算でコアを取り合わないように）。</param>
    public static Stream Open(Stream compressed, CompressedKind kind, string path, int threads = 0)
    {
        if (kind == CompressedKind.Gzip) return OpenRaw(compressed, kind, path, threads);
        // 開く時点でヘッダーを読む形式もある（xz・lzma）。そこでの失敗も同じくそろえる
        try { return new DecodeFailureStream(OpenRaw(compressed, kind, path, threads), Name(kind)); }
        catch (Exception e) when (DecodeFailureStream.IsDecodeFailure(e))
        {
            throw new InvalidDataException($"{Name(kind)}: {e.Message}", e);
        }
    }

    private static Stream OpenRaw(Stream compressed, CompressedKind kind, string path, int threads)
        => kind switch
        {
            CompressedKind.Gzip => GzipDecoder.Open(compressed, out _),
            // bzip2・xz・lzma は OS 標準のライブラリがあればそれで（bzip2 / xz コマンドと同じ速さ）。
            // 無ければ SharpCompress（Windows など）。連結された bz2・xz も続けて読む
            CompressedKind.Bzip2 => (Stream?)SystemBzip2Stream.TryCreate(compressed)
                                    ?? SharpCompress.Compressors.BZip2.BZip2Stream.Create(
                                        compressed, SharpCompress.Compressors.CompressionMode.Decompress, true, false, false),
            // xz は複数ブロックなら並列で展開する（1ブロックなら liblzma が単スレッドで流す）
            CompressedKind.Xz => (Stream?)SystemLzmaStream.TryCreate(compressed, xz: true,
                                                                   threads > 0 ? threads : DecodeThreads)
                                 ?? new SharpCompress.Compressors.Xz.XZStream(compressed),
            CompressedKind.Lzma => (Stream?)SystemLzmaStream.TryCreate(compressed, xz: false) ?? OpenLzma(compressed),
            CompressedKind.Zstd => new ZstdSharp.DecompressionStream(compressed),
            CompressedKind.Lz4 => K4os.Compression.LZ4.Streams.LZ4Stream.Decode(compressed),
            CompressedKind.Brotli => new CheckedBrotliStream(compressed),
            _ => throw new NotSupportedException($"cannot decompress as {Name(kind)}: {path}"),
        };

    /// <summary>生 lzma（13 バイトのヘッダーのあとに本体）。</summary>
    private static Stream OpenLzma(Stream compressed)
    {
        var properties = new byte[5];
        compressed.ReadExactly(properties);
        Span<byte> size = stackalloc byte[8];
        compressed.ReadExactly(size);
        long expanded = BitConverter.ToInt64(size);
        // 0xFF…FF は「長さ不明」。その場合は終端まで読む
        return SharpCompress.Compressors.LZMA.LzmaStream.Create(
            properties, compressed, -1, expanded < 0 ? -1 : expanded, false);
    }

    /// <summary>表示に使う形式名。</summary>
    public static string Name(CompressedKind kind) => kind switch
    {
        CompressedKind.Gzip => "gzip",
        CompressedKind.Zip => "zip",
        CompressedKind.Bzip2 => "bzip2",
        CompressedKind.Xz => "xz",
        CompressedKind.Lzma => "lzma",
        CompressedKind.Zstd => "zstd",
        CompressedKind.Lz4 => "lz4",
        CompressedKind.Brotli => "brotli",
        _ => "?",
    };

    /// <summary>
    /// その形式をどれで展開するか（"OS" = OS 標準のライブラリ、"managed" = .NET のライブラリ）。
    /// mac・Linux で bzip2・xz・lzma が "managed" に落ちていたら、速さが 1/2.4〜1/2.8 になる（気づけるように出す）。
    /// </summary>
    public static string DecoderOf(CompressedKind kind)
    {
        using var empty = new MemoryStream();
        Stream? native = kind switch
        {
            CompressedKind.Bzip2 => SystemBzip2Stream.TryCreate(empty),
            CompressedKind.Xz => SystemLzmaStream.TryCreate(empty, xz: true, DecodeThreads),
            CompressedKind.Lzma => SystemLzmaStream.TryCreate(empty, xz: false),
            _ => null,
        };
        string name = native switch
        {
            null => "managed",
            SystemLzmaStream { Threads: > 1 } lzma => $"OS×{lzma.Threads}",   // 並列デコーダ
            _ => "OS",
        };
        native?.Dispose();
        return name;
    }

    /// <summary>ファイルの形式名（先頭で分かれば）。分からなければ gzip（従来の文言に合わせる）。</summary>
    public static string NameOf(string path)
        => Sniff(path) is var kind && kind != CompressedKind.None ? Name(kind) : "gzip";

    /// <summary>この形式が「1つのテキストを圧縮したもの」か（zip のような書庫ではない）。</summary>
    public static bool IsSingleStream(CompressedKind kind)
        => kind is CompressedKind.Gzip or CompressedKind.Bzip2 or CompressedKind.Xz
                 or CompressedKind.Lzma or CompressedKind.Zstd or CompressedKind.Lz4 or CompressedKind.Brotli;

    /// <summary>その形式でふつうに使う拡張子（展開後の名前を作るときに外す）。</summary>
    public static string[] Extensions(CompressedKind kind) => kind switch
    {
        CompressedKind.Gzip => [".gz"],
        CompressedKind.Bzip2 => [".bz2", ".bzip2"],
        CompressedKind.Xz => [".xz"],
        CompressedKind.Lzma => [".lzma"],
        CompressedKind.Zstd => [".zst", ".zstd"],
        CompressedKind.Lz4 => [".lz4"],
        CompressedKind.Brotli => [".br"],
        _ => [],
    };

    /// <summary>展開したものを置くときの名前（<c>app.log.bz2</c> → <c>app.log</c>）。</summary>
    public static string PlainPath(string path, CompressedKind kind)
    {
        foreach (string extension in Extensions(kind))
            if (Extension(path, extension)) return path[..^extension.Length];
        return path + ".txt";
    }
}

/// <summary>展開ライブラリの失敗を <see cref="InvalidDataException"/> にそろえる薄い包み（読み取り専用）。</summary>
internal sealed class DecodeFailureStream(Stream inner, string format) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        try { return inner.Read(buffer); }
        catch (Exception e) when (IsDecodeFailure(e)) { throw Wrap(e); }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        try { return await inner.ReadAsync(buffer, ct); }
        catch (Exception e) when (IsDecodeFailure(e)) { throw Wrap(e); }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    /// <summary>
    /// 展開の失敗か。中止・メモリ不足・入出力の失敗（ディスク・権限）は、そのまま通す。
    /// 「思ったより早く終わった」（<see cref="EndOfStreamException"/>）は切れた入力なので失敗に数える。
    /// </summary>
    internal static bool IsDecodeFailure(Exception e)
        => e is EndOfStreamException
           || e is not (OperationCanceledException or OutOfMemoryException or IOException
                        or UnauthorizedAccessException or InvalidDataException);

    private InvalidDataException Wrap(Exception e) => new($"{format}: {e.Message}", e);

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// brotli を展開して読む（途中で切れていれば例外）。
///
/// .NET の <see cref="BrotliStream"/> は、入力が途中で終わっても<b>黙って終わる</b>
/// （gz の <see cref="GZipStream"/> と同じ癖）。それでは切れたファイルを最後まで読めたように見せてしまう。
/// 下の <see cref="BrotliDecoder"/> を直に回し、「終わり」の印（<see cref="System.Buffers.OperationStatus.Done"/>）に
/// 届かないまま入力が尽きたら <see cref="InvalidDataException"/> を出す。
/// </summary>
internal sealed class CheckedBrotliStream(Stream input) : Stream
{
    private BrotliDecoder _decoder = new();
    private readonly byte[] _in = new byte[1 << 16];
    private int _start, _end;
    private bool _inputEnded, _done;

    public override int Read(Span<byte> buffer)
    {
        if (_done || buffer.IsEmpty) return 0;
        while (true)
        {
            var status = _decoder.Decompress(_in.AsSpan(_start, _end - _start), buffer, out int consumed, out int written);
            _start += consumed;
            switch (status)
            {
                case System.Buffers.OperationStatus.InvalidData:
                    throw new InvalidDataException("brotli: the data is not valid brotli");
                case System.Buffers.OperationStatus.Done:
                    _done = true;
                    return written;
            }
            if (written > 0) return written;

            // 足りないのは入力。尽きていれば、終わりの印に届かないまま切れている
            if (_inputEnded) throw new InvalidDataException("brotli: the stream ended before its end mark (cut short)");
            if (_start == _end) _start = _end = 0;
            else if (_start > 0)
            {
                Buffer.BlockCopy(_in, _start, _in, 0, _end - _start);
                _end -= _start;
                _start = 0;
            }
            int n = input.Read(_in, _end, _in.Length - _end);
            if (n == 0) _inputEnded = true;
            else _end += n;
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _decoder.Dispose();
            input.Dispose();
        }
        base.Dispose(disposing);
    }
}
