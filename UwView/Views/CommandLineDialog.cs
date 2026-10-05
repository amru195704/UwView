using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
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
    private readonly Button _run;

    private CommandLineDialog(CommandLineViewModel vm, Func<CommandRunResult, Task>? show)
    {
        ViewModel = vm;
        _show = show;
        Title = L["CmdTitle"];
        Width = 860;
        Height = 620;
        MinWidth = 560;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;

        // ── 基準フォルダー（表示＋［選ぶ…］。要裁定 §11-1）──
        _baseFolder = new TextBlock { Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center,
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

        _ignore = new CheckBox { Name = "CmdFollowIgnore", Content = L["CmdFollowIgnore"], Foreground = Brushes.Black,
                                 IsChecked = vm.FollowIgnore };
        ToolTip.SetTip(_ignore, L["TipCmdFollowIgnore"]);
        _ignore.IsCheckedChanged += (_, _) => vm.FollowIgnore = _ignore.IsChecked == true;

        // ── コマンドの行（いちばん大事な表示。§3.2）──
        _command = new SelectableTextBlock { Name = "CmdCommandText", FontFamily = Mono, Foreground = Brushes.Black,
                                             TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var copy = MakeButton("CmdCopyButton", L["CmdCopy"], L["TipCmdCopy"]);
        copy.Click += async (_, _) => await CopyAsync(vm.CommandText);
        var commandRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(copy, Dock.Right);
        commandRow.Children.Add(copy);
        commandRow.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF7)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xCE, 0xD8)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 5), Margin = new Thickness(0, 0, 8, 0), Child = _command,
        });
        _error = new TextBlock { Name = "CmdErrorText", Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x1E, 0x1E)),
                                 TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 0, 0) };

        // ── ファイル一覧・出力欄（タブで切り替える。§3.3・§3.4）──
        _files = new ListBox
        {
            Name = "CmdFileList",
            ItemTemplate = new FuncDataTemplate<CommandFileRow>((row, _) => row is null ? new Panel() : FileRowView(row),
                                                                supportsRecycling: false),
        };
        var fileHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("56,*,90"), Margin = new Thickness(10, 2, 22, 2) };
        AddCell(fileHeader, 0, L["FileListNumber"], HorizontalAlignment.Right);
        AddCell(fileHeader, 1, L["FileListFile"], HorizontalAlignment.Left);
        AddCell(fileHeader, 2, L["FileListSize"], HorizontalAlignment.Right);
        var filesPanel = new DockPanel();
        DockPanel.SetDock(fileHeader, Dock.Top);
        filesPanel.Children.Add(fileHeader);
        filesPanel.Children.Add(_files);
        _filesTab = new TabItem { Header = L.Format("CmdTabFiles", 0), Content = filesPanel };

        _output = new ListBox
        {
            Name = "CmdOutputList",
            FontFamily = Mono,
            ItemTemplate = new FuncDataTemplate<string>((line, _) =>
                new SelectableTextBlock { Text = line ?? "", Foreground = Brushes.Black, FontFamily = Mono }, supportsRecycling: true),
        };
        _notice = new TextBlock { Name = "CmdNotice", Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x4B, 0x00)),
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
        var outputTab = new TabItem { Header = L["CmdTabOutput"], Content = outputPanel };

        _tabs = new TabControl { Name = "CmdTabs", Margin = new Thickness(0, 8, 0, 0) };
        _tabs.Items.Add(_filesTab);
        _tabs.Items.Add(outputTab);
        _tabs.SelectionChanged += (_, _) => vm.SelectedTab = _tabs.SelectedIndex;

        _summary = new TextBlock { Name = "CmdFilesSummary", Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap,
                                   Margin = new Thickness(2, 6, 0, 0) };
        _status = new TextBlock { Name = "CmdStatus", Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center };

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
        top.Children.Add(Row(L["CmdFilePattern"], _filePattern));
        top.Children.Add(Row(L["CmdSearchPattern"], _searchPattern));
        top.Children.Add(_ignore);
        top.Children.Add(commandRow);
        top.Children.Add(_error);

        var body = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        DockPanel.SetDock(_summary, Dock.Bottom);
        body.Children.Add(top);
        body.Children.Add(footer);
        body.Children.Add(_summary);
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
    public static CommandLineDialog ShowFor(Window owner, Func<CommandLineViewModel> create, Func<CommandRunResult, Task>? show)
    {
        if (Open.TryGetValue(owner, out var existing))
        {
            existing.Activate();
            return existing;
        }
        var dialog = new CommandLineDialog(create(), show);
        Open[owner] = dialog;
        dialog.Closed += (_, _) => Open.Remove(owner);
        dialog.Show(owner);
        return dialog;
    }

    /// <summary>自動テスト用：この窓のダイアログ（無ければ null）。</summary>
    internal static CommandLineDialog? For(Window owner) => Open.GetValueOrDefault(owner);

    private void Apply(string? property)
    {
        var vm = ViewModel;
        if (property is null or nameof(vm.BaseFolder)) { _baseFolder.Text = vm.BaseFolder; ToolTip.SetTip(_baseFolder, vm.BaseFolder); }
        if (property is null or nameof(vm.CommandText)) _command.Text = vm.CommandText;
        if (property is null or nameof(vm.ErrorText)) { _error.Text = vm.ErrorText; _error.IsVisible = vm.ErrorText.Length > 0; }
        if (property is null or nameof(vm.Files)) { _files.ItemsSource = vm.Files; _filesTab.Header = L.Format("CmdTabFiles", vm.Files.Count); }
        if (property is null or nameof(vm.FilesSummary)) _summary.Text = vm.FilesSummary;
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

    private async Task RunAsync()
    {
        if (await ViewModel.RunAsync() is { ShowInGui: not null } result && _show is not null)
            await _show(result);
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
        if (file?.TryGetLocalPath() is { } path) ViewModel.SaveOutput(path);
    }

    private static Button MakeButton(string name, string label, string tip)
    {
        var b = new Button { Name = name, Content = label, Padding = new Thickness(14, 4) };
        ToolTip.SetTip(b, tip);
        return b;
    }

    private static Control Row(string label, Control field, Control? right = null)
    {
        var row = new DockPanel();
        var caption = new TextBlock { Text = label, Width = 150, Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center };
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
        var name = new TextBlock { Text = (row.Readable ? "" : "⚠ ") + row.Name, Foreground = Brushes.Black,
                                   Margin = new Thickness(10, 0, 0, 0), TextTrimming = TextTrimming.PrefixCharacterEllipsis };
        ToolTip.SetTip(name, row.FullPath);
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        AddCell(grid, 2, row.Size is { } s ? FormatSize(s) : "—", HorizontalAlignment.Right);
        return grid;
    }

    private static void AddCell(Grid grid, int column, string text, HorizontalAlignment align)
    {
        var cell = new TextBlock { Text = text, Foreground = Brushes.Black, HorizontalAlignment = align };
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
