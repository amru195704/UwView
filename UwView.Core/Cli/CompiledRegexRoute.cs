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
    /// <summary>
    /// 本体に任せる入力の大きさ（展開後の目安）。NativeAOT で余計にかかる時間は約 1.1 ミリ秒/MB（3G 実測）で、
    /// 本体の起動（約 0.15 秒）と釣り合うのが 130MB 前後。余裕をみてこの大きさから。
    /// </summary>
    public const long MinTextBytes = 256L << 20;

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
    /// 判断は実際の検索が使う手がかり（<see cref="SearchService.CreatePrefilter"/>・<see cref="SearchService.IcaseClue"/>）で行う。
    /// 別の規則で見ると、<c>-i 'foo|bar'</c> <c>-i 東京</c> のように実際は全行に当てる式を「絞れる」と取り違えた（外部レビュー 2026-10-04 の指摘5）。
    /// </summary>
    public static bool ScansEveryLine(string pattern, bool ignoreCase = false)
    {
        var options = new SearchOptions(pattern, UseRegex: true, IgnoreCase: ignoreCase);
        return ignoreCase
            ? SearchService.IcaseClue(options, Encoding.UTF8).Length == 0
            : SearchService.CreatePrefilter(options, Encoding.UTF8) is null;
    }

    /// <summary>uvf の引数から判断する。</summary>
    public static bool ForUvf(IReadOnlyList<string> argv)
    {
        if (!Interpreted || HandoffDisabled("UVF_NO_HANDOFF")) return false;
        var (inv, _, _) = UvfCli.Parse(argv);
        return inv is { Regex: true, Pattern: { } pattern, File: { Length: > 0 } file }
               && ScansEveryLine(pattern, inv.IgnoreCase)
               && TextBytes(file) >= MinTextBytes;
    }

    /// <summary>
    /// 入力（1ファイル・複数ファイルの指定）の展開後のおおよその大きさ。<paramref name="sizeOf"/> が null を返したものは
    /// <see cref="Estimate"/> で見積もる。目安を超えたところで数えるのをやめる。
    /// </summary>
    public static long TextBytes(string specification, Func<string, long?>? sizeOf = null)
    {
        IReadOnlyList<string> files = FileSet.IsMultiple(specification)
            ? FileSet.Expand(specification).Files
            : [specification];
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
