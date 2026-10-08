using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using UwView.Controls;
using UwView.Services;
using UwView.Views;

namespace UwView.UiTests;

/// <summary>キーの割り当て（v1.8.2 extFS E-3）。設定の画面・ひな形・ぶつかり・書き出しと読み込み・vi・less 風の操作。</summary>
public class KeyBindingsTests : IDisposable
{
    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (string f in _files) try { File.Delete(f); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static void Press(Window w, Key key, RawInputModifiers mods, string symbol)
    {
        w.KeyPress(key, mods, PhysicalKey.None, symbol);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Type(Window w, string keys)
    {
        foreach (char c in keys)
        {
            var key = char.IsAsciiDigit(c) ? Key.D0 + (c - '0') : char.IsAsciiLetter(c) ? Key.A + (char.ToUpperInvariant(c) - 'A') : Key.None;
            Press(w, key, char.IsAsciiLetterUpper(c) ? RawInputModifiers.Shift : RawInputModifiers.None, c.ToString());
        }
    }

    /// <summary>コードで組んだ部品の中のボタン（名前の範囲が無いので、論理木をたどって探す）。</summary>
    private static Button ButtonIn(Control root, string name)
        => Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root).OfType<Button>().First(b => b.Name == name);

    private static RawInputModifiers Primary => OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    // ── 組み合わせ（画面を使わない）──

    [Theory]
    [InlineData("Primary+Shift+F")]
    [InlineData("Shift+F3")]
    [InlineData("Escape")]
    [InlineData("Primary+OemComma")]
    [InlineData("j")]
    [InlineData("G")]
    [InlineData("$")]
    [InlineData("+")]
    public void 設定に書く形は読み戻すと同じになる(string text)
    {
        Assert.True(KeyChord.TryParse(text, out var chord));
        Assert.Equal(text, chord.ToString());
    }

    [Fact]
    public void 画面の表示はMacとそれ以外で書き分ける()
    {
        var chord = KeyChord.Of(Key.F, ChordMods.Primary | ChordMods.Shift);
        Assert.Equal("⇧⌘F", chord.Display(mac: true));
        Assert.Equal("Ctrl+Shift+F", chord.Display(mac: false));
        Assert.Equal("Esc", KeyChord.Of(Key.Escape).Display(mac: false));
    }

    [Fact]
    public void viless風はjで1行下で文字の欄の中ではjを取らずPrimaryのキーは取る()
    {
        var vi = KeyBindingSet.ViLess(mac: false);
        Assert.Equal(ShortcutAction.LineDown, vi.Find(Key.J, KeyModifiers.None, "j", typing: false, mac: false));
        Assert.Null(vi.Find(Key.J, KeyModifiers.None, "j", typing: true, mac: false));
        Assert.Equal(ShortcutAction.GoToEnd, vi.Find(Key.G, KeyModifiers.Shift, "G", typing: false, mac: false));
        Assert.Equal(ShortcutAction.FocusSearch, vi.Find(Key.F, KeyModifiers.Control, "f", typing: true, mac: false));
        Assert.Equal(ShortcutAction.ToggleTail, vi.Find(Key.F, KeyModifiers.None, "f", typing: false, mac: false));
        Assert.Equal([KeyChord.Of(Key.F3), KeyChord.Of('n')], vi.KeysOf(ShortcutAction.NextHit));
        Assert.Empty(KeyBindingSet.Standard(mac: false).KeysOf(ShortcutAction.LineDown));   // 標準ではキーなし
    }

    [Fact]
    public void 設定には標準と違う操作だけを書き書き出したものは読み込める()
    {
        var vi = KeyBindingSet.ViLess(mac: false);
        var diff = vi.DifferencesFrom(KeyBindingSet.Standard(mac: false));
        Assert.DoesNotContain(nameof(ShortcutAction.FocusSearch), diff.Keys);
        Assert.Equal(["j"], diff[nameof(ShortcutAction.LineDown)]);

        var back = KeyBindingSet.FromFileText(vi.ToFileText(mac: false), mac: false)!;
        foreach (var action in KeyBindingSet.Actions) Assert.Equal(vi.KeysOf(action), back.KeysOf(action));
        Assert.Null(KeyBindingSet.FromFileText("{\"format\":\"other\"}", mac: false));
        Assert.Null(KeyBindingSet.FromFileText("not json", mac: false));
    }

    // ── 設定の画面の「キー」──

    [AvaloniaFact]
    public void ぶつかったキーは知らせてから付け替えられ3つ目は2つ目と入れ替える()
    {
        UiHarness.IsolateSettings();
        bool mac = OperatingSystem.IsMacOS();
        var panel = new KeyBindingsPanel();
        var window = new Window { Content = panel };
        window.Show();
        try
        {
            var jump = panel.RowOf(ShortcutAction.FocusJump);
            var primaryF = KeyChord.Of(Key.F, ChordMods.Primary);
            panel.Offer(jump, primaryF);                                   // 検索欄へ に付いている
            Assert.Contains(primaryF, panel.Bindings.KeysOf(ShortcutAction.FocusSearch));   // まだ付け替えていない
            panel.ResolveConflict(jump, ShortcutAction.FocusSearch, primaryF);
            Assert.DoesNotContain(primaryF, panel.Bindings.KeysOf(ShortcutAction.FocusSearch));
            Assert.Equal([KeyChord.Of(Key.L, ChordMods.Primary), primaryF], panel.Bindings.KeysOf(ShortcutAction.FocusJump));

            panel.Offer(jump, KeyChord.Of(Key.F7));                        // 3 つ目は 2 つ目と入れ替える
            Assert.Equal([KeyChord.Of(Key.L, ChordMods.Primary), KeyChord.Of(Key.F7)], panel.Bindings.KeysOf(ShortcutAction.FocusJump));

            panel.Offer(jump, KeyChord.Of('5'));                           // 数字は前置きに使うので付けない
            Assert.DoesNotContain(KeyChord.Of('5'), panel.Bindings.KeysOf(ShortcutAction.FocusJump));

            // すぐ設定に残る（標準と違う操作だけ）
            var saved = AppSettingsRef.Current.KeyBindings!;
            Assert.Equal(["Primary+L", "F7"], saved[nameof(ShortcutAction.FocusJump)]);
            Assert.Equal([], saved[nameof(ShortcutAction.FocusSearch)]);
            Assert.Equal(ShortcutAction.FocusJump, KeyBindings.For(mac).Find(Key.F7, KeyModifiers.None, null, false, mac));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ひな形で入れ替え読み込みで置き換えられる()
    {
        UiHarness.IsolateSettings();
        var panel = new KeyBindingsPanel();
        var window = new Window { Content = panel };
        window.Show();
        UiHarness.Click(ButtonIn(panel, "KeysPresetVi"));
        Assert.Equal([KeyChord.Of('j')], KeyBindings.Current.KeysOf(ShortcutAction.LineDown));

        string exported = KeyBindings.Current.ToFileText(OperatingSystem.IsMacOS());
        UiHarness.Click(ButtonIn(panel, "KeysPresetStandard"));
        Assert.Empty(KeyBindings.Current.KeysOf(ShortcutAction.LineDown));
        Assert.Empty(AppSettingsRef.Current.KeyBindings!);                 // 標準なら何も書かない

        Assert.True(panel.ImportText(exported));
        Assert.Equal([KeyChord.Of('j')], KeyBindings.Current.KeysOf(ShortcutAction.LineDown));
        Assert.False(panel.ImportText("{}"));
        window.Close();
    }

    // ── 本体の窓で ──

    private async Task<(Window W, MainView V, ViewModels.MainViewModel Vm)> OpenWithVi(int lines = 200)
    {
        string path = UiHarness.WriteTempFile(Enumerable.Range(1, lines).Select(i => $"line {i} alpha beta"));
        _files.Add(path);
        var (w, v, vm) = UiHarness.OpenMainWindow();
        KeyBindings.Apply(KeyBindingSet.ViLess(OperatingSystem.IsMacOS()));
        UiHarness.OpenFile(path);
        await UiHarness.WaitUntil(() => vm.Tabs.Count == 1, "タブが開く");
        await UiHarness.WaitIndexed(vm.Tabs[0].Session);
        await UiHarness.Pump();
        UiHarness.Find<TextView>(v, "TextView").Focus();
        return (w, v, vm);
    }

    [AvaloniaFact]
    public async Task viless風でjkと数字の前置きとgとGで動き検索欄ではjは文字になる()
    {
        var (w, v, vm) = await OpenWithVi();
        try
        {
            var s = vm.Tabs[0].Session;
            Type(w, "j");
            Assert.Equal(1, s.TopLine);
            Type(w, "5j");
            Assert.Equal(6, s.TopLine);
            Type(w, "2k");
            Assert.Equal(4, s.TopLine);
            Type(w, "120g");
            Assert.Equal(119, s.TopLine);
            Type(w, "g");
            Assert.Equal(0, s.TopLine);
            Type(w, "G");
            Assert.True(s.TopLine > 100);

            var search = UiHarness.Find<AutoCompleteBox>(v, "SearchBox");
            Press(w, Key.F, Primary, "f");                                 // 検索欄へ
            long top = s.TopLine;
            Type(w, "j");
            Assert.Equal(top, s.TopLine);                                  // 文字として入る（動かない）
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task アスタリスクで選んだ語を探す()
    {
        var (w, v, vm) = await OpenWithVi();
        try
        {
            var text = UiHarness.Find<TextView>(v, "TextView");
            // ダブルクリックで「alpha」を選んだのと同じ状態にする（1 行目の 7〜12 文字目）
            typeof(TextView).GetField("_wordSel", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(text, ((long, int, int)?)(0L, 7, 12));
            Assert.Equal("alpha", text.SelectedWord);
            Press(w, Key.D8, RawInputModifiers.Shift, "*");
            await UiHarness.WaitSearchDone(vm.Tabs[0].Session);

            Assert.Equal("alpha", vm.SearchText);
            Assert.False(vm.SearchIsRegex);
            Assert.Equal(200, vm.Tabs[0].Session.SearchHits.Count);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task CtrlShiftOで開いているファイルを選べ設定はキーで開く()
    {
        var (w, v, vm) = await OpenWithVi(lines: 10);
        try
        {
            string second = UiHarness.WriteTempFile(["other"]);
            _files.Add(second);
            UiHarness.OpenFile(second);
            await UiHarness.WaitUntil(() => vm.Tabs.Count == 2, "2 つ目のタブ");
            Assert.Same(vm.Tabs[1], vm.ActiveTab);

            Press(w, Key.O, Primary | RawInputModifiers.Shift, "O");
            var switcher = FileSwitcherWindow.Current ?? throw new Xunit.Sdk.XunitException("ファイルを選ぶ窓が開かない");
            switcher.TypeFilter(Path.GetFileName(vm.Tabs[0].FilePath));
            switcher.Choose();
            Assert.Same(vm.Tabs[0], vm.ActiveTab);

            Press(w, Key.OemComma, Primary, ",");
            Assert.NotNull(PreferencesWindow.Current);
            PreferencesWindow.Current!.Close();
        }
        finally { w.Close(); }
    }
}
