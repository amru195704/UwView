using UwView.Core.Cli;

namespace UwView.Core.Tests;

/// <summary>
/// ファイルの指定の波かっこ <c>{a,b}</c>（指示書「ファイル指定の波かっこ」2026-10-11 §5）。
/// </summary>
public class FileSetBraceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uvf_braces_").FullName;

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

    private FileSet.Result Run(string specification) => FileSet.Expand(specification, _dir);

    private IReadOnlyList<string> Expand(string specification)
        => Run(specification).Files.Select(p => p.Replace(Path.DirectorySeparatorChar, '/')).ToList();

    // ── 展開 ─────────────────────────────

    [Theory]
    [InlineData("a.{txt,log}", new[] { "a.txt", "a.log" })]
    [InlineData("app.log{,.1}", new[] { "app.log", "app.log.1" })]                        // 空の選択肢
    [InlineData("{a,b}{1,2}", new[] { "a1", "a2", "b1", "b2" })]                          // 複数（書いた順の組み合わせ）
    [InlineData("x{a,{b,c}}", new[] { "xa", "xb", "xc" })]                                // 入れ子
    [InlineData("{logs,old}/**/*.log", new[] { "logs/**/*.log", "old/**/*.log" })]        // ** と組み合わせ
    [InlineData("plain.log", new[] { "plain.log" })]
    [InlineData("{}", new[] { "{}" })]                                                    // 中が空は文字どおり
    [InlineData("{a}", new[] { "{a}" })]                                                  // カンマが無いのは文字どおり
    [InlineData("a{b", new[] { "a{b" })]                                                  // 閉じていない
    [InlineData("a}b{c,d", new[] { "a}b{c,d" })]
    [InlineData("{x{a,b}}", new[] { "{xa}", "{xb}" })]                                    // bash と同じ
    public void 波かっこを書いた順に展開する(string text, string[] expected)
        => Assert.Equal(expected, Braces.Expand(text));

    [Fact]
    public void 千通りを超えると止める()
    {
        string ten = "{0,1,2,3,4,5,6,7,8,9}";
        Assert.Equal(1000, Braces.Expand(ten + ten + ten).Count);
        var e = Assert.Throws<BraceLimitException>(() => Braces.Expand(ten + ten + ten + "{a,b}"));
        Assert.Equal(2000, e.Count);
        Assert.Contains("2,000", e.MessageFor(ja: true));
        Assert.Throws<BraceLimitException>(() => FileSet.Expand(ten + ten + ten + "{a,b}.log", _dir));
    }

    // ── 区切り ─────────────────────────────

    [Theory]
    [InlineData("**/*.{txt,log},data/*.csv", new[] { "**/*.{txt,log}", "data/*.csv" })]   // 中のカンマで割らない・外のカンマで割る
    [InlineData("*.{txt,log} data/*.csv", new[] { "*.{txt,log}", "data/*.csv" })]         // 空白区切り
    [InlineData("{a b,c}.log x.log", new[] { "{a b,c}.log", "x.log" })]                  // 中の空白で割らない
    [InlineData("a{b,c.log", new[] { "a{b", "c.log" })]                                   // 閉じていなければ今までどおり
    [InlineData("a.log,b.log", new[] { "a.log", "b.log" })]
    [InlineData("a.log b.log", new[] { "a.log", "b.log" })]
    public void 波かっこの中のカンマと空白では区切らない(string specification, string[] expected)
        => Assert.Equal(expected, FileSet.Split(specification));

    [Fact]
    public void 波かっこの選択肢は複数の指定とみる()
    {
        Assert.True(FileSet.IsMultiple("app.log{,.1}"));
        Assert.False(FileSet.IsMultiple(Path.Combine(_dir, "nothing{}.log")));
    }

    // ── ファイルの一覧 ─────────────────────────────

    [Fact]
    public void 展開した選択肢ごとにワイルドカードを当てる()
    {
        Make("a.txt", "b.log", "c.csv", "sub/d.txt", "sub/e.log");
        Assert.Equal(["a.txt", "sub/d.txt", "b.log", "sub/e.log"], Expand("**/*.{txt,log}"));
        Assert.Equal(["a.txt", "b.log", "c.csv"], Expand("*.{txt,log},*.csv"));
    }

    [Fact]
    public void 並びは選択肢の順で重複は一回()
    {
        Make("a.log", "b.log");
        Assert.Equal(["b.log", "a.log"], Expand("{b,a}.log"));
        Assert.Equal(["a.log", "b.log"], Expand("*.{log,log}"));
        Assert.Equal(["a.log", "b.log"], Expand("{a,*}.log"));
    }

    [Fact]
    public void 空の選択肢で平文と圧縮を一度に書ける()
    {
        Make("app.log", "app.log.1.gz", "app.log.2.gz", "other.log");
        Assert.Equal(["app.log", "app.log.1.gz", "app.log.2.gz"], Expand("app.log{,.*.gz}"));
    }

    [Fact]
    public void 名前に波かっこを含む実在ファイルを優先する()
    {
        Make("x{a,b}.log", "xa.log", "xb.log");
        Assert.Equal(["x{a,b}.log"], Expand("x{a,b}.log"));
        File.Delete(Path.Combine(_dir, "x{a,b}.log"));
        Assert.Equal(["xa.log", "xb.log"], Expand("x{a,b}.log"));
    }

    [Fact]
    public void 閉じていない波かっこと空の波かっこは文字どおりの名前()
    {
        Make("a{b.log", "c{}.log");
        Assert.Equal(["a{b.log"], Expand("a{b.log"));
        Assert.Equal(["c{}.log"], Expand("c{}.log"));
        Assert.Equal(["a{b.log", "c{}.log"], Expand("a{b.log,c{}.log"));
    }

    [Fact]
    public void ゼロ件の知らせは書いた指定のまま()
    {
        Make("a.txt");
        var all = Run("*.{csv,json}");
        Assert.Empty(all.Files);
        Assert.Equal(["*.{csv,json}"], all.Missing);
        // 選択肢の一部だけ 0 件なら知らせない
        var some = Run("*.{txt,json}");
        Assert.Single(some.Files);
        Assert.Empty(some.Missing);
    }

    [Fact]
    public void 波かっこの中の区切りもフォルダーをまたぐ()
    {
        Make("logs/2026-09/a.log", "logs/2026-10/b.log", "logs/2026-11/c.log");
        Assert.Equal(["logs/2026-09/a.log", "logs/2026-10/b.log"], Expand("logs/2026-{09,10}/*.log"));
    }

    [Fact]
    public void 大文字小文字の扱いは今までどおり()
    {
        Make("A.LOG", "b.txt");
        var found = Expand("*.{log,txt}");
        if (OperatingSystem.IsLinux()) Assert.Equal(["b.txt"], found);
        else Assert.Equal(["A.LOG", "b.txt"], found);
    }
}
