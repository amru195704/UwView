using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using UwView.Controls;
using UwView.Core;
using UwView.Services;
using UwView.ViewModels;
using UwView.Views;

namespace UwView.UiTests;

/// <summary>フォントとダークテーマ（v1.8.2 extFS E-4）。テーマ・色・フォント・拡大縮小・ハイライタの読みにくい色。</summary>
public class AppearanceTests : IDisposable
{
    public AppearanceTests() => UiHarness.IsolateSettings();

    public void Dispose()
    {
        AppTheme.Apply(AppTheme.System);   // ほかのテストに持ち越さない
        GC.SuppressFinalize(this);
    }

    private static Color ColorOf(IBrush brush) => ((ISolidColorBrush)brush).Color;

    [AvaloniaFact]
    public void テーマを切り替えると色が替わり設定に残る()
    {
        int changed = 0;
        void Count() => changed++;
        ThemeColors.Changed += Count;
        try
        {
            AppTheme.Set(AppTheme.Dark);
            Assert.Equal(ThemeVariant.Dark, Application.Current!.ActualThemeVariant);
            Assert.True(ThemeColors.IsDark);
            Assert.Equal(Color.Parse("#1E1E1E"), ColorOf(ThemeColors.ViewBackground));
            Assert.Equal(AppTheme.Dark, AppSettingsRef.Current.Theme);
            Assert.True(changed > 0);

            AppTheme.Set(AppTheme.Light);
            Assert.Equal(Colors.White, ColorOf(ThemeColors.ViewBackground));
            Assert.Equal(Colors.Black, ColorOf(ThemeColors.Text));
        }
        finally { ThemeColors.Changed -= Count; }
    }

    [AvaloniaFact]
    public void アイコンはダークで明るい絵に替わる()
    {
        var app = Application.Current!;
        Assert.True(app.TryGetResource("Icon_search", ThemeVariant.Light, out var light));
        Assert.True(app.TryGetResource("Icon_search", ThemeVariant.Dark, out var dark));
        Assert.NotSame(light, dark);
    }

    [AvaloniaFact]
    public void フォントの大きさは6から48で結果の一覧は2小さく既定に戻せる()
    {
        int changed = 0;
        void Count() => changed++;
        ViewFont.Changed += Count;
        try
        {
            ViewFont.Set("Menlo", 20);
            Assert.Equal(20, ViewFont.Size);
            Assert.Equal(18, ViewFont.ListSize);
            Assert.StartsWith("Menlo,", ViewFont.Family);
            Assert.Equal("Menlo", AppSettingsRef.Current.BodyFontFamily);

            ViewFont.Zoom(100);
            Assert.Equal(ViewFont.MaxSize, ViewFont.Size);
            ViewFont.Zoom(-100);
            Assert.Equal(ViewFont.MinSize, ViewFont.Size);

            ViewFont.ResetZoom();
            Assert.Equal(ViewFont.DefaultSize, ViewFont.Size);
            Assert.Null(AppSettingsRef.Current.BodyFontSize);   // 既定は書かない
            Assert.True(changed >= 4);
        }
        finally { ViewFont.Changed -= Count; ViewFont.Set(null, ViewFont.DefaultSize); }
    }

    [AvaloniaFact]
    public async Task CtrlプラスとCtrlゼロとCtrlホイールで本文と結果の一覧の文字が変わる()
    {
        string path = UiHarness.WriteTempFile(Enumerable.Range(1, 50).Select(i => $"line {i}"));
        var (w, v, vm) = UiHarness.OpenMainWindow();
        try
        {
            UiHarness.OpenFile(path);
            await UiHarness.WaitUntil(() => vm.Tabs.Count == 1, "タブが開く");
            await UiHarness.WaitIndexed(vm.Tabs[0].Session);
            var primary = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

            w.KeyPress(Key.OemPlus, primary, PhysicalKey.None, "=");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ViewFont.DefaultSize + 1, ViewFont.Size);
            w.KeyPress(Key.D0, primary, PhysicalKey.None, "0");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ViewFont.DefaultSize, ViewFont.Size);

            var text = UiHarness.Find<TextView>(v, "TextView");
            var center = text.TranslatePoint(new Point(text.Bounds.Width / 2, text.Bounds.Height / 2), w)!.Value;
            w.MouseWheel(center, new Vector(0, 1), primary);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ViewFont.DefaultSize + 1, ViewFont.Size);

            var list = new FilterListView();
            Assert.Equal(ViewFont.ListSize, list.FontSize);              // 結果の一覧は本文より 2 小さい
        }
        finally { w.Close(); ViewFont.Set(null, ViewFont.DefaultSize); File.Delete(path); }
    }

    [AvaloniaFact]
    public void 表示の板でテーマと大きさを変えるとすぐ当たる()
    {
        var panel = new AppearancePanel();
        var window = new Window { Content = panel };
        window.Show();
        try
        {
            var dark = panel.GetLogicalDescendants().OfType<RadioButton>().Single(r => r.Name == "ThemeDark");
            dark.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ThemeVariant.Dark, Application.Current!.ActualThemeVariant);

            var size = panel.GetLogicalDescendants().OfType<NumericUpDown>().Single(n => n.Name == "FontSizeBox");
            size.Value = 18;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(18, ViewFont.Size);

            var fonts = panel.GetLogicalDescendants().OfType<ComboBox>().Single(c => c.Name == "FontCombo");
            Assert.True(fonts.ItemCount >= 1);                         // 先頭は「既定」
        }
        finally { window.Close(); ViewFont.Set(null, ViewFont.DefaultSize); }
    }

    [AvaloniaFact]
    public void ハイライタはプリセットの色をダークで替え読みにくい規則に印を付ける()
    {
        Assert.Equal(0xFF5C2828u, HighlightPresets.DarkVariant(0xFFFFD6D6));   // 赤
        Assert.Null(HighlightPresets.DarkVariant(0xFF123456));                 // 利用者の色は替えない

        var darkGray = new HlRuleRow(new HlRule("x", foreground: "#303030"), () => { });
        var lightBg = new HlRuleRow(new HlRule("x", background: "#FFD6D6"), () => { });
        AppTheme.Apply(AppTheme.Light);
        Assert.False(darkGray.LowContrast);                                     // ライトでは出さない
        AppTheme.Apply(AppTheme.Dark);
        Assert.True(darkGray.LowContrast);                                      // 暗い地に暗い文字
        Assert.False(lightBg.LowContrast);                                      // プリセットの色は替えて描く
        Assert.False(new HlRuleRow(new HlRule("x", foreground: "#FFFFFF"), () => { }).LowContrast);
    }
}
