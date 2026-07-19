using System.Globalization;
using System.Text.RegularExpressions;

namespace UwView.Core;

/// <summary>
/// 色分けハイライタの1規則（実装指示書_Ver1.1_色分けハイライタ.md §4）。
/// 検索とは独立に、開いた瞬間から可視行を多色着色する。klogg の Highlighter 規則に相当。
/// 色は "#RRGGBB"（または "#AARRGGBB"）または null。永続化するので mutable な class。
/// </summary>
public sealed class HlRule
{
    public string Pattern { get; set; } = "";
    public bool IsRegex { get; set; }
    public bool IgnoreCase { get; set; }
    /// <summary>前景色（文字色）。null＝変更しない。</summary>
    public string? Foreground { get; set; }
    /// <summary>背景色。null＝変更しない。</summary>
    public string? Background { get; set; }
    /// <summary>行全体を着色するか（false＝マッチ部のみ・キャプチャがあれば捕捉部のみ）。</summary>
    public bool WholeLine { get; set; }
    public bool Enabled { get; set; } = true;

    public HlRule() { }

    public HlRule(string pattern, bool isRegex = false, bool ignoreCase = false,
        string? foreground = null, string? background = null, bool wholeLine = false, bool enabled = true)
    {
        Pattern = pattern; IsRegex = isRegex; IgnoreCase = ignoreCase;
        Foreground = foreground; Background = background; WholeLine = wholeLine; Enabled = enabled;
    }

    public HlRule Clone() => new(Pattern, IsRegex, IgnoreCase, Foreground, Background, WholeLine, Enabled);
}

/// <summary>
/// 規則のセット（klogg の Highlighter set）。アクティブなセットは当面1つ（§10-2）。
/// 規則リストの先頭が最優先（適用は下→上・後勝ちで上書き＝先頭が勝つ・§2）。
/// </summary>
public sealed class HlSet
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    /// <summary>同梱プリセットは読み取り専用（複製して編集する・§4）。</summary>
    public bool ReadOnly { get; set; }
    public List<HlRule> Rules { get; set; } = new();

    public HlSet() { }
    public HlSet(string id, string name, List<HlRule> rules, bool readOnly = false)
    {
        Id = id; Name = name; Rules = rules; ReadOnly = readOnly;
    }

    public HlSet Clone(string? newId = null, string? newName = null) => new(
        newId ?? Guid.NewGuid().ToString("N"),
        newName ?? Name,
        Rules.ConvertAll(r => r.Clone()),
        readOnly: false);

    public override string ToString() => Name; // ComboBox 等の既定表示
}

/// <summary>クイックカラーラベル（選択文字列への即着色。上限32・§2/§10-3）。</summary>
public sealed class ColorLabel
{
    /// <summary>ラベル色 "#RRGGBB"。</summary>
    public string Color { get; set; } = "#FFE066";
    /// <summary>この色に割り当てた文字列（任意個）。</summary>
    public List<string> Keywords { get; set; } = new();

    public ColorLabel() { }
    public ColorLabel(string color) { Color = color; }
}

/// <summary>ハイライタ全体の永続状態（AppSettings に相乗り）。</summary>
public sealed class HighlighterConfig
{
    public List<HlSet> Sets { get; set; } = new();
    public string? ActiveSetId { get; set; }
    /// <summary>クイックラベル（既定は色覚配慮32色パレット・先頭9色にホットキー）。</summary>
    public List<ColorLabel> ColorLabels { get; set; } = new();
}

/// <summary>着色済みの1区間（文字インデックス基準。fg/bg は ARGB packed。0=変更なし）。</summary>
public readonly record struct HlSpan(int Start, int Length, uint Fg, uint Bg);

/// <summary>
/// 1行のテキストに対し、コンパイル済み規則を下→上（先頭勝ち）で適用し、着色区間を返す。
/// 可視行のみに適用するので巨大ファイルでも影響なし（§5）。行デコードは呼び出し側で1回。
/// </summary>
public sealed class CompiledHighlighter
{
    private readonly (HlRule Rule, Regex Regex, uint Fg, uint Bg)[] _rules;

    /// <summary>有効な規則が1つも無ければ null（Render 側で着色スキップ）。</summary>
    public bool IsEmpty => _rules.Length == 0;

    private CompiledHighlighter((HlRule, Regex, uint, uint)[] rules) => _rules = rules;

    public static CompiledHighlighter Build(HlSet? set)
    {
        if (set is null || set.Rules.Count == 0)
            return new CompiledHighlighter(Array.Empty<(HlRule, Regex, uint, uint)>());

        var list = new List<(HlRule, Regex, uint, uint)>(set.Rules.Count);
        foreach (var rule in set.Rules)
        {
            if (!rule.Enabled || rule.Pattern.Length == 0) continue;
            uint fg = ParseColor(rule.Foreground);
            uint bg = ParseColor(rule.Background);
            if (fg == 0 && bg == 0) continue; // 色指定なしは無視
            Regex regex;
            try
            {
                var opts = RegexOptions.CultureInvariant;
                if (rule.IgnoreCase) opts |= RegexOptions.IgnoreCase;
                string pat = rule.IsRegex ? rule.Pattern : Regex.Escape(rule.Pattern);
                regex = new Regex(pat, opts, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException) { continue; } // 不正な正規表現はスキップ
            list.Add((rule, regex, fg, bg));
        }
        return new CompiledHighlighter(list.ToArray());
    }

    /// <summary>
    /// 行テキストの着色区間を返す（Start 昇順・非重複）。着色なしなら空配列。
    /// 規則は配列末尾（＝リスト下）から先頭へ塗り、先頭規則が上書きで勝つ（§2）。
    /// </summary>
    public IReadOnlyList<HlSpan> Highlight(string line)
    {
        if (_rules.Length == 0 || line.Length == 0) return Array.Empty<HlSpan>();

        int n = line.Length;
        uint[]? fg = null;
        uint[]? bg = null;

        // 下→上（末尾→先頭）。後で塗った先頭規則が勝つ。
        for (int r = _rules.Length - 1; r >= 0; r--)
        {
            var (rule, regex, rfg, rbg) = _rules[r];
            try
            {
                if (rule.WholeLine)
                {
                    if (regex.IsMatch(line))
                        Paint(ref fg, ref bg, n, 0, n, rfg, rbg);
                    continue;
                }
                foreach (Match m in regex.Matches(line))
                {
                    if (m.Length == 0) continue;
                    // キャプチャがあれば捕捉部のみ、無ければマッチ全体（§2）
                    bool painted = false;
                    for (int g = 1; g < m.Groups.Count; g++)
                    {
                        var grp = m.Groups[g];
                        if (grp.Success && grp.Length > 0)
                        {
                            Paint(ref fg, ref bg, n, grp.Index, grp.Index + grp.Length, rfg, rbg);
                            painted = true;
                        }
                    }
                    if (!painted)
                        Paint(ref fg, ref bg, n, m.Index, m.Index + m.Length, rfg, rbg);
                }
            }
            catch (RegexMatchTimeoutException) { /* 部分適用のまま */ }
        }

        if (fg is null && bg is null) return Array.Empty<HlSpan>();
        return Coalesce(fg, bg, n);
    }

    private static void Paint(ref uint[]? fg, ref uint[]? bg, int n, int s, int e, uint rfg, uint rbg)
    {
        s = Math.Max(0, s); e = Math.Min(n, e);
        if (rfg != 0)
        {
            fg ??= new uint[n];
            for (int i = s; i < e; i++) fg[i] = rfg;
        }
        if (rbg != 0)
        {
            bg ??= new uint[n];
            for (int i = s; i < e; i++) bg[i] = rbg;
        }
    }

    private static List<HlSpan> Coalesce(uint[]? fg, uint[]? bg, int n)
    {
        var spans = new List<HlSpan>();
        int i = 0;
        while (i < n)
        {
            uint cf = fg?[i] ?? 0, cb = bg?[i] ?? 0;
            if (cf == 0 && cb == 0) { i++; continue; }
            int j = i + 1;
            while (j < n && (fg?[j] ?? 0) == cf && (bg?[j] ?? 0) == cb) j++;
            spans.Add(new HlSpan(i, j - i, cf, cb));
            i = j;
        }
        return spans;
    }

    /// <summary>"#RRGGBB" / "#AARRGGBB" → ARGB packed uint。null/空/不正は 0（＝色なし）。</summary>
    public static uint ParseColor(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        var t = s.AsSpan().Trim();
        if (t.Length > 0 && t[0] == '#') t = t[1..];
        if (t.Length == 6)
        {
            if (uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                return 0xFF000000u | rgb;
        }
        else if (t.Length == 8)
        {
            if (uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
                return argb;
        }
        return 0;
    }
}
