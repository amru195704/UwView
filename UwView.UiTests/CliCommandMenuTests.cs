using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using UwView.Core.Cli;
using UwView.Views;

namespace UwView.UiTests;

/// <summary>
/// ヘルプ「コマンドライン設定…」で uvf をコンソールから使えるようにする（2026-09-14）。
/// 本物の登録（/usr/local/bin・レジストリ）はせず、状態と実行を差し替えて画面の流れだけを見る。
/// </summary>
public class CliCommandMenuTests : IDisposable
{
    private readonly List<string> _asked = [];
    private readonly List<string> _notices = [];
    private readonly List<(CliCommandState State, bool Install)> _applied = [];

    /// <summary>3択で選ぶもの（1=登録し直す・2=解除する・0=閉じる）。</summary>
    private int _choice = 2;

    public CliCommandMenuTests()
    {
        UwView.Localization.Localizer.Instance.SetLanguage("ja");
        CliCommandDialog.AskOverride = m => { _asked.Add(m); return Task.FromResult(true); };
        CliCommandDialog.ChooseOverride = m => { _asked.Add(m); return Task.FromResult(_choice); };
        CliCommandDialog.NoticeOverride = m => _notices.Add(m);
        CliCommandDialog.ApplyOverride = (s, install) =>
        {
            _applied.Add((s.State, install));
            return Task.FromResult(CliCommandResult.Success);
        };
    }

    public void Dispose()
    {
        CliCommandDialog.InspectOverride = null;
        CliCommandDialog.AskOverride = null;
        CliCommandDialog.ChooseOverride = null;
        CliCommandDialog.NoticeOverride = null;
        CliCommandDialog.ApplyOverride = null;
    }

    private static CliCommandStatus Status(CliCommandState state, string? existing = null) =>
        new(state, "uvf", "/Applications/UwView.app/Contents/MacOS/uvf", "/usr/local/bin/uvf", existing);

    private async Task ClickMenu(MainWindow w)
    {
        var item = UiHarness.Find<MenuItem>(w, "WinCliCommandItem");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await UiHarness.WaitUntil(() => _notices.Count > 0, "結果のお知らせ");
    }

    /// <summary>閉じたときなど、お知らせが出ない場合に使う（待たない）。</summary>
    private static async Task ClickMenuWithoutNotice(MainWindow w)
    {
        var item = UiHarness.Find<MenuItem>(w, "WinCliCommandItem");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Task.Yield();
    }

    [AvaloniaFact]
    public async Task 未登録なら確認してから登録し_使い方を知らせる()
    {
        CliCommandDialog.InspectOverride = _ => Status(CliCommandState.NotInstalled);
        var (w, _, _) = UiHarness.OpenMainWindow();
        try
        {
            await ClickMenu(w);
            Assert.Single(_asked);
            Assert.Contains("/usr/local/bin/uvf", _asked[0]);
            Assert.Equal([(CliCommandState.NotInstalled, true)], _applied);
            Assert.Contains("uvf -open", _notices[0]);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task 登録済みなら解除を尋ねる()
    {
        _choice = 2;   // 解除する
        CliCommandDialog.InspectOverride = _ => Status(CliCommandState.Installed);
        var (w, _, _) = UiHarness.OpenMainWindow();
        try
        {
            await ClickMenu(w);
            Assert.Contains("解除", _asked[0]);
            Assert.Equal([(CliCommandState.Installed, false)], _applied);
            Assert.Contains("解除しました", _notices[0]);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task 登録済みならその場で登録し直せる()
    {
        // 「解除してから登録し直す」だと、mac / Linux で管理者パスワードを2回聞かれる
        // （オーナー報告 2026-09-23）。1手で登録し直せること
        _choice = 1;   // 登録し直す
        CliCommandDialog.InspectOverride = _ => Status(CliCommandState.Installed);
        var (w, _, _) = UiHarness.OpenMainWindow();
        try
        {
            await ClickMenu(w);
            Assert.Contains("登録し直す", _asked[0]);
            Assert.Equal([(CliCommandState.Installed, true)], _applied);   // 解除を挟まない
            Assert.Contains("使えるようにしました", _notices[0]);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task 登録済みで閉じたら何もしない()
    {
        _choice = 0;
        CliCommandDialog.InspectOverride = _ => Status(CliCommandState.Installed);
        var (w, _, _) = UiHarness.OpenMainWindow();
        try
        {
            await ClickMenuWithoutNotice(w);
            Assert.Empty(_applied);
            Assert.Empty(_notices);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task 別の場所を指していれば置き換えを尋ねる()
    {
        CliCommandDialog.InspectOverride = _ => Status(CliCommandState.OtherTarget, "/Users/me/old/UwView.app/Contents/MacOS/uvf");
        var (w, _, _) = UiHarness.OpenMainWindow();
        try
        {
            await ClickMenu(w);
            Assert.Contains("/Users/me/old/", _asked[0]);
            Assert.Equal([(CliCommandState.OtherTarget, true)], _applied);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task 起動アプリが無い版や_dmgから起動中は登録しない()
    {
        foreach (var state in new[] { CliCommandState.Missing, CliCommandState.AppNotInPlace })
        {
            _notices.Clear();
            CliCommandDialog.InspectOverride = _ => Status(state);
            var (w, _, _) = UiHarness.OpenMainWindow();
            try { await ClickMenu(w); }
            finally { w.Close(); }
        }
        Assert.Empty(_asked);
        Assert.Empty(_applied);
    }

    [AvaloniaFact]
    public async Task 登録を失敗させると理由を出す()
    {
        CliCommandDialog.InspectOverride = _ => Status(CliCommandState.NotInstalled);
        CliCommandDialog.ApplyOverride = (_, _) => Task.FromResult(new CliCommandResult(false, Error: "Permission denied"));
        var (w, _, _) = UiHarness.OpenMainWindow();
        try
        {
            await ClickMenu(w);
            Assert.Contains("できませんでした", _notices[0]);
            Assert.Contains("Permission denied", _notices[0]);
        }
        finally { w.Close(); }
    }
}
