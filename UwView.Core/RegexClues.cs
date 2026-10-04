using System.Text;

namespace UwView.Core;

/// <summary>
/// 必須リテラル（<see cref="RegexLiterals"/>・2 文字以上）が取れない正規表現から、<b>短い手がかり</b>を取り出す
/// （オーナー指示 2026-10-04「uvf に短い手がかりで絞る直しを試して」）。
///
/// 返すのは UTF-8 のバイト列の集合で、「<b>この正規表現に当たる行は、必ずこのうちどれか1つを含む</b>」ことを保証する。
/// Rust の regex・ripgrep は、1 文字の必須の文字（<c>[0-9]{4}-[0-9]{2}</c> の <c>-</c>、<c>\d+\.\d{6,}</c> の <c>.</c>）や、
/// 文字クラスのバイトの並び（<c>[ぁ-ん]</c> は UTF-8 で <c>E3 81</c> か <c>E3 82</c> で始まる）でも先に絞る。
/// uvf はこれらを全行に当てていて、3G で 2.7 秒（rg 0.27〜1.3 秒）かかっていた（ruvf の比較 2026-10-04）。
///
/// 見るのは式の一番外の並びだけ（保守的に。迷ったら取らない）:
/// <list type="bullet">
/// <item>一番外に <c>|</c> がある・<c>(?i)</c> などのオプションがある → 取らない</item>
/// <item><c>?</c> <c>*</c> <c>{0,…}</c> の付いた要素は必須でない。グループ・<c>.</c>・<c>\d</c> などは手がかりにしない（読み飛ばす）</item>
/// <item>1 文字のリテラル（<c>\.</c> <c>\-</c> なども）→ その文字</item>
/// <item>否定でない文字クラス（文字と範囲だけ）→ ASCII はその 1 バイト、非 ASCII は UTF-8 の先頭 1〜2 バイト。合わせて <see cref="MaxNeedles"/> 個まで</item>
/// <item>ほぼどの行にもある 1 バイト（空白・タブ・<c>&lt; &gt; " = /</c>・数字・よく出る英小文字）だけの手がかりは使わない。
/// 絞れずに探す手間だけが乗る（OSM の <c>-v '^ +&lt;'</c> で 1.1 倍遅くなった。Rust の regex も、ありふれたバイトでは前置きを使わない）</item>
/// </list>
/// いくつも取れたら、いちばん長いバイト列を含むもの、次に候補の少ないものを選ぶ。
/// </summary>
public static class RegexClues
{
    /// <summary>候補の数の上限（1 つずつ探すので、多いと手間が勝つ）。</summary>
    public const int MaxNeedles = 4;

    /// <summary>短い手がかり（大小を区別する正規表現だけ）。取れなければ null。</summary>
    public static IReadOnlyList<byte[]>? Extract(string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return null;
        pattern = RegexDialect.Normalize(pattern);
        var candidates = new List<List<byte[]>>();
        int i = 0;
        try
        {
            while (i < pattern.Length)
            {
                char c = pattern[i];
                List<byte[]>? set = null;
                switch (c)
                {
                    case '|':
                        return null;                        // 一番外の選択（グループの中は SkipGroup が飛ばす）
                    case '(':
                        if (i + 1 < pattern.Length && pattern[i + 1] == '?'
                            && !(i + 2 < pattern.Length && pattern[i + 2] is ':' or '<' or '\'' or '=' or '!' or '>'))
                            return null;                    // (?i) などのオプション
                        i = SkipGroup(pattern, i);
                        if (i < 0) return null;
                        break;
                    case '[':
                        int end = ClassEnd(pattern, i);
                        if (end < 0) return null;
                        set = ClassNeedles(pattern, i + 1, end);
                        i = end + 1;
                        break;
                    case '\\':
                        if (i + 1 >= pattern.Length) return null;
                        char e = pattern[i + 1];
                        if (!char.IsLetterOrDigit(e) && e != '_') set = [Utf8(e)];
                        else if (e == 't') set = [[(byte)'\t']];
                        i += 2;
                        // 引数の付くもの（\p{..} \k<..> \x41 A \cX・後方参照の数字）は引数ごと飛ばす
                        if (e is 'p' or 'P' or 'k' && i < pattern.Length && pattern[i] is '{' or '<' or '\'')
                        {
                            char close = pattern[i] == '{' ? '}' : pattern[i] == '<' ? '>' : '\'';
                            int k = pattern.IndexOf(close, i);
                            if (k < 0) return null;
                            i = k + 1;
                        }
                        else if (e == 'x') i += 2;
                        else if (e == 'u') i += 4;
                        else if (e == 'c') i += 1;
                        else if (char.IsDigit(e)) while (i < pattern.Length && char.IsDigit(pattern[i])) i++;
                        if (i > pattern.Length) return null;
                        break;
                    case ')':
                        return null;                        // 対の合わない括弧（壊れた式）
                    case '*' or '+' or '?' or '{':
                        return null;                        // 先頭に量指定子（壊れた式）
                    case '.' or '^' or '$':
                        i++;
                        break;
                    default:
                        if (char.IsSurrogate(c)) return null;
                        set = [Utf8(c)];
                        i++;
                        break;
                }

                // 量指定子。0 回を許すなら必須でない
                int min = 1;
                bool quantified = true;
                if (i < pattern.Length && pattern[i] is '*' or '?') { min = 0; i++; }
                else if (i < pattern.Length && pattern[i] == '+') i++;
                else if (i < pattern.Length && pattern[i] == '{')
                {
                    int close = pattern.IndexOf('}', i);
                    if (close < 0) return null;
                    if (!int.TryParse(pattern[(i + 1)..close].Split(',')[0], out min)) return null;
                    i = close + 1;
                }
                else quantified = false;
                if (quantified && i < pattern.Length && pattern[i] is '?' or '+') i++;   // 最短一致・強欲

                if (set is { Count: > 0 } && min > 0 && !set.All(IsTooCommon)) candidates.Add(set);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException) { return null; }

        return candidates
            .OrderByDescending(s => s.Max(n => n.Length))
            .ThenBy(s => s.Count)
            .FirstOrDefault();
    }

    /// <summary>XML・ログ・ソースでほぼどの行にもあるバイト（1 バイトの手がかりにしない）。</summary>
    private static ReadOnlySpan<byte> VeryCommon => " \t<>\"=/0123456789etaoinsr"u8;

    private static bool IsTooCommon(byte[] needle) => needle.Length == 1 && VeryCommon.IndexOf(needle[0]) >= 0;

    private static byte[] Utf8(char c) => Encoding.UTF8.GetBytes(c.ToString());

    /// <summary>( から対応する ) の次の位置（文字クラス・逃がした括弧は数えない）。合わなければ -1。</summary>
    private static int SkipGroup(string p, int i)
    {
        int depth = 0;
        for (; i < p.Length; i++)
        {
            char c = p[i];
            if (c == '\\') { i++; continue; }
            if (c == '[') { int e = ClassEnd(p, i); if (e < 0) return -1; i = e; continue; }
            if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return i + 1;
        }
        return -1;
    }

    /// <summary>[ に対応する ] の位置（RegexLiterals と同じ読み方。.NET の減算 [a-z-[aeiou]] は -1）。</summary>
    private static int ClassEnd(string p, int i)
    {
        i++;
        if (i < p.Length && p[i] == '^') i++;
        if (i < p.Length && p[i] == ']') i++;
        while (i < p.Length && p[i] != ']')
        {
            if (p[i] == '\\') i++;
            else if (p[i] == '-' && i + 1 < p.Length && p[i + 1] == '[') return -1;
            else if (p[i] == '[' && i + 1 < p.Length && p[i + 1] == ':')
            {
                int end = p.IndexOf(":]", i + 2, StringComparison.Ordinal);
                if (end > 0) i = end + 1;
            }
            i++;
        }
        return i < p.Length ? i : -1;
    }

    /// <summary>
    /// 文字クラスの中身（[ と ] の間）から、当たる文字が必ず始まるバイト列の集合を作る。
    /// 否定・\d などの逃がし・POSIX の書き方を含むものは null。候補が多すぎても null。
    /// </summary>
    private static List<byte[]>? ClassNeedles(string p, int from, int end)
    {
        if (from < end && p[from] == '^') return null;
        var ranges = new List<(int Lo, int Hi)>();
        int i = from;
        while (i < end)
        {
            int lo = ReadClassChar(p, ref i, end);
            if (lo < 0) return null;
            int hi = lo;
            if (i + 1 < end && p[i] == '-')
            {
                i++;
                hi = ReadClassChar(p, ref i, end);
                if (hi < 0 || hi < lo) return null;
            }
            ranges.Add((lo, hi));
        }
        if (ranges.Count == 0) return null;

        var set = new HashSet<string>(StringComparer.Ordinal);   // バイト列を 16 進の文字列にして重複を外す
        var needles = new List<byte[]>();
        foreach (var (lo, hi) in ranges)
        {
            if (hi - lo > 0x10000) return null;
            for (int cp = lo; cp <= hi; cp++)
            {
                byte[] prefix = cp < 0x80 ? [(byte)cp]
                              : cp < 0x800 ? [(byte)(0xC0 | (cp >> 6))]
                              : [(byte)(0xE0 | (cp >> 12)), (byte)(0x80 | ((cp >> 6) & 0x3F))];
                if (set.Add(Convert.ToHexString(prefix)))
                {
                    needles.Add(prefix);
                    if (needles.Count > MaxNeedles) return null;
                }
            }
        }
        return needles;
    }

    /// <summary>クラスの中の 1 文字（逃がしは記号だけ受ける）。読めなければ -1。</summary>
    private static int ReadClassChar(string p, ref int i, int end)
    {
        char c = p[i];
        if (c == '[') return -1;                         // POSIX・減算
        if (char.IsSurrogate(c)) return -1;
        if (c != '\\') { i++; return c; }
        if (i + 1 >= end) return -1;
        char e = p[i + 1];
        i += 2;
        if (!char.IsLetterOrDigit(e) && e != '_') return e;
        if (e == 't') return '\t';
        return -1;                                       // \d \w \s \p{..} \x.. など
    }
}
