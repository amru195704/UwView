using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace UwView.Localization;

/// <summary>
/// 実行時に言語を切り替えられるローカライズ提供者（§販売戦略 §4）。
/// ResX（Strings.resx=英語 / Strings.ja.resx=日本語）を ResourceManager 経由で引く。
/// インデクサ this[key] を XAML から {loc:Localize Key} でバインドし、
/// 言語切替時に "Item[]" の PropertyChanged を発火して全バインドを更新する。
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    public static Localizer Instance { get; } = new();

    private readonly ResourceManager _rm =
        new("UwView.Localization.Strings", typeof(Localizer).Assembly);

    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    public event PropertyChangedEventHandler? PropertyChanged;

    public CultureInfo Culture => _culture;

    /// <summary>キー→翻訳。未定義キーはキー名を返す（可視化のため）。</summary>
    public string this[string key] => _rm.GetString(key, _culture) ?? key;

    /// <summary>合成書式（数値は現在カルチャで整形して渡すこと）。</summary>
    public string Format(string key, params object?[] args)
        => string.Format(_culture, this[key], args);

    public void SetLanguage(string cultureName)
    {
        CultureInfo c;
        try { c = CultureInfo.GetCultureInfo(cultureName); }
        catch (CultureNotFoundException) { c = CultureInfo.InvariantCulture; }

        if (_culture.Name == c.Name) return;
        _culture = c;
        CultureInfo.CurrentUICulture = c;
        // 全ローカライズバインドを再評価させる（null＝全プロパティ変更。
        // インデクサバインド [key] は "Item[]" だけでは更新されないため null で通知）
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
