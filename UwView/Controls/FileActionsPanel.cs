using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using UwView.Localization;
using UwView.Services;

namespace UwView.Controls;

/// <summary>
/// ファイルについての共通の操作の窓の中身（2026-10-10 オーナー依頼）。
/// 上にフルパス（選んでコピーできる）、下に［パスをコピー］［フォルダーを開く］［ターミナルで開く］［アプリで開く］、
/// いちばん下に結果の 1 行。ステータスバーのファイル名を押したときと、ファイル一覧の行を右クリックしたときに出す。
/// </summary>
public sealed class FileActionsPanel : StackPanel
{
    private readonly string _display;
    private readonly string? _file;
    private readonly string? _appTarget;
    private readonly TextBlock _result;
    private readonly Button _app;

    private static Localizer L => Localizer.Instance;

    /// <param name="display">出すパス（zip の中なら <c>zip!エントリ</c>）。［パスをコピー］はこれを写す。</param>
    /// <param name="file">実際のファイル（フォルダー・ターミナルはこのファイルのフォルダー。zip の中ならその zip）。</param>
    /// <param name="zipEntry">zip の中のファイルか（アプリでは開けない）。</param>
    public FileActionsPanel(string display, string? file, bool zipEntry = false)
    {
        _display = display;
        _file = file;
        Name = "FileActions";
        Spacing = 8;
        MinWidth = 360;
        MaxWidth = 640;

        var path = new TextBox
        {
            Name = "FileActionsPath", Text = display, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
        };
        ToolTip.SetTip(path, L["TipFileActionsPath"]);

        var problem = AppTargetProblem.None;
        _appTarget = zipEntry ? null : file is null ? null : FileActions.AppTarget(file, out problem);
        if (zipEntry) problem = AppTargetProblem.ZipEntry;
        else if (file is null) problem = AppTargetProblem.Missing;

        var copy = Make("FileActionsCopy", "FileActCopy", "TipFileActCopy", async () => await CopyAsync());
        var folder = Make("FileActionsFolder", "FileActFolder", "TipFileActFolder", () => { Reveal(); return Task.CompletedTask; });
        var terminal = Make("FileActionsTerminal", "FileActTerminal", "TipFileActTerminal", () => { Terminal(); return Task.CompletedTask; });
        _app = Make("FileActionsApp", "FileActApp", "TipFileActApp", OpenAppAsync);
        if (_appTarget is null)
        {
            _app.IsEnabled = false;
            ToolTip.SetTip(_app, ProblemText(problem));
            ToolTip.SetShowOnDisabled(_app, true);
        }
        else if (!string.Equals(_appTarget, file, System.StringComparison.Ordinal))
            ToolTip.SetTip(_app, L.Format("TipFileActAppOriginal", Path.GetFileName(_appTarget)));
        bool hasFolder = Folder() is not null;
        folder.IsEnabled = terminal.IsEnabled = hasFolder;

        _result = new TextBlock { Name = "FileActionsResult", Foreground = ThemeColors.Text, TextWrapping = TextWrapping.Wrap };
        Children.Add(path);
        Children.Add(new WrapPanel { ItemSpacing = 6, LineSpacing = 6, Children = { copy, folder, terminal, _app } });
        Children.Add(_result);
    }

    private static Button Make(string name, string label, string tip, System.Func<Task> run)
    {
        var button = new Button { Name = name, Content = L[label] };
        ToolTip.SetTip(button, L[tip]);
        button.Click += async (_, _) => await run();
        return button;
    }

    /// <summary>フォルダーを開く・ターミナルの行き先（ファイルのフォルダー。ファイルが消えていてもフォルダーがあればそこ）。</summary>
    private string? Folder()
    {
        if (_file is null) return null;
        if (Directory.Exists(_file)) return _file;
        string? folder = Path.GetDirectoryName(_file);
        return folder is not null && Directory.Exists(folder) ? folder : null;
    }

    private string ProblemText(AppTargetProblem problem) => problem switch
    {
        AppTargetProblem.ZipEntry => L.Format("FileListAppZipEntry", Path.GetFileName(_display)),
        AppTargetProblem.IndexOnly => L["FileActAppIndexOnly"],
        _ => L.Format("FileListAppMissing", Path.GetFileName(_display)),
    };

    /// <summary>出しているパス（自動テスト用）。</summary>
    internal string PathText => _display;

    /// <summary>結果の 1 行（自動テスト用）。</summary>
    internal string ResultText => _result.Text ?? "";

    /// <summary>［アプリで開く］が押せるか（自動テスト用）。</summary>
    internal bool AppEnabled => _app.IsEnabled;

    /// <summary>アプリで開くファイル（索引なら元のファイル。自動テスト用）。</summary>
    internal string? AppTargetPath => _appTarget;

    internal async Task CopyAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        await clipboard.SetTextAsync(_display);
        _result.Text = L["FileActCopied"];
    }

    internal void Reveal()
    {
        string? target = _file is not null && (File.Exists(_file) || Directory.Exists(_file)) ? _file : Folder();
        _result.Text = target is not null && FileActions.Reveal(target) ? L["FileActFolderOpened"] : L["FileActFailed"];
    }

    internal void Terminal()
    {
        _result.Text = Folder() is { } folder && FileActions.OpenTerminal(folder)
            ? L.Format("FileActTerminalOpened", folder) : L["FileActFailed"];
    }

    internal async Task OpenAppAsync()
    {
        if (_appTarget is null) return;
        bool opened = await FileActions.LaunchAsync(TopLevel.GetTopLevel(this), _appTarget);
        _result.Text = L.Format(opened ? "FileListAppOpened" : "FileListAppFailed", Path.GetFileName(_appTarget));
    }
}
