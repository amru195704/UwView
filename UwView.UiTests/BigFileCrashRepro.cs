using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using UwView.Views;

namespace UwView.UiTests;

/// <summary>
/// 【一時】2026-09-18 のクラッシュ調査用。実機と同じ 10GB ファイルで検索を2回する。
/// 環境変数 UVF_BIGFILE に実ファイルのパスを入れたときだけ動く（普段は飛ばす）。
/// </summary>
public class BigFileCrashRepro
{
    [AvaloniaFact]
    public async Task 大きいファイルで検索を2回する()
    {
        string? path = Environment.GetEnvironmentVariable("UVF_BIGFILE");
        if (path is null || !File.Exists(path)) return;   // 指定が無ければ何もしない

        string word = Environment.GetEnvironmentVariable("UVF_WORD") ?? "東京";

        // 実機と同じ: uvf … -open から起動され、1回目は起動シーケンスの中で走る
        var (window, view, vm) = UiHarness.OpenMainWindow();
        UwView.App.PendingCliSearch = word;
        view.RunStartup([path]);
        await UiHarness.WaitUntil(() => vm.ActiveTab is not null, "タブが開く", 600_000);
        await UiHarness.WaitIndexed(vm.ActiveTab!.Session, 600_000);
        await UiHarness.WaitSearchDone(vm.ActiveTab.Session, 600_000);
        await UiHarness.Pump(40);
        Log($"-open の検索 完了 {vm.ActiveTab.Session.SearchHits.Count:N0} 件");
        EditProgressWindow.Current?.CloseNow();
        await UiHarness.Pump(40);
        Log($"-open の結果一覧={view.FilterResultsOpen}");

        for (int round = 2; round <= 3; round++)
        {
            vm.SearchText = word;
            UiHarness.Click(UiHarness.Find<Button>(view, "SearchButton"));
            Log($"{round} 回目の検索を開始");
            await UiHarness.WaitSearchDone(vm.ActiveTab.Session, 600_000);
            await UiHarness.Pump(40);
            Log($"{round} 回目 完了 {vm.ActiveTab.Session.SearchHits.Count:N0} 件");

            EditProgressWindow.Current?.CloseNow();   // 実機と同じくダイアログを閉じる
            await UiHarness.Pump(40);
            Log($"{round} 回目 結果一覧={view.FilterResultsOpen}");
        }
        Log("落ちずに終わった");
        window.Close();
    }

    /// <summary>xunit は Console を拾わないのでファイルへ書く。</summary>
    private static void Log(string message)
    {
        string line = $"[repro {DateTime.Now:HH:mm:ss}] {message}";
        Console.WriteLine(line);
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "uvf-repro.log"), line + "\n"); }
        catch (IOException) { }
    }
}
