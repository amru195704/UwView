using System.IO.Compression;
using System.Runtime.InteropServices;

namespace UwView.Core;

/// <summary>
/// 圧縮して書き出す口（uvp の <c>-out</c>・<c>-extract</c>。指示書 WideField v1.7 §11）。
/// 読める 7 形式（gz bz2 xz lzma zst lz4 br）をすべて書ける。
///
/// <list type="bullet">
///   <item>xz・lzma・bz2 は、読むときと同じ OS のライブラリ（liblzma・libbz2。Windows は同梱の liblzma）の書き出し側を呼ぶ。
///         xz は複数スレッド・複数ブロックで書く（後から読むときに並列展開が効く。xz 5.6 以降の既定と同じ）</item>
///   <item>gz・br は .NET 標準、zst・lz4 は .NET 向けのライブラリ（ZstdSharp・K4os）。mac の OS には libzstd・liblz4 が無く、
///         これらはどの OS でも書けるので、そちらを使う</item>
///   <item>強さは各コマンドの既定: gz 6・bz2 9・xz 6・lzma 6・zst 3・lz4 1。br は 6（brotli コマンドの既定 11 は非常に遅い）</item>
///   <item>その形式のライブラリが無い環境（Windows の bz2 など）では、テキストに落とさず <see cref="NotSupportedException"/></item>
/// </list>
/// </summary>
public static class CompressedWriters
{
    public const int GzipLevel = 6, Bzip2Level = 9, XzPreset = 6, ZstdLevel = 3, BrotliQuality = 6;

    /// <summary>
    /// <paramref name="output"/> に <paramref name="kind"/> の形で書くストリーム。閉じると圧縮を締めくくる。
    /// </summary>
    /// <param name="threads">xz を書くスレッド数（0 なら <see cref="CompressedFormats.DecodeThreads"/>＝検索と同じ設定）。</param>
    public static Stream Create(Stream output, CompressedKind kind, bool leaveOpen, int threads = 0)
        => kind switch
        {
            CompressedKind.Gzip => new GZipStream(output, CompressionLevel.Optimal, leaveOpen),   // .NET の Optimal は zlib の 6
            CompressedKind.Brotli => new BrotliStream(output, new BrotliCompressionOptions { Quality = BrotliQuality }, leaveOpen),
            CompressedKind.Zstd => new ZstdSharp.CompressionStream(output, ZstdLevel, leaveOpen: leaveOpen),
            CompressedKind.Lz4 => K4os.Compression.LZ4.Streams.LZ4Stream.Encode(
                output, K4os.Compression.LZ4.LZ4Level.L00_FAST, leaveOpen: leaveOpen),
            // OS に libbz2 が無ければ（Windows）SharpCompress で書く。遅いが書けないよりよい
            //（Windows の全体テストで .bz2 の書き出しが3件失敗・2026-09-28）
            CompressedKind.Bzip2 => (Stream?)SystemBzip2WriteStream.TryCreate(output, leaveOpen)
                                    ?? SharpCompress.Compressors.BZip2.BZip2Stream.Create(
                                        output, SharpCompress.Compressors.CompressionMode.Compress,
                                        decompressConcatenated: false, leaveOpen: leaveOpen),
            CompressedKind.Xz => (Stream?)SystemLzmaWriteStream.TryCreate(output, xz: true, leaveOpen,
                                                                         threads > 0 ? threads : CompressedFormats.DecodeThreads)
                                 ?? throw Unavailable("xz", "liblzma"),
            CompressedKind.Lzma => (Stream?)SystemLzmaWriteStream.TryCreate(output, xz: false, leaveOpen, 1)
                                   ?? throw Unavailable("lzma", "liblzma"),
            _ => throw new NotSupportedException($"cannot write {CompressedFormats.Name(kind)}"),
        };

    private static NotSupportedException Unavailable(string extension, string library)
        => new($"cannot write .{extension} in this environment (the {library} library was not found) / "
               + $"この環境では .{extension} を書き出せません（{library} が見つかりません）");
}

/// <summary>OS 標準の libbz2 で bzip2 に圧縮して書くストリーム（書き出し専用）。</summary>
internal sealed unsafe class SystemBzip2WriteStream : Stream
{
    private const int OutSize = 1 << 20;
    private const int BzRun = 0, BzFinish = 2;
    private const int BzOk = 0, BzRunOk = 1, BzFinishOk = 3, BzStreamEnd = 4;

    private readonly Stream _output;
    private readonly bool _leaveOpen;
    private BzStream* _z;
    private byte* _out;

    private SystemBzip2WriteStream(Stream output, bool leaveOpen, BzStream* z, byte* outBuf)
    {
        _output = output;
        _leaveOpen = leaveOpen;
        _z = z;
        _out = outBuf;
    }

    public static SystemBzip2WriteStream? TryCreate(Stream output, bool leaveOpen)
    {
        if (!NativeCompression.Enabled) return null;
        NativeCompression.EnsureResolver();
        var z = (BzStream*)NativeMemory.AllocZeroed((nuint)sizeof(BzStream));
        try
        {
            if (BZ2_bzCompressInit(z, CompressedWriters.Bzip2Level, 0, 0) == BzOk)
                return new SystemBzip2WriteStream(output, leaveOpen, z, (byte*)NativeMemory.Alloc(OutSize));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
        NativeMemory.Free(z);
        return null;
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (_z is null) throw new ObjectDisposedException(nameof(SystemBzip2WriteStream));
        fixed (byte* inPtr = buffer)
        {
            _z->next_in = inPtr;
            _z->avail_in = (uint)buffer.Length;
            while (_z->avail_in > 0)
            {
                if (Step(BzRun) != BzRunOk) throw new IOException("bzip2: could not compress the data");
            }
        }
    }

    private int Step(int action)
    {
        _z->next_out = _out;
        _z->avail_out = OutSize;
        int rc = BZ2_bzCompress(_z, action);
        int produced = OutSize - (int)_z->avail_out;
        if (produced > 0) _output.Write(new ReadOnlySpan<byte>(_out, produced));
        return rc;
    }

    protected override void Dispose(bool disposing)
    {
        if (_z is not null)
        {
            try
            {
                if (disposing)
                {
                    int rc;
                    do { rc = Step(BzFinish); } while (rc == BzFinishOk);
                    if (rc != BzStreamEnd) throw new IOException("bzip2: could not finish the data");
                    _output.Flush();
                }
            }
            finally
            {
                BZ2_bzCompressEnd(_z);
                NativeMemory.Free(_z);
                NativeMemory.Free(_out);
                _z = null;
                _out = null;
                if (disposing && !_leaveOpen) _output.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    ~SystemBzip2WriteStream() => Dispose(false);

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    [StructLayout(LayoutKind.Sequential)]
    private struct BzStream
    {
        public byte* next_in; public uint avail_in; public uint total_in_lo32; public uint total_in_hi32;
        public byte* next_out; public uint avail_out; public uint total_out_lo32; public uint total_out_hi32;
        public nint state, bzalloc, bzfree, opaque;
    }

    [DllImport(NativeCompression.Bzip2)] private static extern int BZ2_bzCompressInit(BzStream* s, int blockSize100k, int verbosity, int workFactor);
    [DllImport(NativeCompression.Bzip2)] private static extern int BZ2_bzCompress(BzStream* s, int action);
    [DllImport(NativeCompression.Bzip2)] private static extern int BZ2_bzCompressEnd(BzStream* s);
}

/// <summary>
/// OS 標準の liblzma で xz（または生の lzma）に圧縮して書くストリーム（書き出し専用）。
/// xz は複数スレッドのエンコーダで書く（複数ブロックになり、後から読むときに並列展開が効く）。
/// </summary>
internal sealed unsafe class SystemLzmaWriteStream : Stream
{
    private const int OutSize = 1 << 20;
    private const int StreamBytes = 256;          // lzma_stream の大きさ（余裕を持って確保し、0 で埋める）
    private const int LzmaOptionsBytes = 256;     // lzma_options_lzma（同上）
    private const int LzmaOk = 0, LzmaStreamEnd = 1;
    private const int LzmaRun = 0, LzmaFinish = 3;
    private const int CheckCrc64 = 4;

    private readonly Stream _output;
    private readonly bool _leaveOpen;
    private LzmaStream* _z;
    private byte* _out;

    private SystemLzmaWriteStream(Stream output, bool leaveOpen, LzmaStream* z, byte* outBuf)
    {
        _output = output;
        _leaveOpen = leaveOpen;
        _z = z;
        _out = outBuf;
    }

    public static SystemLzmaWriteStream? TryCreate(Stream output, bool xz, bool leaveOpen, int threads)
    {
        if (!NativeCompression.Enabled) return null;
        NativeCompression.EnsureResolver();
        var z = (LzmaStream*)NativeMemory.AllocZeroed(StreamBytes);
        try
        {
            int rc;
            if (xz)
            {
                rc = -1;
                if (threads > 1)
                {
                    var mt = new LzmaMt { threads = (uint)threads, preset = CompressedWriters.XzPreset, check = CheckCrc64 };
                    try { rc = lzma_stream_encoder_mt(z, &mt); }
                    catch (EntryPointNotFoundException) { }   // 並列の口が無い古い liblzma
                }
                if (rc != LzmaOk) rc = lzma_easy_encoder(z, CompressedWriters.XzPreset, CheckCrc64);
            }
            else
            {
                void* options = NativeMemory.AllocZeroed(LzmaOptionsBytes);
                try
                {
                    rc = lzma_lzma_preset(options, CompressedWriters.XzPreset) == 0
                        ? lzma_alone_encoder(z, options) : -1;
                }
                finally { NativeMemory.Free(options); }
            }
            if (rc == LzmaOk)
                return new SystemLzmaWriteStream(output, leaveOpen, z, (byte*)NativeMemory.Alloc(OutSize));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
        NativeMemory.Free(z);
        return null;
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (_z is null) throw new ObjectDisposedException(nameof(SystemLzmaWriteStream));
        fixed (byte* inPtr = buffer)
        {
            _z->next_in = inPtr;
            _z->avail_in = (nuint)buffer.Length;
            while (_z->avail_in > 0)
            {
                if (Step(LzmaRun) != LzmaOk) throw new IOException("xz: could not compress the data");
            }
        }
    }

    private int Step(int action)
    {
        _z->next_out = _out;
        _z->avail_out = OutSize;
        int rc = lzma_code(_z, action);
        int produced = OutSize - (int)_z->avail_out;
        if (produced > 0) _output.Write(new ReadOnlySpan<byte>(_out, produced));
        return rc;
    }

    protected override void Dispose(bool disposing)
    {
        if (_z is not null)
        {
            try
            {
                if (disposing)
                {
                    int rc;
                    do { rc = Step(LzmaFinish); } while (rc == LzmaOk);
                    if (rc != LzmaStreamEnd) throw new IOException("xz: could not finish the data");
                    _output.Flush();
                }
            }
            finally
            {
                lzma_end(_z);
                NativeMemory.Free(_z);
                NativeMemory.Free(_out);
                _z = null;
                _out = null;
                if (disposing && !_leaveOpen) _output.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    ~SystemLzmaWriteStream() => Dispose(false);

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>lzma.h の lzma_stream の頭（後ろの予約欄は 0 のまま）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct LzmaStream
    {
        public byte* next_in; public nuint avail_in; public ulong total_in;
        public byte* next_out; public nuint avail_out; public ulong total_out;
    }

    /// <summary>lzma/container.h の lzma_mt（全 128 バイト）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct LzmaMt
    {
        public uint flags; public uint threads; public ulong block_size;
        public uint timeout; public uint preset; public nint filters;
        public int check; public int reserved_enum1; public int reserved_enum2; public int reserved_enum3;
        public uint reserved_int1; public uint reserved_int2; public uint reserved_int3; public uint reserved_int4;
        public ulong memlimit_threading; public ulong memlimit_stop;
        public ulong reserved_int7; public ulong reserved_int8;
        public nint reserved_ptr1, reserved_ptr2, reserved_ptr3, reserved_ptr4;
    }

    [DllImport(NativeCompression.Lzma)] private static extern int lzma_stream_encoder_mt(LzmaStream* s, LzmaMt* options);
    [DllImport(NativeCompression.Lzma)] private static extern int lzma_easy_encoder(LzmaStream* s, uint preset, int check);
    [DllImport(NativeCompression.Lzma)] private static extern byte lzma_lzma_preset(void* options, uint preset);
    [DllImport(NativeCompression.Lzma)] private static extern int lzma_alone_encoder(LzmaStream* s, void* options);
    [DllImport(NativeCompression.Lzma)] private static extern int lzma_code(LzmaStream* s, int action);
    [DllImport(NativeCompression.Lzma)] private static extern void lzma_end(LzmaStream* s);
}
