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

    // ── 複数ファイル（Wide Field v1.7.0 段階3）──────────────────────

    /// <summary>a.log / b.log / logs/c.log を作る（中身は行番号が分かる形）。</summary>
    private void MakeSet()
    {
        foreach (string name in new[] { "a.log", "b.log" })
            File.WriteAllText(P(name), $"1 {name} INFO\n2 {name} ERROR\n3 {name} INFO\n");
        Directory.CreateDirectory(P("logs"));
        File.WriteAllText(P("logs/c.log"), "1 c INFO\n2 c ERROR\n");
    }

    private async Task<Result> InDir(params string[] args)
    {
        string saved = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_dir);
        try { return await Uvf(args); }
        finally { Directory.SetCurrentDirectory(saved); }
    }

    [Theory]
    [InlineData("a.log b.log")]
    [InlineData("a.log,b.log")]
    [InlineData("*.log")]
    public async Task 複数ファイルはファイル名を前置して指定順に出す(string specification)
    {
        MakeSet();
        var run = await InDir(specification, "ERROR");
        Assert.Equal(UvfExit.Found, run.Exit);
        Assert.Equal("a.log:2\t2 a.log ERROR\nb.log:2\t2 b.log ERROR\n", run.Out);
    }

    [Fact]
    public async Task 書いた順を守る()
    {
        MakeSet();
        var run = await InDir("b.log a.log", "ERROR");
        Assert.StartsWith("b.log:2", run.Out);
        Assert.Contains("\na.log:2", run.Out);
    }

    [Fact]
    public async Task 階層付きのワイルドカードも探せる()
    {
        MakeSet();
        // 1件しか当たらなければ、grep と同じくファイル名は前置しない（付けたいときは -H）
        var run = await InDir("*/*.log", "ERROR");
        Assert.Equal("2\t2 c ERROR\n", run.Out);
        var forced = await InDir("*/*.log", "ERROR", "-H");
        Assert.Equal("logs/c.log:2\t2 c ERROR\n", forced.Out.Replace('\\', '/'));
    }

    [Fact]
    public async Task 単一ファイルの出力は今までどおり()
    {
        MakeSet();
        var run = await InDir("a.log", "ERROR");
        Assert.Equal("2\t2 a.log ERROR\n", run.Out);      // ファイル名を付けない
    }

    [Fact]
    public async Task ファイル名を付けるか付けないかを指定できる()
    {
        MakeSet();
        var always = await InDir("a.log", "ERROR", "-H");
        Assert.Equal("a.log:2\t2 a.log ERROR\n", always.Out);

        var never = await InDir("a.log b.log", "ERROR", "-h");
        Assert.Equal("2\t2 a.log ERROR\n2\t2 b.log ERROR\n", never.Out);
    }

    [Fact]
    public async Task 何に広がるかだけを出せる()
    {
        MakeSet();
        var run = await InDir("*.log", "--files");
        Assert.Equal(UvfExit.Found, run.Exit);
        Assert.Equal("1\ta.log\n2\tb.log\n", run.Out);
    }

    [Fact]
    public async Task シェルが展開した形は引用符を促して断る()
    {
        MakeSet();
        var run = await InDir("a.log", "b.log", "ERROR");
        Assert.Equal(UvfExit.Error, run.Exit);
        Assert.Contains("Quote them", run.Err);   // テストは英語表示
        Assert.Equal("", run.Out);
    }

    [Fact]
    public async Task 当たらない断片は知らせて残りを探す()
    {
        MakeSet();
        var run = await InDir("a.log nope.log", "ERROR");
        Assert.Equal(UvfExit.Found, run.Exit);
        Assert.Contains("nope.log", run.Err);
        Assert.Equal("2\t2 a.log ERROR\n", run.Out);   // 残ったのが1件なので前置しない
    }

    [Fact]
    public async Task どの断片も当たらなければエラーにする()
    {
        MakeSet();
        var run = await InDir("*.none", "ERROR");
        Assert.Equal(UvfExit.Error, run.Exit);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task 後ろのファイルが先に来ても止まらない(int threads)
    {
        // 到着順を逆にして、順番待ちの噛み合いを起こす（実機で起きた形。下のテストの説明を参照）
        MultiFileSearch.ArrivalDelayForTests = i => Task.Delay(20 * (6 - i));
        try { await ファイル数よりスレッドが少なくても止まらない本体(threads); }
        finally { MultiFileSearch.ArrivalDelayForTests = null; }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public Task ファイル数よりスレッドが少なくても止まらない(int threads)
        => ファイル数よりスレッドが少なくても止まらない本体(threads);

    private async Task ファイル数よりスレッドが少なくても止まらない本体(int threads)
    {
        // オーナーの比較テストで発覚（2026-09-22・UVF_MAX_THREADS=1 で6ファイル → 固まった）。
        // 出す順番を守るために「自分の番」を待つが、順番待ちの間も枠を握っていたため、
        // 先の番のファイルが枠を取れずに永久に待ち合う
        MakeSet();
        for (int i = 0; i < 4; i++) File.WriteAllText(P($"extra-{i}.log"), $"1 x INFO\n2 extra{i} ERROR\n");

        var run = await WithThreads(threads, () => InDir("*.log", "ERROR"))
                        .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(UvfExit.Found, run.Exit);
        Assert.Equal(6, run.Out.TrimEnd('\n').Split('\n').Length);   // a b extra0..3（順番どおり）
        Assert.StartsWith("a.log:2\t", run.Out);
    }

    /// <summary>スレッド数を指定して走らせる（環境変数は後で必ず戻す）。</summary>
    private static async Task<Result> WithThreads(int threads, Func<Task<Result>> run)
    {
        string? saved = Environment.GetEnvironmentVariable(ThreadBudget.FreeEnvironmentVariable);
        Environment.SetEnvironmentVariable(ThreadBudget.FreeEnvironmentVariable, threads.ToString());
        try { return await run(); }
        finally { Environment.SetEnvironmentVariable(ThreadBudget.FreeEnvironmentVariable, saved); }
    }

    private void WriteGz(string name, string text)
    {
        using var gz = new System.IO.Compression.GZipStream(File.Create(P(name)),
                                                            System.IO.Compression.CompressionLevel.Fastest);
        gz.Write(Encoding.UTF8.GetBytes(text));
    }

    [Fact]
    public async Task 平文とgzを混ぜて探せる()
    {
        // 段階5（オーナー指示 2026-09-23「uvf でも '*.log,*.gz' を」）
        MakeSet();
        WriteGz("c.log.gz", "1 gz INFO\n2 gz ERROR\n");

        var run = await InDir("*.log,*.gz", "ERROR");

        Assert.Equal(UvfExit.Found, run.Exit);
        Assert.Equal("a.log:2\t2 a.log ERROR\nb.log:2\t2 b.log ERROR\nc.log.gz:2\t2 gz ERROR\n", run.Out);
    }

    [Fact]
    public async Task zipは黙って素通りさせない()
    {
        // 平文として走査すると1件も当たらず、「このログにエラーは無い」と誤読させる（2026-09-22）
        MakeSet();
        using (var zip = System.IO.Compression.ZipFile.Open(P("c.zip"), System.IO.Compression.ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("c.log").Open())) w.Write("1 zip ERROR\n");

        var run = await InDir("*", "ERROR");

        Assert.Equal(UvfExit.Error, run.Exit);                    // 探せなかったものがある
        Assert.Contains("c.zip", run.Err);
        Assert.Contains("a.log:2\t", run.Out);                    // 平文のぶんは出す
    }

    [Theory]
    [InlineData("1")]   // OS の zlib
    [InlineData("0")]   // .NET の展開（切れていても例外を出さないので、末尾と照らして気づく）
    public async Task 途中で切れたgzは知らせてexit2(string systemZlib)
    {
        MakeSet();
        var sb = new StringBuilder();
        for (int i = 0; i < 20_000; i++) sb.Append($"{i} gz ERROR payload=xxxxxxxxxxxxxxxx\n");
        WriteGz("c.log.gz", sb.ToString());
        byte[] bytes = File.ReadAllBytes(P("c.log.gz"));
        File.WriteAllBytes(P("c.log.gz"), bytes[..(bytes.Length * 2 / 3)]);

        string? saved = Environment.GetEnvironmentVariable("UWVIEW_SYSTEM_ZLIB");
        Environment.SetEnvironmentVariable("UWVIEW_SYSTEM_ZLIB", systemZlib);
        try
        {
            var run = await InDir("*.log,*.gz", "ERROR");
            Assert.Equal(UvfExit.Error, run.Exit);
            Assert.Contains("c.log.gz", run.Err);
            Assert.Contains("a.log:2\t", run.Out);               // 読めたぶんは出す
        }
        finally { Environment.SetEnvironmentVariable("UWVIEW_SYSTEM_ZLIB", saved); }
    }

    /// <summary>
    /// 複数ファイルを画面で開くのは Pro の役目（uvp が1つの .uwvz に束ねる）。
    /// 無料版が黙って受けると、ワイルドカードのままのパスを開こうとして何も出ない
    /// （オーナー指摘 2026-09-23「uvfWF 'osm17/*.osm' '東京' -open はできないの？」）。
    /// </summary>
    [Fact]
    public async Task 複数ファイルを画面で開こうとしたら断ってuvpを案内する()
    {
        MakeSet();
        var run = await InDir("a.log b.log", "ERROR", "-open");
        Assert.Equal(UvfExit.Error, run.Exit);
        Assert.Empty(run.Launched);                        // 画面は起こさない
        Assert.Contains("uvp", run.Err);                   // 束ねられる方を案内する
        Assert.Contains("-open", run.Err);
    }

    [Fact]
    public async Task 複数ファイルで見つからなければ1を返す()
    {
        MakeSet();
        var run = await InDir("a.log b.log", "NOSUCHWORD");
        Assert.Equal(UvfExit.NotFound, run.Exit);
        Assert.Equal("", run.Out);
    }

    [Fact]
    public async Task 複数ファイルのJSONにはファイル名が入る()
    {
        MakeSet();
        var run = await InDir("a.log b.log", "ERROR", "--json");
        var first = System.Text.Json.JsonDocument.Parse(run.Out.Split('\n')[0]).RootElement;
        Assert.Equal("a.log", first.GetProperty("file").GetString());
        Assert.Equal(2, first.GetProperty("n").GetInt64());
    }

    // ── --json（JSON Lines・Wide Field v1.7.0 段階2）────────────────

    [Theory]
    [InlineData("ERROR")]
    [InlineData("error", "-i")]
    [InlineData("dev[12]", "-E")]
    [InlineData("ERROR", "-v")]
    public async Task JSONはテキスト出力と同じ中身になる(string pattern, string? option = null)
    {
        string log = WriteLog();
        string[] args = option is null ? [log, pattern] : [log, pattern, option];

        var text = await Uvf(args);
        var json = await Uvf([.. args, "--json"]);

        var back = json.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => System.Text.Json.JsonDocument.Parse(line).RootElement)
            .Select(o => $"{o.GetProperty("n").GetInt64()}\t{o.GetProperty("line").GetString()}\n");
        Assert.Equal(text.Out, string.Concat(back));
    }

    [Fact]
    public async Task JSONは1行に1つのオブジェクトで日本語はそのまま出す()
    {
        string path = P("ja.log");
        File.WriteAllText(path, "1行目 東京\n2行目 大阪\n");

        var run = await Uvf(path, "東京", "--json");
        Assert.Equal(UvfExit.Found, run.Exit);
        Assert.Equal("""{"n":1,"line":"1行目 東京"}""" + "\n", run.Out);
    }

    [Fact]
    public async Task JSONと画面表示は一緒に使えない()
    {
        string log = WriteLog();
        var run = await Uvf(log, "ERROR", "--json", "-open");
        Assert.Equal(UvfExit.Error, run.Exit);
        Assert.Contains("--json", run.Err);
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
