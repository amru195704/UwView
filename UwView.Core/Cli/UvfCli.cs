using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace UwView.Core.Cli;

/// <summary>uvf の2つの形のどちらか。</summary>
public enum UvfMode
{
    /// <summary><c>uvf -open [ファイル] [検索パターン]</c> … GUI を起動し、あれば開いて検索まで。</summary>
    OpenGui,
    /// <summary><c>uvf ファイル 検索パターン</c> … 検索して stdout に出す。</summary>
    Search,
    /// <summary><c>uvf ファイル 検索パターン -open</c> … 検索結果を GUI で表示する。</summary>
    SearchInGui,
}

/// <param name="IgnoreCase">-i … 大文字小文字を区別しない。</param>
/// <param name="Regex">-E … パターンを正規表現として扱う。</param>
/// <param name="Invert">-v … 当てはまら<b>ない</b>行を出す。</param>
public sealed record UvfInvocation(UvfMode Mode, string? File, string? Pattern,
                                   bool IgnoreCase = false, bool Regex = false, bool Invert = false);

/// <summary>終了コード（grep 互換。UwView Pro の uvp と同じ）。</summary>
public static class UvfExit
{
    public const int Found = 0;
    public const int NotFound = 1;
    public const int Error = 2;
}

/// <summary>uvf が外から受け取るもの（テストで差し替えるため、プロセス環境に直接触らない）。</summary>
public sealed class UvfEnvironment
{
    public required Stream StdOut { get; init; }
    public required TextWriter StdErr { get; init; }

    /// <summary>GUI を起動する（ファイル・検索パターンはどちらも省略可）。起動できなければ false。</summary>
    public Func<string?, string?, bool>? LaunchGui { get; init; }

    public bool Japanese { get; init; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja";

    /// <summary>
    /// 表示に使うコマンド名。UwView Pro の uvp も、ライセンスが無いときはこの2つの形で動く
    /// （画面の未購入時が無料版と同じ動きなのに合わせる）ので、その場合は "uvp" と名乗る。
    /// </summary>
    public string ToolName { get; init; } = "uvf";
}

/// <summary>
/// 無料版 UwView の CLI（オーナー指示 2026-09-14）。<b>次の2つの形だけ</b>を受け付ける。
///
/// 別の実行ファイルは持たない。<b>GUI 本体（UwView）を <c>--uvf</c> 付きで起動すると CLI として動く</b>
/// （配布物の <c>uvf</c> はその呼び出しを書いただけのスクリプト）。CLI 専用の実行ファイルを入れると
/// .NET 一式をもう1つ抱えることになり、配布物が約 30MB 増えるため（オーナー指摘 2026-09-14）。
/// <code>
/// uvf -open [ファイル] [検索パターン]     GUI を起動。ファイルがあれば開き、パターンがあれば検索まで
/// uvf ファイル 検索パターン [-open]       検索。-open なら結果を GUI で、無ければ stdout へ
/// </code>
///
/// 検索は GUI と同じ <see cref="DocumentSession.StartSearchAsync"/>（大文字小文字を区別する、
/// 正規表現ではない普通の検索）。GUI で開くときも GUI 側で同じ検索をやり直すので、結果は一致する。
///
/// 絞り込み・集計などの段、.uwvz の読み書きは UwView Pro（uvp）の機能で、ここには無い。
/// </summary>
public static class UvfCli
{
    /// <summary>GUI へ検索パターンを渡す引数名（GUI 側の App と同じ）。</summary>
    public const string SearchArgument = "--uvf-search";

    public static string Usage(bool ja, string tool = "uvf") => (ja
        ? """
          使い方（この2つの形だけです）:
            uvf -open [ファイル] [検索パターン]   GUI を起動。ファイルがあれば開き、パターンがあれば検索まで
            uvf ファイル 検索パターン [オプション]  検索して結果を出す

          オプション:
            -i        大文字小文字を区別しない
            -E        パターンを正規表現として扱う
            -v        当てはまらない行を出す
            -open     結果を stdout ではなく GUI で表示する（-i/-E/-v とは併用できません）

          出力は「行番号<TAB>本文」。終了コード: 0=見つかった 1=見つからない 2=エラー
          """
        : """
          Usage (only these two forms):
            uvf -open [file] [pattern]       launch the app; open the file and search if given
            uvf file pattern [options]       search and print the results

          Options:
            -i        ignore case
            -E        treat the pattern as a regular expression
            -v        print the lines that do NOT match
            -open     show the results in the app instead of stdout (cannot be combined with -i/-E/-v)

          Output is "line<TAB>text". Exit codes: 0=found 1=not found 2=error
          """).Replace("uvf ", tool + " ");

    public static (UvfInvocation? Invocation, string? ErrorJa, string? ErrorEn) Parse(IReadOnlyList<string> argv)
    {
        if (argv.Count == 0)
            return (null, "引数がありません", "No arguments");

        // 1) uvf -open [ファイル] [検索パターン]
        if (argv[0] == "-open")
        {
            var rest = argv.Skip(1).ToList();
            if (rest.Contains("-open"))
                return (null, "-open は1回だけ書いてください", "Write -open only once");
            if (rest.Count > 2)
                return (null, $"引数が多すぎます: {string.Join(' ', rest.Skip(2))}",
                              $"Too many arguments: {string.Join(' ', rest.Skip(2))}");
            return (new UvfInvocation(UvfMode.OpenGui,
                                      rest.Count > 0 ? rest[0] : null,
                                      rest.Count > 1 ? rest[1] : null), null, null);
        }

        // 2) uvf ファイル 検索パターン [-i] [-E] [-v] [-open]
        var args = argv.ToList();
        bool open = args.Count > 0 && args[^1] == "-open";
        if (open) args.RemoveAt(args.Count - 1);
        if (args.Contains("-open"))
            return (null, "-open は先頭か末尾に書いてください", "Put -open at the start or at the end");

        // 検索の指定（uvp と同じ綴り）。同じものを2回書いても害はないので黙って受ける
        bool icase = false, regex = false, invert = false;
        for (int i = args.Count - 1; i >= 0; i--)
        {
            switch (args[i])
            {
                case "-i": icase = true; break;
                case "-E": regex = true; break;
                case "-v": invert = true; break;
                default: continue;
            }
            args.RemoveAt(i);
        }

        if (args.Count != 2)
            return (null, "ファイルと検索パターンを1つずつ指定してください",
                          "Specify exactly one file and one search pattern");

        // GUI へ渡せるのは素の検索だけ（画面側に -i/-E/-v の受け口が無い）
        if (open && (icase || regex || invert))
            return (null, "-open と -i/-E/-v は一緒に使えません",
                          "-open cannot be combined with -i/-E/-v");

        return (new UvfInvocation(open ? UvfMode.SearchInGui : UvfMode.Search, args[0], args[1],
                                  icase, regex, invert), null, null);
    }

    public static async Task<int> RunAsync(IReadOnlyList<string> argv, UvfEnvironment env, CancellationToken ct = default)
    {
        bool ja = env.Japanese;
        string T(string j, string e) => ja ? j : e;
        string tool = env.ToolName;
        void Err(string m) => env.StdErr.WriteLine(tool + ": " + m);

        var (inv, errJa, errEn) = Parse(argv);
        if (inv is null)
        {
            Err(ja ? errJa! : errEn!);
            env.StdErr.WriteLine(Usage(ja, tool));
            return UvfExit.Error;
        }

        if (inv.File is not null && !File.Exists(inv.File))
        {
            Err(T($"ファイルが見つかりません: {inv.File}", $"File not found: {inv.File}"));
            return UvfExit.Error;
        }
        if (inv.File is not null
            && inv.File.EndsWith(".uwvz", StringComparison.OrdinalIgnoreCase))
        {
            Err(T(".uwvz は UwView Pro のファイルです（無料版では開けません）",
                  ".uwvz files belong to UwView Pro (the free edition cannot open them)"));
            return UvfExit.Error;
        }
        if (inv.Pattern is { Length: 0 })
        {
            Err(T("検索パターンが空です", "The search pattern is empty"));
            return UvfExit.Error;
        }

        if (inv.Mode is UvfMode.OpenGui or UvfMode.SearchInGui)
        {
            string? file = inv.File is null ? null : Path.GetFullPath(inv.File);
            if (env.LaunchGui is null || !env.LaunchGui(file, inv.Pattern))
            {
                Err(T("UwView（GUI）が見つかりません。インストールされているか確認してください",
                      "UwView (the app) was not found. Check that it is installed."));
                return UvfExit.Error;
            }
            return UvfExit.Found;
        }

        // 圧縮ファイルはここでは検索しない（中身を展開せずに探しても当たらない）。
        // GUI なら「テキストに展開して開く」を選べるので、そちらへ案内する
        var probe = CompressedInput.Probe(inv.File!);
        if (probe.IsCompressed || probe.IsRejected)
        {
            Err(T($"{Path.GetFileName(inv.File)} は圧縮ファイルです。{tool} -open {inv.File} で GUI から開いてください",
                  $"{Path.GetFileName(inv.File)} is compressed. Open it in the app with: {tool} -open {inv.File}"));
            return UvfExit.Error;
        }

        try
        {
            return await SearchToStdoutAsync(inv.File!, inv, env, T, Err, ct);
        }
        catch (OperationCanceledException)
        {
            Err(T("中止しました", "Cancelled"));
            return UvfExit.Error;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Err(e.Message);
            return UvfExit.Error;
        }
    }

    /// <summary>
    /// 1回読みの検索（指示書 2026-09-17）。索引を作らず、通しで読みながら
    /// 行番号・一致・本文をまとめて出す。読みは <see cref="SequentialFileByteSource"/>（pread）。
    /// 50GB の実測で 203 秒 → 60 秒台（媒体の帯域に近い）。
    /// -i / -E / -v も同じ1回読みで扱う。
    /// </summary>
    private static async Task<int> SearchToStdoutAsync(
        string path, UvfInvocation inv, UvfEnvironment env,
        Func<string, string, string> t, Action<string> err, CancellationToken ct)
    {
        var options = new SearchOptions(inv.Pattern!, UseRegex: inv.Regex, IgnoreCase: inv.IgnoreCase);
        var watch = Stopwatch.StartNew();
        await using var src = new SequentialFileByteSource(path);
        var detected = EncodingDetector.Detect(src);
        var encoding = detected.Encoding;

        RawGrepOutcome outcome;
        await using (var w = new StreamWriter(env.StdOut, new UTF8Encoding(false), 1 << 16, leaveOpen: true) { NewLine = "\n" })
        {
            var writer = w;
            outcome = await RawGrep.RunAsync(src, detected.BomLength, encoding, options, inv.Invert, Write, ct);

            void Write(long line, ReadOnlySpan<byte> text)
            {
                writer.Write((line + 1).ToString(CultureInfo.InvariantCulture));
                writer.Write('\t');
                writer.WriteLine(encoding.GetString(text));
            }
        }

        env.StdErr.WriteLine(t($"{env.ToolName}: {outcome.Hits:N0} 件（{watch.Elapsed.TotalSeconds:F2} 秒）",
                               $"{env.ToolName}: {outcome.Hits:N0} results ({watch.Elapsed.TotalSeconds:F2}s)"));

        if (outcome.Truncated)
        {
            err(t($"結果が上限（{SearchService.DefaultMaxHits:N0} 件）で打ち切られました。出力は不完全です",
                  $"Results were cut off at the limit ({SearchService.DefaultMaxHits:N0}). The output is incomplete."));
            return UvfExit.Error;
        }
        return outcome.Hits > 0 ? UvfExit.Found : UvfExit.NotFound;
    }

}
