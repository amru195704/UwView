using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace UwView.Localization;

/// <summary>
/// 実行時に言語を切り替えられるローカライズ提供者（§販売戦略 §4）。
///
/// 文字列は「サテライトアセンブリ」ではなく<b>本体アセンブリに同梱した2つのリソース</b>を
/// 言語コードで直接引く（Strings.resx=英語 / StringsJa.resx=日本語）。
/// WASM ではサテライトリソース(.resources.wasm)を実行時に同期ロードできず英語へ
/// フォールバックしてしまうため、この方式にしている（デスクトップ/WASM 共通で動く）。
///
/// インデクサ this[key] を XAML から {loc:Localize Key} でバインドし、
/// 言語切替時に PropertyChanged を発火して全バインドを更新する。
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    public static Localizer Instance { get; } = new();

    static Localizer()
    {
        // 公式サイトのリンクを表示言語に合わせる（英語UIから日本語ページへ飛ばさないため）。
        // UwView.Core は Localizer を参照できないので、こちらから判定を渡す。
        UwView.Core.SiteLinks.IsJapanese = () => Instance._isJa;
    }

    private readonly ResourceManager _en =
        new("UwView.Localization.Strings", typeof(Localizer).Assembly);
    private readonly ResourceManager _ja =
        new("UwView.Localization.StringsJa", typeof(Localizer).Assembly);

    private CultureInfo _culture = CultureInfo.CurrentUICulture;
    private bool _isJa = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja";

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>数値・日時の整形に使う現在カルチャ。</summary>
    public CultureInfo Culture => _culture;

    /// <summary>キー→翻訳。日本語は StringsJa、無ければ英語、それも無ければキー名。</summary>
    public string this[string key]
    {
        get
        {
            // InvariantCulture 指定＝各リソースの neutral（＝そのまま格納値）を引く。
            // サテライト解決を一切使わないので WASM でも確実に取れる。
            if (_isJa)
            {
                var ja = _ja.GetString(key, CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(ja)) return ja;
            }
            return _en.GetString(key, CultureInfo.InvariantCulture) ?? key;
        }
    }

    /// <summary>合成書式（数値は現在カルチャで整形して渡すこと）。</summary>
    public string Format(string key, params object?[] args)
        => string.Format(_culture, this[key], args);

    public void SetLanguage(string cultureName)
    {
        CultureInfo c;
        try { c = CultureInfo.GetCultureInfo(cultureName); }
        catch (CultureNotFoundException) { c = CultureInfo.InvariantCulture; }

        bool isJa = c.TwoLetterISOLanguageName == "ja";
        if (_culture.Name == c.Name && _isJa == isJa) return;

        _culture = c;
        _isJa = isJa;
        try { CultureInfo.CurrentUICulture = c; CultureInfo.CurrentCulture = c; } catch { /* WASM等で不可なら無視 */ }

        // 全ローカライズバインドを再評価させる（null＝全プロパティ変更）
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
