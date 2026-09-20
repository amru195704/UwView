using System.IO.Compression;
using System.Text;
using UwView.Core.Cli;

namespace UwView.Core.Tests;

/// <summary>
/// 無料版の CLI（uvf）。オーナー指示 2026-09-14 の<b>2つの形だけ</b>:
/// <code>
/// uvf -open [ファイル] [検索パターン]
/// uvf ファイル 検索パターン [-open]
/// </code>
/// 結果の正しさは素朴な参照実装（と、あれば grep -nF）と突き合わせる。
/// </summary>
public class UvfCliTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "uvf-cli-" + Guid.NewGuid().ToString("N"));

    public UvfCliTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string P(string name) => Path.Combine(_dir, name);

    private string WriteLog(string name = "app.log", int lines = 5_000)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < lines; i++)
            sb.Append($"{i:D6} {(i % 10 == 0 ? "ERROR" : i % 7 == 0 ? "error" : "INFO")} dev{i % 4} 東京 seq={i}\n");
        File.WriteAllText(P(name), sb.ToString());
        return P(name);
    }

    private sealed record Result(int Exit, string Out, string Err, List<(string? File, string? Pattern)> Launched);

    private static async Task<Result> Uvf(bool guiFound, params string[] args)
    {
        using var stdout = new MemoryStream();
        var stderr = new StringWriter();
        var launched = new List<(string?, string?)>();
        var env = new UvfEnvironment
        {
            StdOut = stdout, StdErr = stderr, Japanese = false,
            LaunchGui = (f, p) => { launched.Add((f, p)); return guiFound; },
        };
        int code = await UvfCli.RunAsync(args, env);
        return new Result(code, Encoding.UTF8.GetString(stdout.ToArray()), stderr.ToString(), launched);
    }

    private static Task<Result> Uvf(params string[] args) => Uvf(true, args);

    private static string Reference(string path, string pattern)
        => string.Concat(File.ReadAllLines(path)
            .Select((t, i) => (Line: i + 1, Text: t))
            .Where(r => r.Text.Contains(pattern, StringComparison.Ordinal))
            .Select(r => $"{r.Line}\t{r.Text}\n"));

    // ── 形 ────────────────────────────────────────────────

    [Theory]
    [InlineData(new[] { "-open" }, UvfMode.OpenGui, null, null)]
    [InlineData(new[] { "-open", "a.log" }, UvfMode.OpenGui, "a.log", null)]
    [InlineData(new[] { "-open", "a.log", "ERROR" }, UvfMode.OpenGui, "a.log", "ERROR")]
    [InlineData(new[] { "a.log", "ERROR" }, UvfMode.Search, "a.log", "ERROR")]
    [InlineData(new[] { "a.log", "ERROR", "-open" }, UvfMode.SearchInGui, "a.log", "ERROR")]
    public void 受け付けるのは2つの形だけ(string[] args, UvfMode mode, string? file, string? pattern)
    {
        var (inv, ja, _) = UvfCli.Parse(args);
        Assert.True(inv is not null, ja);
        Assert.Equal(new UvfInvocation(mode, file, pattern), inv);
    }

    [Theory]
    [InlineData()]                                         // 引数なし
    [InlineData("a.log")]                                  // パターンが無い
    [InlineData("a.log", "ERROR", "extra")]                // 多い
    [InlineData("a.log", "-open", "ERROR")]                // -open が途中
    [InlineData("-open", "a.log", "ERROR", "extra")]       // -open 形で多い
    [InlineData("-open", "a.log", "-open")]                // -open が2回
    public void それ以外の形はエラーにして使い方を出す(params string[] args)
    {
        var (inv, ja, en) = UvfCli.Parse(args);
        Assert.Null(inv);
        Assert.NotEmpty(ja!);
        Assert.NotEmpty(en!);
    }

    [Fact]
    public async Task 形が違えば終了コード2で使い方を出す()
    {
        var r = await Uvf("only-one-arg");
        Assert.Equal(UvfExit.Error, r.Exit);
        Assert.Contains("Usage", r.Err);
        Assert.Equal("", r.Out);
    }

    // ── 2) uvf ファイル 検索パターン（stdout）───────────────────

    [Fact]
    public async Task 検索結果は行番号と本文で出て参照実装と一致する()
    {
        string log = WriteLog();
        var r = await Uvf(log, "ERROR");
        Assert.Equal(UvfExit.Found, r.Exit);
        Assert.Equal(Reference(log, "ERROR"), r.Out);
        Assert.Empty(r.Launched);
    }

    [Fact]
    public async Task 大文字小文字を区別する普通の検索()
    {
        string log = WriteLog();
        // "error" は小文字の行だけ。"ERROR" の行には当てない（GUI の既定と同じ）
        var r = await Uvf(log, "error");
        Assert.Equal(Reference(log, "error"), r.Out);
        Assert.DoesNotContain("ERROR", r.Out);

        // 正規表現としては扱わない（. は文字そのもの）
        var dot = await Uvf(log, "seq=1.");
        Assert.Equal(Reference(log, "seq=1."), dot.Out);
        Assert.Equal(UvfExit.NotFound, dot.Exit);
    }

    [Fact]
    public async Task 日本語の検索も一致する()
    {
        string log = WriteLog(lines: 300);
        var r = await Uvf(log, "東京");
        Assert.Equal(Reference(log, "東京"), r.Out);
    }

    [Fact]
    public async Task grepの出力と一致する()
    {
        string log = WriteLog();
        var r = await Uvf(log, "dev3");
        string? grep = Grep(log, "dev3");
        if (grep is null) return;   // grep が無い環境では参照実装との一致で担保
        Assert.Equal(grep.Replace(':', '\t').Split('\n').Length, r.Out.Split('\n').Length);
        Assert.Equal(grep, ToGrepForm(r.Out));
    }

    [Fact]
    public async Task 長い行を切り詰めずに出す()
    {
        string longLine = "HEAD " + new string('x', 40_000) + " TAIL";
        File.WriteAllText(P("long.log"), "short\n" + longLine + "\nafter\n");
        var r = await Uvf(P("long.log"), "TAIL");
        Assert.Equal($"2\t{longLine}\n", r.Out);
    }

    [Fact]
    public async Task 終了コードは見つかった0_無し1_エラー2()
    {
        string log = WriteLog();
        Assert.Equal(UvfExit.Found, (await Uvf(log, "ERROR")).Exit);
        Assert.Equal(UvfExit.NotFound, (await Uvf(log, "NO_SUCH_WORD")).Exit);
        Assert.Equal(UvfExit.Error, (await Uvf(P("missing.log"), "ERROR")).Exit);
    }

    [Fact]
    public async Task データはstdoutだけで診断はstderr()
    {
        string log = WriteLog();
        var r = await Uvf(log, "ERROR");
        Assert.DoesNotContain("uvf:", r.Out);
        Assert.Contains("uvf:", r.Err);
    }

    [Fact]
    public async Task uwvzは無料版では開かない()
    {
        File.WriteAllText(P("x.log.uwvz"), "not really");
        var r = await Uvf(P("x.log.uwvz"), "ERROR");
        Assert.Equal(UvfExit.Error, r.Exit);
        Assert.Contains("UwView Pro", r.Err);
    }

    // ── --version / --help（uvp と同じ綴り。オーナー指摘 2026-09-21）────

    [Theory]
    [InlineData("--version")]
    [InlineData("-version")]
    public async Task 版数を聞かれたら出す(string flag)
    {
        var r = await Uvf(flag);
        Assert.Equal(UvfExit.Found, r.Exit);
        Assert.Equal("", r.Err);
        Assert.StartsWith("uvf ", r.Out);
        Assert.EndsWith("\n", r.Out);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public async Task 使い方を聞かれたら標準出力に出す(string flag)
    {
        var r = await Uvf(flag);
        Assert.Equal(UvfExit.Found, r.Exit);
        Assert.Contains("-open", r.Out);        // stdout に出す（エラーではない）
        Assert.Equal("", r.Err);
    }

    [Fact]
    public async Task 版数の指定と一緒に他の引数があれば従来どおり断る()
    {
        var r = await Uvf("--version", "extra");
        Assert.Equal(UvfExit.Error, r.Exit);
    }

    // ── gz は展開しながら検索する（オーナー指示 2026-09-19）────────────

    private string Gzip(string plainPath, string? name = null)
    {
        string gz = P(name ?? Path.GetFileName(plainPath) + ".gz");
        using var fs = File.Create(gz);
        using var z = new GZipStream(fs, CompressionLevel.Fastest);
        z.Write(File.ReadAllBytes(plainPath));
        return gz;
    }

    [Theory]
    [InlineData("ERROR")]
    [InlineData("東京")]
    public async Task gzは平文と同じ結果を出す(string pattern)
    {
        string log = WriteLog();
        string gz = Gzip(log);

        var plain = await Uvf(log, pattern);
        var fromGz = await Uvf(gz, pattern);
        Assert.Equal(UvfExit.Found, fromGz.Exit);
        Assert.Equal(Reference(log, pattern), fromGz.Out);
        Assert.Equal(plain.Out, fromGz.Out);
        Assert.False(File.Exists(P("app.log.gz.uwvz")) || File.Exists(P("app.log.uwvz")), "uvf はファイルを作らない");
    }

    [Theory]
    [InlineData("-i")]
    [InlineData("-E")]
    [InlineData("-v")]
    public async Task gzでもオプション付きの結果が平文と同じ(string option)
    {
        string log = WriteLog();
        string gz = Gzip(log);
        string pattern = option == "-E" ? "dev[12] 東京" : "error";

        var plain = await Uvf(log, pattern, option);
        var fromGz = await Uvf(gz, pattern, option);
        Assert.Equal(plain.Exit, fromGz.Exit);
        Assert.Equal(plain.Out, fromGz.Out);
    }

    [Fact]
    public async Task gzで手元に残す範囲を越える大きさでも平文と同じ()
    {
        // 展開したものは直近 64MB だけ手元に残す。それを何度も越える大きさで確かめる
        string log = WriteLog("big.log", 2_000_000);   // 約 90MB
        string gz = Gzip(log);

        var plain = await Uvf(log, "seq=1999999");
        var fromGz = await Uvf(gz, "seq=1999999");
        Assert.Equal(UvfExit.Found, fromGz.Exit);
        Assert.Equal(plain.Out, fromGz.Out);

        var countPlain = await Uvf(log, "ERROR");
        var countGz = await Uvf(gz, "ERROR");
        Assert.Equal(countPlain.Out, countGz.Out);
    }

    [Fact]
    public async Task gzの長い行と改行の無い最終行も平文と同じ()
    {
        string log = P("long.log");
        File.WriteAllText(log, "head\n" + new string('x', 6_000_000) + "TARGET" + new string('y', 100) + "\nmid TARGET\ntail TARGET");
        string gz = Gzip(log);

        var plain = await Uvf(log, "TARGET");
        var fromGz = await Uvf(gz, "TARGET");
        Assert.Equal(UvfExit.Found, fromGz.Exit);
        Assert.Equal(plain.Out, fromGz.Out);
        Assert.Equal(3, fromGz.Out.Count(c => c == '\n'));
    }

    [Fact]
    public async Task 連結したgzも全部探す()
    {
        string a = WriteLog("a.log", 3_000);
        string b = P("b.log");
        File.WriteAllText(b, "second member ERROR\n");
        string gz = P("cat.log.gz");
        await File.WriteAllBytesAsync(gz, [.. File.ReadAllBytes(Gzip(a)), .. File.ReadAllBytes(Gzip(b))]);

        var r = await Uvf(gz, "ERROR");
        Assert.Equal(UvfExit.Found, r.Exit);
        Assert.EndsWith("3001\tsecond member ERROR\n", r.Out);
    }

    [Fact]
    public async Task 途中で切れたgzはexit2で知らせる()
    {
        string gz = Gzip(WriteLog());
        byte[] whole = await File.ReadAllBytesAsync(gz);
        await File.WriteAllBytesAsync(gz, whole[..(whole.Length * 2 / 3)]);

        var r = await Uvf(gz, "ERROR");
        Assert.Equal(UvfExit.Error, r.Exit);
        // 「壊れている」とは言わない（元のファイルを消されかねない。オーナー指示 2026-09-21）
        Assert.Contains("could not be read to the end", r.Err);
        Assert.Contains("Please keep the original file", r.Err);
        Assert.DoesNotContain("corrupt", r.Err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task zipはGUIで開くよう案内する()
    {
        string zip = P("app.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var entry = new StreamWriter(archive.CreateEntry("app.log").Open()))
            entry.Write("ERROR here\n");

        var r = await Uvf(zip, "ERROR");
        Assert.Equal(UvfExit.Error, r.Exit);
        Assert.Contains("-open", r.Err);
        Assert.Equal("", r.Out);
    }

    // ── -open（GUI）─────────────────────────────────────────

    [Fact]
    public async Task 先頭のopenはGUIを起動してファイルとパターンを渡す()
    {
        string log = WriteLog();
        var none = await Uvf("-open");
        Assert.Equal(UvfExit.Found, none.Exit);
        Assert.Equal([(null, null)], none.Launched);

        var file = await Uvf("-open", log);
        Assert.Equal([(Path.GetFullPath(log), null)], file.Launched);

        var both = await Uvf("-open", log, "ERROR");
        Assert.Equal([(Path.GetFullPath(log), "ERROR")], both.Launched);
        Assert.Equal("", both.Out);
    }

    [Fact]
    public async Task 末尾のopenは結果をGUIで表示しstdoutには出さない()
    {
        string log = WriteLog();
        var r = await Uvf(log, "ERROR", "-open");
        Assert.Equal(UvfExit.Found, r.Exit);
        Assert.Equal([(Path.GetFullPath(log), "ERROR")], r.Launched);
        Assert.Equal("", r.Out);
    }

    [Fact]
    public async Task openで存在しないファイルはGUIを起動せずエラー()
    {
        var r = await Uvf("-open", P("missing.log"), "ERROR");
        Assert.Equal(UvfExit.Error, r.Exit);
        Assert.Empty(r.Launched);
    }

    [Fact]
    public async Task GUIが見つからなければエラーで案内する()
    {
        string log = WriteLog();
        var r = await Uvf(false, "-open", log);
        Assert.Equal(UvfExit.Error, r.Exit);
        Assert.Contains("not found", r.Err);
    }

    // ── 補助 ───────────────────────────────────────────────

    private static string ToGrepForm(string uvf)
        => string.Concat(uvf.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                            .Select(l => { int t = l.IndexOf('\t'); return l[..t] + ":" + l[(t + 1)..] + "\n"; }));

    private static string? Grep(string path, string pattern)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("/usr/bin/env")
            { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "grep", "-nF", pattern, path }) psi.ArgumentList.Add(a);
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return null;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode is 0 or 1 ? output : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }
}
