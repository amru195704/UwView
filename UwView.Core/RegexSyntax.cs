namespace UwView.Core;

/// <summary>
/// 正規表現の字句の読み取り。<see cref="RegexDialect"/>・<see cref="RegexLiterals"/>・<see cref="RegexClues"/> で共用する
/// （外部レビュー 2026-10-04「整理の進め方」4）。3 つがそれぞれ括弧・文字クラス・逃がし・量指定子を読んでいて、
/// 読み方の違いから「当たるはずの行を捨てる」不具合が出ていた（入れ子の <c>(?x)</c> コメント・文字クラスの中の <c>(?P&lt;</c>）。
///
/// <b>.NET の構文を全部は読まない。</b>読めない・扱わない書き方に出会ったら「読めない」（-1・<see cref="GroupKind.Unsupported"/>・
/// <see cref="EscapeKind.Broken"/>）を返し、呼び出し側は抽出をあきらめる（全行に正規表現を当てる＝遅いが答えは変わらない）。
/// </summary>
internal static class RegexSyntax
{
    public enum GroupKind
    {
        /// <summary><c>(…)</c></summary>
        Capturing,
        /// <summary><c>(?:…)</c></summary>
        NonCapturing,
        /// <summary><c>(?&lt;名前&gt;…)</c> <c>(?'名前'…)</c></summary>
        Named,
        /// <summary><c>(?&gt;…)</c></summary>
        Atomic,
        /// <summary>先読み・後読み（本文を消費しない）</summary>
        Lookaround,
        /// <summary>オプション <c>(?i)</c> <c>(?x:…)</c>・コメント <c>(?#…)</c>・条件式 <c>(?(…)…)</c> など。中身を読めない</summary>
        Unsupported,
    }

    public enum EscapeKind
    {
        /// <summary>1 文字のリテラル（<c>\.</c> <c>\-</c> <c>\t</c> など）</summary>
        Literal,
        /// <summary><c>\d</c> <c>\b</c> <c>\p{..}</c> <c>\x41</c> <c>\1</c> など。引数ごと読み飛ばした</summary>
        Other,
        /// <summary>式の終わりで切れている</summary>
        Broken,
    }

    /// <summary>
    /// <paramref name="i"/>（<c>[</c>）から始まる文字クラスの、閉じる <c>]</c> の位置。壊れていれば -1。
    /// 先頭の <c>^</c> と <c>]</c>（文字として読む）・逃がし・<c>[:alpha:]</c>・.NET の減算 <c>[a-z-[aeiou]]</c> の入れ子を読む。
    /// </summary>
    public static int ClassEnd(string p, int i)
    {
        i++;
        if (i < p.Length && p[i] == '^') i++;
        if (i < p.Length && p[i] == ']') i++;
        while (i < p.Length && p[i] != ']')
        {
            if (p[i] == '\\') i++;
            else if (p[i] == '[' && i + 1 < p.Length && p[i + 1] == ':')
            {
                int end = p.IndexOf(":]", i + 2, StringComparison.Ordinal);
                if (end > 0) i = end + 1;
            }
            // 減算。内側のクラスごと飛ばさないと、内側の ] で終わったと誤解する（ソースレビュー 2026-09-19 の指摘6）
            else if (p[i] == '-' && i + 1 < p.Length && p[i + 1] == '[')
            {
                int inner = ClassEnd(p, i + 1);
                if (inner < 0) return -1;
                i = inner;
            }
            i++;
        }
        return i < p.Length ? i : -1;
    }

    /// <summary><paramref name="i"/>（<c>(</c>）の開き方と、中身の始まる位置。</summary>
    public static GroupKind OpenGroup(string p, int i, out int body)
    {
        body = i + 1;
        if (i + 1 >= p.Length || p[i + 1] != '?') return GroupKind.Capturing;
        if (i + 2 >= p.Length) return GroupKind.Unsupported;
        char c = p[i + 2];
        switch (c)
        {
            case ':': body = i + 3; return GroupKind.NonCapturing;
            case '>': body = i + 3; return GroupKind.Atomic;
            case '=' or '!': body = i + 3; return GroupKind.Lookaround;
            case '<' when i + 3 < p.Length && p[i + 3] is '=' or '!': body = i + 4; return GroupKind.Lookaround;
            case '<' or '\'':
                int end = p.IndexOf(c == '<' ? '>' : '\'', i + 3);
                if (end < 0) return GroupKind.Unsupported;
                body = end + 1;
                return GroupKind.Named;
            default:
                return GroupKind.Unsupported;
        }
    }

    /// <summary>
    /// <paramref name="i"/>（<c>(</c>）から、対応する <c>)</c> の次の位置。合わなければ -1。
    /// 中に読めない開き方（オプション・コメント・条件式）があっても -1（<c>(?x)</c> のコメントは <c>)</c> を含められるので、
    /// 括弧を数えられない。外部レビュー 2026-10-04 の指摘2）。
    /// </summary>
    public static int SkipGroup(string p, int i)
    {
        int depth = 0;
        for (; i < p.Length; i++)
        {
            char c = p[i];
            if (c == '\\') { i++; continue; }
            if (c == '[')
            {
                int end = ClassEnd(p, i);
                if (end < 0) return -1;
                i = end;
                continue;
            }
            if (c == '(')
            {
                if (OpenGroup(p, i, out int body) == GroupKind.Unsupported) return -1;
                depth++;
                i = body - 1;           // 名前（(?<名前>）は読み飛ばす
            }
            else if (c == ')' && --depth == 0) return i + 1;
        }
        return -1;
    }

    /// <summary>
    /// <paramref name="i"/>（<c>\</c>）の逃がしを読み、<paramref name="i"/> を次へ進める。
    /// 記号の逃がしと <c>\t</c> だけをリテラルとして読む。ほかは引数ごと飛ばす（<c>\x41</c> の 41 をリテラルと読まないように）。
    /// </summary>
    public static EscapeKind ReadEscape(string p, ref int i, out char literal)
    {
        literal = '\0';
        if (i + 1 >= p.Length) return EscapeKind.Broken;
        char e = p[i + 1];
        i += 2;
        if (char.IsSurrogate(e)) return EscapeKind.Other;   // 絵文字の逃がし（片側だけをリテラルにしない）
        if (!char.IsLetterOrDigit(e) && e != '_') { literal = e; return EscapeKind.Literal; }
        if (e == 't') { literal = '\t'; return EscapeKind.Literal; }
        if ((e is 'p' or 'P' or 'k') && i < p.Length && p[i] is '{' or '<' or '\'')
        {
            int end = p.IndexOf(p[i] == '{' ? '}' : p[i] == '<' ? '>' : '\'', i);
            if (end < 0) return EscapeKind.Broken;
            i = end + 1;
        }
        else if (e == 'x') i += 2;
        else if (e == 'u') i += 4;
        else if (e == 'c') i += 1;
        else if (char.IsDigit(e)) while (i < p.Length && char.IsDigit(p[i])) i++;
        return i > p.Length ? EscapeKind.Broken : EscapeKind.Other;
    }

    /// <summary>
    /// <paramref name="i"/> の量指定子を読み、<paramref name="i"/> を次へ進める。最小回数と、量指定子があったか。
    /// 無ければ (1, false)。読めなければ最小回数 -1（<c>{</c> の後ろが数でない等。.NET では文字の <c>{</c> だが、扱わない）。
    /// </summary>
    public static (int Min, bool Quantified) ReadQuantifier(string p, ref int i)
    {
        if (i >= p.Length) return (1, false);
        int min;
        switch (p[i])
        {
            case '*' or '?': min = 0; i++; break;
            case '+': min = 1; i++; break;
            case '{':
            {
                int end = p.IndexOf('}', i);
                if (end < 0 || !int.TryParse(p.AsSpan(i + 1, end - i - 1).ToString().Split(',')[0], out min) || min < 0)
                    return (-1, false);
                i = end + 1;
                break;
            }
            default: return (1, false);
        }
        if (i < p.Length && p[i] is '?' or '+') i++;   // 最短一致・強欲
        return (min, true);
    }

    /// <summary>
    /// <paramref name="i"/> の文字を 1 文字のリテラルとして読む（サロゲートの対なら 2 つまとめて）。
    /// .NET の量指定子は後ろの 1 つだけに掛かるので、量指定子の付くサロゲートの対と、対にならないサロゲートは null
    /// （<c>a😀?</c> で a と上位サロゲートだけを必須と取り違えた。外部レビュー 2026-10-04 の指摘3）。
    /// </summary>
    public static string? ReadLiteral(string p, ref int i)
    {
        char c = p[i];
        if (!char.IsSurrogate(c)) { i++; return c.ToString(); }
        if (char.IsHighSurrogate(c) && i + 1 < p.Length && char.IsLowSurrogate(p[i + 1])
            && !(i + 2 < p.Length && p[i + 2] is '*' or '+' or '?' or '{'))
        {
            i += 2;
            return p.Substring(i - 2, 2);
        }
        i++;
        return null;
    }

    /// <summary>
    /// 生のバイト列の手がかりに使ってよい文字列か。U+FFFD（壊れたバイトをデコードしたときにも現れ、元のバイト列には無い）と、
    /// 対になっていないサロゲート（UTF-8 にすると EF BF BD になる）を含むものは使えない（外部レビュー 2026-10-04 の指摘1・3）。
    /// </summary>
    public static bool IsByteSafe(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\uFFFD') return false;
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { i++; continue; }
            if (char.IsSurrogate(c)) return false;
        }
        return true;
    }
}
