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

    /// <summary>
    /// POSIX 文字クラスの書き換え先。ripgrep（Rust）の POSIX 文字クラスは <b>ASCII だけ</b>に当たるので、
    /// 結果が変わらない ASCII の書き方を先に、日本語なども含めたいときの書き方を後に添える
    /// （.NET の <c>\d</c> <c>\s</c> <c>\p{L}</c> は全角数字や日本語にも当たり、範囲が広がる。外部レビュー 2026-09-27）。
    /// </summary>
    private static readonly Dictionary<string, (string Ascii, string? Unicode)> PosixReplacement = new()
    {
        ["alpha"] = ("[A-Za-z]", @"\p{L}"), ["digit"] = ("[0-9]", @"\d"), ["alnum"] = ("[A-Za-z0-9]", @"[\p{L}\p{N}]"),
        ["space"] = (@"[ \t\r\n\f\v]", @"\s"), ["upper"] = ("[A-Z]", @"\p{Lu}"), ["lower"] = ("[a-z]", @"\p{Ll}"),
        ["punct"] = (@"[!-/:-@\[-`{-~]", @"\p{P}"), ["xdigit"] = ("[0-9A-Fa-f]", null), ["word"] = ("[A-Za-z0-9_]", @"\w"),
        ["blank"] = (@"[ \t]", null), ["cntrl"] = (@"[\x00-\x1F\x7F]", null), ["graph"] = ("[!-~]", null), ["print"] = ("[ -~]", null),
    };

    /// <summary>
    /// ripgrep などの用字名と、.NET で書くときの近い書き方（.NET は用字名を持たず、Unicode のブロック名を使う）。
    /// 用字とブロックは同じ範囲ではないので、違いも添える（例: \p{Han} は 々・〇・拡張A の 㐀 にも当たるが、
    /// \p{IsCJKUnifiedIdeographs} は当たらない）。
    /// </summary>
    private static readonly Dictionary<string, (string Instead, string Ja, string En)> ScriptReplacement = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Han"] = (@"\p{IsCJKUnifiedIdeographs}",
                   @"漢字の主な範囲。々・〇・拡張A（㐀 など）も含めるなら [\p{IsCJKUnifiedIdeographs}\p{IsCJKUnifiedIdeographsExtensionA}\p{IsCJKCompatibilityIdeographs}々〇]。拡張B 以降は書けません",
                   @"the main range of kanji; to also cover 々 〇 and Extension A (㐀 etc.) use [\p{IsCJKUnifiedIdeographs}\p{IsCJKUnifiedIdeographsExtensionA}\p{IsCJKCompatibilityIdeographs}々〇]; Extension B and later cannot be written"),
        ["Hiragana"] = (@"\p{IsHiragana}", "ほぼ同じ範囲", "almost the same range"),
        ["Katakana"] = (@"\p{IsKatakana}", @"半角カナは含みません。含めるなら [\p{IsKatakana}ｦ-ﾟ]", @"half-width kana are not included; use [\p{IsKatakana}ｦ-ﾟ] for them"),
        ["Hangul"] = (@"\p{IsHangulSyllables}", "音節だけ（字母は含みません）", "syllables only (not jamo)"),
        ["Latin"] = (@"\p{IsBasicLatin}", "ASCII だけ（アクセント付きの文字は含みません）", "ASCII only (not accented letters)"),
        ["Greek"] = (@"\p{IsGreek}", "近い範囲", "a close range"),
        ["Cyrillic"] = (@"\p{IsCyrillic}", "近い範囲", "a close range"),
        ["Arabic"] = (@"\p{IsArabic}", "近い範囲", "a close range"),
        ["Thai"] = (@"\p{IsThai}", "近い範囲", "a close range"),
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
            if (!PosixReplacement.TryGetValue(name, out var r))
                return ($"[[:{name}:]] のような POSIX 文字クラスは使えません（別の意味で探してしまいます）",
                        $"POSIX classes such as [[:{name}:]] are not supported (they would mean something else)");
            string wideJa = r.Unicode is { } u ? $"。日本語なども含めるなら {u}" : "";
            string wideEn = r.Unicode is { } w ? $"; to include Japanese and other scripts, use {w}" : "";
            return ($"[[:{name}:]] のような POSIX 文字クラスは使えません（別の意味で探してしまいます）。"
                    + $"{r.Ascii} を使ってください（ripgrep と同じ ASCII の範囲{wideJa}）",
                    $"POSIX classes such as [[:{name}:]] are not supported (they would mean something else). "
                    + $"Use {r.Ascii} instead (the same ASCII range as ripgrep{wideEn})");
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
            if (unknown.Success && ScriptReplacement.TryGetValue(unknown.Groups["name"].Value, out var script))
            {
                string name = unknown.Groups["name"].Value;
                hintJa = $"（\\p{{{name}}} は使えません。{script.Instead} を使ってください。{script.Ja}）";
                hintEn = $" (\\p{{{name}}} is not supported; use {script.Instead}: {script.En})";
            }
            return ($"正規表現が正しくありません: {e.Message}{hintJa}", $"Invalid regular expression: {e.Message}{hintEn}");
        }
    }
}
