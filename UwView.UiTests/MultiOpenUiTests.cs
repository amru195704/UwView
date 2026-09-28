using System.Text;
using Avalonia.LogicalTree;
using Avalonia.Headless.XUnit;
using UwView.Controls;
using UwView.Core;
using UwView.Core.Cli;
using UwView.Views;

namespace UwView.UiTests;

/// <summary>
/// uvf の複数ファイルの -open とファイル一覧のタブ（実装指示書_uvf複数ファイルGUI表示とタブ §7 画面のテスト）。
/// CLI で本物の受け渡しを作り、起動時と同じ経路で画面へ渡す。
/// </summary>
public class MultiOpenUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "uvf-multiui-" + Guid.NewGuid().ToString("N"));

    /// <summary>CLI が記録するのと同じ形のパス（mac の一時フォルダーは /var → /private/var の別名）。</summary>
    private readonly string _real;

    private string F(string name) => Path.Combine(_real, name);

    public MultiOpenUiTests()
    {
        Directory.CreateDirectory(_dir);
        string saved = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_dir);
        _real = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(saved);
        UwView.App.PendingCliMulti = null;
        UiHarness.ForgetProgressWindow();
    }

    public void Dispose()
    {
        UwView.App.PendingCliMulti = null;
        UiHarness.ForgetProgressWindow();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>f01.log〜fNN.log（各ファイル 20 行。7 行目だけ ERROR）。</summary>
    private void MakeFiles(int count)
    {
        for (int f = 1; f <= count; f++)
        {
            var sb = new StringBuilder();
            for (int i = 1; i <= 20; i++) sb.Append($"f{f:D2} line {i} {(i == 7 ? "ERROR" : "INFO")}\n");
            File.WriteAllText(Path.Combine(_dir, $"f{f:D2}.log"), sb.ToString());
        }
    }

    /// <summary>CLI で探して受け渡しを作り、その置き場所を返す（画面は起こさない）。</summary>
    private async Task<string> Handoff(params string[] args)
    {
        string saved = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_dir);
        try
        {
            var env = new UvfEnvironment
            {
                StdOut = new MemoryStream(), StdErr = new StringWriter(), Japanese = true,
                SettingsFolder = UiHarness.TestAppDataFolder, LaunchGui = (_, _) => true,
            };
            Assert.Equal(UvfExit.Found, await UvfCli.RunAsync(args, env));
            return env.MultiHandoffPath!;
        }
        finally { Directory.SetCurrentDirectory(saved); }
    }

    private static async Task<(MainView View, ViewModels.MainViewModel Vm)> Start(string handoff)
    {
        var (_, view, vm) = UiHarness.OpenMainWindow();
        UwView.App.PendingCliMulti = handoff;
        view.RunStartup([]);
        await UiHarness.WaitUntil(() => view.MultiGroup is not null, "メインが開く");
        await UiHarness.Pump();
        return (view, vm);
    }

    [AvaloniaFact]
    public async Task 三つのファイルの結果を受けるとメインが番号1で結果の窓がn行番号で出る()
    {
        MakeFiles(3);
        var (view, vm) = await Start(await Handoff("*.log", "ERROR", "-open"));

        Assert.Single(vm.Tabs);
        var main = vm.Tabs[0];
        Assert.Equal(F("f01.log"), main.FilePath);
        Assert.Equal("1:f01.log", main.DisplayName);
        Assert.False(main.CanClose);                               // メインは閉じられない

        var results = view.MultiResultsViewModel!;
        Assert.True(results.IsResultSet);
        Assert.Equal(3, results.Rows.Count);
        Assert.Equal(["1:7", "2:7", "3:7"], results.Rows.Select(r => r.LineNumberText));
        Assert.Equal("f03 line 7 ERROR", results.Rows[2].Text);
        Assert.Single(main.Session.SearchHits);                     // そのファイルの当たりを取り込む

        // メインを閉じようとしても閉じない
        vm.RequestClose(main);
        await UiHarness.Pump();
        Assert.Single(vm.Tabs);
    }

    [AvaloniaFact]
    public async Task 結果の3の7をダブルクリックするとメインが3番になり7行目が強調される()
    {
        MakeFiles(3);
        var (view, vm) = await Start(await Handoff("*.log", "ERROR", "-open"));
        var results = view.MultiResultsViewModel!;
        var row = results.Rows[2];
        Assert.Equal("3:7", row.LineNumberText);

        results.OnCursor(row);
        Assert.Equal($"3: {F("f03.log")}", results.CursorFileText);   // 選んだだけでは開かない
        Assert.Equal(F("f01.log"), vm.ActiveTab!.FilePath);

        results.Jump(row);
        await UiHarness.WaitUntil(() => vm.ActiveTab?.FilePath == F("f03.log"), "3番がメインになる");
        await UiHarness.Pump();

        Assert.Single(vm.Tabs);                                     // 差し替え（前のファイルは閉じる）
        Assert.Equal("3:f03.log", vm.ActiveTab!.DisplayName);
        Assert.False(vm.ActiveTab.CanClose);
        var text = UiHarness.Find<TextView>(view, "TextView");
        long line7 = File.ReadAllText(F("f03.log")).Split('\n').Take(6).Sum(l => l.Length + 1);
        Assert.Equal(line7, text.EmphasizedOffset);
    }

    [AvaloniaFact]
    public async Task ファイル一覧でタブで開くをオンにして2番を開くとタブが増える()
    {
        MakeFiles(3);
        var (view, vm) = await Start(await Handoff("*.log", "ERROR", "-open"));
        view.OpenFileList();
        await UiHarness.Pump();
        var list = FileListPopup.Current!;

        list.OpenInTab = true;
        Assert.True(AppSettingsRefCurrent().FileListOpenInTab);    // 設定に残る
        list.OpenAt(1);
        await UiHarness.WaitUntil(() => vm.Tabs.Count == 2, "タブが増える");

        var added = vm.Tabs[1];
        Assert.Equal("2:f02.log", added.DisplayName);
        Assert.True(added.CanClose);
        Assert.Same(added, vm.ActiveTab);

        // メインのタブだけ灰色（追加タブはいつもどおり）
        await UiHarness.Pump();
        var strip = UiHarness.Find<Avalonia.Controls.Primitives.TabStrip>(view, "TabStripControl");
        var mainItem = (Avalonia.Controls.Primitives.TabStripItem)strip.ContainerFromIndex(0)!;
        var addedItem = (Avalonia.Controls.Primitives.TabStripItem)strip.ContainerFromIndex(1)!;
        Assert.True(vm.Tabs[0].IsResultMain);
        Assert.Contains("resultMain", mainItem.Classes);
        Assert.DoesNotContain("resultMain", addedItem.Classes);
        Assert.Equal(Avalonia.Media.Color.Parse("#C8C8C8"), ((Avalonia.Media.ISolidColorBrush)mainItem.Background!).Color);

        // 同じファイルをもう一度開いても、そのタブに切り替えるだけ
        vm.ActiveTab = vm.Tabs[0];
        list.OpenAt(1);
        await UiHarness.Pump();
        Assert.Equal(2, vm.Tabs.Count);
        Assert.Same(added, vm.ActiveTab);

        // オフならメインの中身を差し替える
        list.OpenInTab = false;
        list.OpenAt(2);
        await UiHarness.WaitUntil(() => vm.Tabs[0].FilePath == F("f03.log"), "メインが3番になる");
        Assert.Equal(2, vm.Tabs.Count);
    }

    [AvaloniaFact]
    public async Task ファイルが多い一覧をスクロールしても落ちない()
    {
        // 行の入れ物の使い回しで中身の無い行が来て落ちた（710 ファイルの一覧・2026-09-28）
        MakeFiles(9);
        for (int f = 10; f <= 200; f++) File.WriteAllText(Path.Combine(_dir, $"f{f:D3}.log"), "INFO\nERROR\n");
        var (view, _) = await Start(await Handoff("*.log", "ERROR", "-open"));
        view.OpenFileList();
        await UiHarness.Pump();
        var list = FileListPopup.Current!;
        var rows = list.GetLogicalDescendants().OfType<Avalonia.Controls.ListBox>().Single(b => b.Name == "FileListRows");
        Assert.Equal(200, list.Items.Count);

        for (int i = 0; i < 200; i += 20) { rows.ScrollIntoView(i); list.UpdateLayout(); await UiHarness.Pump(2); }
        rows.ScrollIntoView(199); list.UpdateLayout();
        rows.ScrollIntoView(0); list.UpdateLayout();
        await UiHarness.Pump();
    }

    [AvaloniaFact]
    public async Task 追加で7個開いたあと8個目は断る()
    {
        MakeFiles(9);
        var (view, vm) = await Start(await Handoff("*.log", "ERROR", "-open"));
        view.OpenFileList();
        await UiHarness.Pump();
        var list = FileListPopup.Current!;
        list.OpenInTab = true;

        for (int i = 1; i <= 7; i++)
        {
            list.OpenAt(i);
            int want = i + 1;
            await UiHarness.WaitUntil(() => vm.Tabs.Count == want, $"タブ {want} 個");
        }
        Assert.Equal(FileTabGroup<object>.MaxTabs, view.MultiGroup!.Count);

        list.OpenAt(8);
        await UiHarness.Pump(20);
        Assert.Equal(8, vm.Tabs.Count);                             // 勝手に古いタブを閉じない

        // 1つ閉じれば開ける
        vm.RequestClose(vm.Tabs[3]);
        await UiHarness.WaitUntil(() => vm.Tabs.Count == 7, "1つ閉じる");
        list.OpenAt(8);
        await UiHarness.WaitUntil(() => vm.Tabs.Count == 8, "9番が開く");
        Assert.Equal("9:f09.log", vm.Tabs[^1].DisplayName);
    }

    [AvaloniaFact]
    public async Task 検索語なしならメインと一覧だけで結果の窓は出さない()
    {
        MakeFiles(2);
        var (view, vm) = await Start(await Handoff("-open", "*.log"));
        Assert.Null(view.MultiResultsViewModel);
        Assert.Equal("1:f01.log", vm.Tabs[0].DisplayName);
        Assert.NotNull(FileListPopup.Current);
        Assert.Null(FileListPopup.Current!.HitsOnly);               // 当たりが無いので切り替えも出さない
        Assert.Equal(["1", "2"], FileListPopup.Current.ShownLabels);
    }

    [AvaloniaFact]
    public async Task 番号1がヒット0件ならヒットした最初のファイルをメインに出す()
    {
        // a00.log（番号 1）は当たらない。メインは番号 2 の f01.log、一覧にも 0 件の行は出ない（オーナー報告 2026-09-29）
        MakeFiles(2);
        File.WriteAllText(Path.Combine(_dir, "a00.log"), "INFO only\n");
        var (view, vm) = await Start(await Handoff("*.log", "ERROR", "-open"));

        Assert.Equal("2:f01.log", vm.Tabs[0].DisplayName);
        view.OpenFileList();
        await UiHarness.Pump();
        Assert.Equal(["2", "3"], FileListPopup.Current!.ShownLabels);
    }

    [AvaloniaFact]
    public async Task ファイル一覧は当たりのあるファイルだけと全部を切り替えられる()
    {
        // 当たりのあるのは 1〜3 番、4・5 番（q で始まる）は当たらない（オーナー指示 2026-09-29）
        MakeFiles(3);
        foreach (string name in new[] { "q01.log", "q02.log" })
            File.WriteAllText(Path.Combine(_dir, name), "INFO only\nINFO only\n");
        var (view, vm) = await Start(await Handoff("*.log", "ERROR", "-open"));
        view.OpenFileList();
        await UiHarness.Pump();
        var list = FileListPopup.Current!;

        Assert.True(list.HitsOnly);                                  // 既定は当たりのあるものだけ
        Assert.Equal(["1", "2", "3"], list.ShownLabels);             // 番号は --files と同じまま

        list.HitsOnly = false;
        Assert.False(AppSettingsRefCurrent().FileListHitsOnly);      // 設定に残る
        Assert.Equal(["1", "2", "3", "4", "5"], list.ShownLabels);

        // 当たりの無いファイルでも、タブで開いているあいだは残す
        list.OpenInTab = true;
        list.OpenAt(3);
        await UiHarness.WaitUntil(() => vm.Tabs.Count == 2, "4番をタブで開く");
        list.HitsOnly = true;
        Assert.Equal(["1", "2", "3", "4"], list.ShownLabels);
    }

    [AvaloniaFact]
    public async Task gzの結果をダブルクリックすると展開後の正しい行へ移る()
    {
        MakeFiles(1);
        var sb = new StringBuilder();
        for (int i = 1; i <= 30; i++) sb.Append($"gz line {i} {(i == 25 ? "ERROR" : "INFO")}\n");
        await using (var gz = new System.IO.Compression.GZipStream(
                         File.Create(F("f02.log.gz")), System.IO.Compression.CompressionLevel.Fastest))
            gz.Write(Encoding.UTF8.GetBytes(sb.ToString()));

        var (view, vm) = await Start(await Handoff("f01.log,f02.log.gz", "ERROR", "-open"));
        var results = view.MultiResultsViewModel!;
        var row = results.Rows.Single(r => r.LineNumberText == "2:25");
        results.Jump(row);
        string expanded = F("f02.log");
        await UiHarness.WaitUntil(() => vm.ActiveTab?.FilePath == expanded, "展開したファイルがメインになる");
        await UiHarness.Pump();

        var text = UiHarness.Find<TextView>(view, "TextView");
        long line25 = sb.ToString().Split('\n').Take(24).Sum(l => l.Length + 1);
        Assert.Equal(line25, text.EmphasizedOffset);
    }

    [AvaloniaFact]
    public async Task 検索の後で書き換えたファイルを開くと変わっていると知らせる()
    {
        MakeFiles(2);
        string handoff = await Handoff("*.log", "ERROR", "-open");
        File.AppendAllText(F("f02.log"), "appended ERROR\n");
        var (view, vm) = await Start(handoff);

        view.MultiResultsViewModel!.Jump(view.MultiResultsViewModel.Rows[1]);
        await UiHarness.WaitUntil(() => vm.ActiveTab?.FilePath == F("f02.log"), "2番が開く");
        Assert.Contains(UiHarness.Ja("変わっています", "changed"), vm.Notice);
    }

    private static UwView.Services.AppSettings AppSettingsRefCurrent() => UwView.Services.AppSettingsRef.Current;
}
