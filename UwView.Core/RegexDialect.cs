using System.Text;
using System.Text.RegularExpressions;

namespace UwView.Core;

/// <summary>
/// 正規表現の書き方の違い（.NET と ripgrep／POSIX）を扱う（指示書 WideField v1.7 §10.2）。
///
/// UwView は .NET の正規表現を使う。ripgrep（Rust）から持ってきた式が<b>黙って別の意味で探す</b>のが一番まずいので、
/// 読み替えられるものは読み替え、読み替えられないものは書き換えを案内して止める。
/// <list type="bullet">
///   <item><c>(?P&lt;名前&gt;…)</c>（ripgrep・Python の名前付きグループ）→ <c>(?&lt;名前&gt;…)</c> と同じ意味に読み替える</item>
///   <item><c>[[:alpha:]]</c> などの POSIX 文字クラス → .NET では別の意味（<c>[</c> <c>:</c> <c>a</c>… の集まり）になるのでエラー</item>
///   <item><c>\p{Han}</c> などの用字名 → .NET はエラーにするので、<c>\p{IsCJKUnifiedIdeographs}</c> などを添える</item>
/// </list>
/// </summary>
public static class RegexDialect
{
    /// <summary><c>(?P&lt;</c> を <c>(?&lt;</c> に読み替える（<c>\(</c> のように逃がした括弧は触らない）。</summary>
    public static string Normalize(string pattern)
    {
        if (!pattern.Contains("(?P<", StringComparison.Ordinal)) return pattern;
        var sb = new StringBuilder(pattern.Length);
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length) { sb.Append(c).Append(pattern[++i]); continue; }
            if (c == '(' && string.CompareOrdinal(pattern, i, "(?P<", 0, 4) == 0)
            {
                sb.Append("(?<");
                i += 3;
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static readonly Regex Posix = new(@"\[:(?<name>[a-z]+):\]", RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, string> PosixReplacement = new()
    {
        ["alpha"] = @"\p{L}", ["digit"] = @"\d", ["alnum"] = @"[\p{L}\p{N}]", ["space"] = @"\s",
        ["upper"] = @"\p{Lu}", ["lower"] = @"\p{Ll}", ["punct"] = @"\p{P}", ["xdigit"] = "[0-9A-Fa-f]",
        ["word"] = @"\w", ["blank"] = @"[ \t]", ["cntrl"] = @"\p{Cc}",
    };

    /// <summary>ripgrep などの用字名と、.NET で書くときの名前（.NET は用字名を持たず、Unicode のブロック名を使う）。</summary>
    private static readonly Dictionary<string, string> ScriptReplacement = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Han"] = "IsCJKUnifiedIdeographs", ["Hiragana"] = "IsHiragana", ["Katakana"] = "IsKatakana",
        ["Hangul"] = "IsHangulSyllables", ["Latin"] = "IsBasicLatin", ["Greek"] = "IsGreek",
        ["Cyrillic"] = "IsCyrillic", ["Arabic"] = "IsArabic", ["Thai"] = "IsThai",
    };

    /// <summary>
    /// 使えない書き方か。使えなければ理由と書き換え先（日英）、使えるなら null。
    /// .NET の書き間違い（括弧の数など）もここで見る（探し始める前に止めるため）。
    /// </summary>
    public static (string Ja, string En)? Check(string pattern, bool ignoreCase = false)
    {
        var posix = Posix.Match(pattern);
        if (posix.Success)
        {
            string name = posix.Groups["name"].Value;
            string instead = PosixReplacement.TryGetValue(name, out var r) ? r : @"\p{…}";
            return ($"[[:{name}:]] のような POSIX 文字クラスは使えません（別の意味で探してしまいます）。{instead} を使ってください",
                    $"POSIX classes such as [[:{name}:]] are not supported (they would mean something else). Use {instead} instead");
        }

        try
        {
            _ = new Regex(Normalize(pattern),
                          RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None));
            return null;
        }
        catch (ArgumentException e)
        {
            string hintJa = "", hintEn = "";
            var unknown = System.Text.RegularExpressions.Regex.Match(e.Message, @"Unknown property '(?<name>[^']+)'");
            if (unknown.Success && ScriptReplacement.TryGetValue(unknown.Groups["name"].Value, out var block))
            {
                hintJa = $"（\\p{{{unknown.Groups["name"].Value}}} は使えません。\\p{{{block}}} を使ってください）";
                hintEn = $" (\\p{{{unknown.Groups["name"].Value}}} is not supported; use \\p{{{block}}})";
            }
            return ($"正規表現が正しくありません: {e.Message}{hintJa}", $"Invalid regular expression: {e.Message}{hintEn}");
        }
    }
}
