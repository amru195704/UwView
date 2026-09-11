using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using UwView.Core;
using UwView.Services;
using UwView.ViewModels;
using UwView.Views;

namespace UwView.UiTests;

/// <summary>
/// UVF の UI テスト共通土台。
///
/// 重要（安全策）: App は起動時に実ユーザーの settings.json
/// （検索履歴・お気に入り・前回セッション）を読み書きする。
/// テストがそれを壊さないよう、設定を「メモリ上の新品」に差し替え、
/// 保存先も専用フォルダ（<see cref="TestAppDataFolder"/>）へ逃がす。
/// </summary>
public static class UiHarness
{
    /// <summary>実ユーザー設定を絶対に触らないための隔離フォルダ名。</summary>
    public const string TestAppDataFolder = "UwView-AppUiTests";

    /// <summary>App の静的状態をテスト用に差し替える（テストの先頭で毎回呼ぶ）。</summary>
    public static AppSettings IsolateSettings()
    {
        AppSettings.AppDataFolder = TestAppDataFolder;   // Save() の行き先を実設定から逃がす
        var settings = new AppSettings();                // 実ファイルは読まない（新品から始める）
        settings.Save();
        typeof(UwView.App).GetProperty(nameof(UwView.App.Settings))!
            .GetSetMethod(nonPublic: true)!.Invoke(null, [settings]);
        AppSettingsRef.Current = settings;
        UwView.Localization.Localizer.Instance.SetLanguage("ja");
        OperationLog.Clear();                            // 前のテストの記録を持ち越さない
        return settings;
    }

    /// <summary>本番と同じ MainWindow を組み立てて表示する（Headless なので画面には出ない）。</summary>
    public static (MainWindow Window, MainView View, MainViewModel Vm) OpenMainWindow()
    {
        IsolateSettings();
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var view = window.GetSelfAndLogicalDescendants().OfType<MainView>().Single();
        return (window, view, vm);
    }

    /// <summary>本番と同じ経路（OS からの「これを開け」）でファイルを開く。</summary>
    public static void OpenFile(string path)
    {
        Assert.NotNull(UwView.App.RequestOpenFiles);
        UwView.App.RequestOpenFiles!(new[] { path });
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>行を並べただけの一時ファイルを作る（テスト終了後も残るので呼び側で消す）。</summary>
    public static string WriteTempFile(IEnumerable<string> lines)
    {
        string path = Path.Combine(Path.GetTempPath(),
            $"uvf-uitest-{Guid.NewGuid():N}.log");
        File.WriteAllLines(path, lines);
        return path;
    }

    public static void Click(Button button)
    {
        Assert.True(button.IsEffectivelyEnabled, $"ボタンが無効です: {button.Name}");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    public static T Find<T>(Control root, string name) where T : Control
        => root.FindControl<T>(name)
           ?? root.GetSelfAndLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
           ?? throw new Xunit.Sdk.XunitException($"コントロールが見つかりません: {name}");

    public static async Task WaitUntil(Func<bool> condition, string what, int timeoutMs = 30_000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException($"{timeoutMs / 1000}秒待っても成立しませんでした: {what}");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        Dispatcher.UIThread.RunJobs();
    }

    public static Task WaitIndexed(DocumentSession s, int timeoutMs = 60_000)
        => WaitUntil(() => s.IsIndexed && !s.IsIndexing, "索引完了", timeoutMs);

    public static async Task WaitSearchDone(DocumentSession s, int timeoutMs = 60_000)
    {
        await WaitUntil(() => s.ActiveSearch is not null, "検索の開始", 15_000);
        await WaitUntil(() => !s.IsSearching, "検索完了", timeoutMs);
    }

    public static async Task Pump(int rounds = 10)
    {
        for (int i = 0; i < rounds; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
        Dispatcher.UIThread.RunJobs();
    }
}
