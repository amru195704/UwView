using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using UwView.Controls;
using UwView.Core;
using UwView.ViewModels;

namespace UwView.Views;

public partial class MainView : UserControl
{
    private LineDocument? _doc;
    private DetectedEncoding? _detected;
    private CancellationTokenSource? _indexCts;

    private MainViewModel Vm => (MainViewModel)DataContext!;

    public MainView()
    {
        InitializeComponent();

        TextView.AttachScrollBar(VScroll);
        TextView.StateChanged += (_, _) => UpdateStatus();

        OpenButton.Click += OnOpenClick;
        JumpButton.Click += OnJumpClick;
        CancelButton.Click += (_, _) => _indexCts?.Cancel();
        LineNumberCheck.IsCheckedChanged += (_, _) =>
        {
            TextView.ShowLineNumbers = LineNumberCheck.IsChecked ?? true;
            TextView.InvalidateVisual();
        };
        EncodingCombo.SelectionChanged += OnEncodingChanged;
    }

    // ── ファイルを開く ────────────────────────────────────────

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "テキストファイルを開く",
            AllowMultiple = false
        });
        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (path is null) return;

        await OpenFileAsync(path);
    }

    private async Task OpenFileAsync(string path)
    {
        // 前のドキュメント/索引タスクを破棄
        _indexCts?.Cancel();
        if (_doc is not null) await _doc.DisposeAsync();

        var src = new MmapByteSource(path);
        _detected = EncodingDetector.Detect(src);          // BOM/改行/文字コード判定（一瞬）
        var newline = EncodingDetector.DetectNewline(src);

        var enc = _detected with { Encoding = ResolveEncoding(_detected, Vm.SelectedEncoding.Choice) };
        _doc = new LineDocument(src, enc, newline);

        // §3.1-1 索引を待たず即ページモード表示
        TextView.Document = _doc;
        TextView.ShowLineNumbers = LineNumberCheck.IsChecked ?? true;
        TextView.Focus();

        Vm.FilePath = path;
        Vm.EncodingInfo = $"文字コード: {_doc.Encoding.WebName} " +
                          (Vm.SelectedEncoding.Choice == EncodingChoice.Auto ? $"（自動: {_detected.DisplayName}）" : "（手動）");
        UpdateStatus();

        // §3.1-2 裏で索引構築 → 完了で行モード昇格
        _indexCts = new CancellationTokenSource();
        var ct = _indexCts.Token;
        var progress = new Progress<double>(p => Vm.IndexProgress = p);
        Vm.IsIndexing = true;
        Vm.IndexProgress = 0;
        try
        {
            await _doc.BuildIndexAsync(progress, ct);
            if (!ct.IsCancellationRequested && TextView.Document == _doc)
            {
                TextView.PromoteToLineMode(); // §3.1-3
                UpdateStatus();
            }
        }
        catch (OperationCanceledException) { /* キャンセルはページモードのまま継続 */ }
        finally { Vm.IsIndexing = false; }
    }

    // ── 文字コード手動切替（索引再構築なし §4.6）──────────────

    private void OnEncodingChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_doc is null || _detected is null) return;
        _doc.Encoding = ResolveEncoding(_detected, Vm.SelectedEncoding.Choice);
        Vm.EncodingInfo = $"文字コード: {_doc.Encoding.WebName} " +
                          (Vm.SelectedEncoding.Choice == EncodingChoice.Auto ? $"（自動: {_detected.DisplayName}）" : "（手動）");
        TextView.InvalidateVisual();
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
        if (_doc is null) return;
        var text = (Vm.JumpText ?? "").Trim();
        if (text.Length == 0) return;

        if (text.EndsWith('%'))
        {
            if (double.TryParse(text.TrimEnd('%').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                TextView.JumpToPercent(pct / 100.0);
        }
        else if (TextView.Mode == NavigationMode.Line &&
                 long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var line))
        {
            TextView.JumpToLine(line - 1); // 1 始まり入力 → 0 始まり index
        }
        else if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var off))
        {
            TextView.JumpToPercent(_doc.Length > 0 ? (double)off / _doc.Length : 0); // ページモードはバイト位置扱い
        }
        TextView.Focus();
    }

    // ── ステータス更新 ────────────────────────────────────────

    private void UpdateStatus()
    {
        if (_doc is null) return;
        if (TextView.Mode == NavigationMode.Line && TextView.TotalLines is { } total)
        {
            Vm.ModeInfo = "行モード";
            Vm.PositionInfo = $"総行数 {total:N0} ・ 先頭 {TextView.TopLine + 1:N0} 行目";
        }
        else
        {
            Vm.ModeInfo = "ページモード";
            Vm.PositionInfo = $"{TextView.Percent * 100:0.0}% / {TextView.TopByteOffset:N0} byte";
        }
    }
}
