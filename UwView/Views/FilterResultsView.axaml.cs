using System;
using System.Linq;
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
        // 抽出保存（F4-3）: 開いた時点で前回の指定を入れ直す。
        // 専用のボタンは置かない——「保存」と押した後の動きが同じで、
        // チェックを触っていないかぎり見分けがつかないため（オーナー判断 2026-09-08）。
        // 復元は UVP のみ。UVF はこのオプションを出していないので触らない
        if (_vm.AllowExtractOptions) UwView.Services.ExtractSaveOptions.ApplyTo(_vm);
        CancelSaveButton.Click += (_, _) => _vm.CancelSave();
        CloseButton.Click += (_, _) => CloseRequested?.Invoke();
        ChangeLimitButton.Click += (_, _) => ViewModels.FilterResultsViewModel.OpenSearchLimitSettings?.Invoke();

        RowList.AttachScrollBar(RowScroll);
        RowList.AttachHScrollBar(RowHScroll);
        RowList.Rows = _vm.Rows;
        RowList.RowActivated += row => _vm.Jump(row);
        RowList.CopyRequested += () => _ = CopySelectedAsync();
        RowList.SelectionMenuRequested += ShowSelectionMenu;

        _vm.PropertyChanged += OnVmPropertyChanged;

        // 件数表示（「1/60 件」など）はコードで組み立てて焼き付くので、
        // 言語を切り替えたら作り直す。XAML の {loc:Localize} は自動で追従する
        void OnLanguage(object? _, System.ComponentModel.PropertyChangedEventArgs __) => _vm.RefreshTexts();
        Localizer.Instance.PropertyChanged += OnLanguage;
        _detach += () => Localizer.Instance.PropertyChanged -= OnLanguage;

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

    /// <summary>
    /// 右クリックメニューへ項目を足すためのフック（Pro 拡張用）。
    /// 引数は選択先頭行のジャンプ先オフセット。null を返せば何も足さない。
    /// </summary>
    public Func<long, IReadOnlyList<MenuItem>?>? ExtraMenuItems { get; set; }

    /// <summary>選択行の右クリック → コピー / ファイルに保存（＋Pro 拡張の項目）。</summary>
    private void ShowSelectionMenu(PointerPressedEventArgs e)
    {
        var menu = new MenuFlyout();
        var copyItem = new MenuItem { Header = Localizer.Instance["MenuCopy"] };
        copyItem.Click += (_, _) => _ = CopySelectedAsync();
        var saveItem = new MenuItem { Header = Localizer.Instance["MenuSaveAs"] };
        saveItem.Click += (_, _) => _ = SaveSelectedAsync();
        menu.Items.Add(copyItem);
        menu.Items.Add(saveItem);

        if (ExtraMenuItems is not null
            && RowList.SelectedRowsInOrder().FirstOrDefault(r => r.IsHit) is { } hit
            && ExtraMenuItems(hit.ResolveJumpOffset()) is { Count: > 0 } extra)
        {
            menu.Items.Add(new Separator());
            foreach (var item in extra) menu.Items.Add(item);
        }
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
        _detach?.Invoke();
        _detach = null;
    }

    /// <summary>閉じるときに外す購読（言語切替など）。</summary>
    private Action? _detach;

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
    ///
    /// 本文は<b>取得を待って</b>読む。表示用の同期 API はブラウザー版で未取得のチャンクを
    /// 空文字として返すので、そのまま書くと保存・コピーの結果が空行になる
    /// （再レビュー 2026-09-19 の指摘D。一覧全体の保存は先に直してあった）。
    /// </summary>
    private async System.Threading.Tasks.Task<string?> FormatRowAsync(
        FilterRow row, System.Threading.CancellationToken ct = default)
    {
        if (row.IsSeparator) return "⋯";
        if (await row.GetTextForSaveAsync(ct) is not { } text) return null;
        return _vm.IncludeLineNumbersOnSave && row.LineNumberText.Length > 0
            ? row.LineNumberText + "\t" + text
            : text;
    }

    private async System.Threading.Tasks.Task CopySelectedAsync()
    {
        var rows = SelectedRowsInOrder();
        if (rows.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            if (await FormatRowAsync(row) is not { } line) return;   // 読めなければコピーしない
            sb.AppendLine(line);
        }
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
                string line = await FormatRowAsync(rows[i])
                    ?? throw new IOException("選択行の本文を取得できませんでした。保存を中止します。");
                await writer.WriteLineAsync(line);
                if ((i & 1023) == 1023) await System.Threading.Tasks.Task.Yield();
            }
        }
        catch (IOException failure)
        {
            // 本文を取得できなかった場合もここへ来る（空行を書いて済ませない。指摘D）
            UwView.Services.OperationLog.Record("選択行の保存（失敗）", TimeSpan.Zero, failure.Message);
        }
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

        if (_vm.AllowExtractOptions)
            UwView.Services.ExtractSaveOptions.Remember(_vm);   // 次に開いたとき同じ指定で始める

        bool ja = Localizer.Instance.Culture.TwoLetterISOLanguageName == "ja";
        int rows = _vm.Rows.Count;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await using var stream = await file.OpenWriteAsync();
            await _vm.SaveAsync(stream, new UTF8Encoding(false));
            watch.Stop();
            UwView.Services.OperationLog.Record(ja ? "抽出保存" : "Save extract", watch.Elapsed,
                ja ? $"{rows:N0} 行" : $"{rows:N0} lines");
        }
        catch (OperationCanceledException) { /* キャンセル: 途中までのファイルが残る */ }
        catch (IOException failure)
        {
            // 本文を取得できなかった場合もここへ来る（空行を書いて済ませない。指摘4）
            UwView.Services.OperationLog.Record(ja ? "抽出保存（失敗）" : "Save extract (failed)",
                                                watch.Elapsed, failure.Message);
        }
    }
}
