using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using UwView.Core.Cli;
using UwView.Localization;
using UwView.Services;

namespace UwView.ViewModels;

/// <summary>
/// 「コマンドライン」ダイアログ（v1.8.0 Finder Scope）の中身。画面を出さずに確かめられるよう、
/// 分ける・解釈・展開・実行・出力はすべてここに置く（§8）。uvf・uvp の違いは <see cref="ICommandLineBackend"/>。
///
/// ［検索実行］は「基準フォルダーに <c>cd</c> して、コマンドの行を打った」のと同じ（§7.1）。
/// </summary>
public sealed partial class CommandLineViewModel : ObservableObject
{
    /// <summary>出力欄に見せる行の上限（全部は［保存…］で。§3.4）。</summary>
    public const int MaxOutputLines = 1_000_000;

    /// <summary>これを超える本数は警告する（止めない。§3.3）。</summary>
    public const int ManyFiles = 1000;

    private readonly ICommandLineBackend _backend;
    private CancellationTokenSource? _running;
    private string? _outputFile;

    public CommandLineViewModel(ICommandLineBackend backend, string? baseFolder = null)
    {
        _backend = backend;
        _baseFolder = baseFolder is { Length: > 0 } && Directory.Exists(baseFolder)
            ? baseFolder
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Refresh();
    }

    private static Localizer L => Localizer.Instance;
    private static bool Japanese => L.Culture.TwoLetterISOLanguageName == "ja";

    public string Tool => _backend.Tool;

    [ObservableProperty] private string _baseFolder;
    [ObservableProperty] private string _filePattern = "";
    [ObservableProperty] private string _searchPattern = "";
    [ObservableProperty] private bool _followIgnore = true;

    /// <summary>2 つの欄から作った、コマンドとそっくり同じ 1 行（§3.2）。</summary>
    [ObservableProperty] private string _commandText = "";

    /// <summary>書き方の誤り（コマンドと同じ言葉）。無ければ空。</summary>
    [ObservableProperty] private string _errorText = "";

    [ObservableProperty] private IReadOnlyList<CommandFileRow> _files = [];
    [ObservableProperty] private string _filesSummary = "";

    /// <summary>出力欄の行（先頭 <see cref="MaxOutputLines"/> 行まで）。</summary>
    [ObservableProperty] private IReadOnlyList<string> _outputLines = [];

    /// <summary>コマンドが標準エラーに出した知らせ（色を変えて出す）。</summary>
    [ObservableProperty] private string _notice = "";

    /// <summary>0＝ファイル、1＝出力。</summary>
    [ObservableProperty] private int _selectedTab;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _status = "";

    /// <summary>出力が上限を超えて、先頭だけを見せているか。</summary>
    [ObservableProperty] private bool _outputTruncated;

    /// <summary>［検索実行］を押せるか（誤りが無く、走っていない）。</summary>
    public bool CanRun => ErrorText.Length == 0 && !IsRunning;

    partial void OnBaseFolderChanged(string value) => Refresh();
    partial void OnFilePatternChanged(string value) => Refresh();
    partial void OnSearchPatternChanged(string value) => Refresh();
    partial void OnFollowIgnoreChanged(bool value) => Refresh();
    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(CanRun));
    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(CanRun));

    /// <summary>
    /// ファイル指定パターンを 1 つの引数にする。引用符は要らない（付けても外して読む）。
    /// 空白を含む指定（<c>a.log b.log</c>）もコマンドの第 1 引数と同じく 1 つのまま渡す。
    /// </summary>
    private string? FileArgument()
    {
        string text = FilePattern.Trim();
        if (text.Length == 0) return null;
        if (text[0] is '\'' or '"' && CommandLineSplitter.Split(text) is { Error: null, Args: [string one] }) return one;
        return text;
    }

    /// <summary>2 つの欄から、コマンドの引数の列（argv）を作る（§7.2）。誤りがあれば null と言葉を返す。</summary>
    public (IReadOnlyList<string>? Argv, (string Ja, string En)? Error) BuildArgv()
    {
        var split = CommandLineSplitter.Split(SearchPattern);
        if (split.Error is { } bad) return (null, bad);
        var argv = new List<string>();
        var rest = split.Args;
        // uvp convert / cat：コマンドではファイルより前に書く語
        if (rest.Count > 0 && _backend.IsSubcommand(rest[0])) { argv.Add(rest[0]); rest = [.. rest.Skip(1)]; }
        if (!_backend.TakesNoFile(rest))
        {
            if (FileArgument() is not { } file) return (null, ("ファイルを指定してください", "Specify the files"));
            argv.Add(file);
        }
        argv.AddRange(rest);
        // ☑ を外したら --no-ignore と同じ（検索パターンにも書けば、両方ともオフ）
        if (!FollowIgnore && !argv.Contains("--no-ignore") && !_backend.TakesNoFile(rest)) argv.Add("--no-ignore");
        return (argv, null);
    }

    /// <summary>実際に走らせる引数（<c>-open</c> は画面の中では要らないので外す。§5.1）。</summary>
    private static IReadOnlyList<string> Executable(IReadOnlyList<string> argv)
        => argv.Contains("-open") ? [.. argv.Where(a => a != "-open")] : argv;

    private void Refresh()
    {
        var (argv, error) = BuildArgv();
        CommandText = argv is null ? Tool : Tool + " " + CommandLineSplitter.Join(argv);
        error ??= argv is null ? null : _backend.Check(Executable(argv));
        ErrorText = error is { } e ? (Japanese ? e.Ja : e.En) : "";
    }

    /// <summary>除外の設定（☑ と検索パターンの <c>--no-ignore</c>・<c>--ignore-file</c>）。</summary>
    private IgnoreOptions IgnoreFor(IReadOnlyList<string> argv)
    {
        var (ignore, _, _, _) = IgnoreOptions.Take(argv);
        ignore ??= IgnoreOptions.Default;
        return FollowIgnore ? ignore : ignore with { Enabled = false };
    }

    /// <summary>［ファイル展開］：ファイル指定パターンを広げて一覧にする（<c>--files</c> と同じ。§3.3）。</summary>
    public async Task ExpandAsync()
    {
        var (argv, _) = BuildArgv();
        if (FileArgument() is not { } spec) { Files = []; FilesSummary = L["CmdNoFiles"]; return; }
        var ignore = IgnoreFor(Executable(argv ?? []));
        var list = await WorkingFolder.Run(BaseFolder, () => _backend.Expand(spec, ignore, Japanese));
        ShowFiles(list);
    }

    private void ShowFiles(CommandFileList list)
    {
        Files = list.Rows;
        long total = list.Rows.Sum(r => r.Size ?? 0);
        var parts = new List<string> { L.Format("CmdFilesCount", list.Rows.Count.ToString("N0", L.Culture), FormatSize(total)) };
        if (list.Ignored > 0) parts.Add(L.Format("CmdFilesIgnored", list.Ignored.ToString("N0", L.Culture)));
        if (list.Rows.Count > ManyFiles) parts.Add(L.Format("CmdFilesMany", ManyFiles.ToString("N0", L.Culture)));
        if (list.Rows.Count == 0) parts.Add(L["CmdNoFiles"]);
        parts.AddRange(list.Notices);
        FilesSummary = string.Join(" ・ ", parts);
        SelectedTab = 0;
    }

    /// <summary>
    /// ［検索実行］：基準フォルダーを作業フォルダーにして、コマンドを同じプロセスの中で走らせる。
    /// 文字の出力は出力欄、窓に出すものは呼び手（画面）が <see cref="CommandRunResult.ShowInGui"/> で出す。
    /// </summary>
    public async Task<CommandRunResult?> RunAsync()
    {
        var (typed, _) = BuildArgv();
        if (typed is null || !CanRun) return null;
        var argv = Executable(typed);
        bool openIgnored = argv.Count != typed.Count;
        using var cts = new CancellationTokenSource();
        _running = cts;
        IsRunning = true;
        Status = L["CmdRunning"];
        var progress = new CommandProgress();
        using var ticking = new CancellationTokenSource();
        _ = TickAsync(progress, ticking.Token);
        string outFile = Path.Combine(Path.GetTempPath(), $"uv-cmd-{Guid.NewGuid():N}.out");
        var stderr = new StringWriter { NewLine = "\n" };
        CommandRunResult? result = null;
        try
        {
            await using (var stdout = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16))
            {
                var output = new CommandOutput { StdOut = stdout, StdErr = stderr };
                result = await WorkingFolder.RunAsync(BaseFolder, () => Task.Run(() =>
                {
                    CommandProgress.Current = progress;
                    return _backend.RunAsync(argv, output, Japanese, cts.Token);
                }), cts.Token);
            }
            // コマンドは中止を「中止しました」＋終了コード 2 で返す（例外にはしない）
            Status = cts.IsCancellationRequested ? L["CmdCanceled"]
                : L.Format("CmdExit", result.ExitCode) + (result.ShowInGui is null ? "" : " ・ " + L["CmdShownInWindow"]);
        }
        catch (OperationCanceledException)
        {
            Status = L["CmdCanceled"];
        }
        finally
        {
            ticking.Cancel();
            IsRunning = false;
            _running = null;
        }
        Notice = (openIgnored ? L["CmdOpenIgnored"] + "\n" : "") + stderr.ToString().TrimEnd('\n');
        ShowOutput(outFile);
        return result;
    }

    /// <summary>走っている間、進み具合を 0.5 秒ごとに出す（何本目／全体・読んだ量・残りの目安・経過。§3.5）。</summary>
    private async Task TickAsync(CommandProgress progress, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await Task.Delay(500, ct);
                Status = Describe(progress.Read());
            }
        }
        catch (OperationCanceledException) { }
    }

    internal static string Describe(CommandProgress.Snapshot p)
    {
        var parts = new List<string> { L["CmdRunning"] };
        if (p.FilesTotal > 1)
            parts.Add(L.Format("CmdProgressFiles", p.FilesDone.ToString("N0", L.Culture), p.FilesTotal.ToString("N0", L.Culture)));
        if (p.BytesTotal > 0)
            parts.Add(p.FilesTotal > 1 ? L.Format("CmdProgressBytes", FormatSize(p.BytesDone), FormatSize(p.BytesTotal)) : FormatSize(p.BytesTotal));
        if (p.Remaining is { } left) parts.Add(L.Format("CmdProgressRemaining", FormatTime(left)));
        parts.Add(L.Format("CmdProgressElapsed", FormatTime(p.Elapsed)));
        return string.Join(" ・ ", parts);
    }

    private static string FormatTime(TimeSpan t) => t.TotalSeconds < 60
        ? (Japanese ? $"{t.TotalSeconds:F0} 秒" : $"{t.TotalSeconds:F0} s")
        : (Japanese ? $"{(int)t.TotalMinutes} 分 {t.Seconds} 秒" : $"{(int)t.TotalMinutes} min {t.Seconds} s");

    /// <summary>［中止］。</summary>
    public void Cancel() => _running?.Cancel();

    private void ShowOutput(string file)
    {
        if (_outputFile is { } old && old != file) TryDelete(old);
        _outputFile = file;
        var lines = new List<string>();
        bool truncated = false;
        using (var reader = new StreamReader(file, Encoding.UTF8))
        {
            while (reader.ReadLine() is { } line)
            {
                if (lines.Count >= MaxOutputLines) { truncated = true; break; }
                lines.Add(line);
            }
        }
        OutputLines = lines;
        OutputTruncated = truncated;
        if (lines.Count > 0 || Notice.Length > 0) SelectedTab = 1;
    }

    /// <summary>出力の全部（［保存…］用。出力欄に見せた先頭だけでなく）。</summary>
    public string? OutputFile => _outputFile;

    /// <summary>出力の全部を書き出す（［保存…］）。</summary>
    public void SaveOutput(string path)
    {
        if (_outputFile is { } file) File.Copy(file, path, overwrite: true);
    }

    /// <summary>ダイアログを閉じたとき（一時ファイルを消す）。</summary>
    public void Dispose()
    {
        Cancel();
        if (_outputFile is { } file) TryDelete(file);
        _outputFile = null;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):F2} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MB",
        >= 1L << 10 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B",
    };
}
