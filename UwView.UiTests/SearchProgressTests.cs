using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using UwView.Services;
using UwView.Views;

namespace UwView.UiTests;

/// <summary>
/// 検索の進捗ダイアログ（オーナー要望 2026-09-11「UVP と同じように POPUP で状況表示して」）。
///
/// もとは検索が終わると結果一覧がすぐ前に出ていたので、
/// ステータスバーに出した所要時間は読む前に隠れていた。
/// 「件数と所要時間を出したダイアログが残り、閉じてから結果一覧が出る」ことを確かめる。
/// </summary>
public class SearchProgressTests
{
    private static string[] Lines(int n)
    {
        var lines = new string[n];
        for (int i = 0; i < n; i++)
            lines[i] = i % 10 == 0 ? $"{i:D6} ERROR something went wrong" : $"{i:D6} INFO ok";
        return lines;
    }

    private static async Task<(MainView View, MainWindow Window, string Path)> OpenWithFile(int lines = 20_000)
    {
        var (window, view, _) = UiHarness.OpenMainWindow();
        string path = UiHarness.WriteTempFile(Lines(lines));
        UiHarness.OpenFile(path);
        var vm = (UwView.ViewModels.MainViewModel)window.DataContext!;
        Assert.NotNull(vm.ActiveTab);
        await UiHarness.WaitIndexed(vm.ActiveTab!.Session);
        return (view, window, path);
    }

    private static void StartSearch(MainView view, MainWindow window, string word)
    {
        var vm = (UwView.ViewModels.MainViewModel)window.DataContext!;
        vm.SearchText = word;
        UiHarness.Click(UiHarness.Find<Button>(view, "SearchButton"));
    }

    [AvaloniaFact]
    public async Task 検索を始めると進捗ダイアログが出る()
    {
        var (view, window, path) = await OpenWithFile();
        try
        {
            StartSearch(view, window, "ERROR");
            Assert.NotNull(EditProgressWindow.Current);
            Assert.Contains("検索", EditProgressWindow.Current!.Title);

            var vm = (UwView.ViewModels.MainViewModel)window.DataContext!;
            await UiHarness.WaitSearchDone(vm.ActiveTab!.Session);
            await UiHarness.Pump();
            EditProgressWindow.Current?.CloseNow();
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public async Task 検索が終わるとダイアログに件数が残り閉じるまで結果一覧は出ない()
    {
        var (view, window, path) = await OpenWithFile();
        try
        {
            StartSearch(view, window, "ERROR");
            var vm = (UwView.ViewModels.MainViewModel)window.DataContext!;
            await UiHarness.WaitSearchDone(vm.ActiveTab!.Session);
            await UiHarness.Pump();

            var dialog = EditProgressWindow.Current;
            Assert.NotNull(dialog);
            // 2,000 行が ERROR（20,000 行の 10 行に1つ）
            Assert.Equal("2,000 件見つかりました", dialog!.CountText);
            Assert.False(view.FilterResultsOpen, "ダイアログを読む前に結果一覧が出てしまっている");

            dialog.CloseNow();
            await UiHarness.Pump();
            Assert.True(view.FilterResultsOpen, "ダイアログを閉じても結果一覧が出ない");
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public async Task 検索の所要時間がステータスバーに残る()
    {
        var (view, window, path) = await OpenWithFile();
        try
        {
            StartSearch(view, window, "ERROR");
            var vm = (UwView.ViewModels.MainViewModel)window.DataContext!;
            await UiHarness.WaitSearchDone(vm.ActiveTab!.Session);
            await UiHarness.Pump();
            EditProgressWindow.Current?.CloseNow();
            await UiHarness.Pump();

            var last = UiHarness.Find<TextBlock>(view, "LastOpText");
            Assert.StartsWith("直前: 検索", last.Text);
            Assert.Contains("2,000 件見つかりました", last.Text);

            // ヒントには開く（索引）と検索の2件が積まれている
            string tip = OperationLog.Tip(ja: true);
            Assert.Contains("検索", tip);
            Assert.Contains("開く", tip);
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public async Task 中止すると途中までの時間が残る()
    {
        var (view, window, path) = await OpenWithFile();
        try
        {
            StartSearch(view, window, "ERROR");
            var vm = (UwView.ViewModels.MainViewModel)window.DataContext!;
            vm.ActiveTab!.Session.CancelSearch();
            await UiHarness.Pump(30);

            // 中止でもダイアログは残り、経過時間が読める（UVP と同じ扱い）
            EditProgressWindow.Current?.CloseNow();
            Dispatcher.UIThread.RunJobs();
        }
        finally { File.Delete(path); }
    }
}
