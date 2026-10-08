using System.Collections.Generic;
using System.Linq;

namespace UwView.Services;

/// <summary>
/// 定義済み★に既定で入れる AND・NOT・OR のひな形（v1.8.2 extFS E-1。uvf・uvp の画面で共用）。
///
/// 論理式（<c>-L</c>）は作らないと決めた（2026-10-08・先読みの正規表現で同じ速さが出たため）。
/// 代わりに、その正規表現の書き方をひな形で出す。書き方は説明書（uvf 7.1・uvp 13 章）の表と同じ。
/// </summary>
public static class PredefinedTemplates
{
    public static IReadOnlyList<PredefinedFilter> For(bool japanese) => japanese
        ?
        [
            Template("AND（両方を含む）", "^(?=.*語1)(?=.*語2)", "語1", "語2"),
            Template("NOT（含まない）", "^(?!.*除く語).*語", "除く語", "語"),
            Template("OR（どちらか）", "語1|語2", "語1", "語2"),
        ]
        :
        [
            Template("AND (contains both)", "^(?=.*word1)(?=.*word2)", "word1", "word2"),
            Template("NOT (does not contain)", "^(?!.*excluded).*word", "excluded", "word"),
            Template("OR (either)", "word1|word2", "word1", "word2"),
        ];

    private static PredefinedFilter Template(string name, string pattern, params string[] words)
        => new() { Name = name, Pattern = pattern, IsRegex = true, Placeholders = [.. words] };

    /// <summary>まだ入れていなければ、ひな形を先頭に入れる（同じ名前のものがあれば足さない）。入れたら true。</summary>
    public static bool AddOnce(AppSettings settings, bool japanese)
    {
        if (settings.PredefinedTemplatesAdded) return false;
        var add = For(japanese).Where(t => settings.PredefinedFilters.All(f => f.Name != t.Name)).ToList();
        settings.PredefinedFilters.InsertRange(0, add);
        settings.PredefinedTemplatesAdded = true;
        return true;
    }

    /// <summary>選んだあと検索欄で選んでおく範囲（最初の語）。ひな形でなければ null。</summary>
    public static (int Start, int Length)? FirstPlaceholder(PredefinedFilter filter)
    {
        if (filter.Placeholders is not { Count: > 0 } words) return null;
        int at = filter.Pattern.IndexOf(words[0], System.StringComparison.Ordinal);
        return at < 0 ? null : (at, words[0].Length);
    }
}
