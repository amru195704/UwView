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
/// </list>
/// </summary>
public static class RegexLiterals
{
    /// <summary>候補の数の上限（多すぎると1つずつ探す手間が勝つ）。</summary>
    public const int MaxAlternatives = 16;

    /// <summary>この長さ未満のリテラルは絞り込みに使わない（1文字はほぼ全行に当たる）。</summary>
    public const int MinLength = 2;

    /// <summary>必須リテラルの集合。取れなければ null。</summary>
    public static IReadOnlyList<string>? Extract(string pattern, bool ignoreCase)
    {
        if (ignoreCase || string.IsNullOrEmpty(pattern)) return null;
        try
        {
            var parser = new Parser(pattern);
            var result = parser.ParseAlternation(topLevel: true);
            if (parser.Failed || !parser.AtEnd || result.Best is not { } best) return null;
            if (best.Count > MaxAlternatives || best.Any(s => s.Length < MinLength || s.Contains('�'))) return null;
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
                        if (_i + 1 >= p.Length) { Failed = true; return (null, null); }
                        char e = p[_i + 1];
                        if (!char.IsLetterOrDigit(e) && e != '_') { literal = e.ToString(); _i += 2; }
                        else if (e == 't') { literal = "\t"; _i += 2; }
                        else
                        {
                            // \d \w \s \b \p{..} \x.. \u.... \1 \k<..> など: 切れ目として読み飛ばす。
                            // 引数の付くものは引数ごと飛ばす（\x41 の "41" をリテラルと読まないように）
                            breaks = true;
                            _i += 2;
                            if ((e is 'p' or 'P' or 'k') && _i < p.Length && p[_i] is '{' or '<' or '\'')
                            {
                                char close = p[_i] == '{' ? '}' : p[_i] == '<' ? '>' : '\'';
                                int end = p.IndexOf(close, _i);
                                if (end < 0) { Failed = true; return (null, null); }
                                _i = end + 1;
                            }
                            else if (e == 'x') _i += 2;
                            else if (e == 'u') _i += 4;
                            else if (e == 'c') _i += 1;
                            else if (char.IsDigit(e)) while (_i < p.Length && char.IsDigit(p[_i])) _i++;
                            if (_i > p.Length) { Failed = true; return (null, null); }
                        }
                        break;
                    case '[':
                        SkipClass();
                        if (Failed) return (null, null);
                        breaks = true;
                        break;
                    case '(':
                        _i++;
                        if (_i < p.Length && p[_i] == '?')
                        {
                            if (_i + 1 < p.Length && p[_i + 1] == ':') _i += 2;
                            else if (_i + 1 < p.Length && p[_i + 1] == '>') _i += 2;
                            else if (_i + 2 < p.Length && p[_i + 1] == '<' && p[_i + 2] is not ('=' or '!')
                                     || _i + 1 < p.Length && p[_i + 1] == '\'')
                            {
                                char close = p[_i + 1] == '<' ? '>' : '\'';
                                int end = p.IndexOf(close, _i + 2);
                                if (end < 0) { Failed = true; return (null, null); }
                                _i = end + 1;
                            }
                            else if (_i + 1 < p.Length && p[_i + 1] is '=' or '!'
                                     || _i + 2 < p.Length && p[_i + 1] == '<' && p[_i + 2] is '=' or '!')
                            {
                                // 先読み・後読み: 本文を消費しないので必須リテラルにしない
                                _i--;                                  // '(' から読み直して対応する ')' まで飛ばす
                                SkipGroup();
                                if (Failed) return (null, null);
                                breaks = true;
                                isPure = false;
                                Flush();
                                continue;
                            }
                            else { Failed = true; return (null, null); }   // (?i) などのオプション・条件式は扱わない
                        }
                        var inner = ParseAlternation(topLevel: false);
                        if (Failed) return (null, null);
                        groupSet = inner.PureLiterals;
                        groupBest = inner.Best;
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
                        literal = c.ToString();
                        _i++;
                        break;
                }

                // 量指定子
                var (min, quantified) = ReadQuantifier();
                if (Failed) return (null, null);

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

        /// <summary>量指定子を読む。返り値は最小回数（無ければ 1）と、量指定子があったか。</summary>
        private (int Min, bool Quantified) ReadQuantifier()
        {
            if (_i >= p.Length) return (1, false);
            int min;
            switch (p[_i])
            {
                case '*': min = 0; _i++; break;
                case '?': min = 0; _i++; break;
                case '+': min = 1; _i++; break;
                case '{':
                {
                    int end = p.IndexOf('}', _i);
                    if (end < 0) { Failed = true; return (1, false); }
                    string body = p[(_i + 1)..end];
                    string first = body.Split(',')[0];
                    if (!int.TryParse(first, out min)) { Failed = true; return (1, false); }
                    _i = end + 1;
                    break;
                }
                default: return (1, false);
            }
            if (_i < p.Length && p[_i] is '?' or '+') _i++;   // 最短一致・強欲
            return (min, true);
        }

        private void SkipClass()
        {
            _i++;                                      // '['
            if (_i < p.Length && p[_i] == '^') _i++;
            if (_i < p.Length && p[_i] == ']') _i++;   // 先頭の ] は文字
            while (_i < p.Length && p[_i] != ']')
            {
                if (p[_i] == '\\') _i++;
                else if (p[_i] == '[' && _i + 1 < p.Length && p[_i + 1] == ':')   // [:alpha:] 形式
                {
                    int end = p.IndexOf(":]", _i + 2, StringComparison.Ordinal);
                    if (end > 0) _i = end + 1;
                }
                _i++;
            }
            if (_i >= p.Length) { Failed = true; return; }
            _i++;                                      // ']'
        }

        private void SkipGroup()
        {
            int depth = 0;
            for (; _i < p.Length; _i++)
            {
                char c = p[_i];
                if (c == '\\') { _i++; continue; }
                if (c == '[') { SkipClass(); if (Failed) return; _i--; continue; }
                if (c == '(') depth++;
                else if (c == ')' && --depth == 0) { _i++; return; }
            }
            Failed = true;
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
    {
        _needles = literals.Select(encoding.GetBytes).Where(b => b.Length > 0).ToArray();
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
