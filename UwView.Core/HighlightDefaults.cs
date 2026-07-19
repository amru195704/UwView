namespace UwView.Core;

/// <summary>
/// クイックカラーラベルの既定パレット（実装指示書 §2/§10-3）。
/// 上限32色。相互になるべく離した色覚配慮パレットで、同時多用時も比較的見分けやすい配列。
/// 先頭9色に Ctrl+Shift+1〜9 を割り当てる（klogg のキー体系踏襲）。
/// </summary>
public static class HighlightDefaults
{
    public const int MaxColorLabels = 32;

    /// <summary>32色パレット（前半＝彩度高めで判別容易、後半＝補助色）。</summary>
    public static readonly string[] Palette =
    {
        // 先頭9色: ホットキー割当（見分けやすさ最優先）
        "#E6194B", "#3CB44B", "#4363D8", "#F58231", "#911EB4",
        "#42D4F4", "#F032E6", "#BFEF45", "#FABED4",
        // 10〜32: 追加色（値ごとの個別割当用）
        "#469990", "#DCBEFF", "#9A6324", "#FFFAC8", "#800000",
        "#AAFFC3", "#808000", "#FFD8B1", "#000075", "#A9A9A9",
        "#E6BEFF", "#7F7F00", "#00B4D8", "#B5179E", "#606C38",
        "#BC6C25", "#8ECAE6", "#FB8500", "#7209B7", "#4A4E69",
        "#22577A", "#C9184A", "#2A9D8F",
    };

    /// <summary>既定のクイックラベル群（32色・キーワード未割当）。</summary>
    public static List<ColorLabel> DefaultColorLabels()
    {
        var list = new List<ColorLabel>(Palette.Length);
        foreach (var c in Palette) list.Add(new ColorLabel(c));
        return list;
    }
}
