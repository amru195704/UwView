using System;
using System.Linq;
using Avalonia.Threading;
using UwView.Core;
using UwView.Services;
using UwView.ViewModels;

namespace UwView.Views;

/// <summary>
/// 開いているファイルが外で変わったとき（切り詰め・作り直し・ローテーション・削除）の帯と、
/// 末尾追従のときの自動の読み直し（v1.8.2 extFS E-0。決まりは <see cref="FileFollow"/>）。
/// </summary>
public partial class MainView
{
    private DispatcherTimer? _bannerTimer;

    private void AttachChangeBanner()
    {
        ChangeBannerReload.Click += (_, _) => ReloadActiveTab();
        ChangeBannerClose.Click += (_, _) => HideChangeBanner();
    }

    /// <summary>開いたタブのセッションで、1 秒ごとの見張りを始める（追従していなくても変化は見る）。</summary>
    private void WatchExternalChanges(DocumentSession session)
    {
        session.ExternalChangeDetected += OnExternalChangeDetected;
        session.StartWatch();
    }

    private void OnExternalChangeDetected(object? sender, EventArgs e)
    {
        // 今見ているタブだけ動く。裏のタブは、表に出たときに同じ判断をする（SwitchActive → ApplyExternalChange）
        if (sender is DocumentSession session && ReferenceEquals(_vm?.ActiveTab?.Session, session))
            ApplyExternalChange(_vm.ActiveTab);
    }

    /// <summary>今のタブのセッションで起きた変化に合わせて、帯を出すか読み直す。</summary>
    private void ApplyExternalChange(DocumentTabViewModel? tab)
    {
        if (tab?.Session is not { } session || session.ExternalChange is FileChange.None)
        {
            HideChangeBanner();
            return;
        }
        var change = session.ExternalChange;
        switch (FileFollow.Decide(change, session.IsTailing))
        {
            case FileFollow.Step.Reload:
                bool searched = session.ActiveSearch is not null;
                string path = session.FilePath;
                if (ReloadActiveTab(follow: true) is null) { ShowChangeBanner(session); return; }
                string text = FileFollow.Reloaded(change, path);
                if (searched) text += "　" + L["SearchAgainAfterReload"];
                ShowChangeBanner(text, canReload: false, FileFollow.ReloadedNoticeTime);
                break;
            case FileFollow.Step.Banner or FileFollow.Step.Wait:
                ShowChangeBanner(session);
                break;
            default:
                HideChangeBanner();
                break;
        }
    }

    private void ShowChangeBanner(DocumentSession session)
        => ShowChangeBanner(FileFollow.Notice(session.ExternalChange, session.FilePath, session.IsTailing),
                            FileFollow.CanReload(session.ExternalChange), null);

    private void ShowChangeBanner(string text, bool canReload, TimeSpan? hideAfter)
    {
        _bannerTimer?.Stop();
        ChangeBannerText.Text = text;
        ChangeBannerReload.IsVisible = canReload;
        ChangeBanner.IsVisible = true;
        if (hideAfter is not { } after) return;
        _bannerTimer = new DispatcherTimer { Interval = after };
        _bannerTimer.Tick += (_, _) => HideChangeBanner();
        _bannerTimer.Start();
    }

    private void HideChangeBanner()
    {
        _bannerTimer?.Stop();
        _bannerTimer = null;
        ChangeBanner.IsVisible = false;
    }

    /// <summary>試験用の口が読む、今の帯の文（出ていなければ空）。</summary>
    internal string ChangeBannerShown => ChangeBanner.IsVisible ? ChangeBannerText.Text ?? "" : "";
}
