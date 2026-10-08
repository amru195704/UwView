using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using UwView.Core;
using UwView.Services;
using UwView.ViewModels;
using UwView.Views;

namespace UwView.UiTests;

/// <summary>
/// 開いているファイルが外で切り詰め・ローテーション・削除されたときの帯と、末尾追従の読み直し（v1.8.2 extFS E-0）。
/// 見張りは 1 秒ごとなので、それぞれ数秒待つ。
/// </summary>
public class FollowUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "uwview-follow-ui-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public FollowUiTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "app.log");
        File.WriteAllText(_path, string.Concat(Enumerable.Range(1, 200).Select(i => $"line {i} ERROR\n")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private void Truncate()
    {
        using var fs = new FileStream(_path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        fs.SetLength(0);
    }

    private void Append(string text)
    {
        using var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        fs.Write(Encoding.UTF8.GetBytes(text));
    }

    private async Task<(Window W, MainView V, MainViewModel Vm)> Open()
    {
        var (w, v, vm) = UiHarness.OpenMainWindow();
        UiHarness.OpenFile(_path);
        await UiHarness.WaitUntil(() => vm.Tabs.Count == 1, "タブが開く");
        await UiHarness.WaitIndexed(vm.Tabs[0].Session);
        return (w, v, vm);
    }

    private static Border Banner(MainView v) => UiHarness.Find<Border>(v, "ChangeBanner");
    private static string BannerText(MainView v) => UiHarness.Find<TextBlock>(v, "ChangeBannerText").Text ?? "";

    [AvaloniaFact]
    public async Task 追従していないとき切り詰められたら帯が出て読み直すで読み直せる()
    {
        var (w, v, vm) = await Open();
        try
        {
            var old = vm.Tabs[0].Session;
            Truncate();
            await UiHarness.WaitUntil(() => Banner(v).IsVisible, "帯が出る", 10_000);
            Assert.Equal(FileFollow.Notice(FileChange.Truncated, _path, following: false), BannerText(v));
            Assert.Same(old, vm.Tabs[0].Session);                          // 勝手には読み直さない

            Append("after 1\n");
            UiHarness.Click(UiHarness.Find<Button>(v, "ChangeBannerReload"));
            await UiHarness.WaitIndexed(vm.Tabs[0].Session);

            Assert.NotSame(old, vm.Tabs[0].Session);
            Assert.Equal(1, vm.Tabs[0].Session.Document.TotalLines);
            Assert.False(Banner(v).IsVisible);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task 追従しているとき切り詰められたら自動で読み直して追従を続け検索し直しを案内する()
    {
        var (w, v, vm) = await Open();
        try
        {
            var old = vm.Tabs[0].Session;
            await old.StartSearchAsync(new SearchOptions("ERROR"));
            UiHarness.Find<ToggleButton>(v, "TailToggle").IsChecked = true;
            Assert.True(old.IsTailing);

            Truncate();
            Append("after 1\nafter 2\n");
            await UiHarness.WaitUntil(() => !ReferenceEquals(old, vm.Tabs[0].Session), "読み直す", 10_000);
            var fresh = vm.Tabs[0].Session;

            Assert.True(fresh.IsTailing);
            Assert.True(UiHarness.Find<ToggleButton>(v, "TailToggle").IsChecked);
            Assert.StartsWith(FileFollow.Reloaded(FileChange.Truncated, _path), BannerText(v));
            Assert.Contains(UwView.Localization.Localizer.Instance["SearchAgainAfterReload"], BannerText(v));
            Assert.Null(fresh.ActiveSearch);

            Append("after 3\n");                                            // 読み直したあとも追従する
            await UiHarness.WaitIndexed(fresh);
            await UiHarness.WaitUntil(() => fresh.Document.TotalLines == 3, "追記に追従", 10_000);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task 追従しているとき消されたら待って同じ名前でできたら開き直す()
    {
        var (w, v, vm) = await Open();
        try
        {
            var old = vm.Tabs[0].Session;
            UiHarness.Find<ToggleButton>(v, "TailToggle").IsChecked = true;

            File.Delete(_path);
            await UiHarness.WaitUntil(() => Banner(v).IsVisible, "帯が出る", 10_000);
            Assert.Equal(FileFollow.Notice(FileChange.Deleted, _path, following: true), BannerText(v));
            Assert.False(UiHarness.Find<Button>(v, "ChangeBannerReload").IsVisible);
            Assert.Same(old, vm.Tabs[0].Session);

            File.WriteAllText(_path, "new 1\n");
            await UiHarness.WaitUntil(() => !ReferenceEquals(old, vm.Tabs[0].Session), "開き直す", 10_000);
            await UiHarness.WaitIndexed(vm.Tabs[0].Session);
            Assert.Equal("new 1", vm.Tabs[0].Session.Document.GetLine(0));
            Assert.True(vm.Tabs[0].Session.IsTailing);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task ローテーションされたら追従していなくても帯で知らせ開いている方はそのまま見られる()
    {
        var (w, v, vm) = await Open();
        try
        {
            var old = vm.Tabs[0].Session;
            File.Move(_path, _path + ".1");
            File.WriteAllText(_path, "");
            await UiHarness.WaitUntil(() => Banner(v).IsVisible, "帯が出る", 10_000);

            Assert.Equal(FileFollow.Notice(FileChange.Replaced, _path, following: false), BannerText(v));
            Assert.Equal("line 200 ERROR", old.Document.GetLine(199));
            UiHarness.Click(UiHarness.Find<Button>(v, "ChangeBannerClose"));
            Assert.False(Banner(v).IsVisible);
        }
        finally { w.Close(); }
    }
}
