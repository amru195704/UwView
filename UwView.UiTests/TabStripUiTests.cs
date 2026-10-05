using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UwView.ViewModels;

namespace UwView.UiTests;

/// <summary>
/// タブの見た目（9.27修正）: 名前は本文と同じ大きさ・1行のまま左右にスクロール・
/// 名前は <c>番号:ファイル名</c> で 27 文字まで（越えたら ...）・重ねるとファイル名。
/// </summary>
public class TabStripUiTests : IDisposable
{
    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (string f in _files) try { File.Delete(f); } catch (IOException) { }
    }

    private string Log(string name)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-{name}");
        File.WriteAllText(path, "a\nb\n");
        _files.Add(path);
        return path;
    }

    [AvaloniaFact]
    public async Task 名前は27文字までで越えたら末尾を点で切る()
    {
        string path = Log("x.log");
        await using var session = UwView.Core.DocumentSession.Open(path);
        var tab = new DocumentTabViewModel(session, _ => { });

        string name = session.DisplayName;
        Assert.True(name.Length > DocumentTabViewModel.MaxTabTitleLength);   // 名前は GUID 付きで長い
        Assert.Equal(DocumentTabViewModel.MaxTabTitleLength, tab.TabTitle.Length);
        Assert.EndsWith("...", tab.TabTitle);
        Assert.StartsWith(name[..10], tab.TabTitle);

        tab.TitlePrefix = "12:";
        Assert.StartsWith("12:", tab.TabTitle);
        Assert.Equal(DocumentTabViewModel.MaxTabTitleLength, tab.TabTitle.Length);

        // 重ねると、切らないファイル名（2行目は場所）
        Assert.StartsWith(name + "\n", tab.TabToolTip);
        Assert.EndsWith(path, tab.TabToolTip);
    }

    [AvaloniaFact]
    public async Task 短い名前はそのまま出す()
    {
        string path = Path.Combine(Path.GetTempPath(), "short.log");
        File.WriteAllText(path, "a\n");
        _files.Add(path);
        await using var session = UwView.Core.DocumentSession.Open(path);
        var tab = new DocumentTabViewModel(session, _ => { }) { TitlePrefix = "3:" };
        Assert.Equal("3:short.log", tab.TabTitle);
    }

    [AvaloniaFact]
    public void 題名にペット名と版数が出る()
    {
        var (window, _, _) = UiHarness.OpenMainWindow();
        try { Assert.Matches(@"^UwView\(uvf\)-Finder Scope(\(v[0-9.]+\))?$", window.Title); }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task タブが増えても1行のままで本文と同じ大きさ()
    {
        var (window, view, vm) = UiHarness.OpenMainWindow();
        try
        {
            for (int i = 0; i < 14; i++) UiHarness.OpenFile(Log($"tab{i:D2}.log"));
            await UiHarness.WaitUntil(() => vm.Tabs.Count == 14, "14 タブ");
            await UiHarness.Pump();
            window.UpdateLayout();

            var strip = UiHarness.Find<TabStrip>(view, "TabStripControl");
            var items = strip.GetVisualDescendants().OfType<TabStripItem>().ToList();
            Assert.Equal(14, items.Count);
            double top = items[0].Bounds.Top;
            Assert.All(items, item => Assert.Equal(top, item.Bounds.Top));      // 折り返さない
            Assert.All(items, item => Assert.Equal(14, item.FontSize));

            var scroll = UiHarness.Find<ScrollViewer>(view, "TabScroll");
            Assert.True(scroll.Extent.Width > scroll.Viewport.Width, "左右にスクロールできる幅になっていない");

            // 最後のタブを選ぶと見える位置まで送る
            vm.ActiveTab = vm.Tabs[^1];
            await UiHarness.Pump();
            window.UpdateLayout();
            Assert.True(scroll.Offset.X > 0, "選んだタブまで送っていない");
        }
        finally
        {
            foreach (var t in vm.Tabs.ToList()) vm.RequestClose(t);
            await UiHarness.Pump();
            window.Close();
        }
    }
}
