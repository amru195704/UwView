using System.Text;

namespace UwView.Core;

/// <summary>
/// 正規表現の<b>必須リテラル</b>を取り出す（指示書 2026-09-15「正規表現検索の高速化」A）。
///
/// 返すのは文字列の集合で、「<b>この正規表現に当たる行は、必ずこのうちどれか1つを含む</b>」ことを保証する。
/// 検索ではまずこの文字列をバイト列の高速検索（SIMD の IndexOf）で探し、見つかった行にだけ正規表現を当てる。
/// これまでは当たりようのない行まで全部 UTF-16 にデコードしてから正規表現を当てていて、
/// 50GB で固定文字列の約2倍の時間がかかっていた（rg は正規表現でもリテラルで先に絞るので速度が落ちない）。
///
/// <b>取り出しは保守的</b>に行う。確かに必須だと言えないものは取らない（取れなければ null ＝ 従来どおり全行を見る）。
/// 迷ったら取らない側に倒す——取り損ねても遅いだけだが、必須でないものを取ると<b>ヒットが消える</b>。
/// <list type="bullet">
/// <item>大小無視（IgnoreCase・<c>(?i)</c>）は扱わない</item>
/// <item>文字クラス・<c>.</c>・<c>\d</c> など・後方参照・先読み／後読みはリテラルの切れ目</item>
/// <item><c>?</c> <c>*</c> <c>{0,…}</c> の付いた要素は必須でない。<c>+</c> <c>{1,…}</c> は1回分だけ必須</item>
/// <item>選択肢がすべてリテラルのグループ <c>(bus_stop|traffic_signals)</c> は、候補の集合として前後とつなぐ</item>
/// <item>全体が <c>a|b</c> のときは、枝ごとの必須リテラルの和集合（1つでも取れない枝があれば null）</item>
/// <item>オプション <c>(?i)</c>・コメント・条件式は、グループの中にあっても読まない（null）</item>
/// </list>
/// 字句の読み取りは <see cref="RegexSyntax"/>（RegexClues・RegexDialect と共用）。
/// 長さ・数の上限（速さの都合）は <see cref="SearchTuning.AcceptLiterals"/>。
/// </summary>
public static class RegexLiterals
{
    /// <summary>必須リテラルの集合（使える長さ・数のもの）。取れなければ null。</summary>
    public static IReadOnlyList<string>? Extract(string pattern, bool ignoreCase)
        => ignoreCase ? null : Required(pattern) is { } best && SearchTuning.AcceptLiterals(best) ? best : null;

    /// <summary>
    /// 当たる行が必ずどれかを含む文字列の集合（長さ・数を問わない）。式を読めない・バイト列で探せない文字を含むなら null。
    /// ここは<b>答えを守る条件</b>だけを見る。
    /// </summary>
    internal static IReadOnlyList<string>? Required(string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return null;
        pattern = RegexDialect.Normalize(pattern);   // 照合と同じ読み替え（(?P< のまま読むと必須の文字を取り違える）
        try
        {
            var parser = new Parser(pattern);
            var result = parser.ParseAlternation(topLevel: true);
            if (parser.Failed || !parser.AtEnd || result.Best is not { } best) return null;
            if (best.Count == 0 || best.Any(s => s.Length == 0 || !RegexSyntax.IsByteSafe(s))) return null;
            return best.Distinct(StringComparer.Ordinal).ToList();
        }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// 1つの選択（<c>a|b|c</c>）を読んだ結果。
    /// <see cref="Best"/> は「当たるなら必ずどれかを含む」集合、<see cref="PureLiterals"/> は
    /// 各枝がリテラルだけでできているときのその文字列（前後の文字とつなげられる）。
    /// </summary>
    private sealed record Alternation(List<string>? Best, List<string>? PureLiterals);

    private sealed class Parser(string p)
    {
        private const int MaxAlternatives = SearchTuning.MaxLiteralAlternatives;   // 組み合わせが増えすぎないように
        private int _i;
        public bool Failed { get; private set; }
        public bool AtEnd => _i >= p.Length;

        public Alternation ParseAlternation(bool topLevel)
        {
            var branches = new List<(List<string>? Best, string? Pure)>();
            while (true)
            {
                branches.Add(ParseSequence());
                if (Failed) return new(null, null);
                if (_i < p.Length && p[_i] == '|') { _i++; continue; }
                break;
            }
            if (!topLevel)
            {
                if (_i >= p.Length || p[_i] != ')') { Failed = true; return new(null, null); }
                _i++;
            }

            List<string>? pure = branches.All(b => b.Pure is not null) ? branches.Select(b => b.Pure!).ToList() : null;
            List<string>? best = null;
            if (branches.All(b => b.Best is not null))
            {
                best = branches.SelectMany(b => b.Best!).ToList();
                if (best.Count > MaxAlternatives) best = null;
            }
            return new(best, pure);
        }

        /// <summary>選択の1枝を読む。枝の中で最も絞れる必須リテラル集合と、枝全体がリテラルならその文字列を返す。</summary>
        private (List<string>? Best, string? Pure) ParseSequence()
        {
            var candidates = new List<List<string>>();
            var run = new List<string> { "" };     // いま伸ばしている必須リテラル（候補の集合）
            var pure = new StringBuilder();
            bool isPure = true;

            void Flush()
            {
                if (run.Any(s => s.Length > 0)) candidates.Add(run);
                run = [""];
            }

            while (_i < p.Length && p[_i] != '|' && p[_i] != ')')
            {
                char c = p[_i];
                string? literal = null;     // 1文字のリテラル（読めたとき）
                List<string>? groupSet = null;
                List<string>? groupBest = null;
                bool breaks = false;

                switch (c)
                {
                    case '\\':
                        // \d \w \s \b \p{..} \x.. \u.... \1 \k<..> など（Other）は切れ目として読み飛ばす
                        var kind = RegexSyntax.ReadEscape(p, ref _i, out char escaped);
                        if (kind == RegexSyntax.EscapeKind.Broken) { Failed = true; return (null, null); }
                        if (kind == RegexSyntax.EscapeKind.Literal) literal = escaped.ToString();
                        else breaks = true;
                        break;
                    case '[':
                        int classEnd = RegexSyntax.ClassEnd(p, _i);
                        if (classEnd < 0) { Failed = true; return (null, null); }
                        _i = classEnd + 1;
                        breaks = true;
                        break;
                    case '(':
                        switch (RegexSyntax.OpenGroup(p, _i, out int body))
                        {
                            case RegexSyntax.GroupKind.Unsupported:
                                Failed = true;                       // (?i) などのオプション・コメント・条件式は扱わない
                                return (null, null);
                            case RegexSyntax.GroupKind.Lookaround:
                                // 先読み・後読み: 本文を消費しないので必須リテラルにしない
                                _i = RegexSyntax.SkipGroup(p, _i);
                                if (_i < 0) { Failed = true; return (null, null); }
                                isPure = false;
                                Flush();
                                if (RegexSyntax.ReadQuantifier(p, ref _i).Min < 0) { Failed = true; return (null, null); }
                                continue;
                            default:
                                _i = body;
                                var inner = ParseAlternation(topLevel: false);
                                if (Failed) return (null, null);
                                groupSet = inner.PureLiterals;
                                groupBest = inner.Best;
                                break;
                        }
                        break;
                    case '.' or '^' or '$':
                        _i++;
                        breaks = true;
                        break;
                    case '*' or '+' or '?' or '{':
                        // 先頭に量指定子（壊れた式）: 自分では判断しない
                        Failed = true;
                        return (null, null);
                    default:
                        // サロゲートの対は 2 つまとめて 1 文字。量指定子の付く対・片側だけのものは切れ目
                        literal = RegexSyntax.ReadLiteral(p, ref _i);
                        if (literal is null) breaks = true;
                        break;
                }

                // 量指定子
                var (min, quantified) = RegexSyntax.ReadQuantifier(p, ref _i);
                if (min < 0) { Failed = true; return (null, null); }

                if (quantified) isPure = false;
                if (breaks) { isPure = false; Flush(); continue; }

                if (literal is not null)
                {
                    if (isPure && !quantified) pure.Append(literal);
                    if (min == 0) { Flush(); continue; }             // 無くてもよい
                    run = run.Select(s => s + literal).ToList();
                    if (quantified) Flush();                          // 2回目以降は数が決まらない
                    continue;
                }

                // グループ
                if (min == 0) { isPure = false; Flush(); continue; }
                if (groupSet is not null && !quantified && run.Count * groupSet.Count <= MaxAlternatives)
                {
                    if (isPure && groupSet.Count == 1) pure.Append(groupSet[0]); else isPure = false;
                    run = run.SelectMany(prefix => groupSet.Select(g => prefix + g)).ToList();
                    continue;
                }
                isPure = false;
                Flush();
                if (groupBest is not null && groupBest.All(s => s.Length > 0)) candidates.Add(groupBest);
                else if (groupSet is not null && groupSet.All(s => s.Length > 0)) candidates.Add(groupSet);
            }
            Flush();

            // 最も絞れるもの: 候補のうち一番短い文字列が長いもの（同じなら候補が少ないもの）
            var best = candidates
                .Where(set => set.All(s => s.Length > 0))
                .OrderByDescending(set => set.Min(s => s.Length))
                .ThenBy(set => set.Count)
                .FirstOrDefault();
            return (best, isPure ? pure.ToString() : null);
        }
    }
}

/// <summary>
/// 複数のバイト列のうち、いちばん手前に現れる位置を探す（<see cref="RegexLiterals"/> の候補を行の中から探す用）。
/// 候補ごとに「次に現れる位置」を覚えておき、読み進めた位置を越えたものだけ探し直す。
/// </summary>
public sealed class LiteralFinder
{
    private readonly byte[][] _needles;
    private readonly int[] _next;

    public LiteralFinder(IReadOnlyList<string> literals, Encoding encoding)
        : this(literals.Select(encoding.GetBytes).ToArray()) { }

    /// <summary>バイト列で渡す（<see cref="RegexClues"/> の短い手がかり。文字の途中までのバイト列もある）。</summary>
    public LiteralFinder(IReadOnlyList<byte[]> needles)
    {
        _needles = needles.Where(b => b.Length > 0).ToArray();
        _next = new int[_needles.Length];
    }

    /// <summary>UTF-8 のファイルでだけ使う（他の文字コードではデコード結果とバイト列が1対1にならないことがある）。</summary>
    public static bool Supports(Encoding encoding) => encoding.CodePage == Encoding.UTF8.CodePage;

    /// <summary>新しい領域を探し始める前に呼ぶ。</summary>
    public void Reset() => Array.Fill(_next, -2);

    /// <summary>from 以降で、いずれかの候補がいちばん手前に現れる位置（無ければ -1）。</summary>
    public int IndexOf(ReadOnlySpan<byte> region, int from)
    {
        int best = -1;
        for (int k = 0; k < _needles.Length; k++)
        {
            int at = _next[k];
            if (at == -1) continue;                    // この領域にはもう無い
            if (at < from)
            {
                int rel = region[from..].IndexOf(_needles[k]);
                at = _next[k] = rel < 0 ? -1 : from + rel;
                if (at < 0) continue;
            }
            if (best < 0 || at < best) best = at;
        }
        return best;
    }
}
