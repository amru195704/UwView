using Avalonia.Layout;
using Avalonia.VisualTree;
using Avalonia;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using UwView.Core.Cli;
using UwView.Services;
using UwView.ViewModels;
using UwView.Views;

namespace UwView.UiTests;

/// <summary>
/// 「コマンドライン」ダイアログ（v1.8.0 Finder Scope）の FS-1：2 つの欄・コマンドの行・誤り・［ファイル展開］・文字の出力。
/// 画面のロジックは ViewModel にあるので、ほとんどは ViewModel を直接動かして確かめる。
/// </summary>
public class CommandLineDialogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "uv-cmd-" + Guid.NewGuid().ToString("N"));

    public CommandLineDialogTests()
    {
        UwView.Localization.Localizer.Instance.SetLanguage("ja");
        Directory.CreateDirectory(Path.Combine(_dir, "old"));
        File.WriteAllText(Path.Combine(_dir, "a.log"), "ERROR one\nok\nerror two\n");
        File.WriteAllText(Path.Combine(_dir, "b.log"), "fine\nERROR three\n");
        File.WriteAllText(Path.Combine(_dir, "old", "c.log"), "ERROR four\n");
        File.WriteAllText(Path.Combine(_dir, "note.txt"), "ERROR five\n");
        File.WriteAllText(Path.Combine(_dir, ".ignore"), "b.log\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private CommandLineViewModel Vm(string files, string search)
        => new(new UvfCommandBackend(), _dir) { FilePattern = files, SearchPattern = search };

    [Theory]
    [InlineData("*.log", "ERROR -i", "uvf '*.log' ERROR -i")]
    [InlineData("'*.log'", "ERROR", "uvf '*.log' ERROR")]                        // 引用符は付けても外して読む
    [InlineData("a.log b.log", "ERROR", "uvf 'a.log b.log' ERROR")]              // 空白を含む指定も 1 つの引数
    [InlineData("a.log", "'k=\"x\"' -E", "uvf a.log 'k=\"x\"' -E")]
    [InlineData("a.log", "--help", "uvf --help")]                                 // ファイルを取らないもの
    [InlineData("**/*.log", "東京 -v", "uvf '**/*.log' 東京 -v")]
    public void コマンドの行は2つの欄から作る(string files, string search, string expected)
        => Assert.Equal(expected, Vm(files, search).CommandText);

    [Fact]
    public void 除外のチェックを外すと_no_ignore_と同じ()
    {
        var vm = Vm("*.log", "ERROR");
        vm.FollowIgnore = false;
        Assert.Equal("uvf '*.log' ERROR --no-ignore", vm.CommandText);
    }

    [Theory]
    [InlineData("a.log", "ERROR -uniq", "-uniq は UwView Pro の機能です")]
    [InlineData("a.log", "ERROR -out x.txt", "-out は UwView Pro の機能です")]
    [InlineData("a.log", "ERROR extra words", "ファイルと検索パターンを1つずつ指定してください")]   // uvf と同じ言葉
    [InlineData("a.log", "'ERROR", "引用符 ' が閉じていません")]
    [InlineData("", "ERROR", "ファイルを指定してください")]
    public void 誤りはコマンドと同じ言葉で出て実行できない(string files, string search, string message)
    {
        var vm = Vm(files, search);
        Assert.Contains(message, vm.ErrorText);
        Assert.False(vm.CanRun);
    }

    [AvaloniaFact]
    public async Task ファイル展開は_files_と同じ一覧()
    {
        var vm = Vm("**/*.log", "--files");
        await vm.ExpandAsync();
        var expanded = vm.Files.Select(r => $"{r.Label}\t{r.Name}").ToList();
        Assert.Equal(2, expanded.Count);                       // b.log は .ignore で外れる
        Assert.Contains("除外 1 本", vm.FilesSummary);

        await vm.RunAsync();
        Assert.Equal(expanded, vm.OutputLines);                 // 出力欄はコマンドの --files そのまま
        Assert.Equal(0, vm.SelectedTab);                        // 一覧にも出して、一覧を見せる（§5.1）
        Assert.Equal(expanded, vm.Files.Select(r => $"{r.Label}\t{r.Name}"));
    }

    [Fact]
    public void 検索語なしは一覧だけ()
    {
        var vm = Vm("**/*.log", "");
        Assert.Equal("uvf '**/*.log' --files", vm.CommandText);
        Assert.Equal("", vm.ErrorText);
        vm.SearchPattern = "--no-ignore";
        Assert.Equal("uvf '**/*.log' --files --no-ignore", vm.CommandText);
    }

    [Fact]
    public void 基準フォルダーは終了時の値を次の既定にし_今のタブのフォルダーより先に使う()
    {
        string other = Directory.CreateTempSubdirectory("uv-cmd-other-").FullName;
        try
        {
            var settings = new AppSettings();
            // 終了時の値がまだ無ければ、今のタブのファイルのフォルダー
            var first = new CommandLineViewModel(new UvfCommandBackend(), _dir, settings);
            Assert.Equal(_dir, first.BaseFolder);
            Assert.Equal(_dir, settings.CommandBaseFolder);

            // 使っている間に変えたら、その値が終了時の値になる（保存は終了時にまとめて）
            first.BaseFolder = other;
            Assert.Equal(other, settings.CommandBaseFolder);
            first.BaseFolder = Path.Combine(other, "無いフォルダー");   // 打ちかけの・無いフォルダーは覚えない
            Assert.Equal(other, settings.CommandBaseFolder);

            // 次に GUI から開くと、今のタブのフォルダーより終了時の値が先
            var next = new CommandLineViewModel(new UvfCommandBackend(), _dir, settings);
            Assert.Equal(other, next.BaseFolder);

            // 終了時の値のフォルダーが消えていれば、今のタブのフォルダー
            Directory.Delete(other);
            Assert.Equal(_dir, new CommandLineViewModel(new UvfCommandBackend(), _dir, settings).BaseFolder);
        }
        finally { if (Directory.Exists(other)) Directory.Delete(other, recursive: true); }
    }

    [Fact]
    public void 画面を起動するときは打ったフォルダーを渡す()
    {
        var psi = new System.Diagnostics.ProcessStartInfo("x");
        UwView.Core.Cli.LaunchFolder.Pass(psi);
        Assert.Equal(Directory.GetCurrentDirectory(), psi.Environment[UwView.Core.Cli.LaunchFolder.Variable]);
    }

    [AvaloniaFact]
    public async Task 履歴は1組で残り_次に開くと前回値で_選ぶと戻る()
    {
        var settings = new AppSettings();
        var first = new CommandLineViewModel(new UvfCommandBackend(), _dir, settings)
            { FilePattern = "*.log", SearchPattern = "ERROR", FollowIgnore = false };
        await first.RunAsync();
        var second = new CommandLineViewModel(new UvfCommandBackend(), null, settings) { SearchPattern = "error -i" };
        await second.RunAsync();

        var reopened = new CommandLineViewModel(new UvfCommandBackend(), null, settings);
        Assert.Equal(_dir, reopened.BaseFolder);                // 今のタブが無ければ前回値
        Assert.Equal("*.log", reopened.FilePattern);
        Assert.Equal("error -i", reopened.SearchPattern);
        Assert.False(reopened.FollowIgnore);
        Assert.Equal(2, reopened.History.Count);

        reopened.Recall(reopened.History[1]);
        Assert.Equal("ERROR", reopened.SearchPattern);
        Assert.Equal(["error -i", "ERROR"],
            CommandLineDialog.HistoryItems(reopened.History, e => e.SearchPattern).Select(e => e.SearchPattern));
        Assert.Single(CommandLineDialog.HistoryItems(reopened.History, e => e.FilePattern));   // 同じ値は 1 つに
    }

    [AvaloniaFact]
    public async Task 実行の出力はコマンドと1バイトも違わない()
    {
        var vm = Vm("*.log", "error -i");
        await vm.RunAsync();
        var mine = File.ReadAllBytes(vm.OutputFile!);

        // 同じ指定を、基準フォルダーでコマンドとして走らせる
        var stdout = new MemoryStream();
        var argv = CommandLineSplitter.Split(vm.CommandText).Args.Skip(1).ToList();
        await WorkingFolder.RunAsync(_dir, () => UvfCli.RunAsync(argv,
            new UvfEnvironment { StdOut = stdout, StdErr = new StringWriter(), Japanese = true }));
        Assert.Equal(stdout.ToArray(), mine);
        Assert.NotEmpty(mine);
    }

    [AvaloniaFact]
    public async Task 版とヘルプは出力欄に出る()
    {
        var vm = Vm("", "--version");
        Assert.True(vm.CanRun);
        await vm.RunAsync();
        Assert.StartsWith("uvf ", vm.OutputLines[0]);

        vm.SearchPattern = "--help files";
        await vm.RunAsync();
        Assert.True(vm.OutputLines.Count > 3);
    }

    [AvaloniaFact]
    public void ボタンとキーでダイアログが開き_窓ごとに1つ()
    {
        var (window, view, _) = UiHarness.OpenMainWindow();
        var button = UiHarness.Find<Button>(view, "CommandLineButton");
        Assert.NotNull(ToolTip.GetTip(button));
        UiHarness.Click(button);
        Dispatcher.UIThread.RunJobs();
        var dialog = CommandLineDialog.For(window);
        Assert.NotNull(dialog);

        Assert.True(view.RunShortcut(ShortcutAction.OpenCommandLine));    // 開いていれば前に出すだけ
        Dispatcher.UIThread.RunJobs();
        Assert.Same(dialog, CommandLineDialog.For(window));
        dialog!.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(CommandLineDialog.For(window));
    }

    [AvaloniaFact]
    public async Task ファイルの本数は見出しに切れずに出て_閉じるボタンは閉じる()
    {
        for (int i = 0; i < 1200; i++) File.WriteAllText(Path.Combine(_dir, $"m{i:D4}.txt"), "x\n");
        var (window, view, _) = UiHarness.OpenMainWindow();
        view.OpenCommandLine();
        Dispatcher.UIThread.RunJobs();
        var dialog = CommandLineDialog.For(window)!;
        dialog.ViewModel.BaseFolder = _dir;
        dialog.ViewModel.FilePattern = "*.txt";
        await dialog.ViewModel.ExpandAsync();
        Dispatcher.UIThread.RunJobs();
        var header = dialog.GetVisualDescendants().OfType<TabItem>().First().Header as TextBlock;
        Assert.Equal("ファイル（1,201）", header!.Text);                  // note.txt＋1,200 本
        dialog.UpdateLayout();
        double shown = header.Bounds.Width;
        header.Measure(Size.Infinity);                                     // 同じフォントで、制限なしに測った幅
        Assert.True(shown >= header.DesiredSize.Width - 0.5, $"見出しが切れている: {shown} < {header.DesiredSize.Width}");
        var close = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CmdCloseButton");
        Assert.Equal("閉じる", close.Content);
        // 「閉じる」は右下に固定（窓の幅を変えても、右端・一番下のまま）
        foreach (double width in new[] { 860.0, 1200.0 })
        {
            dialog.Width = width;
            dialog.UpdateLayout();
            var at = close.TranslatePoint(new Point(close.Bounds.Width, close.Bounds.Height), dialog)!.Value;
            Assert.InRange(dialog.Bounds.Width - at.X, 0, 20);
            Assert.InRange(dialog.Bounds.Height - at.Y, 0, 20);
        }
        dialog.Close();
    }

    [Fact]
    public void Ctrl_Shift_K_はコマンドライン()
    {
        Assert.Equal(ShortcutAction.OpenCommandLine,
            ShortcutKeys.Map(Avalonia.Input.Key.K, Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift, "K", false, mac: false));
        Assert.Equal(ShortcutAction.OpenCommandLine,
            ShortcutKeys.Map(Avalonia.Input.Key.K, Avalonia.Input.KeyModifiers.Meta | Avalonia.Input.KeyModifiers.Shift, "K", true, mac: true));
    }
}
