using System.Text;
using System.Runtime.CompilerServices;

namespace UwView.Core.Cli;

/// <summary>
/// 正規表現を全行に当てる大きな検索を、JIT で動く本体（画面のアプリ）に任せるかの判断（mac の NativeAOT の CLI 用）。
///
/// NativeAOT は実行時にコードを作れないので、<c>RegexOptions.Compiled</c> が効かず、正規表現が解釈実行になる。
/// 必須の文字列で候補行を先に絞れない正規表現（<c>-E -v '^ +&lt;'</c> など）は、全行に正規表現を当てるので
/// 約 2 倍遅くなった（3G: 2.75 → 5.58 秒。管理部の全体テスト 2026-09-25 で発覚）。
/// 絞れる正規表現は当てる行が少なく、NativeAOT のままのほうが起動が速い分だけ得。
/// NonBacktracking は速くならなかった（パターンによってはさらに遅い）。
/// </summary>
public static class CompiledRegexRoute
{
    /// <summary>本体に任せる入力の大きさ（展開後の目安。<see cref="SearchTuning.HandoffMinTextBytes"/>）。</summary>
    public const long MinTextBytes = SearchTuning.HandoffMinTextBytes;

    /// <summary>調べる用：これが "1" なら本体に任せない（uvf は UVF_NO_HANDOFF・uvp は UVP_NO_HANDOFF）。</summary>
    public static bool HandoffDisabled(string variable) => Environment.GetEnvironmentVariable(variable) == "1";

    /// <summary>計測用（<c>UV_TRACE=1</c>）。任せたときに、親が子へ「起動した時刻」を渡す環境変数。</summary>
    public const string SpawnAtVariable = "UV_TRACE_SPAWN_AT";

    public static bool Tracing => Environment.GetEnvironmentVariable("UV_TRACE") == "1";

    /// <summary>親（NativeAOT の CLI）：子を起動する直前に時刻を渡す。</summary>
    public static void MarkSpawn(System.Diagnostics.ProcessStartInfo psi)
    {
        if (Tracing) psi.Environment[SpawnAtVariable] = DateTime.UtcNow.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>子（本体の CLI モード）：起動されてから CLI に入るまでの時間を出す（t_spawn）。</summary>
    public static void TraceChildStarted(string tool)
    {
        if (!Tracing || Environment.GetEnvironmentVariable(SpawnAtVariable) is not { } at
            || !long.TryParse(at, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long ticks))
            return;
        Console.Error.WriteLine($"uv_trace: {tool} handoff child_started t_spawn={(DateTime.UtcNow.Ticks - ticks) / 1e7:F3} "
                                + $"jit={System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeCompiled}");
    }

    /// <summary>親：子が終わったときに全体の時間を出す（t_total。この実行ファイルが起動してから）。</summary>
    public static void TraceHandoffFinished(string tool, string gui)
    {
        if (!Tracing) return;
        double total = (DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalSeconds;
        Console.Error.WriteLine($"uv_trace: {tool} handoff=yes gui={gui} t_total={total:F3}");
    }

    /// <summary>この実行ファイルは正規表現をコンパイルできない（NativeAOT）。</summary>
    public static bool Interpreted => !RuntimeFeature.IsDynamicCodeCompiled;

    /// <summary>
    /// 必須の文字列で候補行を絞れない正規表現か（全行に正規表現を当てることになる）。
    /// 短い手がかり（<see cref="RegexClues"/>・大小を区別するときだけ使う）で絞れる式は、当てる行が減るので任せない。
    /// 任せると、本体の起動と、ファイルごとの正規表現のコンパイルが乗るだけだった（カーネル 8.6 万本で 2 回目 12.9 秒。2026-10-04）。
    /// 判断は実際の検索と同じところ（<see cref="SearchPlan.Method"/>）で行う。別の規則で見ると、
    /// <c>-i 'foo|bar'</c> <c>-i 東京</c> のように実際は全行に当てる式を「絞れる」と取り違えた（外部レビュー 2026-10-04 の指摘5）。
    /// </summary>
    public static bool ScansEveryLine(string pattern, bool ignoreCase = false)
        => PlanOf(pattern, ignoreCase).Method == SearchMethod.EveryLine;

    /// <summary>
    /// 1 本の大きな入力に短い手がかり（1 文字）で絞る式を当てるとき、手がかりのある行が多くて絞れない（＝全行に当てることになる）か。
    /// 先頭の <paramref name="sampleBytes"/> を読んで数える（<see cref="ClueWatch"/> と同じ割合）。多ければ本体（JIT）に任せる。
    /// 10G の <c>[0-9]{3}-[0-9]{4}"</c> は 3 割の行に <c>-</c> があり、全行に当てると AOT 5.12 秒・本体 3.87 秒。
    /// 手がかりで絞れる式まで任せると、本体の起動の分だけ遅かった（1G の <c>[ぁ-ん]{3,}</c> 0.150 → 0.361 秒。grix テスト 2026-10-05）。
    /// 多数ファイルは任せない（ファイルを開く手間が主で、本体の起動の分だけ遅い。カーネル 8.6 万本：AOT 4.9 秒・本体 5.1 秒）。
    /// uvp は使わない（10 並列で解釈実行の遅れが隠れる。10G：AOT 2.18 秒・本体 2.21 秒）。圧縮ファイルは読まずに false。
    /// </summary>
    public static bool ShortClueIsDense(string path, SearchPlan plan, int sampleBytes = 8 << 20)
    {
        if (!plan.UsesShortClue || plan.NewClueFinder() is not { } clues) return false;
        try
        {
            if (CompressedInput.Probe(path).Kind != CompressedKind.None) return false;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
            byte[] buf = new byte[(int)Math.Min(sampleBytes, fs.Length)];
            int got = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
            var region = buf.AsSpan(0, got);
            int last = region.LastIndexOf((byte)'\n');
            if (last < 0) return false;
            region = region[..(last + 1)];
            long lines = NewlineCounter.Count(region), candidates = 0;
            if (lines < 1000) return false;                     // 数えるには短すぎる
            clues.Reset();
            for (int from = 0; from < region.Length; )
            {
                int at = clues.IndexOf(region, from);
                if (at < 0) break;
                candidates++;
                int nl = region[at..].IndexOf((byte)'\n');
                from = at + nl + 1;
            }
            return candidates > lines * SearchTuning.MaxShortClueLineShare;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    private static SearchPlan PlanOf(string pattern, bool ignoreCase)
        => PreparedSearch.Create(new SearchOptions(pattern, UseRegex: true, IgnoreCase: ignoreCase)).For(Encoding.UTF8);

    /// <summary>任せる判断のために広げた入力（任せなかったとき、同じプロセスの CLI がもう一度広げずに使う）。</summary>
    public sealed record ExpandedInput(string Specification, FileSet.Result Result);

    /// <summary>uvf の引数から判断する。</summary>
    public static bool ForUvf(IReadOnlyList<string> argv) => ForUvf(argv, out _);

    /// <summary>
    /// uvf の引数から判断する。複数ファイルの指定を広げたら <paramref name="expanded"/> に返す
    /// （任せないときは、同じプロセスの CLI がそれを使う。任せたときは、子が自分で広げ直す。親と子で一覧は受け渡さない）。
    /// 広げるときは、検索と同じ除外（<c>--no-ignore</c>・<c>--ignore-file</c>）を使う。
    /// </summary>
    public static bool ForUvf(IReadOnlyList<string> argv, out ExpandedInput? expanded)
    {
        expanded = null;
        if (!Interpreted || HandoffDisabled("UVF_NO_HANDOFF")) return false;
        var (inv, _, _) = UvfCli.Parse(argv);
        if (inv is not { Regex: true, Pattern: { } pattern, File: { Length: > 0 } file }) return false;
        var plan = PlanOf(pattern, inv.IgnoreCase);
        bool single = !FileSet.IsMultiple(file);
        if (single && plan.Method != SearchMethod.EveryLine)
            return Estimate(file) >= MinTextBytes && ShortClueIsDense(file, plan);   // 大きいときだけ先頭を読む
        if (plan.Method != SearchMethod.EveryLine) return false;
        if (single) return Estimate(file) >= MinTextBytes;
        expanded = new ExpandedInput(file, FileSet.Expand(file, ignore: inv.Ignore));
        return TextBytes(expanded.Result.Files) >= MinTextBytes;
    }

    /// <summary>
    /// 入力（1ファイル・複数ファイルの指定）の展開後のおおよその大きさ。<paramref name="sizeOf"/> が null を返したものは
    /// <see cref="Estimate"/> で見積もる。目安を超えたところで数えるのをやめる。
    /// </summary>
    public static long TextBytes(string specification, Func<string, long?>? sizeOf = null)
        => TextBytes(FileSet.IsMultiple(specification) ? FileSet.Expand(specification).Files : [specification], sizeOf);

    /// <summary>広げた一覧の、展開後のおおよその大きさ（目安を超えたところで数えるのをやめる）。</summary>
    public static long TextBytes(IReadOnlyList<string> files, Func<string, long?>? sizeOf = null)
    {
        long total = 0;
        foreach (string file in files)
        {
            total += sizeOf?.Invoke(file) ?? Estimate(file);
            if (total >= MinTextBytes) break;
        }
        return total;
    }

    /// <summary>1ファイルの展開後のおおよその大きさ。圧縮は大きさの 8 倍とみる（テキストはおおむね 1/8〜1/10 に縮む）。</summary>
    public static long Estimate(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return 0;
            return CompressedInput.Probe(path).Kind == CompressedKind.None ? info.Length : info.Length * 8;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }
}
