using System.IO.Compression;

namespace UwView.Core.Tests;

/// <summary>
/// gz の展開（<see cref="GzipDecoder"/>。macOS は OS 標準の zlib を直接呼ぶ）。
/// 結果は .NET の GZipStream と同じで、切れた・壊れた gz は読みの中で例外になること。
/// </summary>
public class GzipDecoderTests
{
    private static byte[] Gzip(byte[] plain, CompressionLevel level = CompressionLevel.Fastest)
    {
        using var ms = new MemoryStream();
        using (var z = new GZipStream(ms, level, leaveOpen: true)) z.Write(plain);
        return ms.ToArray();
    }

    private static byte[] Decode(byte[] gz, out bool verified)
    {
        using var s = GzipDecoder.Open(new MemoryStream(gz), out verified);
        using var outMs = new MemoryStream();
        var buf = new byte[1000];   // 小さい読みでも途切れず続くこと
        int n;
        while ((n = s.Read(buf, 0, buf.Length)) > 0) outMs.Write(buf, 0, n);
        return outMs.ToArray();
    }

    private static byte[] Random(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        for (int i = 0; i < data.Length; i += 3) data[i] = (byte)'a';   // 圧縮が効く程度に偏らせる
        return data;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100_000)]
    [InlineData(5_000_000)]   // 入力の読み単位（1MB）を何度も越える
    public void GZipStreamと同じ中身になる(int length)
    {
        byte[] plain = Random(length, length);
        byte[] got = Decode(Gzip(plain), out bool verified);
        Assert.Equal(plain, got);
        Assert.Equal(OperatingSystem.IsMacOS(), verified);
    }

    [Fact]
    public void 連結したgzは続けて読む()
    {
        byte[] a = Random(3_000_000, 1), b = Random(10, 2), c = Random(200_000, 3);
        byte[] gz = [.. Gzip(a), .. Gzip(b), .. Gzip(c)];
        Assert.Equal((byte[])[.. a, .. b, .. c], Decode(gz, out _));
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(0.9)]
    [InlineData(-8)]   // 末尾の照合用8バイトだけ落とす
    [InlineData(-1)]
    public void 途中で切れたgzは例外になる(double cut)
    {
        if (!OperatingSystem.IsMacOS()) return;   // .NET の展開は黙って終わる（呼び出し側で照らす）
        byte[] gz = Gzip(Random(2_000_000, 9));
        int keep = cut < 0 ? gz.Length + (int)cut : (int)(gz.Length * cut);
        Assert.Throws<InvalidDataException>(() => Decode(gz[..keep], out _));
    }

    [Fact]
    public void CRCが合わないgzは例外になる()
    {
        byte[] gz = Gzip(Random(100_000, 4));
        gz[^8] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => Decode(gz, out _));
    }

    [Fact]
    public void 閉じると元のストリームも閉じる()
    {
        var inner = new MemoryStream(Gzip("abc"u8.ToArray()));
        GzipDecoder.Open(inner, out _).Dispose();
        Assert.False(inner.CanRead);
    }
}
