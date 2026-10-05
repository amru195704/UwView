using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UwView.Core;
using UwView.Core.Cli;

namespace UwView.Services;

/// <summary>検索の結果を本体の窓に出す先（画面が渡す）。</summary>
/// <param name="ShowFile">1 本のファイル：開いて（開いていればそのタブで）、結果一覧に出す。引数はファイル・検索語・CLI が探した結果・検索の種類。</param>
/// <param name="ShowMany">複数ファイル：CLI が探した結果（<see cref="MultiHandoff"/>）を、複数ファイルの結果として出す。</param>
public sealed record UvfGuiTarget(Func<string, string, string?, string?, Task> ShowFile, Func<string, Task> ShowMany);

/// <summary>
/// 無料版の「コマンドライン」ダイアログの中身。解釈・展開・実行は uvf のコマンドとまったく同じ
///（uvf が断るものは GUI も同じ言葉で断る。v1.8.0 Finder Scope §5.2）。
/// 検索は、コマンドの <c>-open</c> と同じ道で探して（文字は出さず）、結果を本体の窓に出す（§4）。
/// </summary>
public sealed class UvfCommandBackend(UvfGuiTarget? gui = null) : ICommandLineBackend
{
    public string Tool => "uvf";

    /// <summary>UwView Pro だけの書き方（無料版では実行せず、1 行の案内を出す）。</summary>
    internal static readonly string[] ProOnly =
        ["-uniq", "-sort", "-seq", "-head", "-tail", "-limit", "-C", "-grep", "-w", "-out", "--csv", "-replace",
         "-extract", "--info", "--rebuild", "--force", "--no-line-number"];

    public bool TakesNoFile(IReadOnlyList<string> searchArgs)
        => searchArgs.Count > 0 && searchArgs[0] is "--help" or "-h" or "-help" or "--Help" or "--version" or "-version"
                                                   or "--Version" or "--tune";

    public IReadOnlyList<string> WithoutSearch(string file) => [file, "--files"];

    public (string Ja, string En)? Check(IReadOnlyList<string> argv)
    {
        if (argv.Count == 0) return ("ファイルを指定してください", "Specify the files");
        if (argv.FirstOrDefault(a => ProOnly.Contains(a)) is { } pro)
            return ($"{pro} は UwView Pro の機能です", $"{pro} is a UwView Pro feature");
        if (TakesNoFile(argv)) return null;
        var (_, ja, en) = UvfCli.Parse(argv);
        return ja is null ? null : (ja, en!);
    }

    public CommandFileList Expand(string specification, IgnoreOptions ignore, bool japanese)
        => CommandFileLists.From(FileSet.Expand(specification, ignore: ignore), japanese, Tool);

    public async Task<CommandRunResult> RunAsync(IReadOnlyList<string> argv, CommandOutput output, bool japanese,
                                                 CancellationToken ct)
    {
        // 検索（--json・--files でないもの）は、末尾の -open と同じに走らせて、画面へ渡すものを受け取る
        bool toGui = gui is not null
                     && UvfCli.Parse(argv).Invocation is { Mode: UvfMode.Search, Pattern.Length: > 0, Json: false, ListFiles: false };
        (string? File, string? Pattern, string? Hits, string? Options, string? Many)? launched = null;
        UvfEnvironment env = null!;
        env = new UvfEnvironment
        {
            LaunchGui = toGui
                ? (file, pattern) =>
                {
                    launched = (file, pattern, env.HandoffPath, env.SearchOptionLetters, env.MultiHandoffPath);
                    return true;
                }
                : null,
            StdOut = output.StdOut,
            StdErr = output.StdErr,
            Japanese = japanese,
            AppVersion = AppEdition.Version,
            BuildNumber = AppEdition.BuildNumberOf(typeof(UvfCommandBackend).Assembly),
            SettingsFolder = AppSettings.AppDataFolder,
        };
        int code = await UvfCli.RunAsync(toGui ? [.. argv, "-open"] : argv, env, ct).ConfigureAwait(false);
        if (launched is not { } to || ct.IsCancellationRequested) return new CommandRunResult(code);
        return new CommandRunResult(code, () => to.Many is { } many
            ? gui!.ShowMany(many)
            : gui!.ShowFile(to.File!, to.Pattern!, to.Hits, to.Options));
    }
}
