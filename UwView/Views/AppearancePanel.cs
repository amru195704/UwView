using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwView.Localization;
using UwView.Services;

namespace UwView.Views;

/// <summary>
/// 設定の画面の「表示」（v1.8.2 extFS E-4 §5。uvf・uvp の設定の画面の両方に入れる）。
/// テーマ（OS に合わせる・ライト・ダーク）と、本文のフォント・大きさ。変えたらすぐ画面に当たり、設定に残る。
/// </summary>
public sealed class AppearancePanel : UserControl
{
    private readonly ComboBox _font;
    private readonly NumericUpDown _size;
    private readonly TextBlock _preview;
    private bool _loading;

    private static Localizer L => Localizer.Instance;

    /// <summary>フォントの選択肢（null は既定）。</summary>
    internal sealed record FontChoice(string? Family, string Label)
    {
        public override string ToString() => Label;
    }

    public AppearancePanel()
    {
        RadioButton Theme(string name, string label, string value)
        {
            var r = new RadioButton { Name = name, GroupName = "AppTheme", Content = label, Foreground = ThemeColors.Text,
                                      IsChecked = (AppSettingsRef.Current.Theme ?? AppTheme.System) == value };
            r.IsCheckedChanged += (_, _) => { if (r.IsChecked == true && !_loading) AppTheme.Set(value); };
            return r;
        }
        var themes = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12,
            Children =
            {
                Theme("ThemeSystem", L["ThemeSystem"], AppTheme.System),
                Theme("ThemeLight", L["ThemeLight"], AppTheme.Light),
                Theme("ThemeDark", L["ThemeDark"], AppTheme.Dark),
            },
        };

        _font = new ComboBox { Name = "FontCombo", MinWidth = 320, ItemsSource = FontChoices() };
        ToolTip.SetTip(_font, L["TipFontFamily"]);
        _size = new NumericUpDown
        {
            Name = "FontSizeBox", Minimum = (decimal)ViewFont.MinSize, Maximum = (decimal)ViewFont.MaxSize,
            Increment = 1, FormatString = "0", Width = 120,
        };
        ToolTip.SetTip(_size, L["TipFontSize"]);
        _preview = new TextBlock { Text = L["FontPreview"], TextWrapping = TextWrapping.Wrap, Foreground = ThemeColors.Text };
        var reset = new Button { Name = "AppearanceReset", Content = L["AppearanceReset"], Padding = new Thickness(8, 2) };
        ToolTip.SetTip(reset, L["TipAppearanceReset"]);
        reset.Click += (_, _) => { ViewFont.Set(null, ViewFont.DefaultSize); Load(); };

        _font.SelectionChanged += (_, _) => { if (!_loading) Apply(); };
        _size.ValueChanged += (_, _) => { if (!_loading) Apply(); };

        Content = new StackPanel
        {
            Margin = new Thickness(4),
            Spacing = 10,
            Children =
            {
                Heading(L["ThemeLabel"]), themes,
                new TextBlock { Text = L["ThemeNote"], Foreground = ThemeColors.Text, TextWrapping = TextWrapping.Wrap },
                new Border { Height = 1, Background = ThemeColors.Get("Uv_DDDDDD", Brushes.LightGray), Margin = new Thickness(0, 4) },
                Heading(L["FontLabel"]),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _font,
                    new TextBlock { Text = L["FontSizeLabel"], Foreground = ThemeColors.Text, VerticalAlignment = VerticalAlignment.Center }, _size } },
                new Border
                {
                    BorderBrush = ThemeColors.Get("Uv_CCCCCC", Brushes.LightGray), BorderThickness = new Thickness(1),
                    Background = ThemeColors.ViewBackground, Padding = new Thickness(10, 8), Child = _preview,
                },
                new TextBlock { Text = L["FontZoomNote"], Foreground = ThemeColors.Text, TextWrapping = TextWrapping.Wrap },
                reset,
            },
        };
        Load();
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.Bold, Foreground = ThemeColors.Text };

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ViewFont.Changed += Load;   // Ctrl＋ホイールで変えたら、ここの数字も合わせる
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ViewFont.Changed -= Load;
        base.OnDetachedFromVisualTree(e);
    }

    private void Load()
    {
        _loading = true;
        try
        {
            var family = AppSettingsRef.Current.BodyFontFamily;
            var choices = (IReadOnlyList<FontChoice>)_font.ItemsSource!;
            _font.SelectedItem = choices.FirstOrDefault(c => c.Family == family) ?? choices[0];
            _size.Value = (decimal)ViewFont.Size;
            _preview.FontFamily = new FontFamily(ViewFont.Family);
            _preview.FontSize = ViewFont.Size;
        }
        finally { _loading = false; }
    }

    private void Apply()
    {
        string? family = (_font.SelectedItem as FontChoice)?.Family;
        double size = (double)(_size.Value ?? (decimal)ViewFont.DefaultSize);
        ViewFont.Set(family, size);
    }

    /// <summary>既定・等幅のフォント（上に）・そのほかのフォント。等幅かは「i」と「W」の幅で見る。</summary>
    private static List<FontChoice> FontChoices()
    {
        var list = new List<FontChoice> { new(null, L["FontDefault"]) };
        var mono = new List<string>();
        var other = new List<string>();
        foreach (var family in FontManager.Current.SystemFonts.Select(f => f.Name).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(family) || family.StartsWith('.')) continue;
            (IsMonospace(family) ? mono : other).Add(family);
        }
        list.AddRange(mono.Select(f => new FontChoice(f, L.Format("FontMonospace", f))));
        list.AddRange(other.Select(f => new FontChoice(f, f)));
        // 選んでいたフォントがこの機械に無くても、選んだままにする
        if (AppSettingsRef.Current.BodyFontFamily is { Length: > 0 } saved && list.All(c => c.Family != saved))
            list.Insert(1, new FontChoice(saved, saved));
        return list;
    }

    private static bool IsMonospace(string family)
    {
        try
        {
            var face = new Typeface(new FontFamily(family));
            double Width(string s) => new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 20, Brushes.Black).Width;
            double narrow = Width("iiiiiiiiii"), wide = Width("WWWWWWWWWW");
            return narrow > 0 && Math.Abs(narrow - wide) < 0.5;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return false; }
    }
}
