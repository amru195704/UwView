using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace UwView.Core.Tests;

/// <summary>
/// .uwvz のブロックの zstd（<see cref="ZstdBlockCompressor"/>・<see cref="ZstdBlockDecompressor"/>）。
/// Linux は OS の libzstd、mac は ZstdSharp。<b>どちらで作ったものもどちらで読める</b>こと
///（mac で作った .uwvz を Linux で読む、その逆）を確かめる。
/// </summary>
public class ZstdBlockTests
{
    private static byte[] Sample()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 200_000; i++) sb.Append("line ").Append(i).Append(" 東京 <tag k=\"name\" v=\"x\"/>\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    [Fact]
    public void 圧縮して展開すると元に戻る()
    {
        byte[] src = Sample();
        var packed = new byte[src.Length + src.Length / 16 + 1024];
        using var c = new ZstdBlockCompressor(1);
        int n = c.Wrap(src, packed);

        var back = new byte[src.Length];
        using var d = new ZstdBlockDecompressor();
        Assert.Equal(src.Length, d.Unwrap(packed.AsSpan(0, n), back));
        Assert.Equal(src, back);
    }

    [Fact]
    public void OSのzstdとZstdSharpは互いに読める()
    {
        byte[] src = Sample();
        var packed = new byte[src.Length + src.Length / 16 + 1024];
        var back = new byte[src.Length];

        using (var c = new ZstdBlockCompressor(1))
        {
            int n = c.Wrap(src, packed);
            using var managed = new ZstdSharp.Decompressor();
            Assert.Equal(src, managed.Unwrap(packed.AsSpan(0, n)).ToArray());
        }

        using (var managed = new ZstdSharp.Compressor(1))
        {
            int n = managed.Wrap(src, packed);
            using var d = new ZstdBlockDecompressor();
            Assert.Equal(src.Length, d.Unwrap(packed.AsSpan(0, n), back));
            Assert.Equal(src, back);
        }
    }

    [Fact]
    public void 壊れたものは例外にする()
    {
        byte[] src = Sample();
        var packed = new byte[src.Length + src.Length / 16 + 1024];
        using var c = new ZstdBlockCompressor(1);
        int n = c.Wrap(src, packed);
        packed[n / 2] ^= 0xFF;

        using var d = new ZstdBlockDecompressor();
        Assert.ThrowsAny<Exception>(() => d.Unwrap(packed.AsSpan(0, n), new byte[src.Length]));
    }

    [Fact]
    public void LinuxとWindowsではOSのlibzstdを使う()
    {
        // Windows は実行ファイルの隣に同梱した libzstd.dll（build/native/win-x64）。mac の OS には無い
        bool expected = OperatingSystem.IsLinux()
                        || OperatingSystem.IsWindows() && File.Exists(Path.Combine(AppContext.BaseDirectory, "libzstd.dll"));
        using var c = new ZstdBlockCompressor(1);
        using var d = new ZstdBlockDecompressor();
        Assert.Equal(expected, c.IsNative);
        Assert.Equal(expected, d.IsNative);
    }

    [Fact]
    public void リゾルバは返すたびに参照を1つ増やす()
    {
        // NativeAOT は同時に解決して負けた側のハンドルを Free する。同じハンドルの使い回しだと使用中のライブラリが外れ、Linux で当たりが多いと落ちた（1.7.2.8）
        var type = typeof(ZstdBlockCompressor).Assembly.GetType("UwView.Core.NativeCompression")!;
        var resolve = type.GetMethod("Resolve", BindingFlags.NonPublic | BindingFlags.Static)!;
        (string lib, string export) = OperatingSystem.IsMacOS() ? ("libz", "zlibVersion") : ("libzstd", "ZSTD_versionNumber");
        nint Resolve() => (nint)resolve.Invoke(null, [lib, type.Assembly, null])!;

        nint kept = Resolve();
        if (kept == 0) return;
        for (int i = 0; i < 20; i++) NativeLibrary.Free(Resolve());

        Assert.True(NativeLibrary.TryGetExport(kept, export, out nint fn));
        Assert.NotEqual(0, fn);
    }
}
