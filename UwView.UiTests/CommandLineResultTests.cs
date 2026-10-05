using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using UwView.Core.Cli;
using UwView.Services;
using UwView.ViewModels;
using UwView.Views;

namespace UwView.UiTests;

/// <summary>
/// 「コマンドライン」ダイアログ（v1.8.0 Finder Scope）の FS-2：検索の結果を本体の窓に出す・進み具合・中止。
/// 窓に出したものの件数・並び・中身は、同じ指定のコマンドの出力と同じであること（§4）。
/// </summary>
public class CommandLineResultTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "uv-cmdres-" + Guid.NewGuid().ToString("N"));
    private readonly string _real;

    public CommandLineResultTests()
    {
        UwView.Localization.Localizer.Instance.SetLanguage("ja");
        Directory.CreateDirectory(Path.Combine(_dir, "old"));
        File.WriteAllText(Path.Combine(_dir, "a.log"), "ERROR one\nok\nerror two\n");
        File.WriteAllText(Path.Combine(_dir, "b.log"), "fine\nERROR three\n");
        File.WriteAllText(Path.Combine(_dir, "old", "c.log"), "ERROR four\nINFO\n");
        File.WriteAllText(Path.Combine(_dir, ".ignore"), "b.log\n");
        _real = WorkingFolder.Run(_dir, Directory.GetCurrentDirectory).GetAwaiter().GetResult();   // macOS の /private/var
        UiHarness.ForgetProgressWindow();
    }

    public void Dispose()
    {
        UiHarness.ForgetProgressWindow();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>ボタンでダイアログを開き、2 つの欄を入れて実行し、窓に出す（ダイアログと同じ手順）。</summary>
    private async Task<(MainView View, ViewModels.MainViewModel Vm, CommandLineViewModel Cmd, CommandRunResult? Result)>
        RunInDialog(string files, string search, (MainView View, ViewModels.MainViewModel Vm)? reuse = null)
    {
        MainView view;
        ViewModels.MainViewModel vm;
        if (reuse is { } r) (view, vm) = r;
        else (_, view, vm) = UiHarness.OpenMainWindow();
        view.OpenCommandLine();
        Dispatcher.UIThread.RunJobs();
        var cmd = CommandLineDialog.For((Avalonia.Controls.Window)Avalonia.Controls.TopLevel.GetTopLevel(view)!)!.ViewModel;
        cmd.BaseFolder = _dir;
        cmd.FilePattern = files;
        cmd.SearchPattern = search;
        var result = await cmd.RunAsync();
        if (result?.ShowInGui is { } show) await show();
        await UiHarness.Pump();
        return (view, vm, cmd, result);
    }

    /// <summary>同じ指定をコマンドとして基準フォルダーで走らせた標準出力（行）。</summary>
    private async Task<string[]> Cli(params string[] argv)
    {
        var stdout = new MemoryStream();
        await WorkingFolder.RunAsync(_dir, () => UvfCli.RunAsync(argv,
            new UvfEnvironment { StdOut = stdout, StdErr = new StringWriter(), Japanese = true }));
        return System.Text.Encoding.UTF8.GetString(stdout.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    [AvaloniaFact]
    public async Task 一本のファイルの検索はタブで開いて結果一覧に出る()
    {
        var (_, vm, cmd, result) = await RunInDialog("a.log", "error -i");
        Assert.NotNull(result?.ShowInGui);
        Assert.Empty(cmd.OutputLines);                                // 文字は出さない（-open と同じ）
        Assert.Contains("本体の窓", cmd.Status);

        var tab = Assert.Single(vm.Tabs);
        Assert.Equal(Path.Combine(_real, "a.log"), tab.FilePath);
        // 件数・行はコマンドと同じ
        var cli = await Cli("a.log", "error", "-i");
        Assert.Equal(cli.Length, tab.Session.SearchHits.Count);
    }

    [AvaloniaFact]
    public async Task 開いているファイルならそのタブで出す()
    {
        var first = await RunInDialog("a.log", "ERROR");
        var again = await RunInDialog("a.log", "error -i", (first.View, first.Vm));
        var tab = Assert.Single(again.Vm.Tabs);
        Assert.Equal(2, tab.Session.SearchHits.Count);
    }

    [AvaloniaFact]
    public async Task 複数ファイルの検索は複数ファイルの結果として出る()
    {
        var (view, vm, cmd, _) = await RunInDialog("**/*.log", "ERROR");
        Assert.Empty(cmd.OutputLines);
        var rows = view.MultiResultsViewModel!.Rows;
        // 並び・中身はコマンドと同じ（b.log は .ignore で外れる）
        var cli = await Cli("**/*.log", "ERROR");
        Assert.Equal(cli.Length, rows.Count);
        Assert.Equal(["1:1", "2:1"], rows.Select(r => r.LineNumberText));
        Assert.Equal(["ERROR one", "ERROR four"], rows.Select(r => r.Text));
        Assert.Single(vm.Tabs);
    }

    [AvaloniaFact]
    public async Task 続けて実行すると前の複数ファイルの結果を片付けて出し直す()
    {
        var first = await RunInDialog("**/*.log", "ERROR");
        var old = first.View.MultiResultsViewModel;
        var again = await RunInDialog("**/*.log", "error -i", (first.View, first.Vm));
        Assert.NotSame(old, again.View.MultiResultsViewModel);
        Assert.Equal(3, again.View.MultiResultsViewModel!.Rows.Count);
        Assert.Single(again.Vm.Tabs);                                  // 前の結果のタブは閉じた
    }

    [AvaloniaFact]
    public async Task 書いた_open_は無視してその旨を出す()
    {
        var (_, vm, cmd, result) = await RunInDialog("a.log", "ERROR -open");
        Assert.Equal("uvf a.log ERROR", cmd.CommandText);               // 行は実際に走らせる形（-open を外す）
        Assert.Equal("", cmd.ErrorText);
        Assert.Contains("-open は画面の中では要りません", cmd.Notice);
        Assert.NotNull(result?.ShowInGui);
        Assert.Single(vm.Tabs);
    }

    [AvaloniaTheory]
    [InlineData("saved.txt", false)]
    [InlineData("saved.json", true)]
    public async Task 窓に出した検索も保存でき_中身はコマンドと同じ(string name, bool json)
    {
        var (_, _, cmd, result) = await RunInDialog("**/*.log", "ERROR");
        Assert.NotNull(result?.ShowInGui);
        string path = Path.Combine(_dir, name);
        await cmd.SaveOutputAsync(path);
        var cli = await Cli(json ? ["**/*.log", "ERROR", "--json"] : ["**/*.log", "ERROR"]);
        Assert.Equal(cli, File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(Path.GetFullPath(path), cmd.WrittenFile);
    }

    [AvaloniaFact]
    public async Task 進み具合は読み終えた本数と量を数える()
    {
        var progress = new CommandProgress();
        await WorkingFolder.RunAsync(_dir, () => Task.Run(() =>
        {
            CommandProgress.Current = progress;
            return UvfCli.RunAsync(["**/*.log", "ERROR"],
                new UvfEnvironment { StdOut = new MemoryStream(), StdErr = new StringWriter(), Japanese = true });
        }));
        var p = progress.Read();
        Assert.Equal(2, p.FilesTotal);
        Assert.Equal(2, p.FilesDone);
        long size = new FileInfo(Path.Combine(_dir, "a.log")).Length + new FileInfo(Path.Combine(_dir, "old", "c.log")).Length;
        Assert.Equal(size, p.BytesTotal);
        Assert.Equal(size, p.BytesDone);
        Assert.Null(CommandProgress.Current);                          // 呼び手には漏れない

        string text = CommandLineViewModel.Describe(new CommandProgress.Snapshot(3, 12, 1L << 30, 4L << 30, TimeSpan.FromSeconds(10)));
        Assert.Equal("実行しています… ・ 3 / 12 本 ・ 1.00 GB / 4.00 GB ・ 残り 約 30 秒 ・ 経過 10 秒", text);
    }

    [AvaloniaFact]
    public async Task 中止できる()
    {
        for (int i = 0; i < 40; i++) File.WriteAllText(Path.Combine(_dir, $"m{i:D2}.txt"), "ERROR\n");
        MultiFileSearch.ArrivalDelayForTests = _ => Task.Delay(100);
        try
        {
            var cmd = new CommandLineViewModel(new UvfCommandBackend(), _dir) { FilePattern = "*.txt", SearchPattern = "ERROR" };
            var run = cmd.RunAsync();
            await UiHarness.WaitUntil(() => cmd.IsRunning, "走り出す");
            cmd.Cancel();
            await run;
            Assert.False(cmd.IsRunning);
            Assert.Equal("中止しました", cmd.Status);
            Assert.True(cmd.OutputLines.Count < 40);
        }
        finally { MultiFileSearch.ArrivalDelayForTests = null; }
    }
}
