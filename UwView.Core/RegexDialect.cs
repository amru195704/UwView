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
    /// <summary>
    /// <c>(?P&lt;</c> を <c>(?&lt;</c> に読み替える（<c>\(</c> のように逃がした括弧・文字クラスの中・コメントの中は触らない）。
    /// <list type="bullet">
    /// <item><c>[(?P&lt;]</c> は 4 文字のどれかに当たる文字クラスで、中の P を消すと別の式になる（外部レビュー 2026-10-04 の指摘4）</item>
    /// <item>コメント <c>(?#…)</c> と、<c>(?x)</c> が効いている範囲の <c>#</c> から行末までは、そのまま写す。
    /// コメントの中の <c>[</c> を文字クラスの始まりと読むと、後ろの <c>(?P&lt;</c> まで写してしまい、
    /// 対応している書き方が「正しくない正規表現」になった（<c>(?# [)(?P&lt;n&gt;a)]</c>。外部再レビュー 2026-10-05 の指摘2）</item>
    /// </list>
    /// 文字クラスの終わりは <see cref="RegexSyntax.ClassEnd"/> で読む（必須リテラルの取り出しと同じ読み方）。
    /// </summary>
    public static string Normalize(string pattern)
    {
        if (!pattern.Contains("(?P<", StringComparison.Ordinal)) return pattern;
        var sb = new StringBuilder(pattern.Length);
        var outer = new Stack<bool>();   // グループに入る前の x（グループを閉じたら戻す）
        bool x = false;                  // いま空白と # コメントを読み飛ばす書き方（x）が効いているか
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length) { sb.Append(c).Append(pattern[++i]); continue; }
            if (x && c == '#')
            {
                int nl = pattern.IndexOf('\n', i);
                int end = nl < 0 ? pattern.Length - 1 : nl;
                sb.Append(pattern, i, end - i + 1);
                i = end;
                continue;
            }
            if (c == '[' && RegexSyntax.ClassEnd(pattern, i) is var classEnd and >= 0)
            {
                sb.Append(pattern, i, classEnd - i + 1);
                i = classEnd;
                continue;
            }
            if (c == '(')
            {
                if (string.CompareOrdinal(pattern, i, "(?#", 0, 3) == 0)
                {
                    int close = pattern.IndexOf(')', i);       // .NET のコメントは最初の ) で終わる（入れ子にならない）
                    int end = close < 0 ? pattern.Length - 1 : close;
                    sb.Append(pattern, i, end - i + 1);
                    i = end;
                    continue;
                }
                if (ReadOptions(pattern, i, out int after, out bool? xSet, out bool scoped))
                {
                    sb.Append(pattern, i, after - i);
                    i = after - 1;
                    if (scoped) outer.Push(x);                // (?x:…) は閉じるまで
                    if (xSet is { } on) x = on;               // (?x) はいまのグループの終わりまで
                    continue;
                }
                outer.Push(x);
                if (string.CompareOrdinal(pattern, i, "(?P<", 0, 4) == 0)
                {
                    sb.Append("(?<");
                    i += 3;
                    continue;
                }
                sb.Append(c);
                continue;
            }
            if (c == ')' && outer.Count > 0) x = outer.Pop();
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// <paramref name="i"/>（<c>(</c>）がオプション <c>(?imnsx-imnsx)</c>／<c>(?imnsx-imnsx:</c> なら true。
    /// <paramref name="after"/> はその後ろの位置、<paramref name="xSet"/> は x を付けた（true）・外した（false）か、触っていない（null）、
    /// <paramref name="scoped"/> は <c>:</c> で始まるグループ（閉じるまで効く）か。
    /// </summary>
    private static bool ReadOptions(string p, int i, out int after, out bool? xSet, out bool scoped)
    {
        after = i; xSet = null; scoped = false;
        if (i + 2 >= p.Length || p[i + 1] != '?') return false;
        bool off = false, any = false;
        int k = i + 2;
        for (; k < p.Length; k++)
        {
            char c = p[k];
            if (c == '-') { off = true; continue; }
            if (c is 'i' or 'm' or 'n' or 's' or 'x') { any = true; if (c == 'x') xSet = !off; continue; }
            break;
        }
        if (!any || k >= p.Length || p[k] is not (')' or ':')) { xSet = null; return false; }
        scoped = p[k] == ':';
        after = k + 1;
        return true;
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
                   @"漢字の主な範囲。々・〇・拡張A（㐀 など）も含めるなら [\p{IsCJKUnifiedIdeographs}\p{IsCJKUnifiedIdeographsExtensionA}\p{IsCJKCompatibilityIdeographs}々〇]。拡張B 以降はこのブロック指定の書き方では含められません（.NET の正規表現は UTF-16 の1文字ずつで照合するため）",
                   @"the main range of kanji; to also cover 々 〇 and Extension A (㐀 etc.) use [\p{IsCJKUnifiedIdeographs}\p{IsCJKUnifiedIdeographsExtensionA}\p{IsCJKCompatibilityIdeographs}々〇]; Extension B and later cannot be covered by these block names (.NET regex matches UTF-16 code units)"),
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
