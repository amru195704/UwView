using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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

    /// <summary>
    /// -open のとき、CLI が見つけた結果を書いたファイル（<see cref="CliHandoff"/>）。
    /// <see cref="LaunchGui"/> はこれが入っていれば GUI へ一緒に渡す（画面が検索し直さずに済む）。
    /// UwView Pro の uvp は自前の受け渡しを持っているので、こちらは見ない。
    /// </summary>
    public string? HandoffPath { get; set; }

    /// <summary>-open のとき、検索の種類（<c>i</c>/<c>E</c>/<c>v</c> の並び）。<see cref="LaunchGui"/> が GUI へ渡す。</summary>
    public string? SearchOptionLetters { get; set; }

    public bool Japanese { get; init; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja";

    /// <summary><c>--version</c> で出す版数（本体が渡す。uvp と同じ形で出す）。</summary>
    public string AppVersion { get; init; } = "";

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
    /// <summary>索引の目印の間隔（画面の既定と同じ。<see cref="LineDocument"/> の blockLines）。</summary>
    private const int IndexBlockLines = 256;

    /// <summary>GUI へ検索パターンを渡す引数名（GUI 側の App と同じ）。</summary>
    public const string SearchArgument = "--uvf-search";

    /// <summary>
    /// GUI へ検索の種類を渡す引数名。値は <c>i</c>（大小無視）<c>E</c>（正規表現）<c>v</c>（含まない）の並び。
    /// 受け渡しが使えなかったとき、画面が<b>違う条件で検索し直さない</b>ために要る。
    /// </summary>
    public const string OptionsArgument = "--uvf-search-opts";

    /// <summary>この呼び出しの種類を <see cref="OptionsArgument"/> の値にする。</summary>
    public static string OptionLetters(UvfInvocation inv)
        => (inv.IgnoreCase ? "i" : "") + (inv.Regex ? "E" : "") + (inv.Invert ? "v" : "");

    public static string Usage(bool ja, string tool = "uvf") => (ja
        ? """
          使い方（この2つの形だけです）:
            uvf -open [ファイル] [検索パターン]   GUI を起動。ファイルがあれば開き、パターンがあれば検索まで
            uvf ファイル 検索パターン [オプション]  検索して結果を出す

          オプション:
            -i        大文字小文字を区別しない
            -E        パターンを正規表現として扱う
            -v        当てはまらない行を出す
            -open     結果を stdout ではなく GUI で表示する（-i/-E/-v と併用できます）

          そのほか:
            uvf --version   版数を出す
            uvf --help      この使い方を出す

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
            -open     show the results in the app instead of stdout (can be combined with -i/-E/-v)

          Also:
            uvf --version   print the version
            uvf --help      print this usage

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

        return (new UvfInvocation(open ? UvfMode.SearchInGui : UvfMode.Search, args[0], args[1],
                                  icase, regex, invert), null, null);
    }

    public static async Task<int> RunAsync(IReadOnlyList<string> argv, UvfEnvironment env, CancellationToken ct = default)
    {
        bool ja = env.Japanese;
        string T(string j, string e) => ja ? j : e;
        string tool = env.ToolName;
        void Err(string m) => env.StdErr.WriteLine(tool + ": " + m);

        // --version / --help（uvp と同じ綴り。無かったので入れた。オーナー指摘 2026-09-21）
        if (argv.Count == 1)
        {
            switch (argv[0])
            {
                case "--version" or "-version" or "--Version":
                    await WriteLineAsync(env.StdOut, $"{tool} {(env.AppVersion.Length > 0 ? env.AppVersion : "?")}", ct);
                    return UvfExit.Found;
                case "--help" or "-h" or "-help" or "--Help":
                    await WriteLineAsync(env.StdOut, Usage(ja, tool), ct);
                    return UvfExit.Found;
            }
        }

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

        // 正規表現の書き間違いは、探し始める前にここで伝える。
        // -open の結果集めは下の try の外で走るので、そちらでは例外が素通りしていた
        //（再レビュー 2026-09-19 の指摘F。画面も起動しないまま終了コード2で返す）
        if (inv.Regex && inv.Pattern is { Length: > 0 } pattern)
        {
            try
            {
                _ = SearchService.BuildRegex(new SearchOptions(pattern, UseRegex: true, IgnoreCase: inv.IgnoreCase));
            }
            catch (Exception e) when (e is RegexParseException or ArgumentException)
            {
                Err(T($"正規表現が正しくありません: {e.Message}", $"Invalid regular expression: {e.Message}"));
                return UvfExit.Error;
            }
        }

        if (inv.Mode is UvfMode.OpenGui or UvfMode.SearchInGui)
        {
            string? file = inv.File is null ? null : Path.GetFullPath(inv.File);

            // 検索まで頼まれているなら、ここで探してから渡す（画面が同じ検索をやり直さずに済む。
            // 50GB なら丸ごと読み直す時間がそのまま浮く。オーナー指示 2026-09-18）。
            // -i / -E / -v もここで解決するので、そのまま -open に付けられる（同 2026-09-18）
            if (inv.Mode == UvfMode.SearchInGui && file is not null && inv.Pattern is { Length: > 0 })
            {
                env.SearchOptionLetters = OptionLetters(inv);
                var check = CompressedInput.Probe(file);
                if (!check.IsCompressed && !check.IsRejected)
                    env.HandoffPath = await CollectForGuiAsync(file, inv, ct);
            }

            if (env.LaunchGui is null || !env.LaunchGui(file, inv.Pattern))
            {
                if (env.HandoffPath is { } stale) { try { File.Delete(stale); } catch (IOException) { } }
                Err(T("UwView（GUI）が見つかりません。インストールされているか確認してください",
                      "UwView (the app) was not found. Check that it is installed."));
                return UvfExit.Error;
            }
            return UvfExit.Found;
        }

        // gz は展開しながら探す（gzip -dc | grep と同じ。オーナー指示 2026-09-19）。
        // 読めない gz・zip は、理由を添えて断る（「圧縮ファイルです」だけだと、
        // 中身が gzip でない .gz にも同じ文が出て分からない。オーナー指示 2026-09-21）
        var probe = CompressedInput.Probe(inv.File!);
        bool gzip = probe is { Kind: CompressedKind.Gzip, IsRejected: false };
        if (!gzip && (probe.IsCompressed || probe.IsRejected))
        {
            Err(RejectText(probe.Reject, inv.File!, tool, T));
            return UvfExit.Error;
        }

        try
        {
            return await SearchToStdoutAsync(inv.File!, gzip, inv, env, T, Err, ct);
        }
        catch (InvalidDataException) when (gzip)
        {
            // 文字コードの判定など、検索を始める前に読めなくなった場合
            Err(GzNotReadable(inv.File!, T, partialOutput: false));
            return UvfExit.Error;
        }
        catch (OperationCanceledException)
        {
            Err(T("中止しました", "Cancelled"));
            return UvfExit.Error;
        }
        catch (RegexParseException e)
        {
            // 正規表現の書き間違い。異常終了ではなく、書き方の誤りとして伝える
            //（ソースレビュー 2026-09-19 の指摘8）
            Err(T($"正規表現が正しくありません: {e.Message}", $"Invalid regular expression: {e.Message}"));
            return UvfExit.Error;
        }
        catch (RegexMatchTimeoutException)
        {
            Err(T("正規表現の処理に時間がかかりすぎました（式を見直してください）",
                  "The regular expression took too long (try a simpler pattern)"));
            return UvfExit.Error;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Err(e.Message);
            return UvfExit.Error;
        }
    }

    private static async Task WriteLineAsync(Stream stdout, string text, CancellationToken ct)
    {
        await using var w = new StreamWriter(stdout, new UTF8Encoding(false), 1 << 12, leaveOpen: true) { NewLine = "\n" };
        await w.WriteLineAsync(text.AsMemory(), ct);
    }

    /// <summary>
    /// 受け付けられない入力の案内（理由ごと）。<b>「壊れています」とは言わない</b>
    ///（別形式・作り方の違いのことが多く、元のファイルを消されかねない。オーナー指示 2026-09-21）。
    /// </summary>
    private static string RejectText(CompressedReject reject, string path, string tool,
                                     Func<string, string, string> t)
    {
        string name = Path.GetFileName(path);
        return reject switch
        {
            CompressedReject.NotGzip => t(
                $"{name} は gzip として読めません（名前は .gz ですが、中身が gzip の形ではありません）。"
                + "別の形式かもしれません。元のファイルは消さないでください。",
                $"{name} cannot be read as gzip (the name ends with .gz but the contents are not gzip). "
                + "It may be another format. Please keep the original file."),
            CompressedReject.NotZip => t(
                $"{name} は zip として読めません（名前は .zip ですが、中身が zip の形ではありません）。"
                + "別の形式かもしれません。元のファイルは消さないでください。",
                $"{name} cannot be read as zip (the name ends with .zip but the contents are not zip). "
                + "It may be another format. Please keep the original file."),
            CompressedReject.TarArchive => t(
                $"{name} は複数のファイルをまとめた tar です。扱えるのは「1つのテキストを gzip したもの」だけです"
                + "（先に展開してください）。",
                $"{name} is a tar archive holding several files. This tool handles a single gzip-compressed text file "
                + "(please extract it first)."),
            CompressedReject.NestedGzip => t(
                $"{name} は gzip が二重にかかっています。1回だけ gzip したものを扱えます"
                + "（一度 gunzip してから試してください）。",
                $"{name} is gzip-compressed twice. This tool handles a file compressed once "
                + "(please gunzip it once first)."),
            CompressedReject.Corrupt => t(
                $"{name} は gzip として読めません（先頭を展開できませんでした）。"
                + "別の形式か、途中で切れている可能性があります。元のファイルは消さないでください。",
                $"{name} cannot be read as gzip (the beginning could not be decompressed). "
                + "It may be another format, or cut short. Please keep the original file."),
            CompressedReject.Unreadable => t(
                $"{name} を読めませんでした（アクセスできないか、使用中かもしれません）。",
                $"{name} could not be read (no access, or the file is in use)."),
            // 理由が無い＝形としては正しい圧縮ファイル（いまは zip）。画面へ案内する
            _ => t($"{name} は zip です。いまは直接検索できません。{tool} -open {path} で画面から開いてください",
                   $"{name} is a zip file. Searching it directly is not supported yet. "
                   + $"Open it in the app with: {tool} -open {path}"),
        };
    }

    /// <summary>
    /// gz を最後まで読めなかったときの案内。
    ///
    /// <b>「壊れています」とは言わない。</b>ここに来るのは「途中で切れている」か
    /// 「1つのファイルを1回 gzip したもの、という想定と違う作り方」がほとんどで、
    /// ファイル自体は正しいことが多い。壊れていると言われた利用者が元のファイルを
    /// 消してしまう恐れがあるので、そう言い切らず、消さないよう添える（オーナー指示 2026-09-21）。
    /// </summary>
    private static string GzNotReadable(string path, Func<string, string, string> t, bool partialOutput)
    {
        string name = Path.GetFileName(path);
        string ja = $"{name} を最後まで読めませんでした（gzip として読み切れません）。"
                    + "途中で切れているか、作り方が想定と違うファイルかもしれません。";
        string en = $"{name} could not be read to the end (it could not be read through as gzip). "
                    + "It may be cut short, or made in a way this tool does not expect.";
        if (partialOutput)
        {
            ja += "ここまでの出力は不完全です。";
            en += " The output so far is incomplete.";
        }
        return t(ja + "元のファイルは消さないでください。", en + " Please keep the original file.");
    }

    /// <summary>
    /// -open のときに、CLI 側で検索して結果を画面へ渡せる形にする（見つからなければ null＝画面が検索する）。
    /// 出すのは行頭の位置だけなので、50GB でも数MB にしかならない。
    /// </summary>
    private static async Task<string?> CollectForGuiAsync(string path, UvfInvocation inv, CancellationToken ct)
    {
        try
        {
            await using var src = new SequentialFileByteSource(path);
            var detected = EncodingDetector.Detect(src);
            var options = new SearchOptions(inv.Pattern!, UseRegex: inv.Regex, IgnoreCase: inv.IgnoreCase);

            var hits = new List<long>();
            var lines = new List<long>();
            // 検索のついでに索引の目印も集める（画面がファイルを読み直さずに済む）。
            // ただし走査は 0x0A を区切りとするので、それで正しい文字コード・改行のときだけ渡す
            //（UTF-16 や CR 単独は画面側で作らせる。ソースレビュー 2026-09-19 の指摘2）
            var sep = LineSeparator.For(detected.Encoding, EncodingDetector.DetectNewline(src));
            bool canHandOverIndex = sep is { UnitSize: 1, Value: (byte)'\n' };
            var marks = canHandOverIndex ? new RawGrep.IndexMarks(IndexBlockLines) : null;
            var outcome = await RawGrep.RunAsync(src, detected.BomLength, detected.Encoding, options, inv.Invert,
                                                 (line, lineStart, _) => { hits.Add(lineStart); lines.Add(line); },
                                                 ct, marks);
            return new CliHandoff(inv.Pattern!, inv.IgnoreCase, inv.Regex, inv.Invert,
                                  src.Length, outcome.Truncated, [.. hits], [.. lines],
                                  marks is null ? 0 : IndexBlockLines, marks is null ? null : [.. marks.Marks],
                                  marks?.NewlineCount ?? 0, marks?.LastByte ?? 0).WriteTemp();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException
                                       or OperationCanceledException)
        {
            return null;   // 渡せなくても困らない（画面が普通に検索する）
        }
    }

    /// <summary>
    /// 1回読みの検索（指示書 2026-09-17）。索引を作らず、通しで読みながら
    /// 行番号・一致・本文をまとめて出す。読みは <see cref="SequentialFileByteSource"/>（pread）。
    /// 50GB の実測で 203 秒 → 60 秒台（媒体の帯域に近い）。
    /// -i / -E / -v も同じ1回読みで扱う。
    /// </summary>
    private static async Task<int> SearchToStdoutAsync(
        string path, bool gzip, UvfInvocation inv, UvfEnvironment env,
        Func<string, string, string> t, Action<string> err, CancellationToken ct)
    {
        var options = new SearchOptions(inv.Pattern!, UseRegex: inv.Regex, IgnoreCase: inv.IgnoreCase);
        var watch = Stopwatch.StartNew();
        await using IByteSource src = gzip ? new GzipStreamByteSource(path) : new SequentialFileByteSource(path);
        var detected = EncodingDetector.Detect(src);
        var encoding = detected.Encoding;

        RawGrepOutcome outcome;
        await using (var w = new StreamWriter(env.StdOut, new UTF8Encoding(false), 1 << 16, leaveOpen: true) { NewLine = "\n" })
        {
            var writer = w;
            try
            {
                outcome = await RawGrep.RunAsync(src, detected.BomLength, encoding, options, inv.Invert, Write, ct);
            }
            catch (InvalidDataException e) when (gzip)
            {
                await w.FlushAsync(ct);
                err(GzNotReadable(path, t, partialOutput: true));
                return UvfExit.Error;
            }

            void Write(long line, long _, ReadOnlySpan<byte> text)
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
