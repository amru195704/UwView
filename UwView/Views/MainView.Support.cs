using System;
using UwView.Core;
using UwView.Services;

namespace UwView.Views;

/// <summary>
/// 支援の案内の帯（指示-20261113-05・案 A／C）。決まりは <see cref="SupportPrompt"/>、文面は <see cref="SupportPromptText"/>。
/// 出すのは起動直後だけ（作業の途中には出さない）。<see cref="SupportPrompt.Available"/> が false
///（無料版の本体以外・自動テスト）なら何もしない＝使った日も数えない。
/// </summary>
public partial class MainView
{
    /// <summary>いま出している帯（無ければ null。自動テスト用）。</summary>
    internal SupportBannerView? SupportBanner => SupportBannerHost.Content as SupportBannerView;

    /// <summary>
    /// 使ったことを記録し、今日出す帯があれば出す（日付を渡す形＝試験で差し替えられる）。
    /// 出した時点で「出した」と記録して保存するので、ボタンを押さずに閉じても［後で］と同じ扱いになる。
    /// </summary>
    internal SupportPromptKind ShowSupportBannerIfDue(DateOnly today)
    {
        if (!SupportPrompt.Available) return SupportPromptKind.None;
        var settings = AppSettingsRef.Current;
        settings.SupportBanner ??= new SupportPromptState();
        var kind = SupportPrompt.Begin(settings.SupportBanner, today, out bool changed);
        if (changed) settings.Save();
        if (kind == SupportPromptKind.None) return kind;

        var banner = new SupportBannerView(kind);
        banner.Chose += choice =>
        {
            var s = AppSettingsRef.Current;
            s.SupportBanner ??= new SupportPromptState();
            switch (choice)
            {
                case SupportBannerView.Choice.Support: SupportPrompt.MarkSupported(s.SupportBanner); s.Save(); break;
                case SupportBannerView.Choice.Never: SupportPrompt.MarkNeverShow(s.SupportBanner); s.Save(); break;
                // ［後で］・×: 出したときに記録済み（90 日後に 1 回だけ）
            }
            HideSupportBanner();
        };
        SupportBannerHost.Content = banner;
        SupportBannerHost.IsVisible = true;
        return kind;
    }

    /// <summary>帯を畳む（設定で「支援の案内を表示しない」にしたときも呼ぶ）。</summary>
    internal void HideSupportBanner()
    {
        SupportBannerHost.IsVisible = false;
        SupportBannerHost.Content = null;
    }
}
