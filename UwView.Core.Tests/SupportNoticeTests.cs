using System.Text;
using UwView.Core.Cli;

namespace UwView.Core.Tests;

/// <summary>
/// 支援の案内（指示-20261113-05）: 起動したときの手順（<see cref="SupportPrompt.Begin"/>）と、
/// CLI（uvf）の <c>--version</c>／<c>--help</c> の最後の 1 行。
/// 検査課の確認項目: ①CLI の通常の出力に何も混ざらない ②表示条件どおりに出る ③設定で止まる。
/// </summary>
public class SupportNoticeTests : IDisposable
{
    private static readonly DateOnly Start = new(2026, 11, 25);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "uvf-support-" + Guid.NewGuid().ToString("N"));

    public SupportNoticeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    // ── 起動したときの手順（画面の帯はこれで出すかを決める）────

    /// <summary>年末の期間にかからない始まりの日（3/1）。</summary>
    private static readonly DateOnly Spring = new(2026, 3, 1);

    /// <summary>start から days 日、毎日 1 回起動したとして、出た帯の一覧。</summary>
    private static (SupportPromptState State, List<(DateOnly Day, SupportPromptKind Kind)> Shown) LaunchEveryDay(DateOnly start, int days)
    {
        var s = new SupportPromptState();
        var shown = new List<(DateOnly, SupportPromptKind)>();
        for (int i = 0; i < days; i++)
        {
            var day = start.AddDays(i);
            var kind = SupportPrompt.Begin(s, day, out _);
            if (kind != SupportPromptKind.None) shown.Add((day, kind));
        }
        return (s, shown);
    }

    [Fact]
    public void 毎日起動すると30日目に最初の帯が1回だけ出る()
    {
        var (_, shown) = LaunchEveryDay(Spring, 30);
        Assert.Empty(shown);
        (_, shown) = LaunchEveryDay(Spring, 31);
        Assert.Single(shown);
        Assert.Equal((Spring.AddDays(30), SupportPromptKind.First), shown[0]);
    }

    [Fact]
    public void 帯を出した時点で記録するので閉じただけでも後でと同じ()
    {
        var (s, _) = LaunchEveryDay(Spring, 31);
        Assert.Equal(1, s.ShownCount);
        Assert.Equal(Spring.AddDays(30).DayNumber, s.LastShownDay);
        // 次の日に起動しても出ない（ボタンを押さずに閉じた＝［後で］）。90 日後に 1 回だけ
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Begin(s, Spring.AddDays(31), out _));
        Assert.Equal(SupportPromptKind.Reminder, SupportPrompt.Begin(s, Spring.AddDays(120), out _));
    }

    [Fact]
    public void 二十五日に入れて毎日使うと年末の呼びかけが出る()
    {
        // 11/25 に 2.0 を入れて毎日使う → 12/25 に「30 日以上・10 日以上」を満たす。期間（〜1/10）の中なので呼びかけ
        var (_, shown) = LaunchEveryDay(Start, 60);
        Assert.Equal(SupportPromptKind.Season, shown[0].Kind);
        Assert.Equal(new DateOnly(2026, 12, 25), shown[0].Day);
        Assert.Single(shown);   // 期間中に 2 回は出ない（次は 90 日後）
    }

    [Fact]
    public void 一日に何度起動しても数えるのは一回で保存の要否も分かる()
    {
        var s = new SupportPromptState();
        SupportPrompt.Begin(s, Start, out bool first);
        SupportPrompt.Begin(s, Start, out bool second);
        Assert.True(first);
        Assert.False(second);
        Assert.Equal(1, s.UsedDays);
    }

    [Fact]
    public void もう表示しないと支援するを押した人には出さない()
    {
        var a = new SupportPromptState();
        var b = new SupportPromptState();
        for (int i = 0; i < 20; i++) { SupportPrompt.RecordUse(a, Start.AddDays(i)); SupportPrompt.RecordUse(b, Start.AddDays(i)); }
        SupportPrompt.MarkNeverShow(a);
        SupportPrompt.MarkSupported(b);
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Begin(a, Start.AddDays(40), out _));
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Begin(b, Start.AddDays(40), out _));
    }

    // ── 文面（金額を書かない・受け皿は Sponsors だけ）────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 文面に金額を書かない(bool ja)
    {
        var all = new List<string>
        {
            SupportPromptText.Title(SupportPromptKind.First, ja), SupportPromptText.Title(SupportPromptKind.Season, ja),
            SupportPromptText.Body1(SupportPromptKind.First, ja), SupportPromptText.Body1(SupportPromptKind.Season, ja),
            SupportPromptText.Body2(ja), SupportPromptText.Body3(ja), SupportPromptText.ProLine(ja),
            SupportPromptText.SupportButton(ja), SupportPromptText.LaterButton(ja), SupportPromptText.NeverButton(ja),
            SupportPromptText.SettingLabel(ja), SupportPromptText.SettingHint(ja), SupportPromptText.CliLine(ja),
        };
        foreach (string t in all)
            Assert.DoesNotMatch(@"\$[0-9]|[0-9,]+円|¥", t);
        Assert.EndsWith(SupportPrompt.SponsorsUrl, SupportPromptText.CliLine(ja));   // URL は行末
        Assert.DoesNotContain("\n", SupportPromptText.CliLine(ja));                   // 1 行
    }

    // ── CLI の 1 行 ────

    private sealed record Result(int Exit, string Out, string Err);

    private static async Task<Result> Run(bool supportNotice, string settingsFolder, bool ja, params string[] args)
    {
        using var stdout = new MemoryStream();
        var stderr = new StringWriter();
        var env = new UvfEnvironment
        {
            StdOut = stdout, StdErr = stderr, Japanese = ja,
            AppVersion = "2.0.0", BuildNumber = "26.11.20.10",
            SupportNotice = supportNotice,
            SettingsFolder = settingsFolder,
            LaunchGui = (_, _) => true,
        };
        int code = await UvfCli.RunAsync(args, env);
        return new Result(code, Encoding.UTF8.GetString(stdout.ToArray()), stderr.ToString());
    }

    /// <summary>設定ファイルの無いフォルダ名（読むだけ。何も作らない）。</summary>
    private static string NoSettings() => "UwView-no-settings-" + Guid.NewGuid().ToString("N");

    [Theory]
    [InlineData("--version", true)]
    [InlineData("--version", false)]
    [InlineData("--help", true)]
    [InlineData("-h", false)]
    public async Task 版数と使い方の最後に1行だけ出す(string flag, bool ja)
    {
        var r = await Run(true, NoSettings(), ja, flag);
        Assert.Equal(UvfExit.Found, r.Exit);
        Assert.Equal("", r.Err);
        Assert.EndsWith(SupportPromptText.CliLine(ja) + "\n", r.Out);
        Assert.Equal(1, CountOccurrences(r.Out, SupportPrompt.SponsorsUrl));
    }

    [Fact]
    public async Task 版数の1行目は変えない()
    {
        var r = await Run(true, NoSettings(), false, "--version");
        Assert.Equal("uvf 2.0.0 (build 26.11.20.10)\n" + SupportPromptText.CliLine(false) + "\n", r.Out);
    }

    [Fact]
    public async Task 通常の検索の出力には混ざらない()
    {
        string log = Path.Combine(_dir, "app.log");
        File.WriteAllText(log, "INFO a\nERROR b\nINFO c\n");
        var r = await Run(true, NoSettings(), true, log, "ERROR");
        Assert.Equal(UvfExit.Found, r.Exit);
        Assert.DoesNotContain(SupportPrompt.SponsorsUrl, r.Out);
        Assert.DoesNotContain(SupportPrompt.SponsorsUrl, r.Err);
        Assert.Equal("2\tERROR b\n", r.Out);
    }

    [Fact]
    public async Task 引数の誤りの使い方にも出さない()
    {
        var r = await Run(true, NoSettings(), false, "--version", "extra");
        Assert.Equal(UvfExit.Error, r.Exit);
        Assert.DoesNotContain(SupportPrompt.SponsorsUrl, r.Out + r.Err);
    }

    [Fact]
    public async Task 無料版のuvf以外には出さない()
    {
        var r = await Run(false, NoSettings(), false, "--version");
        Assert.DoesNotContain(SupportPrompt.SponsorsUrl, r.Out);
        r = await Run(false, NoSettings(), false, "--help");
        Assert.DoesNotContain(SupportPrompt.SponsorsUrl, r.Out);
    }

    [Theory]
    [InlineData("""{"SupportBanner":{"Disabled":true}}""", true)]
    [InlineData("""{"SupportBanner":{"Supported":true}}""", true)]
    [InlineData("""{"SupportBanner":{"Disabled":false,"Supported":false,"UsedDays":3}}""", false)]
    [InlineData("""{"Language":"ja"}""", false)]
    [InlineData("""壊れた設定""", false)]
    public void 設定で止めたかを読む(string json, bool stopped)
    {
        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, json);
        Assert.Equal(stopped, CliSettings.ReadSupportStoppedFrom(path));
    }

    [Fact]
    public void 設定ファイルが無ければ止めていない()
        => Assert.False(CliSettings.ReadSupportStoppedFrom(Path.Combine(_dir, "none.json")));

    private static int CountOccurrences(string s, string part)
    {
        int n = 0;
        for (int i = s.IndexOf(part, StringComparison.Ordinal); i >= 0; i = s.IndexOf(part, i + part.Length, StringComparison.Ordinal)) n++;
        return n;
    }
}
