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
using Avalonia.Threading;
using UwView.Core;
using UwView.Localization;
using UwView.ViewModels;

namespace UwView.Views;

public partial class MainView : UserControl
{
    private MainViewModel? _vm;
    private DocumentSession? _statusSession;
    private bool _suppressEncodingApply;
    private bool _suppressToggleApply;

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

        // 検索（§11-①②⑥）
        SearchButton.Click += (_, _) => StartSearch();
        SearchBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { StartSearch(); e.Handled = true; } };
        ClearSearchButton.Click += (_, _) => ClearSearch();
        NextHitButton.Click += (_, _) => GoToHit(next: true);
        PrevHitButton.Click += (_, _) => GoToHit(next: false);
        Minimap.JumpRequested += (_, pct) => { TextView.JumpToPercent(pct); TextView.Focus(); };
        TextView.StateChanged += (_, _) => Minimap.ViewOffset = TextView.CurrentOffset;

        // UI 言語切替（§販売戦略 §4）
        LanguageCombo.SelectionChanged += OnLanguageChanged;

        // フィルタ表示（§11-⑤）
        FilterToggle.IsCheckedChanged += (_, _) =>
        {
            if (_suppressToggleApply || _vm?.ActiveTab is not { } t) return;
            t.Session.FilterActive = FilterToggle.IsChecked == true;
            TextView.Refresh();
            UpdateStatus();
        };

        // ブックマーク（§11-④）
        BookmarkToggleButton.Click += (_, _) =>
        {
            if (_vm?.ActiveTab is not { } t) return;
            t.Session.ToggleBookmark(TextView.CurrentOffset);
            TextView.Refresh();
            Minimap.InvalidateVisual();
        };
        NextBookmarkButton.Click += (_, _) => GoToBookmark(next: true);
        PrevBookmarkButton.Click += (_, _) => GoToBookmark(next: false);

        // リアルタイム Tail（§11-③）
        TailToggle.IsCheckedChanged += (_, _) =>
        {
            if (_suppressToggleApply || _vm?.ActiveTab is not { } t) return;
            if (TailToggle.IsChecked == true) { t.Session.StartTail(); TextView.GoToEnd(); }
            else t.Session.StopTail();
        };

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
        {
            _statusSession.IndexProgressChanged -= OnActiveIndexProgress;
            _statusSession.SearchUpdated -= OnSearchUpdated;
            _statusSession.TailGrew -= OnTailGrew;
            if (_statusSession.Source is INotifyDataArrived oldNotify)
                oldNotify.DataArrived -= OnDataArrived;
        }

        TextView.Session = tab?.Session;
        Minimap.Session = tab?.Session;
        _statusSession = tab?.Session;

        if (_statusSession is not null)
        {
            _statusSession.IndexProgressChanged += OnActiveIndexProgress;
            _statusSession.SearchUpdated += OnSearchUpdated;
            _statusSession.TailGrew += OnTailGrew;
            // Blob 等の async I/O 実装: 裏でチャンクが届いたら再描画（WASM 用）
            if (_statusSession.Source is INotifyDataArrived notify)
                notify.DataArrived += OnDataArrived;
        }

        // ツールバーの文字コード選択・検索条件・トグル状態を Active タブのものへ復元
        _suppressEncodingApply = true;
        _vm!.SelectedEncoding = _vm.EncodingOptions.First(
            o => o.Choice == (tab?.SelectedEncoding ?? EncodingChoice.Auto));
        _suppressEncodingApply = false;

        _suppressToggleApply = true;
        FilterToggle.IsChecked = tab?.Session.FilterActive ?? false;
        TailToggle.IsChecked = tab?.Session.IsTailing ?? false;
        TailToggle.IsEnabled = tab?.Session.SupportsTail ?? false;
        _suppressToggleApply = false;

        var search = tab?.Session.ActiveSearch;
        _vm.SearchText = search?.Pattern ?? "";
        _vm.SearchIsRegex = search?.UseRegex ?? false;
        _vm.SearchIgnoreCase = search?.IgnoreCase ?? false;

        UpdateEncodingInfo();
        UpdateSearchInfo();
        UpdateStatus();
        Minimap.ViewOffset = TextView.CurrentOffset;
        if (tab is not null) TextView.Focus();
    }

    // ── 検索（§11-①②⑥）────────────────────────────────────

    private void StartSearch()
    {
        if (_vm?.ActiveTab is not { } tab) return;
        var text = (_vm.SearchText ?? "").Trim();
        if (text.Length == 0) { ClearSearch(); return; }

        _ = tab.Session.StartSearchAsync(new SearchOptions(text, _vm.SearchIsRegex, _vm.SearchIgnoreCase));
        TextView.Refresh(); // ハイライト regex は即時有効
    }

    private void ClearSearch()
    {
        if (_vm?.ActiveTab is not { } tab) return;
        tab.Session.ClearSearch();
        _vm.SearchText = "";
        TextView.Refresh();
        Minimap.InvalidateVisual();
    }

    private void GoToHit(bool next)
    {
        if (_vm?.ActiveTab is not { } tab) return;
        long from = TextView.CurrentOffset;
        long? hit = next ? tab.Session.NextHit(from) : tab.Session.PrevHit(from);
        if (hit is { } off)
        {
            TextView.JumpToOffset(off);
            TextView.Focus();
        }
    }

    private void GoToBookmark(bool next)
    {
        if (_vm?.ActiveTab is not { } tab) return;
        long from = TextView.CurrentOffset;
        long? mark = next ? tab.Session.NextBookmark(from) : tab.Session.PrevBookmark(from);
        if (mark is { } off)
        {
            TextView.JumpToOffset(off);
            TextView.Focus();
        }
    }

    private void OnTailGrew(object? sender, EventArgs e)
    {
        // 追記検知: 末尾追従して再描画（索引も増分拡張済み）
        TextView.GoToEnd();
        Minimap.InvalidateVisual();
        UpdateStatus();
    }

    private void OnSearchUpdated(object? sender, EventArgs e)
    {
        UpdateSearchInfo();
        Minimap.InvalidateVisual();
    }

    private void UpdateSearchInfo()
    {
        if (_vm is null) return;
        var s = _vm.ActiveTab?.Session;
        if (s is null || s.ActiveSearch is null) { _vm.SearchInfo = ""; return; }

        string count = L.Format("SearchHits", N(s.SearchHits.Count));
        if (s.IsSearching)
            _vm.SearchInfo = L.Format("SearchProgress", s.SearchProgress.ToString("P0", L.Culture), N(s.SearchHits.Count));
        else
            _vm.SearchInfo = count + (s.SearchTruncated ? L["SearchTruncated"] : "");
    }

    private void OnActiveIndexProgress(object? sender, EventArgs e) => UpdateStatus();

    private void OnDataArrived(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(() => { TextView.Refresh(); });

    // ── ファイルを開く（複数選択・D&D で一括追加）──────────────

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var sessions = await App.DocumentOpener.PickFilesAsync(top);
        AddSessions(sessions);
    }

    private void AddSessions(IEnumerable<DocumentSession> sessions)
    {
        if (_vm is null) return;
        DocumentTabViewModel? last = null;
        foreach (var session in sessions)
        {
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

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return;
        var sessions = files.Select(f => f.TryGetLocalPath())
                            .OfType<string>()
                            .Select(App.DocumentOpener.OpenLocalPath)
                            .OfType<DocumentSession>()
                            .ToList();
        AddSessions(sessions);
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

    private static Localizer L => Localizer.Instance;
    private static string N(long n) => n.ToString("N0", L.Culture);

    private void UpdateEncodingInfo()
    {
        if (_vm?.ActiveTab is not { } tab) { if (_vm is not null) _vm.EncodingInfo = ""; return; }
        var name = tab.Session.Document.Encoding.WebName;
        _vm.EncodingInfo = tab.SelectedEncoding == EncodingChoice.Auto
            ? L.Format("EncodingInfoAuto", name, tab.Session.DetectedEncoding.DisplayName)
            : L.Format("EncodingInfoManual", name);
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

        if (s.FilterActive && s.SearchHits.Count > 0)
        {
            _vm.ModeInfo = L["ModeFilter"];
            _vm.PositionInfo = L.Format("PositionFilter", N(s.SearchHits.Count), N(s.FilterTopHitIndex + 1));
        }
        else if (TextView.Mode == ViewMode.Line && TextView.TotalLines is { } total)
        {
            _vm.ModeInfo = L["ModeLine"];
            _vm.PositionInfo = L.Format("PositionLine", N(total), N(TextView.TopLine + 1));
        }
        else
        {
            _vm.ModeInfo = L["ModePage"];
            _vm.PositionInfo = L.Format("PositionPage",
                (TextView.Percent * 100).ToString("0.0", L.Culture), N(TextView.TopByteOffset / 1024));
        }
    }

    // ── UI 言語切替（§販売戦略 §4）────────────────────────────

    private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_vm?.SelectedLanguage is not { } lang) return;
        L.SetLanguage(lang.Code);
        App.Settings.Language = lang.Code;
        App.Settings.Save();

        // 文字コード「自動」ラベルを言語に追従（他は universal 名なので不変）
        _suppressEncodingApply = true;
        bool autoSelected = _vm.SelectedEncoding?.Choice == EncodingChoice.Auto;
        _vm.EncodingOptions[0] = new EncodingOption(EncodingChoice.Auto, L["EncodingAuto"]);
        if (autoSelected) _vm.SelectedEncoding = _vm.EncodingOptions[0];
        _suppressEncodingApply = false;

        // 動的文字列（コード生成分）を今の言語で作り直す
        _vm.RaiseFilePathChanged();
        UpdateEncodingInfo();
        UpdateSearchInfo();
        UpdateStatus();
    }
}
