using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using UwView.Localization;
using UwView.Services;
using UwView.ViewModels;

namespace UwView.Views;

/// <summary>
/// 「コマンドライン」ダイアログ（v1.8.0 Finder Scope §3）。uvf／uvp のコマンドと同じ書き方で、
/// ファイル指定パターンと検索パターンを入れて実行する。<b>開いたまま本体を操作できる</b>（モードレス・1 つの窓に 1 つ）。
/// 中身は <see cref="CommandLineViewModel"/>、uvf と uvp の違いは <see cref="ICommandLineBackend"/>。
/// </summary>
public sealed class CommandLineDialog : Window
{
    private static readonly Dictionary<Window, CommandLineDialog> Open = [];
    private static Localizer L => Localizer.Instance;
    private static readonly FontFamily Mono = new("Menlo, Consolas, DejaVu Sans Mono, monospace");

    public CommandLineViewModel ViewModel { get; }

    /// <summary>結果を本体の窓に出す（ViewModel が返した受け渡しを、UI のスレッドで呼ぶ）。</summary>
    private readonly Func<CommandRunResult, Task>? _show;

    /// <summary>ファイル一覧の行をダブルクリックしたとき、本体で開く係（無ければ何もしない）。</summary>
    private readonly Func<string, Task>? _openFile;

    private readonly TextBox _filePattern;
    private readonly TextBox _searchPattern;
    private readonly TextBlock _baseFolder;
    private readonly CheckBox _ignore;
    private readonly SelectableTextBlock _command;
    private readonly TextBlock _error;
    private readonly ListBox _files;
    private readonly ListBox _output;
    private readonly TextBlock _notice;
    private readonly TextBlock _summary;
    private readonly TextBlock _status;
    private readonly TabControl _tabs;
    private readonly TabItem _filesTab;
    private readonly TextBlock _filesTabHeader;
    private readonly Button _run;
    private readonly Button _rebuild;
    private readonly TextBlock _written;
    private readonly DockPanel _writtenRow;

    private CommandLineDialog(CommandLineViewModel vm, Func<CommandRunResult, Task>? show, Func<string, Task>? openFile)
    {
        ViewModel = vm;
        _show = show;
        _openFile = openFile;
        Title = L["CmdTitle"];
        Width = 860;
        Height = 620;
        MinWidth = 560;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;

        // ── 基準フォルダー（表示＋［選ぶ…］。要裁定 §11-1）──
        _baseFolder = new TextBlock { Foreground = UwView.Services.ThemeColors.Text, VerticalAlignment = VerticalAlignment.Center,
                                      TextTrimming = TextTrimming.PrefixCharacterEllipsis };
        var pick = MakeButton("CmdPickFolderButton", L["CmdPickFolder"], L["TipCmdPickFolder"]);
        pick.Click += async (_, _) => await PickFolderAsync();
        var folderRow = Row(L["CmdBaseFolder"], _baseFolder, pick);

        _filePattern = new TextBox { Name = "CmdFilePattern", FontFamily = Mono, PlaceholderText = "**/*.log,old/*.gz" };
        _filePattern.TextChanged += (_, _) => vm.FilePattern = _filePattern.Text ?? "";
        _searchPattern = new TextBox { Name = "CmdSearchPattern", FontFamily = Mono, PlaceholderText = "ERROR -i" };
        _searchPattern.TextChanged += (_, _) => vm.SearchPattern = _searchPattern.Text ?? "";
        _searchPattern.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter || !vm.CanRun) return;
            e.Handled = true;
            await RunAsync();
        };
        ToolTip.SetTip(_filePattern, L["TipCmdFilePattern"]);
        ToolTip.SetTip(_searchPattern, L["TipCmdSearchPattern"]);

        _ignore = new CheckBox { Name = "CmdFollowIgnore", Content = L["CmdFollowIgnore"], Foreground = UwView.Services.ThemeColors.Text,
                                 IsChecked = vm.FollowIgnore };
        ToolTip.SetTip(_ignore, L["TipCmdFollowIgnore"]);
        _ignore.IsCheckedChanged += (_, _) => vm.FollowIgnore = _ignore.IsChecked == true;

        // ── コマンドの行（いちばん大事な表示。§3.2）──
        _command = new SelectableTextBlock { Name = "CmdCommandText", FontFamily = Mono, Foreground = UwView.Services.ThemeColors.Text,
                                             TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var copy = MakeButton("CmdCopyButton", L["CmdCopy"], L["TipCmdCopy"]);
        copy.Click += async (_, _) => await CopyAsync(vm.CommandText);
        var commandRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(copy, Dock.Right);
        commandRow.Children.Add(copy);
        if (OperatingSystem.IsWindows())
        {
            var copyPs = MakeButton("CmdCopyPowerShellButton", L["CmdCopyPowerShell"], L["TipCmdCopyPowerShell"]);
            copyPs.Margin = new Thickness(0, 0, 6, 0);
            copyPs.Click += async (_, _) => await CopyAsync(vm.CommandTextPowerShell);
            DockPanel.SetDock(copyPs, Dock.Right);
            commandRow.Children.Add(copyPs);
        }
        commandRow.Children.Add(new Border
        {
            Background = UwView.Services.ThemeColors.Get("Uv_F2F4F7", new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF7))),
            BorderBrush = UwView.Services.ThemeColors.Get("Uv_C8CED8", new SolidColorBrush(Color.FromRgb(0xC8, 0xCE, 0xD8))),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 5), Margin = new Thickness(0, 0, 8, 0), Child = _command,
        });
        _error = new TextBlock { Name = "CmdErrorText", Foreground = UwView.Services.ThemeColors.Get("Uv_B01E1E", new SolidColorBrush(Color.FromRgb(0xB0, 0x1E, 0x1E))),
                                 TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 0, 0) };

        // ── ファイル一覧・出力欄（タブで切り替える。§3.3・§3.4）──
        _files = new ListBox
        {
            Name = "CmdFileList",
            ItemTemplate = new FuncDataTemplate<CommandFileRow>((row, _) => row is null ? new Panel() : FileRowView(row),
                                                                supportsRecycling: false),
        };
        ToolTip.SetTip(_files, L["TipCmdFileList"]);
        _files.DoubleTapped += async (_, _) =>
        {
            if (_files.SelectedItem is CommandFileRow { Readable: true } row && _openFile is not null) await _openFile(row.FullPath);
        };
        var fileHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("56,*,90"), Margin = new Thickness(10, 2, 22, 2) };
        AddCell(fileHeader, 0, L["FileListNumber"], HorizontalAlignment.Right);
        AddCell(fileHeader, 1, L["FileListFile"], HorizontalAlignment.Left);
        AddCell(fileHeader, 2, L["FileListSize"], HorizontalAlignment.Right);
        var filesPanel = new DockPanel();
        DockPanel.SetDock(fileHeader, Dock.Top);
        filesPanel.Children.Add(fileHeader);
        filesPanel.Children.Add(_files);
        // 見出しは TextBlock にする（文字列のままだと、本数が増えても幅が最初の「ファイル（0）」のままで切れた）
        _filesTabHeader = TabHeader(L.Format("CmdTabFiles", 0));
        _filesTab = new TabItem { Header = _filesTabHeader, Content = filesPanel };

        _output = new ListBox
        {
            Name = "CmdOutputList",
            FontFamily = Mono,
            ItemTemplate = new FuncDataTemplate<string>((line, _) =>
                new SelectableTextBlock { Text = line ?? "", Foreground = UwView.Services.ThemeColors.Text, FontFamily = Mono }, supportsRecycling: true),
        };
        _notice = new TextBlock { Name = "CmdNotice", Foreground = UwView.Services.ThemeColors.Get("Uv_8A4B00", new SolidColorBrush(Color.FromRgb(0x8A, 0x4B, 0x00))),
                                  TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 4) };
        var copyOut = MakeButton("CmdCopyOutputButton", L["CmdCopy"], L["TipCmdCopyOutput"]);
        copyOut.Click += async (_, _) => await CopyAsync(string.Join("\n", vm.OutputLines));
        var saveOut = MakeButton("CmdSaveOutputButton", L["CmdSave"], L["TipCmdSaveOutput"]);
        saveOut.Click += async (_, _) => await SaveOutputAsync();
        var outButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        outButtons.Children.Add(copyOut);
        outButtons.Children.Add(saveOut);
        var outTop = new DockPanel();
        DockPanel.SetDock(outButtons, Dock.Right);
        outTop.Children.Add(outButtons);
        outTop.Children.Add(_notice);
        var outputPanel = new DockPanel();
        DockPanel.SetDock(outTop, Dock.Top);
        outputPanel.Children.Add(outTop);
        outputPanel.Children.Add(_output);
        var outputTab = new TabItem { Header = TabHeader(L["CmdTabOutput"]), Content = outputPanel };

        _tabs = new TabControl { Name = "CmdTabs", Margin = new Thickness(0, 8, 0, 0) };
        _tabs.Items.Add(_filesTab);
        _tabs.Items.Add(outputTab);
        _tabs.SelectionChanged += (_, _) => vm.SelectedTab = _tabs.SelectedIndex;

        _summary = new TextBlock { Name = "CmdFilesSummary", Foreground = UwView.Services.ThemeColors.Text, TextWrapping = TextWrapping.Wrap,
                                   Margin = new Thickness(2, 6, 0, 0) };
        _status = new TextBlock { Name = "CmdStatus", Foreground = UwView.Services.ThemeColors.Text, VerticalAlignment = VerticalAlignment.Center };
        // Keep（元ファイルが減った）のときだけ出す。前の索引は .bak-日時 に退避してから作り直す（コマンドの --rebuild と同じ）
        _rebuild = MakeButton("CmdRebuildButton", L["CmdRebuild"], L["TipCmdRebuild"]);
        _rebuild.Margin = new Thickness(0, 6, 0, 0);
        _rebuild.Click += async (_, _) => await RebuildAsync();
        // 書いたファイル（-out・convert・［保存…］）：「書きました」＋［開く］［フォルダーを表示］（§5.1）
        _written = new TextBlock { Name = "CmdWrittenText", Foreground = UwView.Services.ThemeColors.Text, VerticalAlignment = VerticalAlignment.Center,
                                   TextTrimming = TextTrimming.PrefixCharacterEllipsis };
        var openWritten = MakeButton("CmdOpenWrittenButton", L["CmdOpenWritten"], L["TipCmdOpenWritten"]);
        openWritten.Click += async (_, _) =>
        {
            if (vm.WrittenFile is { } path && _openFile is not null) await _openFile(path);
        };
        var revealWritten = MakeButton("CmdRevealWrittenButton", L["CmdRevealWritten"], L["TipCmdRevealWritten"]);
        revealWritten.Click += (_, _) => { if (vm.WrittenFile is { } path) RevealInFolder(path); };
        var writtenButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        writtenButtons.Children.Add(openWritten);
        writtenButtons.Children.Add(revealWritten);
        _writtenRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(writtenButtons, Dock.Right);
        _writtenRow.Children.Add(writtenButtons);
        _writtenRow.Children.Add(_written);
        var summaryRow = new DockPanel();
        DockPanel.SetDock(_rebuild, Dock.Right);
        summaryRow.Children.Add(_rebuild);
        summaryRow.Children.Add(_summary);

        // ── ボタン（§3.5）──
        var expand = MakeButton("CmdExpandButton", L["CmdExpand"], L["TipCmdExpand"]);
        expand.Click += async (_, _) => await vm.ExpandAsync();
        _run = MakeButton("CmdRunButton", L["CmdRun"], L["TipCmdRun"]);
        _run.Click += async (_, _) =>
        {
            if (vm.IsRunning) vm.Cancel(); else await RunAsync();
        };
        var close = MakeButton("CmdCloseButton", L["CmdClose"], L["TipCmdClose"]);
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(expand);
        buttons.Children.Add(_run);
        buttons.Children.Add(close);
        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(_status);

        var top = new StackPanel { Spacing = 6 };
        top.Children.Add(folderRow);
        top.Children.Add(Row(L["CmdFilePattern"], _filePattern, HistoryButton("CmdFileHistoryButton", e => e.FilePattern)));
        top.Children.Add(Row(L["CmdSearchPattern"], _searchPattern, HistoryButton("CmdSearchHistoryButton", e => e.SearchPattern)));
        top.Children.Add(_ignore);
        top.Children.Add(commandRow);
        top.Children.Add(_error);

        var body = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        DockPanel.SetDock(summaryRow, Dock.Bottom);
        DockPanel.SetDock(_writtenRow, Dock.Bottom);
        body.Children.Add(top);
        body.Children.Add(footer);
        body.Children.Add(_writtenRow);
        body.Children.Add(summaryRow);
        body.Children.Add(_tabs);
        Content = body;

        vm.PropertyChanged += (_, e) => Apply(e.PropertyName);
        Apply(null);
        Closing += (_, e) =>
        {
            // 実行中なら、止めてよいか尋ねる（§3.5）
            if (!vm.IsRunning || _closeConfirmed) return;
            e.Cancel = true;
            _ = ConfirmCloseAsync();
        };
        Closed += (_, _) => vm.Dispose();
    }

    private bool _closeConfirmed;

    private async Task ConfirmCloseAsync()
    {
        if (!await ConfirmDialog.AskAsync(this, L["CmdTitle"], L["CmdStopAndClose"], L["CmdStopYes"], L["CmdStopNo"])) return;
        ViewModel.Cancel();
        _closeConfirmed = true;
        Close();
    }

    /// <summary>
    /// 窓ごとに 1 つ。開いていれば前に出す。<paramref name="show"/> は結果を本体の窓に出す係（無ければ出力欄だけ）。
    /// </summary>
    public static CommandLineDialog ShowFor(Window owner, Func<CommandLineViewModel> create, Func<CommandRunResult, Task>? show,
                                            Func<string, Task>? openFile = null)
    {
        if (Open.TryGetValue(owner, out var existing))
        {
            existing.Activate();
            return existing;
        }
        var dialog = new CommandLineDialog(create(), show, openFile);
        Open[owner] = dialog;
        dialog.Closed += (_, _) => Open.Remove(owner);
        dialog.Show(owner);
        return dialog;
    }

    /// <summary>この窓のダイアログ（無ければ null）。</summary>
    public static CommandLineDialog? For(Window owner) => Open.GetValueOrDefault(owner);

    private void Apply(string? property)
    {
        var vm = ViewModel;
        if (property is null or nameof(vm.BaseFolder)) { _baseFolder.Text = vm.BaseFolder; ToolTip.SetTip(_baseFolder, vm.BaseFolder); }
        if (property is null or nameof(vm.CommandText)) _command.Text = vm.CommandText;
        if (property is null or nameof(vm.ErrorText)) { _error.Text = vm.ErrorText; _error.IsVisible = vm.ErrorText.Length > 0; }
        if (property is null or nameof(vm.Files)) { _files.ItemsSource = vm.Files; _filesTabHeader.Text = L.Format("CmdTabFiles", vm.Files.Count.ToString("N0", L.Culture)); RemeasureTabs(); }
        if (property is null or nameof(vm.FilesSummary)) _summary.Text = vm.FilesSummary;
        if (property is null or nameof(vm.OffersRebuild)) _rebuild.IsVisible = vm.OffersRebuild;
        if (property is null or nameof(vm.WrittenFile))
        {
            _writtenRow.IsVisible = vm.WrittenFile is not null;
            _written.Text = vm.WrittenFile is { } w ? $"{L["CmdWritten"]} {w}" : "";
            ToolTip.SetTip(_written, vm.WrittenFile);
        }
        // 画面の外から入れた値（フォルダーのドロップ・［作り直す…］など）を欄に映す
        if (property is null or nameof(vm.FilePattern) && _filePattern.Text != vm.FilePattern) _filePattern.Text = vm.FilePattern;
        if (property is null or nameof(vm.SearchPattern) && _searchPattern.Text != vm.SearchPattern) _searchPattern.Text = vm.SearchPattern;
        if (property is null or nameof(vm.FollowIgnore) && _ignore.IsChecked != vm.FollowIgnore) _ignore.IsChecked = vm.FollowIgnore;
        if (property is null or nameof(vm.OutputLines) or nameof(vm.OutputTruncated))
        {
            _output.ItemsSource = vm.OutputLines;
            if (vm.OutputTruncated) _notice.Text = L.Format("CmdTruncated", CommandLineViewModel.MaxOutputLines.ToString("N0", L.Culture));
        }
        if (property is null or nameof(vm.Notice)) { _notice.Text = vm.Notice; _notice.IsVisible = vm.Notice.Length > 0 || vm.OutputTruncated; }
        if (property is null or nameof(vm.SelectedTab)) _tabs.SelectedIndex = vm.SelectedTab;
        if (property is null or nameof(vm.Status)) _status.Text = vm.Status;
        if (property is null or nameof(vm.IsRunning) or nameof(vm.CanRun) or nameof(vm.ErrorText))
        {
            _run.Content = vm.IsRunning ? L["CmdCancel"] : L["CmdRun"];
            ToolTip.SetTip(_run, vm.IsRunning ? L["TipCmdCancel"] : L["TipCmdRun"]);
            _run.IsEnabled = vm.IsRunning || vm.CanRun;
        }
    }

    /// <summary>［作り直す…］：<c>--rebuild</c> を足して走らせる（作り直してよいかは、走らせる前に中身が尋ねる）。</summary>
    private async Task RebuildAsync()
    {
        ViewModel.AddRebuild();
        await RunAsync();
    }

    private async Task RunAsync()
    {
        var result = await ViewModel.RunAsync();
        if (result is { ShowInGui: not null } && _show is not null)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await _show(result);
            ViewModel.NoteShown(watch.Elapsed);
        }
        // -replace・cat を -out なしで：出力を新しいタブで開く（読むだけ・保存できる。要裁定 §11-5）
        if (result?.OutputTabName is { } name && ViewModel.OutputFile is { } output && _openFile is not null)
            await _openFile(CopyForTab(output, name));
    }

    /// <summary>出力を、タブで開ける名前の一時ファイルに写す（ダイアログを閉じても消えない場所）。</summary>
    internal static string CopyForTab(string output, string name)
    {
        string folder = Path.Combine(Path.GetTempPath(), "UwView-output", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.Copy(output, path, overwrite: true);
        return path;
    }

    /// <summary>ファイルのあるフォルダーを、OS のファイル画面で開く（Mac は選んだ状態で）。</summary>
    private static void RevealInFolder(string path)
    {
        try
        {
            var start = OperatingSystem.IsMacOS() ? new System.Diagnostics.ProcessStartInfo("open", ["-R", path])
                : OperatingSystem.IsWindows() ? new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                : new System.Diagnostics.ProcessStartInfo("xdg-open", [Path.GetDirectoryName(path) ?? "."]);
            start.UseShellExecute = false;
            System.Diagnostics.Process.Start(start);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    private async Task PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = L["CmdBaseFolder"],
            AllowMultiple = false,
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(ViewModel.BaseFolder),
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) ViewModel.BaseFolder = path;
    }

    private async Task CopyAsync(string text)
    {
        if (Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }

    private async Task SaveOutputAsync()
    {
        if (ViewModel.OutputFile is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L["CmdSave"],
            SuggestedFileName = "output.txt",
        });
        if (file?.TryGetLocalPath() is { } path) await ViewModel.SaveOutputAsync(path);
    }

    /// <summary>
    /// ▼（履歴）。その欄の値を新しい順に並べ、選ぶと基準フォルダー・2 つの欄・除外のチェックを 1 組で戻す（§3.1）。
    /// </summary>
    private Button HistoryButton(string name, Func<CommandHistoryEntry, string> field)
    {
        var button = new Button { Name = name, Content = "▼", Padding = new Thickness(8, 4) };
        ToolTip.SetTip(button, L["TipCmdHistory"]);
        button.Click += (_, _) =>
        {
            var menu = new MenuFlyout();
            foreach (var entry in HistoryItems(ViewModel.History, field))
            {
                var item = new MenuItem { Header = field(entry), FontFamily = Mono };
                ToolTip.SetTip(item, $"{entry.BaseFolder}\n{ViewModel.Tool} {entry.FilePattern} {entry.SearchPattern}");
                item.Click += (_, _) => ViewModel.Recall(entry);
                menu.Items.Add(item);
            }
            if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = L["CmdHistoryEmpty"], IsEnabled = false });
            menu.ShowAt(button);
        };
        return button;
    }

    /// <summary>履歴のうち、その欄の値ごとに一番新しいもの（空の値は出さない）。</summary>
    internal static IEnumerable<CommandHistoryEntry> HistoryItems(IEnumerable<CommandHistoryEntry> history,
                                                                 Func<CommandHistoryEntry, string> field)
        => history.Where(e => field(e).Length > 0).DistinctBy(field);

    /// <summary>見出しの長さが変わったら、タブとタブ列を測り直させる（見出しだけ書き換えても、タブは最初の幅のままだった）。</summary>
    private void RemeasureTabs()
    {
        foreach (var item in _tabs.GetVisualDescendants().OfType<TabItem>()) item.InvalidateMeasure();
        if (_filesTab.GetVisualParent() is Layoutable strip) strip.InvalidateMeasure();
    }

    private static TextBlock TabHeader(string text) => new() { Text = text, FontSize = 15, Foreground = UwView.Services.ThemeColors.Text };

    private static Button MakeButton(string name, string label, string tip)
    {
        var b = new Button { Name = name, Content = label, Padding = new Thickness(14, 4) };
        ToolTip.SetTip(b, tip);
        return b;
    }

    private static Control Row(string label, Control field, Control? right = null)
    {
        var row = new DockPanel();
        var caption = new TextBlock { Text = label, Width = 150, Foreground = UwView.Services.ThemeColors.Text, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(caption, Dock.Left);
        row.Children.Add(caption);
        if (right is not null)
        {
            DockPanel.SetDock(right, Dock.Right);
            right.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(right);
        }
        row.Children.Add(field);
        return row;
    }

    private static Control FileRowView(CommandFileRow row)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("56,*,90") };
        AddCell(grid, 0, row.Label, HorizontalAlignment.Right);
        var name = new TextBlock { Text = (row.Readable ? "" : "⚠ ") + row.Name, Foreground = UwView.Services.ThemeColors.Text,
                                   Margin = new Thickness(10, 0, 0, 0), TextTrimming = TextTrimming.PrefixCharacterEllipsis };
        ToolTip.SetTip(name, row.FullPath);
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        AddCell(grid, 2, row.Size is { } s ? FormatSize(s) : "—", HorizontalAlignment.Right);
        return grid;
    }

    private static void AddCell(Grid grid, int column, string text, HorizontalAlignment align)
    {
        var cell = new TextBlock { Text = text, Foreground = UwView.Services.ThemeColors.Text, HorizontalAlignment = align };
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):F2} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MB",
        >= 1L << 10 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B",
    };
}
