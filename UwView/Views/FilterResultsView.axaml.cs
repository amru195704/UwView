using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UwView.Controls;
using UwView.Localization;
using UwView.ViewModels;

namespace UwView.Views;

/// <summary>
/// フィルタ結果ポップアップの中身（UserControl）。
/// デスクトップは FilterResultsWindow がこれをホスト、ブラウザ(WASM)は OverlayLayer がホストする。
/// Window 固有機能（Close/Title）はイベントでホストへ委譲する。
/// </summary>
public partial class FilterResultsView : UserControl
{
    private readonly FilterResultsViewModel _vm;
    private bool _disposed;

    /// <summary>閉じる要求（ホストが Window.Close / オーバーレイ除去を行う）。</summary>
    public event Action? CloseRequested;
    /// <summary>タイトル変化（デスクトップの Window.Title 用）。</summary>
    public event Action<string>? TitleChanged;

    public string CurrentTitle { get; private set; } = "";

    public FilterResultsView(FilterResultsViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();

        SaveButton.Click += OnSaveClick;
        CancelSaveButton.Click += (_, _) => _vm.CancelSave();
        CloseButton.Click += (_, _) => CloseRequested?.Invoke();

        RowList.AttachScrollBar(RowScroll);
        RowList.Rows = _vm.Rows;
        RowList.RowActivated += row => _vm.Jump(row);
        RowList.CopyRequested += () => _ = CopySelectedAsync();
        RowList.SelectionMenuRequested += ShowSelectionMenu;

        _vm.PropertyChanged += OnVmPropertyChanged;
        UpdateTitle();
    }

    /// <summary>
    /// 表示された時点で行リストへフォーカスを移し（矢印/Enter/Cmd+C を即使えるように）、
    /// 前回閉じたときの表示位置へ戻す。
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // レイアウト確定後に実行する:
        // - TopRow はここより前だと VisibleRows が未定でクランプされてしまう
        // - Focus をアタッチ直前に呼ぶと WASM では効かず、最初のクリックが
        //   フォーカス移動に消費されて選択に反映されない
        Dispatcher.UIThread.Post(() =>
        {
            RowList.TopRow = _vm.SavedTopRow;
            RowList.Focus();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>選択行の右クリック → コピー / ファイルに保存。</summary>
    private void ShowSelectionMenu(PointerPressedEventArgs e)
    {
        var menu = new MenuFlyout();
        var copyItem = new MenuItem { Header = Localizer.Instance["MenuCopy"] };
        copyItem.Click += (_, _) => _ = CopySelectedAsync();
        var saveItem = new MenuItem { Header = Localizer.Instance["MenuSaveAs"] };
        saveItem.Click += (_, _) => _ = SaveSelectedAsync();
        menu.Items.Add(copyItem);
        menu.Items.Add(saveItem);
        menu.ShowAt(RowList, showAtPointer: true);
    }

    // XAMLプレビュー用（実行時は上のコンストラクタのみ使用）
    public FilterResultsView() : this(new FilterResultsViewModel(_ => { }, maxContext: 0)) { }

    /// <summary>
    /// ホストが閉じるときに呼ぶ（購読解除＋表示位置の保存）。多重呼び出し安全。
    /// VM は破棄しない: 閉じて開き直したときに ±N・表示位置を引き継ぐため、
    /// 所有者（MainView）が使い回す。VM の破棄は所有者が行う。
    /// </summary>
    public void DisposeView()
    {
        if (_disposed) return;
        _disposed = true;
        _vm.SavedTopRow = RowList.TopRow;
        _vm.PropertyChanged -= OnVmPropertyChanged;
    }

    // ── Pro 拡張用フック（矩形選択オーバーレイ・追加ボタンの挿入先）────
    public Avalonia.Controls.Panel ToolbarHost => ToolbarPanel;
    public Avalonia.Controls.Panel ListHost => ListArea;
    public FilterListView ResultsList => RowList;
    public FilterResultsViewModel ViewModel => _vm;

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilterResultsViewModel.DocumentName))
            UpdateTitle();
        else if (e.PropertyName == nameof(FilterResultsViewModel.Rows))
            RowList.Rows = _vm.Rows;
    }

    private void UpdateTitle()
    {
        string name = _vm.DocumentName;
        CurrentTitle = name.Length == 0
            ? Localizer.Instance["FilterResultsTitle"]
            : $"{Localizer.Instance["FilterResultsTitle"]} — {name}";
        TitleChanged?.Invoke(CurrentTitle);
    }

    // ── 選択行の copy / save ─────────────────────────────────

    /// <summary>選択行を表示順で返す。</summary>
    private List<FilterRow> SelectedRowsInOrder() => RowList.SelectedRowsInOrder();

    /// <summary>
    /// 選択行の copy/save の1行分。ツールバーの「行番号を含める」に従う
    /// （一覧全体の保存・矩形選択の copy/save と同じ扱いに統一）。
    /// </summary>
    private string FormatRow(FilterRow row) =>
        _vm.IncludeLineNumbersOnSave ? FormatRowWithLineNumber(row) : (row.IsSeparator ? "⋯" : row.Text);

    private static string FormatRowWithLineNumber(FilterRow row) =>
        row.IsSeparator ? "⋯"
        : (row.LineNumberText.Length > 0 ? row.LineNumberText + "\t" : "") + row.Text;

    private async System.Threading.Tasks.Task CopySelectedAsync()
    {
        var rows = SelectedRowsInOrder();
        if (rows.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var row in rows) sb.AppendLine(FormatRow(row));
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(sb.ToString());
    }

    private async System.Threading.Tasks.Task SaveSelectedAsync()
    {
        var rows = SelectedRowsInOrder();
        if (rows.Count == 0) return;
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localizer.Instance["SaveSelectionTitle"],
            SuggestedFileName = "selection.txt",
            DefaultExtension = "txt",
        });
        if (file is null) return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1 << 16);
            for (int i = 0; i < rows.Count; i++)
            {
                await writer.WriteLineAsync(FormatRow(rows[i]));
                if ((i & 1023) == 1023) await System.Threading.Tasks.Task.Yield();
            }
        }
        catch (IOException) { /* 書き込み失敗は黙って中断 */ }
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (_vm.IsSaving || _vm.Rows.Count == 0) return;
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;

        string baseName = Path.GetFileNameWithoutExtension(_vm.DocumentName);
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localizer.Instance["SaveFilterTitle"],
            SuggestedFileName = (baseName.Length == 0 ? "filter" : baseName + "-filter") + ".txt",
            DefaultExtension = "txt",
        });
        if (file is null) return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await _vm.SaveAsync(stream, new UTF8Encoding(false));
        }
        catch (OperationCanceledException) { /* キャンセル: 途中までのファイルが残る */ }
        catch (IOException) { /* 書き込み失敗は黙って中断（v1） */ }
    }
}
