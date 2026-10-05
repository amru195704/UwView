using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UwView.Core;
using UwView.Core.Cli;

namespace UwView.Services;

/// <summary>
/// 無料版の「コマンドライン」ダイアログの中身。解釈・展開・実行は uvf のコマンドとまったく同じ
///（uvf が断るものは GUI も同じ言葉で断る。v1.8.0 Finder Scope §5.2）。
/// </summary>
public sealed class UvfCommandBackend : ICommandLineBackend
{
    public string Tool => "uvf";

    /// <summary>UwView Pro だけの書き方（無料版では実行せず、1 行の案内を出す）。</summary>
    internal static readonly string[] ProOnly =
        ["-uniq", "-sort", "-seq", "-head", "-tail", "-limit", "-C", "-grep", "-w", "-out", "--csv", "-replace",
         "-extract", "--info", "--rebuild", "--force", "--no-line-number"];

    public bool TakesNoFile(IReadOnlyList<string> searchArgs)
        => searchArgs.Count > 0 && searchArgs[0] is "--help" or "-h" or "-help" or "--Help" or "--version" or "-version"
                                                   or "--Version" or "--tune";

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
        var env = new UvfEnvironment
        {
            StdOut = output.StdOut,
            StdErr = output.StdErr,
            Japanese = japanese,
            AppVersion = AppEdition.Version,
            BuildNumber = AppEdition.BuildNumberOf(typeof(UvfCommandBackend).Assembly),
            SettingsFolder = AppSettings.AppDataFolder,
        };
        int code = await UvfCli.RunAsync(argv, env, ct).ConfigureAwait(false);
        return new CommandRunResult(code);
    }
}
