namespace UwView.Core.Tests;

/// <summary>相対パスの起点を渡す <see cref="LinkedFile.Info(string, string?)"/>（多数のファイルの確認で getcwd を 1 回にする）。</summary>
public class LinkedFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uvf_linked_").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void 起点を渡すと相対パスをそこから見る()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        File.WriteAllText(Path.Combine(_dir, "sub", "a.log"), "12345\n");

        var info = LinkedFile.Info(Path.Combine("sub", "a.log"), _dir);

        Assert.True(info.Exists);
        Assert.Equal(6, info.Length);
        Assert.Equal(Path.Combine(_dir, "sub", "a.log"), info.FullName);
    }

    [Fact]
    public void 絶対パスは起点にかかわらずそのまま()
    {
        string file = Path.Combine(_dir, "b.log");
        File.WriteAllText(file, "x\n");

        Assert.Equal(file, LinkedFile.Info(file, Path.GetTempPath()).FullName);
    }

    [Fact]
    public void 起点を渡してもリンクはリンク先を見る()
    {
        if (OperatingSystem.IsWindows()) return;   // シンボリックリンクの作成に権限が要る
        File.WriteAllText(Path.Combine(_dir, "real.log"), "abcdefgh\n");
        File.CreateSymbolicLink(Path.Combine(_dir, "link.log"), "real.log");   // 相対リンク

        var info = LinkedFile.Info("link.log", _dir);

        Assert.Equal(9, info.Length);
    }
}
