using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UwView.Core;
using UwView.Views;

namespace UwView.UiTests;

/// <summary>
/// 支援の案内の帯（指示-20261113-05）。日付を渡して出す・出さないを決める。
/// 検査課の確認項目: ②表示条件どおりに出る ③設定で止まる（①CLI は UwView.Core.Tests の SupportNoticeTests）。
/// <see cref="SupportPrompt.Available"/> は全体で 1 つの状態なので、各テストで立てて最後に戻す
///（UI テストは並列に走らない＝TestAppBuilder）。
/// </summary>
public class SupportBannerUiTests
{
    private static readonly DateOnly Spring = new(2026, 3, 1);

    /// <summary>メイン画面を開き、設定に「Spring から days 日、毎日使った」記録を入れる（今日の分はまだ数えない）。</summary>
    private static (MainWindow Window, MainView View) OpenWithUse(int days)
    {
        var (w, view, _) = UiHarness.OpenMainWindow();
        var s = UwView.Services.AppSettingsRef.Current.SupportBanner;
        for (int i = 0; i < days; i++) SupportPrompt.RecordUse(s, Spring.AddDays(i));
        return (w, view);
    }

    private static T Named<T>(Control root, string name) where T : Control
        => root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
           ?? throw new Xunit.Sdk.XunitException($"見つかりません: {name}");

    /// <summary>
    /// 画面を開いて（このときはまだ無料版扱いにしない＝起動時の帯は出ない・実際の今日を数えない）、
    /// 記録を入れてから無料版扱いにして body を走らせる。最後に元に戻して閉じる。
    /// </summary>
    private static void WithAvailable(int days, Action<MainWindow, MainView> body)
    {
        bool before = SupportPrompt.Available;
        var (w, view) = OpenWithUse(days);
        SupportPrompt.Available = true;
        try { body(w, view); }
        finally
        {
            SupportPrompt.Available = before;
            w.Close();
        }
    }

    [AvaloniaFact]
    public void 無料版でなければ出さず日も数えない()
    {
        var (w, view) = OpenWithUse(20);
        try
        {
            Assert.False(SupportPrompt.Available);   // 自動テスト・同じ部品を使う別のアプリでは立っていない
            Assert.Equal(SupportPromptKind.None, view.ShowSupportBannerIfDue(Spring.AddDays(40)));
            Assert.Null(view.SupportBanner);
            Assert.Equal(20, UwView.Services.AppSettingsRef.Current.SupportBanner.UsedDays);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void 条件を満たさない日は出さない()
        => WithAvailable(9, (w, view) =>
        {   // 使った日 9 日（+今日で 10 日）だが、30 日たっていない
            Assert.Equal(SupportPromptKind.None, view.ShowSupportBannerIfDue(Spring.AddDays(29)));
            Assert.Null(view.SupportBanner);
            Assert.Equal(10, UwView.Services.AppSettingsRef.Current.SupportBanner.UsedDays);   // 数えるのは数える
        });

    [AvaloniaFact]
    public void 条件を満たした日に下の帯が出てモーダルにならない()
        => WithAvailable(20, (w, view) =>
        {
            Assert.Equal(SupportPromptKind.First, view.ShowSupportBannerIfDue(Spring.AddDays(30)));
            Dispatcher.UIThread.RunJobs();
            var banner = view.SupportBanner ?? throw new Xunit.Sdk.XunitException("帯が出ない");
            Assert.True(banner.IsEffectivelyVisible);
            Assert.Equal("UwView をお使いいただき、ありがとうございます", Named<TextBlock>(banner, "SupportTitle").Text);
            Assert.Equal("支援する（GitHub Sponsors）", Named<Button>(banner, "SupportSponsorButton").Content);
            Assert.Equal("後で", Named<Button>(banner, "SupportLaterButton").Content);
            Assert.Equal("もう表示しない", Named<Button>(banner, "SupportNeverButton").Content);
            Assert.Contains("UwView Pro", Named<TextBlock>(banner, "SupportProLink").Text);
            // 本文・検索欄はそのまま使える（覆わない・止めない）
            Assert.True(UiHarness.Find<Control>(view, "TextView").IsEffectivelyEnabled);
            Assert.True(UiHarness.Find<Button>(view, "SearchButton").IsEffectivelyEnabled);
            // 帯は本文より下
            w.UpdateLayout();
            var bannerTop = banner.TranslatePoint(new Point(0, 0), w)!.Value.Y;
            var textTop = UiHarness.Find<Control>(view, "TextView").TranslatePoint(new Point(0, 0), w)!.Value.Y;
            Assert.True(bannerTop > textTop, "帯が画面の下にない");
            // 出した時点で記録済み（閉じただけでも［後で］と同じ）
            Assert.Equal(1, UwView.Services.AppSettingsRef.Current.SupportBanner.ShownCount);
        });

    [AvaloniaFact]
    public void 後でを押すと畳んで90日後に1回だけ出る()
        => WithAvailable(20, (w, view) =>
        {
            view.ShowSupportBannerIfDue(Spring.AddDays(30));
            UiHarness.Click(Named<Button>(view.SupportBanner!, "SupportLaterButton"));
            Assert.Null(view.SupportBanner);
            Assert.Equal(SupportPromptKind.None, view.ShowSupportBannerIfDue(Spring.AddDays(119)));
            Assert.Equal(SupportPromptKind.Reminder, view.ShowSupportBannerIfDue(Spring.AddDays(120)));
            UiHarness.Click(Named<Button>(view.SupportBanner!, "SupportCloseButton"));   // × も［後で］と同じ
            Assert.Null(view.SupportBanner);
            Assert.Equal(SupportPromptKind.None, view.ShowSupportBannerIfDue(Spring.AddDays(250)));   // 2 回まで（11/6＝年末の期間の外）
        });

    [AvaloniaFact]
    public void 支援するはSponsorsを開いて以後出さない()
        => WithAvailable(20, (w, view) =>
        {
            view.ShowSupportBannerIfDue(Spring.AddDays(30));
            var opened = new List<string>();
            view.SupportBanner!.OpenUrl = opened.Add;
            UiHarness.Click(Named<Button>(view.SupportBanner!, "SupportSponsorButton"));
            Assert.Equal(new[] { SupportPrompt.SponsorsUrl }, opened);
            Assert.Null(view.SupportBanner);
            Assert.True(UwView.Services.AppSettingsRef.Current.SupportBanner.Supported);
            Assert.Equal(SupportPromptKind.None, view.ShowSupportBannerIfDue(new DateOnly(2026, 12, 20)));   // 年末も出ない
        });

    [AvaloniaFact]
    public void もう表示しないを押すと設定の項目もオンになる()
        => WithAvailable(20, (w, view) =>
        {
            view.ShowSupportBannerIfDue(Spring.AddDays(30));
            UiHarness.Click(Named<Button>(view.SupportBanner!, "SupportNeverButton"));
            Assert.True(UwView.Services.AppSettingsRef.Current.SupportBanner.Disabled);
            Assert.Equal(SupportPromptKind.None, view.ShowSupportBannerIfDue(new DateOnly(2026, 12, 20)));

            PreferencesWindow.Open(w);
            Dispatcher.UIThread.RunJobs();
            var prefs = PreferencesWindow.Current!;
            prefs.UpdateLayout();
            try { Assert.True(Named<CheckBox>(prefs, "SupportBannerOffBox").IsChecked); }
            finally { prefs.Close(); }
        });

    [AvaloniaFact]
    public void 設定で止めると出ている帯も畳み以後出ない()
        => WithAvailable(20, (w, view) =>
        {
            view.ShowSupportBannerIfDue(Spring.AddDays(30));
            Assert.NotNull(view.SupportBanner);
            PreferencesWindow.SupportBannerClosed = view.HideSupportBanner;   // 本番は Loaded で差し込む
            PreferencesWindow.Open(w);
            Dispatcher.UIThread.RunJobs();
            var prefs = PreferencesWindow.Current!;
            prefs.UpdateLayout();
            try
            {
                var box = Named<CheckBox>(prefs, "SupportBannerOffBox");
                Assert.Equal("支援の案内を表示しない", box.Content);
                box.IsChecked = true;
                Dispatcher.UIThread.RunJobs();
            }
            finally { prefs.Close(); }
            Assert.Null(view.SupportBanner);
            Assert.True(UwView.Services.AppSettingsRef.Current.SupportBanner.Disabled);
            Assert.Equal(SupportPromptKind.None, view.ShowSupportBannerIfDue(Spring.AddDays(200)));
        });

    [AvaloniaFact]
    public void 無料版でなければ設定の項目も出さない()
    {
        var (w, _) = OpenWithUse(0);
        try
        {
            PreferencesWindow.Open(w);
            Dispatcher.UIThread.RunJobs();
            var prefs = PreferencesWindow.Current!;
            prefs.UpdateLayout();
            try { Assert.DoesNotContain(prefs.GetVisualDescendants().OfType<CheckBox>(), c => c.Name == "SupportBannerOffBox"); }
            finally { prefs.Close(); }
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void 年末の期間は呼びかけの文面になる()
        => WithAvailable(0, (w, view) =>
        {
            var s = UwView.Services.AppSettingsRef.Current.SupportBanner;
            var start = new DateOnly(2026, 11, 20);
            for (int i = 0; i < 25; i++) SupportPrompt.RecordUse(s, start.AddDays(i));
            Assert.Equal(SupportPromptKind.Season, view.ShowSupportBannerIfDue(new DateOnly(2026, 12, 20)));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("今年もお使いいただき、ありがとうございました", Named<TextBlock>(view.SupportBanner!, "SupportTitle").Text);
            Assert.StartsWith("UwView（無料版）は来年も無料です。", Named<TextBlock>(view.SupportBanner!, "SupportBody1").Text);
        });
}
