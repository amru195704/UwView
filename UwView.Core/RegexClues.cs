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
/// <item>否定でない文字クラス（文字と範囲だけ）→ ASCII はその 1 バイト、非 ASCII は UTF-8 の先頭 1〜2 バイト。合わせて <see cref="SearchTuning.MaxShortClueNeedles"/> 個まで</item>
/// <item>ほぼどの行にもある 1 バイト（空白・タブ・<c>&lt; &gt; " = /</c>・数字・よく出る英小文字）だけの手がかりは使わない。
/// 絞れずに探す手間だけが乗る（OSM の <c>-v '^ +&lt;'</c> で 1.1 倍遅くなった。Rust の regex も、ありふれたバイトでは前置きを使わない）</item>
/// </list>
/// いくつも取れたら、いちばん長いバイト列を含むもの、次に候補の少ないものを選ぶ（<see cref="SearchTuning.ChooseShortClue"/>）。
/// 字句の読み取りは <see cref="RegexSyntax"/>（RegexLiterals と共用）。
/// </summary>
public static class RegexClues
{
    /// <summary>短い手がかり（大小を区別する正規表現だけ）。取れなければ null。</summary>
    public static IReadOnlyList<byte[]>? Extract(string pattern)
        => Candidates(pattern) is { } candidates ? SearchTuning.ChooseShortClue(candidates) : null;

    /// <summary>
    /// 手がかりの候補（どれを使っても取りこぼさない集合の並び）。式を読めなければ null。
    /// ここは<b>答えを守る条件</b>だけを見る。どれを使うか（速さの都合）は <see cref="SearchTuning.ChooseShortClue"/>。
    /// </summary>
    internal static List<List<byte[]>>? Candidates(string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return null;
        pattern = RegexDialect.Normalize(pattern);
        var candidates = new List<List<byte[]>>();
        int i = 0;
        while (i < pattern.Length)
        {
            char c = pattern[i];
            List<byte[]>? set = null;
            switch (c)
            {
                case '|':
                    return null;                        // 一番外の選択（グループの中は SkipGroup が飛ばす）
                case '(':
                    // (?i) などのオプション・コメントは、後ろの意味を変えるので読まない（グループの中のものは SkipGroup が見る）
                    if (RegexSyntax.OpenGroup(pattern, i, out _) == RegexSyntax.GroupKind.Unsupported) return null;
                    i = RegexSyntax.SkipGroup(pattern, i);
                    if (i < 0) return null;
                    break;
                case '[':
                    int end = RegexSyntax.ClassEnd(pattern, i);
                    if (end < 0) return null;
                    set = ClassNeedles(pattern, i + 1, end);
                    i = end + 1;
                    break;
                case '\\':
                    var kind = RegexSyntax.ReadEscape(pattern, ref i, out char literal);
                    if (kind == RegexSyntax.EscapeKind.Broken) return null;
                    if (kind == RegexSyntax.EscapeKind.Literal) set = Clue(literal.ToString());
                    break;
                case ')':
                    return null;                        // 対の合わない括弧（壊れた式）
                case '*' or '+' or '?' or '{':
                    return null;                        // 先頭に量指定子（壊れた式）
                case '.' or '^' or '$':
                    i++;
                    break;
                default:
                    set = RegexSyntax.ReadLiteral(pattern, ref i) is { } text ? Clue(text) : null;
                    break;
            }

            // 量指定子。0 回を許すなら必須でない
            var (min, _) = RegexSyntax.ReadQuantifier(pattern, ref i);
            if (min < 0) return null;
            if (set is { Count: > 0 } && min > 0) candidates.Add(set);
        }
        return candidates;
    }

    /// <summary>
    /// 1 文字（サロゲートの対も）の手がかり。U+FFFD（置換文字）は使わない：壊れたバイト（FF など）をデコードしたときにも現れ、
    /// 元のバイト列には EF BF BD が無いので、行を捨ててしまう（外部レビュー 2026-10-04 の指摘1）。
    /// </summary>
    private static List<byte[]>? Clue(string text) => RegexSyntax.IsByteSafe(text) ? [Encoding.UTF8.GetBytes(text)] : null;

    /// <summary>
    /// 文字クラスの中身（[ と ] の間）から、当たる文字が必ず始まるバイト列の集合を作る。
    /// 否定・\d などの逃がし・POSIX の書き方・減算を含むものは null。候補が多すぎても null（数え上げを打ち切るため）。
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

        var seen = new HashSet<int>();       // 先頭のバイト列（1〜2 バイト）を数にして重複を外す
        var needles = new List<byte[]>();
        foreach (var (lo, hi) in ranges)
        {
            if (hi - lo > 0x10000) return null;
            if (lo <= 0xFFFD && 0xFFFD <= hi) return null;   // 置換文字（Clue を参照）
            for (int cp = lo; cp <= hi; cp++)
            {
                byte[] prefix = cp < 0x80 ? [(byte)cp]
                              : cp < 0x800 ? [(byte)(0xC0 | (cp >> 6))]
                              : [(byte)(0xE0 | (cp >> 12)), (byte)(0x80 | ((cp >> 6) & 0x3F))];
                if (seen.Add(prefix.Length == 1 ? prefix[0] : 0x10000 | prefix[0] << 8 | prefix[^1]))
                {
                    needles.Add(prefix);
                    if (needles.Count > SearchTuning.MaxShortClueNeedles) return null;
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
