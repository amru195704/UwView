using System.Reflection;
using System.Runtime.InteropServices;

namespace UwView.Core.Tests;

/// <summary>OS の展開ライブラリを探す係（NativeCompression）。</summary>
public class NativeCompressionTests
{
    [Fact]
    public void リゾルバは返すたびに参照を1つ増やす()
    {
        // NativeAOT は同時に解決して負けた側のハンドルを Free する。同じハンドルの使い回しだと使用中のライブラリが外れ、Linux で当たりが多いと落ちた（1.7.2.8）
        var type = typeof(UwView.Core.Cli.UvfCli).Assembly.GetType("UwView.Core.NativeCompression")!;
        var resolve = type.GetMethod("Resolve", BindingFlags.NonPublic | BindingFlags.Static)!;
        (string lib, string export) = OperatingSystem.IsMacOS() ? ("libz", "zlibVersion") : ("liblzma", "lzma_version_number");
        nint Resolve() => (nint)resolve.Invoke(null, [lib, type.Assembly, null])!;

        nint kept = Resolve();
        if (kept == 0) return;
        for (int i = 0; i < 20; i++) NativeLibrary.Free(Resolve());

        Assert.True(NativeLibrary.TryGetExport(kept, export, out nint fn));
        Assert.NotEqual(0, fn);
    }
}
