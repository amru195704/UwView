using System.Runtime.InteropServices;

namespace UwView.Core;

/// <summary>
/// OS 標準の展開ライブラリを直接呼ぶための入口（zlib・bzip2・liblzma・liblz4・libzstd）。
///
/// ripgrep の <c>-z</c> は <c>bzip2</c>・<c>xz</c> コマンドへパイプする。そのコマンドの中身は
/// このライブラリなので、<b>同じものを直接呼べば同じ速さで展開できる</b>（外部コマンドは呼ばない）。
/// .NET 向けの展開ライブラリ（SharpCompress）は bzip2 で 1/2.4、xz で 1/2.8 の速さだった
/// （2026-09-24 実測・オーナー決定「mac・Linux は OS のライブラリに切り替える」）。
///
/// 見つからない環境（Windows など）では null を返し、呼び出し側は今までの展開に戻す。
/// 計測で比べたいときは <c>UWVIEW_SYSTEM_DECODERS=0</c> で OS のライブラリを使わない。
///
/// <see cref="NativeLibrary.SetDllImportResolver"/> は<b>1つのアセンブリに1回しか</b>登録できないので、
/// 探し方はここにまとめる（zlib もここで探す）。
/// </summary>
internal static class NativeCompression
{
    public const string Zlib = "libz";
    public const string Bzip2 = "libbz2";
    public const string Lzma = "liblzma";
    public const string Lz4 = "liblz4";
    public const string Zstd = "libzstd";

    /// <summary>bzip2・lzma で OS のライブラリを使うか（既定は使う。0 で使わない）。</summary>
    public static bool Enabled => Environment.GetEnvironmentVariable("UWVIEW_SYSTEM_DECODERS") != "0";

    private static readonly Dictionary<string, nint> Loaded = [];
    private static int _resolverSet;

    public static void EnsureResolver()
    {
        if (Interlocked.Exchange(ref _resolverSet, 1) != 0) return;
        try { NativeLibrary.SetDllImportResolver(typeof(NativeCompression).Assembly, Resolve); }
        catch (InvalidOperationException) { /* ほかの経路で登録済み */ }
    }

    private static nint Resolve(string name, System.Reflection.Assembly assembly, DllImportSearchPath? path)
    {
        string[]? candidates = name switch
        {
            Zlib => OperatingSystem.IsMacOS() ? ["/usr/lib/libz.dylib", "libz.dylib"] : ["libz.so.1", "libz.so"],
            Bzip2 => OperatingSystem.IsMacOS()
                ? ["/usr/lib/libbz2.dylib", "libbz2.dylib"]
                : ["libbz2.so.1", "libbz2.so.1.0", "libbz2.so"],
            Lzma => OperatingSystem.IsMacOS()
                ? ["/usr/lib/liblzma.dylib", "liblzma.5.dylib"]
                : ["liblzma.so.5", "liblzma.so"],
            // lz4 は Linux だけ（mac の OS には無い。mac は .NET 向けの展開で lz4 コマンドと互角だった）
            Lz4 => OperatingSystem.IsLinux() ? ["liblz4.so.1", "liblz4.so"] : null,
            // zstd も Linux だけ（mac の OS には無い）。.uwvz の中身の圧縮・展開に使う
            Zstd => OperatingSystem.IsLinux() ? ["libzstd.so.1", "libzstd.so"] : null,
            _ => null,
        };
        if (candidates is null) return 0;
        lock (Loaded)
        {
            if (Loaded.TryGetValue(name, out nint cached)) return cached;
            nint handle = 0;
            foreach (string candidate in candidates)
                if (NativeLibrary.TryLoad(candidate, out handle)) break;
            Loaded[name] = handle;
            return handle;
        }
    }
}

/// <summary>
/// OS 標準の libbz2 で bzip2 を展開する読み取り専用ストリーム。
/// 連結された bz2（pbzip2 などが作る）も続けて読む。終わりの印に届く前に入力が尽きたら例外。
/// </summary>
internal sealed unsafe class SystemBzip2Stream : Stream
{
    private const int InputSize = 1 << 20;
    private const int BzOk = 0, BzStreamEnd = 4;

    private readonly Stream _input;
    private BzStream* _z;
    private byte* _in;
    private bool _inputEnded, _finished, _midStream;

    private SystemBzip2Stream(Stream input, BzStream* z, byte* inBuf)
    {
        _input = input;
        _z = z;
        _in = inBuf;
    }

    public static SystemBzip2Stream? TryCreate(Stream input)
    {
        if (!NativeCompression.Enabled) return null;
        NativeCompression.EnsureResolver();
        var z = (BzStream*)NativeMemory.AllocZeroed((nuint)sizeof(BzStream));
        try
        {
            if (BZ2_bzDecompressInit(z, 0, 0) == BzOk)
                return new SystemBzip2Stream(input, z, (byte*)NativeMemory.Alloc(InputSize));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
        NativeMemory.Free(z);
        return null;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_z is null) throw new ObjectDisposedException(nameof(SystemBzip2Stream));
        if (buffer.Length == 0 || _finished) return 0;

        fixed (byte* outPtr = buffer)
        {
            while (true)
            {
                if (_z->avail_in == 0 && !_inputEnded) Refill();
                if (_z->avail_in == 0 && _inputEnded)
                {
                    // 1本が終わった直後に入力も尽きた＝きれいに終わった。途中なら切れている
                    if (!_midStream) { _finished = true; return 0; }
                    throw new InvalidDataException("bzip2: the data ended before its end mark (the file may be cut short)");
                }

                _z->next_out = outPtr;
                _z->avail_out = (uint)buffer.Length;
                _midStream = true;
                int rc = BZ2_bzDecompress(_z);
                int produced = buffer.Length - (int)_z->avail_out;

                if (rc == BzStreamEnd)
                {
                    // 1本終わった。続きがあれば次の bz2（連結）として読み直す
                    _midStream = false;
                    BZ2_bzDecompressEnd(_z);
                    uint rest = _z->avail_in;
                    byte* restPtr = _z->next_in;
                    NativeMemory.Clear(_z, (nuint)sizeof(BzStream));
                    if (BZ2_bzDecompressInit(_z, 0, 0) != BzOk) throw new InvalidDataException("bzip2: could not restart");
                    _z->next_in = restPtr;
                    _z->avail_in = rest;
                }
                else if (rc != BzOk)
                {
                    throw new InvalidDataException($"bzip2: could not read the data (error {rc})");
                }

                if (produced > 0) return produced;
            }
        }
    }

    private void Refill()
    {
        int n = _input.Read(new Span<byte>(_in, InputSize));
        if (n <= 0) { _inputEnded = true; return; }
        _z->next_in = _in;
        _z->avail_in = (uint)n;
    }

    protected override void Dispose(bool disposing)
    {
        if (_z is not null)
        {
            BZ2_bzDecompressEnd(_z);
            NativeMemory.Free(_z);
            NativeMemory.Free(_in);
            _z = null;
            _in = null;
            if (disposing) _input.Dispose();
        }
        base.Dispose(disposing);
    }

    ~SystemBzip2Stream() => Dispose(false);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>bzlib.h の bz_stream（64bit でも並びは C の既定どおり）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BzStream
    {
        public byte* next_in; public uint avail_in; public uint total_in_lo32; public uint total_in_hi32;
        public byte* next_out; public uint avail_out; public uint total_out_lo32; public uint total_out_hi32;
        public nint state, bzalloc, bzfree, opaque;
    }

    [DllImport(NativeCompression.Bzip2)] private static extern int BZ2_bzDecompressInit(BzStream* s, int verbosity, int small);
    [DllImport(NativeCompression.Bzip2)] private static extern int BZ2_bzDecompress(BzStream* s);
    [DllImport(NativeCompression.Bzip2)] private static extern int BZ2_bzDecompressEnd(BzStream* s);
}

/// <summary>
/// OS 標準の liblzma で xz（または生の lzma）を展開する読み取り専用ストリーム。
/// xz は連結されたものも続けて読む。終わりの印に届く前に入力が尽きたら例外。
///
/// <b>xz は複数スレッドで展開する</b>（実装指示書_xz並列展開_2026-09-24）。
/// xz 5.6 以降は既定で複数ブロックのファイルを作り、ブロックは独立に展開できる。
/// 外部 xz は約9コアで展開していて、1コアのこちらは 6.2 倍負けていた（1コアあたりはこちらが速い）。
/// liblzma 5.4 からの並列デコーダ（<c>lzma_stream_decoder_mt</c>）を使う——ブロックを配り、
/// <b>出力はブロック順に揃え</b>、メモリの上限を超えそうなら<b>自分で単スレッドに落とす</b>
/// （指示書 §4.4 の「大きすぎるときは並列度を落とす」をそのまま持っている）。
/// 1ブロックのファイルは並べようがないので、従来どおりの速さで流れる（§4.3）。
/// 古い liblzma（5.2 以前）には並列デコーダが無いので、単スレッドで展開する。
/// 生の lzma には複数ブロックの仕組みが無いので、いつも単スレッド。
/// </summary>
internal sealed unsafe class SystemLzmaStream : Stream
{
    private const int InputSize = 1 << 20;
    private const int LzmaOk = 0, LzmaStreamEnd = 1, LzmaBufError = 10;
    private const int LzmaRun = 0, LzmaFinish = 3;
    private const uint LzmaConcatenated = 0x08;
    // lzma_stream は版によって後ろに予約欄が増えることがある。多めに確保して 0 で埋めておく
    private const int StreamBytes = 256;

    private readonly Stream _input;
    private LzmaStream* _z;
    private byte* _in;
    private bool _inputEnded, _finished;
    private readonly string _format;

    /// <summary>展開に使うスレッド数の上限（1 なら単スレッドのデコーダ）。</summary>
    public int Threads { get; }

    private SystemLzmaStream(Stream input, LzmaStream* z, byte* inBuf, string format, int threads)
    {
        _input = input;
        _z = z;
        _in = inBuf;
        _format = format;
        Threads = threads;
    }

    /// <param name="xz">true なら xz（.xz）、false なら生の lzma（.lzma）。</param>
    /// <param name="threads">xz の展開に使うスレッド数（段階1の設定。1 以下なら単スレッド）。</param>
    public static SystemLzmaStream? TryCreate(Stream input, bool xz, int threads = 1)
    {
        if (!NativeCompression.Enabled) return null;
        NativeCompression.EnsureResolver();
        var z = (LzmaStream*)NativeMemory.AllocZeroed(StreamBytes);
        try
        {
            int used = 1;
            int rc;
            if (xz && threads > 1 && TryInitMultiThreaded(z, threads) is { } mt)
            {
                rc = mt;
                used = threads;
            }
            else
            {
                rc = xz ? lzma_stream_decoder(z, ulong.MaxValue, LzmaConcatenated)
                        : lzma_alone_decoder(z, ulong.MaxValue);
            }
            if (rc == LzmaOk)
                return new SystemLzmaStream(input, z, (byte*)NativeMemory.Alloc(InputSize), xz ? "xz" : "lzma", used);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
        NativeMemory.Free(z);
        return null;
    }

    /// <summary>
    /// 並列デコーダで始める。liblzma が古くて口が無ければ null（呼び出し側は単スレッドで始める）。
    ///
    /// メモリは「スレッド数 × ブロックの展開後の大きさ」ぶん要る。上限（<see cref="ThreadingMemoryLimit"/>）を
    /// 超えそうなときは liblzma が自分で単スレッドに切り替える（止まりはしない）。
    /// </summary>
    private static int? TryInitMultiThreaded(LzmaStream* z, int threads)
    {
        var options = new LzmaMt
        {
            flags = LzmaConcatenated,
            threads = (uint)threads,
            timeout = 0,
            memlimit_threading = ThreadingMemoryLimit(),
            memlimit_stop = ulong.MaxValue,       // 単スレッドのデコーダと同じく、止めはしない
        };
        try { return lzma_stream_decoder_mt(z, &options); }
        catch (EntryPointNotFoundException) { return null; }   // liblzma 5.2 以前
    }

    /// <summary>
    /// 並列で展開するときに使ってよいメモリ（使えるメモリの 1/4、多くても 4GB）。
    /// 1ブロックが巨大なファイル（-T1 や古い xz で作ったもの）でメモリを食い潰さないための上限。
    /// </summary>
    private static ulong ThreadingMemoryLimit()
    {
        long available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        long quarter = available > 0 ? available / 4 : 1L << 30;
        return (ulong)Math.Clamp(quarter, 256L << 20, 4L << 30);
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_z is null) throw new ObjectDisposedException(nameof(SystemLzmaStream));
        if (buffer.Length == 0 || _finished) return 0;

        fixed (byte* outPtr = buffer)
        {
            while (true)
            {
                if (_z->avail_in == 0 && !_inputEnded)
                {
                    int n = _input.Read(new Span<byte>(_in, InputSize));
                    if (n <= 0) _inputEnded = true;
                    else { _z->next_in = _in; _z->avail_in = (nuint)n; }
                }

                _z->next_out = outPtr;
                _z->avail_out = (nuint)buffer.Length;
                // 入力が尽きたら FINISH で「これで終わり」と伝える（連結 xz の終わりを確かめるため）
                int rc = lzma_code(_z, _inputEnded ? LzmaFinish : LzmaRun);
                int produced = buffer.Length - (int)_z->avail_out;

                if (rc == LzmaStreamEnd) _finished = true;
                else if (rc == LzmaBufError && _inputEnded && produced == 0)
                    throw new InvalidDataException($"{_format}: the data ended before its end mark (the file may be cut short)");
                else if (rc != LzmaOk && rc != LzmaBufError)
                    throw new InvalidDataException($"{_format}: could not read the data (error {rc})");

                if (produced > 0) return produced;
                if (_finished) return 0;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_z is not null)
        {
            lzma_end(_z);
            NativeMemory.Free(_z);
            NativeMemory.Free(_in);
            _z = null;
            _in = null;
            if (disposing) _input.Dispose();
        }
        base.Dispose(disposing);
    }

    ~SystemLzmaStream() => Dispose(false);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>lzma.h の lzma_stream の頭（ここで触るのは前の方だけ。後ろの予約欄は 0 のまま）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct LzmaStream
    {
        public byte* next_in; public nuint avail_in; public ulong total_in;
        public byte* next_out; public nuint avail_out; public ulong total_out;
    }

    /// <summary>lzma/container.h の lzma_mt（5.4 以降。並びは C の既定どおり・全 128 バイト）。</summary>
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

    [DllImport(NativeCompression.Lzma)] private static extern int lzma_stream_decoder(LzmaStream* s, ulong memlimit, uint flags);
    [DllImport(NativeCompression.Lzma)] private static extern int lzma_stream_decoder_mt(LzmaStream* s, LzmaMt* options);
    [DllImport(NativeCompression.Lzma)] private static extern int lzma_alone_decoder(LzmaStream* s, ulong memlimit);
    [DllImport(NativeCompression.Lzma)] private static extern int lzma_code(LzmaStream* s, int action);
    [DllImport(NativeCompression.Lzma)] private static extern void lzma_end(LzmaStream* s);
}

/// <summary>
/// OS 標準の liblz4（Linux）で lz4 のフレーム形式を展開する読み取り専用ストリーム。
/// 連結されたフレームも続けて読む。フレームの途中で入力が尽きたら例外。
///
/// .NET 向けの展開（K4os）は、Linux の VM（2 vCPU・lz4 1.10）で lz4 コマンドの約 1.6 倍の時間がかかり、
/// rg -z に 1/1.14 と負けていた（2026-09-25）。lz4 コマンドの中身はこのライブラリなので、直接呼べば同じ速さになる。
/// <b>ただし 1.10 より前の liblz4 は使わない</b>: Ubuntu 24.04 の 1.9.4 では、lz4 コマンドでも K4os より遅かった
///（3G: lz4 -dc 1.12 秒・uvf＋K4os 1.0 秒・uvf＋liblz4 1.05 秒。1.10 の lz4 -dc は 0.75 秒）。
/// </summary>
internal sealed unsafe class SystemLz4Stream : Stream
{
    private const int InputSize = 1 << 20;
    private const uint Lz4fVersion = 100;
    private const int MinLibraryVersion = 11000;   // 1.10.0（LZ4_versionNumber の形）

    private readonly Stream _input;
    private nint _dctx;
    private byte* _in;
    private int _inPos, _inLen;
    private bool _inputEnded, _inFrame;

    private SystemLz4Stream(Stream input, nint dctx, byte* inBuf)
    {
        _input = input;
        _dctx = dctx;
        _in = inBuf;
    }

    public static SystemLz4Stream? TryCreate(Stream input)
    {
        if (!NativeCompression.Enabled || !OperatingSystem.IsLinux()) return null;
        NativeCompression.EnsureResolver();
        try
        {
            if (LZ4_versionNumber() < MinLibraryVersion) return null;
            nint dctx;
            if (LZ4F_isError(LZ4F_createDecompressionContext(&dctx, Lz4fVersion)) == 0)
                return new SystemLz4Stream(input, dctx, (byte*)NativeMemory.Alloc(InputSize));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
        return null;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_dctx == 0) throw new ObjectDisposedException(nameof(SystemLz4Stream));
        if (buffer.Length == 0) return 0;

        fixed (byte* outPtr = buffer)
        {
            while (true)
            {
                if (_inPos == _inLen && !_inputEnded) Refill();
                if (_inPos == _inLen && _inputEnded)
                {
                    // フレームの切れ目で入力も尽きた＝きれいに終わった。途中なら切れている
                    if (!_inFrame) return 0;
                    throw new InvalidDataException("lz4: the data ended before the end of the frame (the file may be cut short)");
                }

                nuint dstSize = (nuint)buffer.Length;
                nuint srcSize = (nuint)(_inLen - _inPos);
                nuint rc = LZ4F_decompress(_dctx, outPtr, &dstSize, _in + _inPos, &srcSize, null);
                if (LZ4F_isError(rc) != 0)
                    throw new InvalidDataException(
                        $"lz4: could not read the data ({Marshal.PtrToStringAnsi(LZ4F_getErrorName(rc))})");
                _inPos += (int)srcSize;
                _inFrame = rc != 0;   // 0 はフレームを読み終えたしるし（続きがあれば次のフレームとして読む）
                if (dstSize > 0) return (int)dstSize;
            }
        }
    }

    private void Refill()
    {
        int n = _input.Read(new Span<byte>(_in, InputSize));
        if (n <= 0) { _inputEnded = true; return; }
        _inPos = 0;
        _inLen = n;
    }

    protected override void Dispose(bool disposing)
    {
        if (_dctx != 0)
        {
            LZ4F_freeDecompressionContext(_dctx);
            NativeMemory.Free(_in);
            _dctx = 0;
            _in = null;
            if (disposing) _input.Dispose();
        }
        base.Dispose(disposing);
    }

    ~SystemLz4Stream() => Dispose(false);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    [DllImport(NativeCompression.Lz4)] private static extern nuint LZ4F_createDecompressionContext(nint* dctx, uint version);
    [DllImport(NativeCompression.Lz4)] private static extern nuint LZ4F_freeDecompressionContext(nint dctx);
    [DllImport(NativeCompression.Lz4)]
    private static extern nuint LZ4F_decompress(nint dctx, byte* dst, nuint* dstSize, byte* src, nuint* srcSize, void* options);
    [DllImport(NativeCompression.Lz4)] private static extern uint LZ4F_isError(nuint code);
    [DllImport(NativeCompression.Lz4)] private static extern nint LZ4F_getErrorName(nuint code);
    [DllImport(NativeCompression.Lz4)] private static extern int LZ4_versionNumber();
}

/// <summary>
/// ブロック1つを zstd の1フレームに圧縮する（.uwvz の中身）。Linux は OS の libzstd、無ければ ZstdSharp。
///
/// ZstdSharp（.NET 向けの移植）は、同じレベル 1 で C の zstd の約 1.8 倍の CPU 時間がかかった
///（3G: ZstdSharp 約 4.3 秒・zstd -1 -T1 2.43 秒）。コアの少ない機械では .uwvz の1回目がそのまま遅くなる
///（Linux 2 vCPU で lz4 の1回目 4.08 秒のうち約 2 秒強。2026-09-25）。
/// 出力はどちらも標準の zstd フレームなので、どちらで作った .uwvz もどちらでも読める。
/// </summary>
public sealed unsafe class ZstdBlockCompressor : IDisposable
{
    private readonly int _level;
    private nint _cctx;
    private ZstdSharp.Compressor? _managed;

    public ZstdBlockCompressor(int level)
    {
        _level = level;
        _cctx = NativeZstd.CreateCCtx();
        if (_cctx == 0) _managed = new ZstdSharp.Compressor(level);
    }

    /// <summary>OS の libzstd で圧縮しているか。</summary>
    public bool IsNative => _cctx != 0;

    /// <summary>src を圧縮して dst に書き、書いた長さを返す（dst が足りなければ例外）。</summary>
    public int Wrap(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        if (_managed is not null) return _managed.Wrap(src, dst);
        fixed (byte* s = src)
        fixed (byte* d = dst)
        {
            nuint rc = NativeZstd.ZSTD_compressCCtx(_cctx, d, (nuint)dst.Length, s, (nuint)src.Length, _level);
            if (NativeZstd.ZSTD_isError(rc) != 0)
                throw new InvalidOperationException($"zstd: could not compress ({NativeZstd.ErrorName(rc)})");
            return (int)rc;
        }
    }

    public void Dispose()
    {
        if (_cctx != 0) { NativeZstd.ZSTD_freeCCtx(_cctx); _cctx = 0; }
        _managed?.Dispose();
        _managed = null;
    }
}

/// <summary>zstd の1フレームを展開する（.uwvz の中身）。Linux は OS の libzstd、無ければ ZstdSharp。</summary>
public sealed unsafe class ZstdBlockDecompressor : IDisposable
{
    private nint _dctx;
    private ZstdSharp.Decompressor? _managed;

    public ZstdBlockDecompressor()
    {
        _dctx = NativeZstd.CreateDCtx();
        if (_dctx == 0) _managed = new ZstdSharp.Decompressor();
    }

    public bool IsNative => _dctx != 0;

    /// <summary>src を展開して dst に書き、書いた長さを返す（壊れていれば <see cref="InvalidDataException"/>）。</summary>
    public int Unwrap(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        if (_managed is not null) return _managed.Unwrap(src, dst);
        fixed (byte* s = src)
        fixed (byte* d = dst)
        {
            nuint rc = NativeZstd.ZSTD_decompressDCtx(_dctx, d, (nuint)dst.Length, s, (nuint)src.Length);
            if (NativeZstd.ZSTD_isError(rc) != 0)
                throw new InvalidDataException($"zstd: could not decompress ({NativeZstd.ErrorName(rc)})");
            return (int)rc;
        }
    }

    public void Dispose()
    {
        if (_dctx != 0) { NativeZstd.ZSTD_freeDCtx(_dctx); _dctx = 0; }
        _managed?.Dispose();
        _managed = null;
    }
}

internal static unsafe class NativeZstd
{
    /// <summary>OS の libzstd の圧縮の器（使えなければ 0）。</summary>
    public static nint CreateCCtx() => Try(() => ZSTD_createCCtx());

    /// <summary>OS の libzstd の展開の器（使えなければ 0）。</summary>
    public static nint CreateDCtx() => Try(() => ZSTD_createDCtx());

    private static nint Try(Func<nint> create)
    {
        if (!NativeCompression.Enabled || !OperatingSystem.IsLinux()) return 0;
        NativeCompression.EnsureResolver();
        try { return create(); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return 0; }
    }

    public static string ErrorName(nuint code) => Marshal.PtrToStringAnsi(ZSTD_getErrorName(code)) ?? code.ToString();

    [DllImport(NativeCompression.Zstd)] public static extern nint ZSTD_createCCtx();
    [DllImport(NativeCompression.Zstd)] public static extern nuint ZSTD_freeCCtx(nint cctx);
    [DllImport(NativeCompression.Zstd)]
    public static extern nuint ZSTD_compressCCtx(nint cctx, byte* dst, nuint dstCapacity, byte* src, nuint srcSize, int level);
    [DllImport(NativeCompression.Zstd)] public static extern nint ZSTD_createDCtx();
    [DllImport(NativeCompression.Zstd)] public static extern nuint ZSTD_freeDCtx(nint dctx);
    [DllImport(NativeCompression.Zstd)]
    public static extern nuint ZSTD_decompressDCtx(nint dctx, byte* dst, nuint dstCapacity, byte* src, nuint srcSize);
    [DllImport(NativeCompression.Zstd)] public static extern uint ZSTD_isError(nuint code);
    [DllImport(NativeCompression.Zstd)] public static extern nint ZSTD_getErrorName(nuint code);
}
