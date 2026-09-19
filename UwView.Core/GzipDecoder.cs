using System.IO.Compression;
using System.Runtime.InteropServices;

namespace UwView.Core;

/// <summary>
/// gz を展開しながら読むストリームを作る。
///
/// macOS は OS 標準の zlib（/usr/lib/libz.dylib）を直接呼ぶ。.NET の <see cref="GZipStream"/> より
/// 1.6〜1.8 倍速い（Apple M4 実測 2026-09-19: 50GB 展開 30.2 秒 → 18.5 秒。gzip -dc は 26.3 秒）。
/// OS に必ずあるので同梱物は増えない。ほかの OS と、読み込めなかったときは <see cref="GZipStream"/>。
/// </summary>
public static class GzipDecoder
{
    /// <summary>
    /// <paramref name="compressed"/> を gz として展開しながら読むストリーム（閉じると compressed も閉じる）。
    /// </summary>
    /// <param name="verifiesTrailer">
    /// true なら、途中で切れた・壊れた gz は読みの中で <see cref="InvalidDataException"/> になる
    /// （zlib が末尾の CRC32・長さを照らす）。false（<see cref="GZipStream"/>）は切れていても黙って終わるので、
    /// 呼び出し側で <see cref="CompressedInput.VerifyGzipOutput"/> を使って照らすこと。
    /// </param>
    public static Stream Open(Stream compressed, out bool verifiesTrailer)
    {
        if (OperatingSystem.IsMacOS() && SystemZlibStream.TryCreate(compressed) is { } fast)
        {
            verifiesTrailer = true;
            return fast;
        }
        verifiesTrailer = false;
        return new GZipStream(compressed, CompressionMode.Decompress);
    }
}

/// <summary>OS 標準の zlib で gz を展開する読み取り専用ストリーム（連結 gz も続けて読む）。</summary>
internal sealed unsafe class SystemZlibStream : Stream
{
    private const string Lib = "/usr/lib/libz.dylib";
    private const int InputSize = 1 << 20;
    private const int ZOk = 0, ZStreamEnd = 1, ZBufError = -5;

    private readonly Stream _input;
    private ZStream* _z;          // zlib は構造体の位置を覚えて照合するので、動かないメモリに置く
    private byte* _in;
    private bool _inputEnded;
    private bool _finished;

    private SystemZlibStream(Stream input, ZStream* z, byte* inBuf)
    {
        _input = input;
        _z = z;
        _in = inBuf;
    }

    public static SystemZlibStream? TryCreate(Stream input)
    {
        var z = (ZStream*)NativeMemory.AllocZeroed((nuint)sizeof(ZStream));
        try
        {
            // 15+16: gzip の包みだけを受け付け、末尾の CRC32・長さも照らす
            if (inflateInit2_(z, 15 + 16, "1.2.12", sizeof(ZStream)) == ZOk)
                return new SystemZlibStream(input, z, (byte*)NativeMemory.Alloc(InputSize));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
        NativeMemory.Free(z);
        return null;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_z is null) throw new ObjectDisposedException(nameof(SystemZlibStream));
        if (buffer.Length == 0 || _finished) return 0;

        fixed (byte* outPtr = buffer)
        {
            while (true)
            {
                if (_z->avail_in == 0 && !_inputEnded)
                {
                    int n = _input.Read(new Span<byte>(_in, InputSize));
                    if (n <= 0) _inputEnded = true;
                    else { _z->next_in = _in; _z->avail_in = (uint)n; }
                }

                _z->next_out = outPtr;
                _z->avail_out = (uint)buffer.Length;
                int rc = inflate(_z, 0);
                int produced = buffer.Length - (int)_z->avail_out;

                if (rc == ZStreamEnd)
                {
                    // 1つのメンバーが末尾の照合まで済んだ。続きがあれば次のメンバー（cat a.gz b.gz）
                    if (_z->avail_in == 0 && _inputEnded) _finished = true;
                    else if (_z->avail_in == 0 && !HasMoreInput()) _finished = true;
                    else if (inflateReset(_z) != ZOk) throw Corrupt("reset failed");
                }
                else if (rc == ZBufError && _z->avail_in == 0 && _inputEnded && _z->total_in == 0)
                    _finished = true;   // 0 バイトのファイル（.NET は空の中身をこう書く）。GZipStream と同じく空とする
                else if (rc == ZBufError && _z->avail_in == 0 && _inputEnded)
                    throw new InvalidDataException("gzip truncated: the compressed data ended before the gzip trailer");
                else if (rc != ZOk && rc != ZBufError)
                    throw Corrupt(_z->msg == 0 ? $"inflate error {rc}" : Marshal.PtrToStringAnsi(_z->msg)!);

                if (produced > 0) return produced;
                if (_finished) return 0;
            }
        }
    }

    // メンバーの終わりで入力を使い切っていたら、次を読んで続きがあるか確かめる
    private bool HasMoreInput()
    {
        int n = _input.Read(new Span<byte>(_in, InputSize));
        if (n <= 0) { _inputEnded = true; return false; }
        _z->next_in = _in;
        _z->avail_in = (uint)n;
        return true;
    }

    private static InvalidDataException Corrupt(string detail) => new($"gzip corrupted: {detail}");

    protected override void Dispose(bool disposing)
    {
        if (_z is not null)
        {
            inflateEnd(_z);
            NativeMemory.Free(_z);
            NativeMemory.Free(_in);
            _z = null;
            _in = null;
            if (disposing) _input.Dispose();
        }
        base.Dispose(disposing);
    }

    ~SystemZlibStream() => Dispose(false);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    [StructLayout(LayoutKind.Sequential)]
    private struct ZStream
    {
        public byte* next_in; public uint avail_in; public nuint total_in;
        public byte* next_out; public uint avail_out; public nuint total_out;
        public nint msg, state, zalloc, zfree, opaque;
        public int data_type; public nuint adler, reserved;
    }

    [DllImport(Lib)] private static extern int inflateInit2_(ZStream* s, int windowBits, string version, int size);
    [DllImport(Lib)] private static extern int inflate(ZStream* s, int flush);
    [DllImport(Lib)] private static extern int inflateReset(ZStream* s);
    [DllImport(Lib)] private static extern int inflateEnd(ZStream* s);
}
