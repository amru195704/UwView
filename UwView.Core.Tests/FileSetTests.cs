using UwView.Core.Cli;

namespace UwView.Core.Tests;

/// <summary>
/// 指定文字列 (A) の展開（Wide Field v1.7.0 段階3・指示書 §2.2・§2.2b）。
/// 並びは「書いた順 → 名前順」、重複は先に出た側、区切りは空白とカンマの両方。
/// </summary>
public class FileSetTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uvf_fileset_").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private void Make(params string[] relativePaths)
    {
        foreach (string relative in relativePaths)
        {
            string path = Path.Combine(_dir, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x\n");
        }
    }

    private IReadOnlyList<string> Expand(string specification)
        => FileSet.Expand(specification, _dir).Files
                  .Select(p => p.Replace(Path.DirectorySeparatorChar, '/')).ToList();

    [Theory]
    [InlineData("a.log b.log", 2)]
    [InlineData("a.log,b.log", 2)]
    [InlineData("a.log,  b.log", 2)]
    [InlineData("a.log", 1)]
    public void 空白でもカンマでも区切れる(string specification, int expected)
    {
        Make("a.log", "b.log");
        Assert.Equal(expected, Expand(specification).Count);
    }

    [Fact]
    public void 断片は書いた順に並ぶ()
    {
        Make("a.log", "b.log", "c.log");
        Assert.Equal(["c.log", "a.log", "b.log"], Expand("c.log a.log b.log"));
    }

    [Fact]
    public void ひとつの断片が広がったときは名前順()
    {
        Make("b.log", "a.log", "C.log");
        // ロケールに依存しないバイト順（大文字が先）
        Assert.Equal(["C.log", "a.log", "b.log"], Expand("*.log"));
    }

    [Fact]
    public void 重複は先に出た側を採る()
    {
        Make("a.log", "b.log");
        Assert.Equal(["a.log", "b.log"], Expand("a.log *.log"));
    }

    [Fact]
    public void 階層付きのワイルドカードも受ける()
    {
        Make("logs/x.log", "logs/y.log", "err/z.log", "top.log");
        Assert.Equal(["logs/x.log", "logs/y.log"], Expand("logs/*.log"));
        Assert.Equal(["err/z.log", "logs/x.log", "logs/y.log"], Expand("*/*.log"));
    }

    [Fact]
    public void 複数の断片を混ぜられる()
    {
        Make("logs/x.log", "err/z.log");
        Assert.Equal(["logs/x.log", "err/z.log"], Expand("logs/*.log,err/*.log"));
    }

    [Fact]
    public void 二重アスタリスクは何段でも降りる()
    {
        Make("a.log", "logs/x.log", "logs/deep/y.log");
        var found = Expand("**/*.log");
        Assert.Contains("logs/deep/y.log", found);
        Assert.Contains("logs/x.log", found);
    }

    [Fact]
    public void 書いたとおりの区切りで返す()
    {
        // Windows で OS の区切り（\）に直して返すと、指定（osm/x）と出力（osm\x）が食い違い、
        // ほかの道具の出力と突き合わせられない（オーナー報告 2026-09-22・Windows の比較テストで不一致）
        Make("logs/x.log", "logs/y.log");
        Assert.All(FileSet.Expand("logs/*.log", _dir).Files, p => Assert.DoesNotContain('\\', p));
        Assert.Equal(["logs/x.log", "logs/y.log"], FileSet.Expand("logs/*.log", _dir).Files);
    }

    [Fact]
    public void 区切りがバックスラッシュでも受ける()
    {
        Make("logs/x.log");
        Assert.Equal(["logs/x.log"], Expand(@"logs\x.log"));
        // 書いた形（\）のまま返すのは Windows だけ（Unix では \ は区切りではないので開けなくなる）
        Assert.Equal(OperatingSystem.IsWindows() ? [@"logs\x.log"] : ["logs/x.log"],
                     FileSet.Expand(@"logs\x.log", _dir).Files);
    }

    [Fact]
    public void 当たらなかった断片はそのまま返す()
    {
        Make("a.log");
        var result = FileSet.Expand("a.log nope.log '*.none'", _dir);
        Assert.Single(result.Files);
        Assert.Equal(["nope.log", "'*.none'"], result.Missing);
    }

    [Fact]
    public void 名前に空白を含むパスはカンマ区切りで指定できる()
    {
        Make("Program Files/app.log", "other.log");
        Assert.Equal(["Program Files/app.log", "other.log"], Expand("Program Files/app.log,other.log"));
        // カンマが無いと空白で割れてしまい、1件も当たらない（指示書 §2.2b の注意点）
        Assert.Empty(Expand("Program Files/app.log"));
    }

    [Theory]
    [InlineData("a.log", false)]
    [InlineData("a.log b.log", true)]
    [InlineData("a.log,b.log", true)]
    [InlineData("*.log", true)]
    [InlineData("logs/*/a.log", true)]
    public void 複数指定かどうかを見分ける(string specification, bool multiple)
        => Assert.Equal(multiple, FileSet.IsMultiple(specification));
}
