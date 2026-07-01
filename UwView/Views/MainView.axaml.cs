using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using UwView.Core;
using UwView.ViewModels;

namespace UwView.Views;

public partial class MainView : UserControl
{
    private MainViewModel? _vm;
    private DocumentSession? _statusSession;
    private bool _suppressEncodingApply;

    public MainView()
    {
        InitializeComponent();

        TextView.AttachScrollBar(VScroll);
        TextView.StateChanged += (_, _) => UpdateStatus();

        OpenButton.Click += OnOpenClick;
        JumpButton.Click += OnJumpClick;
        CancelButton.Click += (_, _) => _vm?.ActiveTab?.Session.CancelIndex();
        LineNumberCheck.IsCheckedChanged += (_, _) =>
        {
            TextView.ShowLineNumbers = LineNumberCheck.IsChecked ?? true;
            TextView.InvalidateVisual();
        };
        EncodingCombo.SelectionChanged += OnEncodingChanged;

        DataContextChanged += OnDataContextChanged;
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.CloseTabRequested -= OnCloseTabRequested;
        }
        _vm = DataContext as MainViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.CloseTabRequested += OnCloseTabRequested;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActiveTab))
            SwitchActive(_vm?.ActiveTab);
    }

    // ── タブ切替（状態保持で即時・索引再構築なし §4.7）──────────

    private void SwitchActive(DocumentTabViewModel? tab)
    {
        if (_statusSession is not null)
            _statusSession.IndexProgressChanged -= OnActiveIndexProgress;

        TextView.Session = tab?.Session;
        _statusSession = tab?.Session;

        if (_statusSession is not null)
            _statusSession.IndexProgressChanged += OnActiveIndexProgress;

        // ツールバーの文字コード選択を Active タブのものへ復元
        _suppressEncodingApply = true;
        _vm!.SelectedEncoding = _vm.EncodingOptions.First(
            o => o.Choice == (tab?.SelectedEncoding ?? EncodingChoice.Auto));
        _suppressEncodingApply = false;

        UpdateEncodingInfo();
        UpdateStatus();
        if (tab is not null) TextView.Focus();
    }

    private void OnActiveIndexProgress(object? sender, EventArgs e) => UpdateStatus();

    // ── ファイルを開く（複数選択・D&D で一括追加）──────────────

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "テキストファイルを開く",
            AllowMultiple = true
        });
        await OpenPathsAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    private async Task OpenPathsAsync(IEnumerable<string> paths)
    {
        if (_vm is null) return;
        DocumentTabViewModel? last = null;
        foreach (var path in paths)
        {
            DocumentSession session;
            try { session = DocumentSession.Open(path); }
            catch { continue; } // 開けないファイルはスキップ

            var tab = new DocumentTabViewModel(session, t => _vm!.RequestClose(t));
            session.IndexCompleted += (_, _) => OnIndexCompleted(tab);
            _vm.Tabs.Add(tab);
            last = tab;

            _ = session.BuildIndexAsync(); // 各タブ独立に背景索引
        }
        if (last is not null)
            _vm.ActiveTab = last;
    }

    private void OnIndexCompleted(DocumentTabViewModel tab)
    {
        if (_vm?.ActiveTab == tab)
        {
            TextView.Refresh(); // ページモード→行モード昇格を反映
            UpdateStatus();
        }
    }

    // ── タブを閉じる（Dispose で mmap 解放）────────────────────

    private async void OnCloseTabRequested(object? sender, DocumentTabViewModel tab)
    {
        if (_vm is null) return;
        int idx = _vm.Tabs.IndexOf(tab);
        bool wasActive = _vm.ActiveTab == tab;
        _vm.Tabs.Remove(tab);

        if (wasActive)
        {
            _vm.ActiveTab = _vm.Tabs.Count > 0
                ? _vm.Tabs[Math.Min(idx, _vm.Tabs.Count - 1)]
                : null;
            if (_vm.ActiveTab is null)
            {
                TextView.Session = null;
                UpdateStatus();
            }
        }
        await tab.DisposeAsync();
    }

    // ── ドラッグ&ドロップ ─────────────────────────────────────

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return;
        await OpenPathsAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    // ── 文字コード手動切替（索引再構築なし §4.6）──────────────

    private void OnEncodingChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEncodingApply || _vm?.ActiveTab is not { } tab || _vm.SelectedEncoding is null) return;
        tab.SelectedEncoding = _vm.SelectedEncoding.Choice;
        tab.Session.SetEncoding(ResolveEncoding(tab.Session.DetectedEncoding, tab.SelectedEncoding));
        UpdateEncodingInfo();
        TextView.Refresh();
    }

    private static Encoding ResolveEncoding(DetectedEncoding detected, EncodingChoice choice) => choice switch
    {
        EncodingChoice.Auto => detected.Encoding,
        EncodingChoice.Utf8 or EncodingChoice.Utf8Bom => new UTF8Encoding(false),
        EncodingChoice.ShiftJis => EncodingDetector.ShiftJis,
        EncodingChoice.EucJp => EncodingDetector.EucJp,
        EncodingChoice.Utf16Le => Encoding.Unicode,
        EncodingChoice.Utf16Be => Encoding.BigEndianUnicode,
        _ => detected.Encoding
    };

    // ── ジャンプ ──────────────────────────────────────────────

    private void OnJumpClick(object? sender, RoutedEventArgs e)
    {
        if (_vm?.ActiveTab is not { } tab) return;
        var doc = tab.Session.Document;
        var text = (_vm.JumpText ?? "").Trim();
        if (text.Length == 0) return;

        if (text.EndsWith('%'))
        {
            if (double.TryParse(text.TrimEnd('%').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                TextView.JumpToPercent(pct / 100.0);
        }
        else if (TextView.Mode == ViewMode.Line &&
                 long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var line))
        {
            TextView.JumpToLine(line - 1); // 1 始まり入力 → 0 始まり index
        }
        else if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var off))
        {
            TextView.JumpToPercent(doc.Length > 0 ? (double)off / doc.Length : 0); // ページモードはバイト位置扱い
        }
        TextView.Focus();
    }

    // ── ステータス更新 ────────────────────────────────────────

    private void UpdateEncodingInfo()
    {
        if (_vm?.ActiveTab is not { } tab) { if (_vm is not null) _vm.EncodingInfo = ""; return; }
        var choice = tab.SelectedEncoding;
        _vm.EncodingInfo = $"文字コード: {tab.Session.Document.Encoding.WebName} " +
                           (choice == EncodingChoice.Auto ? $"（自動: {tab.Session.DetectedEncoding.DisplayName}）" : "（手動）");
    }

    private void UpdateStatus()
    {
        if (_vm is null) return;
        if (_vm.ActiveTab is not { } tab)
        {
            _vm.ModeInfo = ""; _vm.PositionInfo = ""; _vm.EncodingInfo = "";
            _vm.IsIndexing = false; _vm.IndexProgress = 0;
            return;
        }

        var s = tab.Session;
        _vm.IsIndexing = s.IsIndexing;
        _vm.IndexProgress = s.IndexProgress;

        if (TextView.Mode == ViewMode.Line && TextView.TotalLines is { } total)
        {
            _vm.ModeInfo = "行モード";
            _vm.PositionInfo = $"総行数 {total:N0} ・ 先頭 {TextView.TopLine + 1:N0} 行目";
        }
        else
        {
            _vm.ModeInfo = "ページモード";
            _vm.PositionInfo = $"{TextView.Percent * 100:0.0}% / {TextView.TopByteOffset:N0} byte";
        }
    }
}
