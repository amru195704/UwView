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
}
