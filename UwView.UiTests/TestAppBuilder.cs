using Avalonia;
using Avalonia.Headless;
using UwView;

// UIテストは Avalonia のアプリ・表示言語・設定ファイルという「全体で1つ」の状態を触る。
// xUnit は既定でテストクラスを並列に走らせるため、そのままだと
// 言語を切り替えるテストと日本語を期待するテストが同時に動いて落ちる。
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

[assembly: AvaloniaTestApplication(typeof(UwView.UiTests.TestAppBuilder))]

namespace UwView.UiTests;

/// <summary>
/// Headless（ウィンドウを開かない）UI テスト用の AppBuilder。
/// <c>Program.BuildAvaloniaApp()</c> との違いは <c>UsePlatformDetect()</c> を
/// <c>UseHeadless()</c> に差し替えた1点のみ。Application は本番と同じ <see cref="UwView.App"/>。
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        // App.OnFrameworkInitializationCompleted は Headless でも走り、
        // 実ユーザーの settings.json（検索履歴・お気に入り・セッション）を読み書きしてしまう。
        // 起動より先にフォルダを差し替えて、テストが実設定に触れないようにする。
        UwView.Services.AppSettings.AppDataFolder = UiHarness.TestAppDataFolder;
        return AppBuilder.Configure<UwView.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
            .WithInterFont();
    }
}
