using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace UwView.Services;

/// <summary>
/// 自前で描く部品（本文・ミニマップ・結果の一覧・Pro の編集）が使う色（v1.8.2 extFS E-4）。
/// 値は Themes/Palette.axaml（ライト／ダーク）。テーマが替わったら <see cref="Changed"/> で描き直す。
/// </summary>
public static class ThemeColors
{
    private static readonly Dictionary<string, IBrush> Cache = [];
    private static ThemeVariant? _cachedFor;
    private static Application? _hookedApp;   // どの App に付けたか（替わったら付け直す）

    private static Action? _changed;

    /// <summary>テーマが替わった（OS の切り替え・設定の画面）。描き直すこと。</summary>
    public static event Action? Changed
    {
        add { Hook(); _changed += value; }
        remove => _changed -= value;
    }

    /// <summary>今のテーマがダークか。</summary>
    public static bool IsDark => Variant == ThemeVariant.Dark;

    private static ThemeVariant Variant => Application.Current?.ActualThemeVariant ?? ThemeVariant.Light;

    /// <summary>色を引く（見つからなければ fallback）。</summary>
    public static IBrush Get(string key, IBrush fallback)
    {
        Hook();
        var variant = Variant;
        if (!Equals(_cachedFor, variant)) { Cache.Clear(); _cachedFor = variant; }
        if (Cache.TryGetValue(key, out var brush)) return brush;
        brush = Application.Current?.TryGetResource(key, variant, out var value) == true && value is IBrush b ? b : fallback;
        return Cache[key] = brush;
    }

    /// <summary>テーマの切り替えを受け取る準備（App が替わっていれば付け直す）。</summary>
    internal static void EnsureHooked() => Hook();

    private static void Hook()
    {
        if (Application.Current is not { } app || ReferenceEquals(app, _hookedApp)) return;
        if (_hookedApp is { } old) old.ActualThemeVariantChanged -= OnVariantChanged;
        _hookedApp = app;
        app.ActualThemeVariantChanged += OnVariantChanged;
    }

    private static void OnVariantChanged(object? sender, EventArgs e)
    {
        Cache.Clear();
        _changed?.Invoke();
    }

    // 部品が使う色（名前は Palette.axaml と同じ）
    public static IBrush ViewBackground => Get("Uv_View_Background", Brushes.White);
    public static IBrush ViewText => Get("Uv_View_Text", Brushes.Black);
    public static IBrush LineNumber => Get("Uv_View_LineNumber", new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)));
    public static IBrush Gutter => Get("Uv_View_Gutter", new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)));
    public static IBrush Match => Get("Uv_View_Match", new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x66)));
    public static IBrush Bookmark => Get("Uv_View_Bookmark", new SolidColorBrush(Color.FromRgb(0x1A, 0x6F, 0xE8)));
    public static IBrush Emphasis => Get("Uv_View_Emphasis", new SolidColorBrush(Color.FromRgb(0xCB, 0xE8, 0xFA)));
    public static IBrush Selection => Get("Uv_View_Selection", new SolidColorBrush(Color.FromRgb(0xB4, 0xD5, 0xEE)));
    public static IBrush ContextBg => Get("Uv_View_ContextBg", new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)));
    public static IBrush HitNumber => Get("Uv_View_HitNumber", new SolidColorBrush(Color.FromRgb(0x1A, 0x6F, 0xE8)));
    public static IBrush MinimapBackground => Get("Uv_Minimap_Background", new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)));
    public static IBrush MinimapHit => Get("Uv_Minimap_Hit", new SolidColorBrush(Color.FromRgb(0xE8, 0x71, 0x1A)));
    public static IBrush MinimapView => Get("Uv_Minimap_View", new SolidColorBrush(Color.FromArgb(0x60, 0x40, 0x40, 0x40)));
    public static IBrush EditSelection => Get("Uv_Edit_Selection", new SolidColorBrush(Color.FromArgb(0x66, 0x1A, 0x6F, 0xE8)));
    public static IBrush EditCaret => Get("Uv_Edit_Caret", new SolidColorBrush(Color.FromRgb(0x1A, 0x6F, 0xE8)));
    public static IBrush EditMark => Get("Uv_Edit_Mark", new SolidColorBrush(Color.FromRgb(0xE8, 0x8A, 0x1A)));
    public static IBrush RectSelection => Get("Uv_Rect_Selection", new SolidColorBrush(Color.FromArgb(0x55, 0x2A, 0x78, 0xD6)));

    /// <summary>窓の文字・地（コードで組む窓用）。</summary>
    public static IBrush Text => Get("Uv_Text", Brushes.Black);
    public static IBrush Surface => Get("Uv_Surface", Brushes.White);

    /// <summary>
    /// 明るさ（0〜1。sRGB の相対輝度）。ハイライタの地が明るければ、ダークでも文字を黒にして読めるようにする。
    /// </summary>
    public static double Luminance(Color c)
    {
        static double Lin(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    /// <summary>2 色の明るさの比（1〜21）。3 より小さいと読みにくい。</summary>
    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}

/// <summary>テーマの設定（OS に合わせる・ライト・ダーク。v1.8.2 extFS E-4）。</summary>
public static class AppTheme
{
    public const string System = "System";
    public const string Light = "Light";
    public const string Dark = "Dark";

    /// <summary>設定の値を画面に当てる（OS に合わせるなら、OS の切り替えにその場でついていく）。</summary>
    public static void Apply(string? theme)
    {
        if (Application.Current is not { } app) return;
        ThemeColors.EnsureHooked();
        app.RequestedThemeVariant = theme switch
        {
            Light => ThemeVariant.Light,
            Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    /// <summary>テーマを変えて設定に残す。</summary>
    public static void Set(string theme)
    {
        AppSettingsRef.Current.Theme = theme;
        AppSettingsRef.Current.Save();
        Apply(theme);
    }
}

/// <summary>本文と結果の一覧のフォント（v1.8.2 extFS E-4）。拡大・縮小もここ。</summary>
public static class ViewFont
{
    public const string DefaultFamily = "Cascadia Mono,Menlo,Consolas,Courier New,monospace";
    public const double DefaultSize = 14;
    public const double MinSize = 6;
    public const double MaxSize = 48;

    /// <summary>結果の一覧は本文より 2 小さい（1.8.1 までの 14 と 12 の関係のまま）。</summary>
    public const double ListOffset = 2;

    private static string? _typefaceFor;
    private static Typeface _typeface;

    /// <summary>フォントか大きさが変わった（本文・結果の一覧は測り直して描き直す）。</summary>
    public static event Action? Changed;

    /// <summary>選んだフォント（無ければ既定）。選んだものが無い機械では、既定の並びに落ちる。</summary>
    public static string Family
        => AppSettingsRef.Current.BodyFontFamily is { Length: > 0 } f ? $"{f},{DefaultFamily}" : DefaultFamily;

    public static double Size => Math.Clamp(AppSettingsRef.Current.BodyFontSize ?? DefaultSize, MinSize, MaxSize);

    public static double ListSize => Math.Max(MinSize, Size - ListOffset);

    public static Typeface Typeface
    {
        get
        {
            string family = Family;
            if (_typefaceFor != family) { _typeface = new Typeface(new FontFamily(family)); _typefaceFor = family; }
            return _typeface;
        }
    }

    /// <summary>フォントと大きさを決めて設定に残す（family が null なら既定）。</summary>
    public static void Set(string? family, double size)
    {
        var settings = AppSettingsRef.Current;
        settings.BodyFontFamily = string.IsNullOrWhiteSpace(family) ? null : family;
        double clamped = Math.Clamp(Math.Round(size), MinSize, MaxSize);
        settings.BodyFontSize = clamped == DefaultSize ? null : clamped;
        settings.Save();
        Changed?.Invoke();
    }

    /// <summary>拡大・縮小（Ctrl＋ホイール・Ctrl＋＋／－）。</summary>
    public static void Zoom(int steps) => Set(AppSettingsRef.Current.BodyFontFamily, Size + steps);

    /// <summary>既定の大きさに戻す（Ctrl＋0）。</summary>
    public static void ResetZoom() => Set(AppSettingsRef.Current.BodyFontFamily, DefaultSize);
}
