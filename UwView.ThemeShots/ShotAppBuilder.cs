using Avalonia;
using Avalonia.Headless;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
[assembly: AvaloniaTestApplication(typeof(UwView.ThemeShots.ShotAppBuilder))]

namespace UwView.ThemeShots;

/// <summary>UI テストの TestAppBuilder との違いは 1 点: 写しを撮るので、描画を省かず Skia で本当に描く。</summary>
public static class ShotAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        UwView.Services.AppSettings.AppDataFolder = UwView.UiTests.UiHarness.TestAppDataFolder;
        return AppBuilder.Configure<UwView.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
    }
}
