using UwView.Core.Cli;

namespace UwView.Core.Tests;

/// <summary>
/// ** の展開で、下のフォルダーの一覧を見つけた先から先読みする（除外の決まりのある所で止める。2026-10-06）。
/// 先読みは一覧だけで、除外の判定はたどる側がする。結果が変わらないことを確かめる。
/// </summary>
public class FileSetPrefetchTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uvf_prefetch_").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private void Make(string relative, string text = "x\n")
    {
        string path = Path.Combine(_dir, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private List<string> Expand(string specification)
        => [.. FileSet.Expand(specification, _dir).Files.Select(p => p.Replace(Path.DirectorySeparatorChar, '/'))];

    [Fact]
    public void 除外の決まりが無い深い木を全部拾う()
    {
        var expected = new List<string>();
        for (int a = 0; a < 6; a++)
            for (int b = 0; b < 6; b++)
                for (int c = 0; c < 3; c++)
                {
                    string rel = $"d{a}/e{b}/f{c}/g/x{c}.c";
                    Make(rel);
                    expected.Add(rel);
                }
        Make("d0/skip.h.txt");
        expected.Sort(StringComparer.Ordinal);

        Assert.Equal(expected, Expand("**/*.c"));
    }

    [Theory]
    [InlineData(".ignore", false)]
    [InlineData(".gitignore", true)]     // .gitignore は git の中でだけ効く（ripgrep と同じ）
    public void 途中のフォルダーの除外ファイルは今までどおり効く(string ignoreFile, bool inGit)
    {
        if (inGit) Directory.CreateDirectory(Path.Combine(_dir, "a", ".git"));
        Make("a/b/keep.c");
        Make("a/b/c/" + ignoreFile, "drop/\n*.tmp.c\n");
        Make("a/b/c/ok.c");
        Make("a/b/c/no.tmp.c");
        Make("a/b/c/drop/d/e/gone.c");
        Make("a/b/c/x/y/z/deep.c");

        Assert.Equal(["a/b/c/ok.c", "a/b/c/x/y/z/deep.c", "a/b/keep.c"], Expand("**/*.c"));
    }

    [Fact]
    public void 上のフォルダーに決まりがあれば深い先読みをしない()
    {
        Make("sub/a.c");
        var none = new IgnoreTree(IgnoreOptions.Default, _dir);
        Assert.False(none.MayIgnoreBelow(Path.Combine(_dir, "sub")));

        Make(".ignore", "*.tmp\n");
        var withRules = new IgnoreTree(IgnoreOptions.Default, _dir);
        Assert.True(withRules.MayIgnoreBelow(Path.Combine(_dir, "sub")));
    }
}
