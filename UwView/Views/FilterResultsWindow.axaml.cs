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
using Avalonia.VisualTree;
using UwView.Localization;
using UwView.ViewModels;

namespace UwView.Views;

/// <summary>
/// フィルタ結果ポップアップ（機能修正指示書_検索フィルタPopup.md）。
/// 非モーダル。メイン画面と同時表示し、ダブルクリック/Enter で実テキストへジャンプ、
/// [保存…] でフィルタ結果を別ファイルへ逐次書き出す。
/// </summary>
public partial class FilterResultsWindow : Window
{
    private readonly FilterResultsViewModel _vm;

    public FilterResultsWindow(FilterResultsViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();

        SaveButton.Click += OnSaveClick;
        CancelSaveButton.Click += (_, _) => _vm.CancelSave();
        TopmostToggle.IsCheckedChanged += (_, _) => Topmost = TopmostToggle.IsChecked == true;

        RowList.DoubleTapped += (_, _) => JumpToSelected();
        RowList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { JumpToSelected(); e.Handled = true; }
            else if (e.Key == Key.C && (e.KeyModifiers.HasFlag(KeyModifiers.Control)
                                     || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
            { _ = CopySelectedAsync(); e.Handled = true; }
        };

        // ドラッグで行範囲選択（メイン画面の行選択と同じ操作感。Cmd/Shift+クリックの
        // 個別選択は ListBox 既定動作のまま）
        RowList.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);
        RowList.AddHandler(PointerMovedEvent, OnListPointerMoved, RoutingStrategies.Tunnel);
        RowList.AddHandler(PointerReleasedEvent, OnListPointerReleased, RoutingStrategies.Tunnel);

        // 選択行の右クリック → コピー / ファイルに保存
        var selMenu = new MenuFlyout();
        var copyItem = new MenuItem { Header = Localizer.Instance["MenuCopy"] };
        copyItem.Click += (_, _) => _ = CopySelectedAsync();
        var saveItem = new MenuItem { Header = Localizer.Instance["MenuSaveAs"] };
        saveItem.Click += (_, _) => _ = SaveSelectedAsync();
        selMenu.Items.Add(copyItem);
        selMenu.Items.Add(saveItem);
        RowList.ContextFlyout = selMenu;

        _vm.PropertyChanged += OnVmPropertyChanged;
        UpdateTitle();
        Closed += (_, _) =>
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.Dispose();
        };
    }

    // XAMLプレビュー用（実行時は上のコンストラクタのみ使用）
    public FilterResultsWindow() : this(new FilterResultsViewModel(_ => { }, maxContext: 0)) { }

    // ── Pro 拡張用フック（矩形選択オーバーレイ・追加ボタンの挿入先）────
    public Avalonia.Controls.Panel ToolbarHost => ToolbarPanel;
    public Avalonia.Controls.Panel ListHost => ListArea;
    public ListBox ResultsList => RowList;
    public FilterResultsViewModel ViewModel => _vm;

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilterResultsViewModel.DocumentName))
            UpdateTitle();
    }

    private void UpdateTitle()
    {
        string name = _vm.DocumentName;
        Title = name.Length == 0
            ? Localizer.Instance["FilterResultsTitle"]
            : $"{Localizer.Instance["FilterResultsTitle"]} — {name}";
    }

    private void JumpToSelected()
        => _vm.Jump(RowList.SelectedItem as FilterRow);

    // ── ドラッグ行選択 ───────────────────────────────────────

    private int _dragAnchorIndex = -1;
    private bool _rangeDragging;

    /// <summary>RowList 座標のピクセル位置 → 行 index（可視行の最寄りへクランプ）。</summary>
    private int IndexFromPoint(Avalonia.Point p)
    {
        ListBoxItem? best = null;
        double bestDist = double.MaxValue;
        foreach (var c in RowList.GetRealizedContainers())
        {
            if (c is not ListBoxItem item) continue;
            if (item.TranslatePoint(new Avalonia.Point(0, 0), RowList) is not { } o) continue;
            double dist = p.Y < o.Y ? o.Y - p.Y
                        : p.Y >= o.Y + item.Bounds.Height ? p.Y - (o.Y + item.Bounds.Height)
                        : 0;
            if (dist < bestDist) { bestDist = dist; best = item; if (dist == 0) break; }
        }
        return best is null ? -1 : RowList.IndexFromContainer(best);
    }

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pt = e.GetCurrentPoint(RowList);
        if (pt.Properties.IsLeftButtonPressed
            && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Meta)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _dragAnchorIndex = IndexFromPoint(e.GetPosition(RowList));
            _rangeDragging = false;
        }
    }

    private void OnListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragAnchorIndex < 0) return;
        if (!e.GetCurrentPoint(RowList).Properties.IsLeftButtonPressed) { _dragAnchorIndex = -1; return; }

        var p = e.GetPosition(RowList);

        // 端で1行ずつスクロール（移動イベント駆動の簡易版）
        if (Avalonia.VisualTree.VisualExtensions.FindDescendantOfType<ScrollViewer>(RowList) is { } sv)
        {
            if (p.Y < 0) sv.Offset = new Avalonia.Vector(sv.Offset.X, Math.Max(0, sv.Offset.Y - 18));
            else if (p.Y > RowList.Bounds.Height) sv.Offset = new Avalonia.Vector(sv.Offset.X, sv.Offset.Y + 18);
        }

        int idx = IndexFromPoint(p);
        if (idx < 0) return;
        if (!_rangeDragging && idx == _dragAnchorIndex) return; // 同一行内はクリック扱い

        _rangeDragging = true;
        var sel = RowList.Selection;
        sel.BeginBatchUpdate();
        sel.Clear();
        sel.SelectRange(Math.Min(_dragAnchorIndex, idx), Math.Max(_dragAnchorIndex, idx));
        sel.EndBatchUpdate();
        e.Handled = true; // ListBox 既定のポインタ選択と競合させない
    }

    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragAnchorIndex = -1;
        _rangeDragging = false;
    }

    // ── 選択行の copy / save ─────────────────────────────────

    /// <summary>選択行を表示順で返す（複数選択は選んだ順になるため index でソート）。</summary>
    private List<FilterRow> SelectedRowsInOrder()
    {
        var rows = new List<(int Index, FilterRow Row)>();
        foreach (var item in RowList.SelectedItems ?? (System.Collections.IList)Array.Empty<object>())
        {
            if (item is FilterRow row)
                rows.Add((RowList.Items.IndexOf(item), row));
        }
        rows.Sort((a, b) => a.Index.CompareTo(b.Index));
        return rows.ConvertAll(r => r.Row);
    }

    /// <summary>選択行の copy/save は常に元ファイルの行番号を先頭に付ける。</summary>
    private static string FormatRow(FilterRow row) =>
        row.IsSeparator ? "⋯"
        : (row.LineNumberText.Length > 0 ? row.LineNumberText + "\t" : "") + row.Text;

    private async System.Threading.Tasks.Task CopySelectedAsync()
    {
        var rows = SelectedRowsInOrder();
        if (rows.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var row in rows) sb.AppendLine(FormatRow(row));
        if (Clipboard is { } clipboard)
            await clipboard.SetTextAsync(sb.ToString());
    }

    private async System.Threading.Tasks.Task SaveSelectedAsync()
    {
        var rows = SelectedRowsInOrder();
        if (rows.Count == 0) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
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

        string baseName = Path.GetFileNameWithoutExtension(_vm.DocumentName);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
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
