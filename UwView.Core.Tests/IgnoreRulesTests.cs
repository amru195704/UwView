using UwView.Core.Cli;

namespace UwView.Core.Tests;

/// <summary>
/// .ignore・.gitignore に従う（指示書 WideField v1.7 §12）。書式（§12.3）は1行ずつ、強さと効く範囲（§12.1・§12.2）は
/// 小さなフォルダーの木で確かめる。突き合わせ（rg --files と同じ一覧になるか）は UwTest/uv_ignore_test.sh。
/// </summary>
public class IgnoreRulesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "uvf-ignore-" + Guid.NewGuid().ToString("N"));

    public IgnoreRulesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private IgnoreMatch Match(string rules, string relative, bool isDirectory = false)
        => IgnoreFile.Parse(rules, _dir).Match(Path.Combine(_dir, relative), isDirectory);

    // ── 書式（§12.3）──

    [Theory]
    [InlineData("*.log", "a.log", IgnoreMatch.Ignore)]
    [InlineData("*.log", "deep/er/a.log", IgnoreMatch.Ignore)]          // 名前だけ＝どの深さにも当たる
    [InlineData("*.log", "a.txt", IgnoreMatch.None)]
    [InlineData("/build", "build", IgnoreMatch.Ignore)]
    [InlineData("/build", "src/build", IgnoreMatch.None)]               // 先頭 / で直下だけ
    [InlineData("a/b.log", "a/b.log", IgnoreMatch.Ignore)]
    [InlineData("a/b.log", "x/a/b.log", IgnoreMatch.None)]              // 途中に / があればこのフォルダーから
    [InlineData("doc/*.md", "doc/x.md", IgnoreMatch.Ignore)]
    [InlineData("doc/*.md", "doc/sub/x.md", IgnoreMatch.None)]          // * は / をまたがない
    [InlineData("**/tmp", "a/b/tmp", IgnoreMatch.Ignore)]
    [InlineData("**/tmp", "tmp", IgnoreMatch.Ignore)]
    [InlineData("out/**", "out/a/b.txt", IgnoreMatch.Ignore)]
    [InlineData("out/**", "out", IgnoreMatch.None)]                     // 中のものだけ
    [InlineData("a/**/b", "a/b", IgnoreMatch.Ignore)]
    [InlineData("a/**/b", "a/x/y/b", IgnoreMatch.Ignore)]
    [InlineData("file?.txt", "file1.txt", IgnoreMatch.Ignore)]
    [InlineData("file?.txt", "file10.txt", IgnoreMatch.None)]
    [InlineData("[a-c].txt", "b.txt", IgnoreMatch.Ignore)]
    [InlineData("[a-c].txt", "d.txt", IgnoreMatch.None)]
    [InlineData("[!a-c].txt", "d.txt", IgnoreMatch.Ignore)]
    [InlineData("[!a-c].txt", "a.txt", IgnoreMatch.None)]
    [InlineData("\\#note", "#note", IgnoreMatch.Ignore)]                // \# で # そのもの
    [InlineData("#note", "#note", IgnoreMatch.None)]                    // # で始まる行は注釈
    [InlineData("\\!keep", "!keep", IgnoreMatch.Ignore)]
    [InlineData("a.log   ", "a.log", IgnoreMatch.Ignore)]               // 行末の空白は無視
    [InlineData("a\\ ", "a ", IgnoreMatch.Ignore)]                      // \ で空白そのもの
    [InlineData("A.LOG", "a.log", IgnoreMatch.None)]                    // 大文字・小文字は区別する
    public void 書式(string rules, string path, IgnoreMatch expected)
        => Assert.Equal(expected, Match(rules, path));

    [Fact]
    public void 末尾のスラッシュはフォルダーだけに当たる()
    {
        Assert.Equal(IgnoreMatch.Ignore, Match("build/", "build", isDirectory: true));
        Assert.Equal(IgnoreMatch.None, Match("build/", "build", isDirectory: false));
    }

    [Fact]
    public void 後の行が勝ち感嘆符で取り消す()
    {
        Assert.Equal(IgnoreMatch.Include, Match("*.log\n!keep.log", "keep.log"));
        Assert.Equal(IgnoreMatch.Ignore, Match("*.log\n!keep.log", "drop.log"));
        Assert.Equal(IgnoreMatch.Ignore, Match("!keep.log\n*.log", "keep.log"));
    }

    [Fact]
    public void CRLFとBOMも読む()
    {
        var file = IgnoreFile.Parse("﻿*.log\r\n!keep.log\r\n", _dir);
        Assert.Equal(IgnoreMatch.Ignore, file.Match(Path.Combine(_dir, "a.log"), false));
        Assert.Equal(IgnoreMatch.Include, file.Match(Path.Combine(_dir, "keep.log"), false));
    }

    [Theory]
    // 名前・拡張子の規則は辞書で引き、ほかの規則とあわせて「後の行が勝つ」（v1.7.3.6.3）
    [InlineData("*.log\n!keep.log", "keep.log", IgnoreMatch.Include)]
    [InlineData("!keep.log\n*.log", "keep.log", IgnoreMatch.Ignore)]
    [InlineData("keep.log\n!*.log", "keep.log", IgnoreMatch.Include)]
    [InlineData("!*.log\nkeep.log", "deep/keep.log", IgnoreMatch.Ignore)]
    [InlineData("*.log\n!k*.log", "keep.log", IgnoreMatch.Include)]
    [InlineData("!k*.log\n*.log", "keep.log", IgnoreMatch.Ignore)]
    [InlineData("*.gz\n!a.tar.gz", "a.tar.gz", IgnoreMatch.Include)]
    [InlineData("*.o\nbuild/", "build", IgnoreMatch.None)]              // フォルダー専用はファイルに当てない
    [InlineData("!build\nbuild/", "build", IgnoreMatch.Include)]
    [InlineData("*.o", "x.o.txt", IgnoreMatch.None)]
    [InlineData("*.o", ".o", IgnoreMatch.Ignore)]
    [InlineData("*.mod.c", "a.mod.c", IgnoreMatch.Ignore)]
    [InlineData("Makefile", "sub/Makefile", IgnoreMatch.Ignore)]
    public void 名前と拡張子の規則も後の行が勝つ(string rules, string relative, IgnoreMatch expected)
        => Assert.Equal(expected, Match(rules, relative));

    [Fact]
    public void フォルダー専用の規則はフォルダーに当てる()
    {
        Assert.Equal(IgnoreMatch.Ignore, Match("!build\nbuild/", "build", isDirectory: true));
        Assert.Equal(IgnoreMatch.Ignore, Match("*.d/", "x.d", isDirectory: true));
        Assert.Equal(IgnoreMatch.None, Match("*.d/", "x.d"));
    }

    [Fact]
    public void フォルダーの外はパス全体で照らす()
    {
        // ripgrep と同じ: 名前だけの規則はどこにでも当たり、/ で固定した規則は外には当たらない
        Assert.Equal(IgnoreMatch.Ignore, IgnoreFile.Parse("*.log", Path.Combine(_dir, "sub")).Match(Path.Combine(_dir, "a.log"), false));
        Assert.Equal(IgnoreMatch.None, IgnoreFile.Parse("/a.log", Path.Combine(_dir, "sub")).Match(Path.Combine(_dir, "a.log"), false));
    }

    // ── 木で確かめる（§12.1・§12.2）──

    private void Touch(string relative, string text = "x\n")
    {
        string path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private string[] Listed(string spec, IgnoreOptions? ignore = null)
        => [.. FileSet.Expand(spec, _dir, ignore).Files.Select(f => f.Replace('\\', '/'))];

    [Fact]
    public void gitignoreはgitのリポジトリの中だけで効く()
    {
        Touch(".gitignore", "*.log\n");
        Touch("a.log");
        Touch("b.txt");
        Assert.Equal(["a.log", "b.txt"], Listed("**"));        // .git が無い＝効かない

        Directory.CreateDirectory(Path.Combine(_dir, ".git"));
        Assert.Equal(["b.txt"], Listed("**"));
    }

    [Fact]
    public void ignoreはリポジトリの外でも効く()
    {
        Touch(".ignore", "*.log\n");
        Touch("a.log");
        Touch("b.txt");
        Assert.Equal(["b.txt"], Listed("**"));
    }

    [Fact]
    public void 除外したフォルダーには降りず中の感嘆符でも戻せない()
    {
        Directory.CreateDirectory(Path.Combine(_dir, ".git"));
        Touch(".gitignore", "build/\n!build/keep.log\n");
        Touch("build/keep.log");
        Touch("build/other.log");
        Touch("src/a.cs");
        var result = FileSet.Expand("**", _dir);

        Assert.Equal(["src/a.cs"], result.Files.Select(f => f.Replace('\\', '/')));
        Assert.Equal(0, result.IgnoredFiles);
        Assert.Equal(1, result.IgnoredFolders);   // 中は見ていない
    }

    [Fact]
    public void 深いフォルダーのほうが勝つ()
    {
        Directory.CreateDirectory(Path.Combine(_dir, ".git"));
        Touch(".gitignore", "*.log\n");
        Touch("sub/.gitignore", "!keep.log\n");
        Touch("sub/keep.log");
        Touch("sub/drop.log");
        Touch("top.log");
        Assert.Equal(["sub/keep.log"], Listed("**"));
    }

    [Fact]
    public void ignoreはgitignoreより強い()
    {
        Directory.CreateDirectory(Path.Combine(_dir, ".git"));
        Touch(".gitignore", "*.log\n");
        Touch(".ignore", "!keep.log\n");
        Touch("keep.log");
        Touch("drop.log");
        Assert.Equal(["keep.log"], Listed("**"));
    }

    [Fact]
    public void git_info_excludeも読む()
    {
        Directory.CreateDirectory(Path.Combine(_dir, ".git", "info"));
        File.WriteAllText(Path.Combine(_dir, ".git", "info", "exclude"), "*.tmp\n");
        Touch("a.tmp");
        Touch("b.txt");
        Assert.Equal(["b.txt"], Listed("**"));
    }

    [Fact]
    public void 親フォルダーの除外も効く()
    {
        Touch(".ignore", "*.log\n");
        Touch("sub/a.log");
        Touch("sub/b.txt");
        Assert.Equal(["b.txt"], [.. FileSet.Expand("*", Path.Combine(_dir, "sub")).Files]);
    }

    /// <summary>
    /// --no-ignore は .ignore・.gitignore 類だけを止め、--ignore-file の規則は効いたまま（ripgrep 15 と同じ）。
    /// 足した規則も止めるのは --no-ignore-files（外部レビュー 2026-09-27 の指摘で合わせた）。
    /// </summary>
    [Fact]
    public void 足したファイルは一番弱く_no_ignoreでも効き_no_ignore_filesで止まる()
    {
        Touch(".ignore", "!keep.log\n");
        Touch("keep.log");
        Touch("drop.log");
        Touch("b.txt");
        string extra = Path.Combine(_dir, "..", Path.GetFileName(_dir) + ".extra-ignore");
        File.WriteAllText(extra, "*.log\n");
        try
        {
            Assert.Equal(["b.txt", "keep.log"], Listed("**", new IgnoreOptions(ExtraFiles: [extra])));   // .ignore の ! が勝つ
            Assert.Equal(["b.txt"], Listed("**", new IgnoreOptions(Enabled: false, ExtraFiles: [extra])));   // .ignore の ! も止まり、*.log が効く
            Assert.Equal(["b.txt", "drop.log", "keep.log"],
                         Listed("**", new IgnoreOptions(Enabled: false, ExtraFiles: [extra], ExtraEnabled: false)));
            Assert.Equal(["b.txt", "drop.log", "keep.log"],
                         Listed("**", new IgnoreOptions(ExtraFiles: [extra], ExtraEnabled: false)));   // 足した規則だけ止める
        }
        finally { File.Delete(extra); }
    }

    [Fact]
    public void 名前を書いたファイルは除外しない()
    {
        Touch(".ignore", "*.log\n");
        Touch("a.log");
        Touch("b.log");
        Assert.Equal(["a.log", "b.log"], Listed("a.log,b.log"));
        Assert.Empty(Listed("*.log"));
    }

    [Fact]
    public void 名前を書いたフォルダーの中は除外の規則で見る()
    {
        // 'build/*.log' の build は名前を書いたフォルダー（除外しない）。中のファイルは規則で見る（ripgrep と同じ）
        Touch(".ignore", "build/\n*.tmp\n");
        Touch("build/a.log");
        Touch("build/b.tmp");
        Assert.Equal(["build/a.log"], Listed("build/*"));
    }

    [Fact]
    public void 除外で一件も当たらなくなった指定を知らせる()
    {
        Touch(".ignore", "*.log\n");
        Touch("a.log");
        var result = FileSet.Expand("*.log,*.txt", _dir);

        Assert.Equal(["*.log", "*.txt"], result.Missing);
        Assert.Equal(["*.log"], result.MissingByIgnore);
        Assert.Contains("--no-ignore", result.MissingNotice("*.log", ja: true, "uvf"));
        Assert.DoesNotContain("--no-ignore", result.MissingNotice("*.txt", ja: true, "uvf"));
        Assert.Contains("除外 1 本", result.IgnoredNotice(ja: true, "uvf"));
        Assert.Contains("1 files excluded", result.IgnoredNotice(ja: false, "uvf"));
    }

    [Fact]
    public void 引数から除外の指定を取り出す()
    {
        var (options, rest, error, _) = IgnoreOptions.Take(["--no-ignore", "a.log", "--ignore-file", "x.ignore", "語", "--ignore-file", "y", "--no-ignore-files"]);
        Assert.Null(error);
        Assert.Equal(["a.log", "語"], rest);
        Assert.False(options!.Enabled);
        Assert.False(options.ExtraEnabled);
        Assert.Equal(["x.ignore", "y"], options.ExtraFiles);

        Assert.Null(IgnoreOptions.Take(["a.log", "語"]).Options);   // 何も無ければ既定（null）
        Assert.NotNull(IgnoreOptions.Take(["a.log", "--ignore-file"]).ErrorJa);
    }

    [Fact]
    public async Task uvfのfilesは除外した本数をstderrに出す()
    {
        Touch(".ignore", "*.log\n");
        Touch("a.log");
        Touch("b.txt");
        using var stdout = new MemoryStream();
        using var stderr = new StringWriter();
        string previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_dir);
        try
        {
            int code = await UvfCli.RunAsync(["*", "--files"], new UvfEnvironment { StdOut = stdout, StdErr = stderr, Japanese = false });
            Assert.Equal(0, code);
            Assert.Equal("1\tb.txt\n", System.Text.Encoding.UTF8.GetString(stdout.ToArray()).ReplaceLineEndings("\n"));
            Assert.Contains("1 files excluded", stderr.ToString());

            stdout.SetLength(0);
            code = await UvfCli.RunAsync(["*", "--files", "--no-ignore"], new UvfEnvironment { StdOut = stdout, StdErr = new StringWriter(), Japanese = false });
            Assert.Equal("1\ta.log\n2\tb.txt\n", System.Text.Encoding.UTF8.GetString(stdout.ToArray()).ReplaceLineEndings("\n"));
        }
        finally { Directory.SetCurrentDirectory(previous); }
    }

    /// <summary>--ignore-file を使ったときは、知らせに --no-ignore-files も添える（--no-ignore では足した規則が止まらないため）。</summary>
    [Fact]
    public void 足した規則があるときは知らせにno_ignore_filesを添える()
    {
        Touch("a.log");
        Touch("b.txt");
        string extra = Path.Combine(_dir, "..", Path.GetFileName(_dir) + ".extra2");
        File.WriteAllText(extra, "*.log\n");
        try
        {
            var with = FileSet.Expand("*.log,*.txt", _dir, new IgnoreOptions(ExtraFiles: [extra]));
            Assert.Contains("--no-ignore-files", with.IgnoredNotice(ja: true, "uvf"));
            Assert.Contains("--no-ignore-files", with.MissingNotice("*.log", ja: false, "uvf"));
            var without = FileSet.Expand("*.log", _dir);
            Assert.Null(without.IgnoredNotice(ja: true, "uvf"));
        }
        finally { File.Delete(extra); }
    }
}
