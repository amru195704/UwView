using System.Text;
using Avalonia.Headless.XUnit;
using UwView.Core.Cli;
using UwView.Views;

namespace UwView.UiTests;

/// <summary>
/// uvf の -open で渡した検索パターンを、GUI が開いたファイルで検索する（オーナー指示 2026-09-14）。
/// CLI の stdout と<b>同じ件数</b>になることを確かめる（同じ検索関数・同じ条件）。
/// </summary>
public class CliOpenSearchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "uvf-cliopen-" + Guid.NewGuid().ToString("N"));

    public CliOpenSearchTests()
    {
        Directory.CreateDirectory(_dir);
        UwView.App.PendingCliSearch = null;
        UiHarness.ForgetProgressWindow();   // 前のテストが残した窓の参照を持ち越さない
    }

    public void Dispose()
    {
        UwView.App.PendingCliSearch = null;
        UiHarness.ForgetProgressWindow();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string WriteLog()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 4_000; i++)
            sb.Append($"{i:D6} {(i % 9 == 0 ? "ERROR" : i % 5 == 0 ? "error" : "INFO")} dev{i % 4}\n");
        string path = Path.Combine(_dir, "app.log");
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    [AvaloniaFact]
    public async Task openで渡したパターンで検索しCLIと同じ件数になる()
    {
        string log = WriteLog();

        // CLI（stdout）
        using var stdout = new MemoryStream();
        int code = await UvfCli.RunAsync([log, "ERROR"], new UvfEnvironment
        {
            StdOut = stdout, StdErr = new StringWriter(), Japanese = false,
        });
        Assert.Equal(UvfExit.Found, code);
        int cliCount = Encoding.UTF8.GetString(stdout.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        // GUI（-open と同じ経路: 起動引数でパターンを受け取り、ファイルを開いて検索）
        var (window, view, vm) = UiHarness.OpenMainWindow();
        vm.SearchIgnoreCase = true;     // 画面側の設定が残っていても、CLI と同じ条件で検索すること
        var args = UwView.App.ExtractCliSearch(
            [UvfCli.SearchArgument, Convert.ToBase64String(Encoding.UTF8.GetBytes("ERROR")), log], out var pattern);
        UwView.App.PendingCliSearch = pattern;
        view.RunStartup(args);

        await UiHarness.WaitUntil(() => vm.ActiveTab is not null, "タブが開く");
        await UiHarness.WaitSearchDone(vm.ActiveTab!.Session);
        await UiHarness.Pump();

        var session = vm.ActiveTab.Session;
        Assert.Equal("ERROR", session.ActiveSearch!.Pattern);
        Assert.False(session.ActiveSearch.IgnoreCase, "大小無視で検索している（CLI と条件が違う）");
        Assert.False(session.ActiveSearch.UseRegex);
        Assert.Equal(cliCount, session.SearchHits.Count);
        Assert.NotNull(EditProgressWindow.Current);   // 画面での検索と同じく、件数と時間の窓が出る
    }

    [AvaloniaFact]
    public void 起動引数から検索パターンを抜き出す()
    {
        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("東京 -x"));
        var rest = UwView.App.ExtractCliSearch([UvfCli.SearchArgument, b64, "/tmp/a.log"], out var pattern);
        Assert.Equal(["/tmp/a.log"], rest);
        Assert.Equal("東京 -x", pattern);

        // 壊れていても起動は止めない（ファイルは普通に開く）
        var rest2 = UwView.App.ExtractCliSearch([UvfCli.SearchArgument, "@@not-base64@@", "/tmp/b.log"], out var bad);
        Assert.Equal(["/tmp/b.log"], rest2);
        Assert.Null(bad);
    }

    [AvaloniaFact]
    public async Task CLIが渡した結果をそのまま使い検索し直さない()
    {
        string log = WriteLog();

        // CLI と同じ手順で受け渡しファイルを作る
        var env = new UvfEnvironment
        {
            StdOut = new MemoryStream(), StdErr = new StringWriter(), Japanese = false,
            LaunchGui = (_, _) => true,
        };
        Assert.Equal(UvfExit.Found, await UvfCli.RunAsync([log, "ERROR", "-open"], env));
        Assert.NotNull(env.HandoffPath);

        // 画面側: 渡された結果を取り込む（同じ検索はしない）
        var (window, view, vm) = UiHarness.OpenMainWindow();
        var args = UwView.App.ExtractCliSearch(
            [UvfCli.SearchArgument, Convert.ToBase64String(Encoding.UTF8.GetBytes("ERROR")),
             CliHandoff.Argument, env.HandoffPath!, log], out var pattern, out var hits);
        Assert.Equal(env.HandoffPath, hits);
        UwView.App.PendingCliSearch = pattern;
        UwView.App.PendingCliHits = hits;
        view.RunStartup(args);

        await UiHarness.WaitUntil(() => vm.ActiveTab is not null, "タブが開く");
        await UiHarness.WaitUntil(() => vm.ActiveTab!.Session.SearchHits.Count > 0, "結果の取り込み");
        await UiHarness.Pump();

        var session = vm.ActiveTab!.Session;
        Assert.False(session.IsSearching);
        Assert.Equal("ERROR", session.ActiveSearch!.Pattern);
        Assert.False(File.Exists(env.HandoffPath!));           // 使ったら消える

        // CLI の stdout と同じ件数・同じ行
        using var stdout = new MemoryStream();
        await UvfCli.RunAsync([log, "ERROR"], new UvfEnvironment
        {
            StdOut = stdout, StdErr = new StringWriter(), Japanese = false,
        });
        var lines = Encoding.UTF8.GetString(stdout.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(lines.Length, session.SearchHits.Count);
        Assert.Equal(long.Parse(lines[0].Split('\t')[0]),
                     session.Document.OffsetToLineIndex(session.SearchHits[0]) + 1);

        window.Close();
    }

    [AvaloniaFact]
    public async Task 索引ができる前でも渡された結果を出せる()
    {
        // オーナー指摘 2026-09-18「索引作成の待ちが余計」。
        // 行番号も渡ってくるので、索引の完成を待たずに結果一覧を出せること
        string log = WriteLog();
        var env = new UvfEnvironment
        {
            StdOut = new MemoryStream(), StdErr = new StringWriter(), Japanese = false,
            LaunchGui = (_, _) => true,
        };
        Assert.Equal(UvfExit.Found, await UvfCli.RunAsync([log, "ERROR", "-open"], env));
        var handoff = CliHandoff.TakeFrom(env.HandoffPath!);
        Assert.NotNull(handoff);
        Assert.Equal(handoff!.Hits.Length, handoff.Lines.Length);      // 行番号も同じ数だけ来る

        // 索引を作っていないセッションへ取り込む
        var (window, view, vm) = UiHarness.OpenMainWindow();
        UiHarness.OpenFile(log);
        await UiHarness.WaitUntil(() => vm.ActiveTab is not null, "タブが開く");
        var session = vm.ActiveTab!.Session;
        session.AdoptSearchResults(handoff.ToOptions(), handoff.Hits, handoff.Truncated, handoff.Lines);

        Assert.Equal(handoff.Hits.Length, session.SearchHits.Count);
        Assert.NotNull(session.SearchHitLines);
        Assert.Equal(handoff.Lines, session.SearchHitLines);

        // 索引ができる前でも、結果一覧の行番号が空にならない
        var results = new UwView.ViewModels.FilterResultsViewModel(_ => { }, maxContext: 1);
        results.SetSession(session);
        Assert.NotEmpty(results.Rows);
        // 渡された行番号がそのまま行に乗る（索引から解き直していない）
        Assert.Equal(handoff.Lines[0], results.Rows[0].LineIndex);
        Assert.Equal((handoff.Lines[0] + 1).ToString("N0"), results.Rows[0].LineNumberText);
        Assert.NotEqual("", results.Rows[0].Text);        // 本文は行頭位置から直接読める

        // 渡された行番号は、索引から解いたものと同じ
        await UiHarness.WaitIndexed(session);
        for (int i = 0; i < Math.Min(20, handoff.Hits.Length); i++)
            Assert.Equal(session.Document.OffsetToLineIndex(handoff.Hits[i]), handoff.Lines[i]);

        window.Close();
    }
}
