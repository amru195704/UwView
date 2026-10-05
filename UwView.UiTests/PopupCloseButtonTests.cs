using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UwView.ViewModels;
using UwView.Views;

namespace UwView.UiTests;

/// <summary>ポップアップの「閉じる」は右下に固定（オーナー指示 2026-10-05）。窓の大きさを変えても右端・一番下。</summary>
public class PopupCloseButtonTests
{
    public PopupCloseButtonTests() => UwView.Localization.Localizer.Instance.SetLanguage("ja");

    private static void AssertBottomRight(Window window, Button close, params (double W, double H)[] sizes)
    {
        Assert.Equal("閉じる", close.Content);
        foreach (var (w, h) in sizes)
        {
            window.Width = w;
            window.Height = h;
            window.UpdateLayout();
            Assert.True(close.IsEffectivelyVisible, "閉じるが見えていない");
            var corner = close.TranslatePoint(new Point(close.Bounds.Width, close.Bounds.Height), window)!.Value;
            Assert.InRange(window.Bounds.Width - corner.X, 0, 24);
            Assert.InRange(window.Bounds.Height - corner.Y, 0, 24);
        }
    }

    [AvaloniaFact]
    public void 結果一覧の閉じるは右下()
    {
        var (owner, _, _) = UiHarness.OpenMainWindow();
        var results = new FilterResultsWindow(new FilterResultsViewModel(_ => { }, maxContext: 0));
        results.Show(owner);
        Dispatcher.UIThread.RunJobs();
        var close = results.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CloseButton");
        AssertBottomRight(results, close, (760, 520), (1200, 800), (420, 300));
        UiHarness.Click(close);
        Dispatcher.UIThread.RunJobs();
        Assert.False(results.IsVisible);
    }

    [AvaloniaFact]
    public void コマンドラインのダイアログの閉じるは右下()
    {
        var (owner, view, _) = UiHarness.OpenMainWindow();
        view.OpenCommandLine();
        Dispatcher.UIThread.RunJobs();
        var dialog = CommandLineDialog.For(owner)!;
        var close = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CmdCloseButton");
        AssertBottomRight(dialog, close, (860, 620), (1200, 800), (560, 420));
        dialog.Close();
    }
}
