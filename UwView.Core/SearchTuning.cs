namespace UwView.Core;

/// <summary>
/// 検索の<b>速さの都合</b>で決めている値と選び方（外部レビュー 2026-10-04「整理の進め方」5）。
///
/// ここにあるものは、変えても<b>答えは変わらない</b>（速さだけが変わる）。答えを守る条件
///（置換文字・サロゲート・読めない構文・文字コード・大小無視の k）は <see cref="RegexSyntax"/>・<see cref="RegexLiterals"/>・
/// <see cref="RegexClues"/>・<see cref="SearchPlan"/> の側にあり、ここでは扱わない。
///
/// 「必ず含む手がかりがある」ことと「手がかりのある行が少ない」ことは別。ここの値は実際のデータでの見込みで、
/// 手がかりで実際にどれだけ絞れたかは <c>UV_TRACE=1</c> の <c>clue_lines</c>（手がかりのあった行）と <c>match_lines</c> で測れる。
/// </summary>
public static class SearchTuning
{
    /// <summary>この長さ未満の必須リテラルは使わない（1 文字はほぼ全行に当たる。1 文字は <see cref="RegexClues"/> が選んで使う）。</summary>
    public const int MinLiteralLength = 2;

    /// <summary>必須リテラルの候補の数の上限（多すぎると 1 つずつ探す手間が勝つ）。</summary>
    public const int MaxLiteralAlternatives = 16;

    /// <summary>短い手がかりの候補の数の上限（同上）。</summary>
    public const int MaxShortClueNeedles = 4;

    /// <summary>
    /// 短い手がかりのある行がこの割合を超えたら、手がかりをやめて全行に当てる（<see cref="ClueWatch"/>）。
    /// 手がかりのある行が多いと、行を探し直す手間が全行に当てるより重くなる
    /// （10G の <c>[0-9]{3}-[0-9]{4}"</c> は 3 割の行に <c>-</c> があり、全行 3.87 秒・手がかり 4.86 秒。
    /// 3G の 1 割（<c>[0-9]{4}-[0-9]{2}</c>）は手がかり 1.38 秒・全行 3.08 秒。どちらも本体（JIT）。2026-10-05）。
    /// </summary>
    public const double MaxShortClueLineShare = 0.25;

    /// <summary>割合を決めるまでに見る行の数（決めるのは 1 回だけ）。</summary>
    public const long ShortClueSampleLines = 100_000;

    /// <summary>
    /// 本体（JIT）に任せる入力の大きさ（展開後の目安。mac の NativeAOT の CLI）。NativeAOT で余計にかかる時間は
    /// 約 1.1 ミリ秒/MB（3G 実測）で、本体の起動（約 0.15 秒）と釣り合うのが 130MB 前後。余裕をみてこの大きさから。
    /// </summary>
    public const long HandoffMinTextBytes = 256L << 20;

    /// <summary>
    /// XML・ログ・ソースでほぼどの行にもあるバイト（1 バイトの手がかりにしない）。絞れずに探す手間だけが乗る
    ///（OSM の <c>-v '^ +&lt;'</c> で 1.1 倍遅くなった。Rust の regex も、ありふれたバイトでは前置きを使わない）。
    /// </summary>
    public static ReadOnlySpan<byte> VeryCommonBytes => " \t<>\"=/0123456789etaoinsr"u8;

    /// <summary>必須リテラルの集合を使うか（どれも十分に長く、数が多すぎない）。</summary>
    public static bool AcceptLiterals(IReadOnlyList<string> literals)
        => literals.Count is > 0 and <= MaxLiteralAlternatives && literals.All(s => s.Length >= MinLiteralLength);

    /// <summary>
    /// 短い手がかりの候補（どれを使っても取りこぼさない集合の並び）から 1 つ選ぶ。ありふれた 1 バイトだけの集合は使わない。
    /// いちばん長いバイト列を含むもの、次に候補の少ないものを選ぶ。使えるものが無ければ null。
    /// </summary>
    public static IReadOnlyList<byte[]>? ChooseShortClue(IEnumerable<List<byte[]>> candidates)
        => candidates
            .Where(set => set.Count is > 0 and <= MaxShortClueNeedles && !set.All(IsTooCommon))
            .OrderByDescending(set => set.Max(n => n.Length))
            .ThenBy(set => set.Count)
            .FirstOrDefault();

    private static bool IsTooCommon(byte[] needle) => needle.Length == 1 && VeryCommonBytes.IndexOf(needle[0]) >= 0;
}
