using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using UwView.Services;
using UwView.UiTests;
using UwView.Views;

namespace UwView.ThemeShots;

/// <summary>
/// uvf の窓をライト／ダーク × 100%／150% で撮る（v1.8.2 extFS E-4 §5）。
/// 出力: UV_THEME_SHOTS（無ければ一時フォルダーの uv-theme-shots）に <c>{OS}_uvf_{窓}_{テーマ}_{倍率}.png</c>。
/// 説明書に使うときは UV_THEME_SHOTS_FILE（例 /tmp/logs/app.log）で開くファイルの場所を決める（題名・帯・ステータスに写る）。
/// </summary>
public class ThemeShotTests
{
    private static readonly double[] Scales = [1.0, 1.5];

    private static string Dir
    {
        get
        {
            string dir = Environment.GetEnvironmentVariable("UV_THEME_SHOTS") is { Length: > 0 } d
                ? d : Path.Combine(Path.GetTempPath(), "uv-theme-shots");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string Os => OperatingSystem.IsMacOS() ? "mac" : OperatingSystem.IsWindows() ? "win" : "linux";

    private static void Save(Window w, string name, string theme)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        foreach (double scale in Scales)
        {
            var size = new PixelSize((int)Math.Ceiling(w.Bounds.Width * scale), (int)Math.Ceiling(w.Bounds.Height * scale));
            using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
            bitmap.Render(w);
            bitmap.Save(Path.Combine(Dir, $"{Os}_uvf_{name}_{theme}_{(int)(scale * 100)}.png"));
        }
    }

    private static T Owned<T>(Window owner) where T : Window
        => owner.OwnedWindows.OfType<T>().LastOrDefault() ?? throw new Xunit.Sdk.XunitException($"{typeof(T).Name} が開かない");

    [AvaloniaTheory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public async Task 窓を撮る(string theme)
    {
        var lines = Enumerable.Range(1, 300).Select(i =>
            $"2026-10-09 12:{i / 60:00}:{i % 60:00}.123 {(i % 7 == 0 ? "ERROR" : i % 5 == 0 ? "WARN" : "INFO")} worker-{i % 9} request id={i:00000} took {i % 97}ms");
        string path;
        if (Environment.GetEnvironmentVariable("UV_THEME_SHOTS_FILE") is { Length: > 0 } fixedPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(fixedPath))!);
            File.WriteAllLines(fixedPath, lines);
            path = Path.GetFullPath(fixedPath);
        }
        else path = UiHarness.WriteTempFile(lines);
        var (w, v, vm) = UiHarness.OpenMainWindow();
        AppTheme.Apply(theme);
        try
        {
            w.Width = 1200; w.Height = 720;
            UiHarness.OpenFile(path);
            await UiHarness.WaitUntil(() => vm.Tabs.Count == 1, "タブが開く");
            var session = vm.Tabs[0].Session;
            await UiHarness.WaitIndexed(session);
            vm.SearchText = "ERROR";
            UiHarness.Click(UiHarness.Find<Button>(v, "SearchButton"));
            await UiHarness.WaitSearchDone(session);
            session.SetBookmarks([session.Document.LineStartOffset(3), session.Document.LineStartOffset(13)]);
            typeof(MainView).GetMethod("ShowChangeBanner", BindingFlags.Instance | BindingFlags.NonPublic,
                                       [typeof(string), typeof(bool), typeof(TimeSpan?)])!
                .Invoke(v, [FileFollow.Notice(Core.FileChange.Truncated, path, following: false), true, null]);
            await UiHarness.Pump();
            Save(w, "main", theme);

            UiHarness.Click(UiHarness.Find<Button>(v, "FilterResultsButton"));
            await UiHarness.WaitUntil(() => v.FilterResultsOpen, "結果の一覧が開く", 10_000);
            var results = (Window)typeof(MainView).GetField("_filterResultsWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(v)!;
            results.Width = 760; results.Height = 420;
            Save(results, "results", theme);

            PreferencesWindow.Open(w);
            var prefs = PreferencesWindow.Current!;
            Save(prefs, "settings-keys", theme);
            prefs.Tabs.SelectedIndex = 1;
            await UiHarness.Pump();
            Save(prefs, "settings-display", theme);
            prefs.Close();

            UiHarness.Click(UiHarness.Find<Button>(v, "HighlighterButton"));
            await UiHarness.Pump();
            Save(Owned<HighlighterWindow>(w), "highlighter", theme);

            v.RunShortcut(ShortcutAction.OpenCommandLine);
            await UiHarness.Pump();
            Save(Owned<CommandLineDialog>(w), "commandline", theme);

            v.RunShortcut(ShortcutAction.OpenFileSwitcher);
            await UiHarness.Pump();
            Save(Owned<FileSwitcherWindow>(w), "fileswitcher", theme);
        }
        finally
        {
            foreach (var owned in w.OwnedWindows.ToList()) owned.Close();
            w.Close();
            AppTheme.Apply(AppTheme.System);
            File.Delete(path);
        }
    }
}
