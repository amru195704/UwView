using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using UwView.Controls;
using UwView.Services;
using UwView.Views;

namespace UwView.UiTests;

/// <summary>
/// キー操作の最小セットとブックマークを残す（実装指示書_klogg比較の追加機能_v1.7.3.6 §4・§6）。
/// </summary>
public class KeysAndBookmarksUiTests : IDisposable
{
    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (string f in _files) try { File.Delete(f); } catch (IOException) { }
    }

    /// <summary>「line N」を 1〜count 行（3 の倍数の行だけ ERROR）。</summary>
    private string Log(int count = 100)
    {
        string path = UiHarness.WriteTempFile(Enumerable.Range(1, count).Select(i => $"line {i}{(i % 3 == 0 ? " ERROR" : "")}"));
        _files.Add(path);
        return path;
    }

    private static RawInputModifiers Primary => OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static void Press(Window w, Key key, RawInputModifiers mods = RawInputModifiers.None, string symbol = "")
    {
        w.KeyPress(key, mods, PhysicalKey.None, symbol);
        Dispatcher.UIThread.RunJobs();
    }

    private static long LineStart(string path, int line)
        => File.ReadAllText(path).Split('\n').Take(line - 1).Sum(l => Encoding.UTF8.GetByteCount(l) + 1);

    private static async Task<(Window W, MainView V, ViewModels.MainViewModel Vm)> Open(string path)
    {
        var (w, v, vm) = UiHarness.OpenMainWindow();
        UiHarness.OpenFile(path);
        await UiHarness.WaitUntil(() => vm.Tabs.Count == 1, "タブが開く");
        await UiHarness.WaitIndexed(vm.Tabs[0].Session);
        await UiHarness.Pump();
        return (w, v, vm);
    }

    // ── キーの割り当て（画面を使わない）──

    [Theory]
    [InlineData(Key.F, KeyModifiers.Control, false, ShortcutAction.FocusSearch)]
    [InlineData(Key.F, KeyModifiers.Meta, true, ShortcutAction.FocusSearch)]
    [InlineData(Key.F3, KeyModifiers.None, false, ShortcutAction.NextHit)]
    [InlineData(Key.F3, KeyModifiers.Shift, false, ShortcutAction.PrevHit)]
    [InlineData(Key.G, KeyModifiers.Meta, true, ShortcutAction.NextHit)]
    [InlineData(Key.G, KeyModifiers.Meta | KeyModifiers.Shift, true, ShortcutAction.PrevHit)]
    [InlineData(Key.L, KeyModifiers.Control, false, ShortcutAction.FocusJump)]
    [InlineData(Key.B, KeyModifiers.Meta, true, ShortcutAction.ToggleBookmark)]
    [InlineData(Key.F, KeyModifiers.Control | KeyModifiers.Shift, false, ShortcutAction.ToggleTail)]
    [InlineData(Key.F5, KeyModifiers.None, false, ShortcutAction.Reload)]
    public void キーの割り当て(Key key, KeyModifiers mods, bool mac, ShortcutAction expected)
        => Assert.Equal(expected, ShortcutKeys.Map(key, mods, null, typing: false, mac));

    [Fact]
    public void Macでは素のCtrlはCmdの代わりにならない()
    {
        Assert.Null(ShortcutKeys.Map(Key.F, KeyModifiers.Control, null, typing: false, mac: true));
        Assert.Null(ShortcutKeys.Map(Key.G, KeyModifiers.Control, null, typing: false, mac: false));   // Cmd+G は Mac だけ
    }

    [Fact]
    public void 角かっこは文字で見て配列によらない()
    {
        Assert.Equal(ShortcutAction.PrevBookmark, ShortcutKeys.Map(Key.OemOpenBrackets, KeyModifiers.None, "[", false, false));
        Assert.Equal(ShortcutAction.NextBookmark, ShortcutKeys.Map(Key.Oem6, KeyModifiers.None, "]", false, false));
        // AltGr（Ctrl+Alt）で打つ配列
        Assert.Equal(ShortcutAction.PrevBookmark,
                     ShortcutKeys.Map(Key.D8, KeyModifiers.Control | KeyModifiers.Alt, "[", false, false));
        Assert.Null(ShortcutKeys.Map(Key.OemOpenBrackets, KeyModifiers.Control, "[", false, false));
    }

    [Fact]
    public void 文字を打つ欄では角かっこを取らずEscで本文へ戻る()
    {
        Assert.Null(ShortcutKeys.Map(Key.OemOpenBrackets, KeyModifiers.None, "[", typing: true, mac: false));
        Assert.Equal(ShortcutAction.LeaveInput, ShortcutKeys.Map(Key.Escape, KeyModifiers.None, null, typing: true, mac: false));
        Assert.Null(ShortcutKeys.Map(Key.Escape, KeyModifiers.None, null, typing: false, mac: false));
        Assert.Equal(ShortcutAction.NextHit, ShortcutKeys.Map(Key.F3, KeyModifiers.None, null, typing: true, mac: false));
    }

    // ── キー操作（画面）──

    [AvaloniaFact]
    public async Task 検索欄とジャンプ欄へ移りEnterで移動しEscで本文へ戻る()
    {
        string path = Log();
        var (w, v, _) = await Open(path);
        try
        {
            Press(w, Key.F, Primary);
            Assert.IsType<TextBox>(w.FocusManager!.GetFocusedElement());
            Assert.True(UiHarness.Find<AutoCompleteBox>(v, "SearchBox").IsKeyboardFocusWithin);

            Press(w, Key.L, Primary);
            var jump = UiHarness.Find<TextBox>(v, "JumpBox");
            Assert.True(jump.IsFocused);
            jump.Text = "50";
            Press(w, Key.Enter);
            var text = UiHarness.Find<TextView>(v, "TextView");
            Assert.Equal(LineStart(path, 50), text.EmphasizedOffset);
            Assert.True(text.IsFocused);                                 // 移動したら本文へ

            Press(w, Key.F, Primary);
            Press(w, Key.Escape);
            Assert.True(text.IsFocused);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task F3で次の当たりShiftF3で前の当たり()
    {
        string path = Log();
        var (w, v, vm) = await Open(path);
        try
        {
            var box = UiHarness.Find<AutoCompleteBox>(v, "SearchBox");
            box.Text = "ERROR";
            UiHarness.Click(UiHarness.Find<Button>(v, "SearchButton"));
            await UiHarness.WaitSearchDone(vm.Tabs[0].Session);
            var text = UiHarness.Find<TextView>(v, "TextView");
            text.Focus();

            Press(w, Key.F3);
            long first = text.EmphasizedOffset!.Value;
            Press(w, Key.F3);
            Assert.Equal(LineStart(path, 6), text.EmphasizedOffset);
            Assert.True(text.EmphasizedOffset > first);
            Press(w, Key.F3, RawInputModifiers.Shift);
            Assert.Equal(first, text.EmphasizedOffset);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task CtrlBで付けて角かっこで前後へ移る_検索欄では角かっこは文字になる()
    {
        string path = Log();
        var (w, v, vm) = await Open(path);
        try
        {
            var session = vm.Tabs[0].Session;
            var text = UiHarness.Find<TextView>(v, "TextView");
            text.Focus();
            Press(w, Key.B, Primary);                                    // 1 行目
            Assert.Equal([0L], session.Bookmarks);
            session.ToggleBookmark(LineStart(path, 30));
            session.ToggleBookmark(LineStart(path, 60));

            Press(w, Key.Oem6, RawInputModifiers.None, "]");
            Assert.Equal(LineStart(path, 30), text.CurrentOffset);
            Press(w, Key.Oem6, RawInputModifiers.None, "]");
            Assert.Equal(LineStart(path, 60), text.CurrentOffset);
            Press(w, Key.OemOpenBrackets, RawInputModifiers.None, "[");
            Assert.Equal(LineStart(path, 30), text.CurrentOffset);

            Press(w, Key.F, Primary);                                    // 検索欄にいる間は移らない
            Press(w, Key.Oem6, RawInputModifiers.None, "]");
            Assert.Equal(LineStart(path, 30), text.CurrentOffset);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task F5で読み直すと同じタブの位置でブックマークも戻る()
    {
        string path = Log();
        var (w, v, vm) = await Open(path);
        try
        {
            var old = vm.Tabs[0].Session;
            old.ToggleBookmark(LineStart(path, 40));
            File.AppendAllText(path, "line 101 added\n");

            Press(w, Key.F5);
            await UiHarness.WaitUntil(() => vm.Tabs.Count == 1 && !ReferenceEquals(vm.Tabs[0].Session, old), "開き直す");
            var fresh = vm.Tabs[0].Session;
            Assert.Same(vm.ActiveTab, vm.Tabs[0]);
            Assert.Equal([LineStart(path, 40)], fresh.Bookmarks);            // 追記だけなので戻る
            Assert.Equal(new FileInfo(path).Length, fresh.Document.Length);   // 追記ぶんも読んでいる
            Assert.Equal(UiHarness.Ja("ファイルを読み直しました", "Reloaded the file"), vm.Notice);
        }
        finally { w.Close(); }
    }

    // ── ブックマークを残す ──

    [AvaloniaFact]
    public async Task 閉じて開き直すとブックマークが戻る_全部外すと記録も消える()
    {
        string path = Log();
        var (w, _, vm) = await Open(path);
        try
        {
            vm.Tabs[0].Session.ToggleBookmark(LineStart(path, 10));
            vm.Tabs[0].Session.ToggleBookmark(LineStart(path, 20));
            vm.RequestClose(vm.Tabs[0]);
            await UiHarness.WaitUntil(() => vm.Tabs.Count == 0, "閉じる");

            UiHarness.OpenFile(path);
            await UiHarness.WaitUntil(() => vm.Tabs.Count == 1, "開き直す");
            var session = vm.Tabs[0].Session;
            Assert.Equal([LineStart(path, 10), LineStart(path, 20)], session.Bookmarks);

            session.ToggleBookmark(LineStart(path, 10));
            session.ToggleBookmark(LineStart(path, 20));
            Assert.DoesNotContain(UwView.App.Settings.PerFileStates, s => s.PathKey == Path.GetFullPath(path));
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task 中身が変わっていたら戻さずに知らせる()
    {
        string path = Log();
        var (w, _, vm) = await Open(path);
        try
        {
            vm.Tabs[0].Session.ToggleBookmark(LineStart(path, 10));
            vm.RequestClose(vm.Tabs[0]);
            await UiHarness.WaitUntil(() => vm.Tabs.Count == 0, "閉じる");
            File.WriteAllText(path, "rewritten\n" + File.ReadAllText(path));   // 先頭が変わった（ローテーションなど）

            UiHarness.OpenFile(path);
            await UiHarness.WaitUntil(() => vm.Tabs.Count == 1, "開き直す");
            Assert.Empty(vm.Tabs[0].Session.Bookmarks);
            Assert.Contains(UiHarness.Ja("ブックマークを戻しませんでした", "were not restored"), vm.Notice);
            Assert.DoesNotContain(UwView.App.Settings.PerFileStates, s => s.PathKey == Path.GetFullPath(path));
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task ブックマークの一覧を行番号と本文で書き出す()
    {
        string path = Log();
        var (w, _, vm) = await Open(path);
        try
        {
            var session = vm.Tabs[0].Session;
            session.ToggleBookmark(LineStart(path, 42));
            session.ToggleBookmark(LineStart(path, 7));
            using var output = new MemoryStream();

            Assert.Equal(2, await BookmarkMemory.ExportAsync(session, output));
            Assert.Equal("7\tline 7\n42\tline 42 ERROR\n", Encoding.UTF8.GetString(output.ToArray()).Replace("\r\n", "\n"));
            Assert.EndsWith("-bookmarks.txt", BookmarkMemory.ExportFileName(session));
        }
        finally { w.Close(); }
    }
}
