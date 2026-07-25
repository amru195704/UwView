using System;
using System.Globalization;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UwView.Core;
using UwView.Localization;
using UwView.ViewModels;

namespace UwView.Views;

/// <summary>
/// 色分けハイライタ管理ダイアログの中身（UserControl）。
/// デスクトップは HighlighterWindow がホスト、ブラウザ(WASM)は OverlayLayer がホストする。
/// Window 固有機能（Close・名前プロンプトの ShowDialog）はホスト種別で分岐する。
/// </summary>
public partial class HighlighterView : UserControl
{
    private readonly HighlighterViewModel _vm;
    private bool _committed;

    /// <summary>閉じる要求（ホストが Window.Close / オーバーレイ除去を行う）。</summary>
    public event Action? CloseRequested;
    /// <summary>「保存」「設定」で確定したとき（呼び出し側で App.Settings.Save して適用）。</summary>
    public Action? Applied { get; set; }
    /// <summary>「キャンセル」または閉じるで破棄したとき（呼び出し側で編集前へ戻す）。</summary>
    public Action? Cancelled { get; set; }

    public HighlighterView(HighlighterViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();

        AddButton.Click += (_, _) => _vm.AddRule();
        ExportButton.Click += OnExportClick;
        ImportButton.Click += OnImportClick;
        SaveButton.Click += OnSaveClick;                       // 保存: 名前を指定して保存＆閉じる
        ApplyButton.Click += (_, _) => { _vm.CommitToActive(); Commit(); }; // 設定: 現アクティブへ確定＆閉じる
        CancelButton.Click += (_, _) => CloseRequested?.Invoke(); // キャンセル: 破棄（NotifyClosed で Cancelled）
        ReloadButton.Click += (_, _) => _vm.ReloadSelected();  // 選択中の保存済みセットを再現

        // 行内ボタン（▲▼✕）は Classes で識別して一括処理
        RuleList.AddHandler(Button.ClickEvent, OnRowButtonClick);
    }

    // XAMLプレビュー用
    public HighlighterView() : this(new HighlighterViewModel(new Core.HlSet())) { }

    private void Commit()
    {
        _committed = true;
        Applied?.Invoke();
        CloseRequested?.Invoke();
    }

    /// <summary>ホストが実際に閉じたときに呼ぶ（未確定なら破棄＝Cancelled）。多重呼び出し安全。</summary>
    public void NotifyClosed()
    {
        if (_committed) return;
        _committed = true; // 二重発火防止
        Cancelled?.Invoke();
    }

    private void OnRowButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button b || b.Tag is not HlRuleRow row) return;
        if (b.Classes.Contains("moveUp")) _vm.MoveUp(row);
        else if (b.Classes.Contains("moveDown")) _vm.MoveDown(row);
        else if (b.Classes.Contains("delRule")) _vm.RemoveRule(row);
        else if (b.Classes.Contains("fgUp")) Apply(row, isBg: false, brighter: true);   // 文字色 ▲＝明るく
        else if (b.Classes.Contains("fgDown")) Apply(row, isBg: false, brighter: false); // 文字色 ▼＝暗く
        else if (b.Classes.Contains("bgUp")) Apply(row, isBg: true, brighter: true);     // 背景色 ▲＝明るく
        else if (b.Classes.Contains("bgDown")) Apply(row, isBg: true, brighter: false);  // 背景色 ▼＝暗く
    }

    private static void Apply(HlRuleRow row, bool isBg, bool brighter)
    {
        string current = isBg ? row.Background : row.Foreground;
        string adjusted = Adjust(current, brighter, defaultForBg: isBg);
        if (isBg) row.Background = adjusted; else row.Foreground = adjusted;
    }

    /// <summary>各チャンネルを ±0x10 して明るく/暗くする（#RRGGBB を返す。0〜255でクランプ）。</summary>
    private static string Adjust(string? hex, bool brighter, bool defaultForBg)
    {
        uint argb = CompiledHighlighter.ParseColor(hex);
        if (argb == 0) argb = defaultForBg ? 0xFF808080u : 0xFF000000u; // 背景=グレー / 文字=黒 から開始
        int r = (byte)(argb >> 16), g = (byte)(argb >> 8), b = (byte)argb;
        int d = brighter ? 0x10 : -0x10;
        r = Math.Clamp(r + d, 0, 255);
        g = Math.Clamp(g + d, 0, 255);
        b = Math.Clamp(b + d, 0, 255);
        return string.Create(CultureInfo.InvariantCulture, $"#{r:X2}{g:X2}{b:X2}");
    }

    /// <summary>「保存」= 名前を尋ねて（既定＝現在のプリセット名）そのセットとして保存＆閉じる。
    /// ブラウザ(WASM)は入れ子 Window を開けないため、現在名でそのまま保存する。</summary>
    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            var prompt = new NamePromptWindow(_vm.SaveNameCandidates(), _vm.CurrentName);
            var name = await prompt.ShowDialog<string?>(owner);
            if (string.IsNullOrWhiteSpace(name)) return;
            _vm.SaveAs(name);
        }
        else
        {
            // ブラウザ: 名前プロンプト無しで現在名に保存
            if (string.IsNullOrWhiteSpace(_vm.CurrentName)) return;
            _vm.SaveAs(_vm.CurrentName);
        }
        Commit(); // 永続化＆閉じる
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = Localizer.Instance["HlExport"],
            SuggestedFileName = "highlighters.uwvhl",
            DefaultExtension = "uwvhl",
        });
        if (file is null) return;
        try
        {
            var json = Services.HighlighterIo.Export(new[] { _vm.Set });
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json);
        }
        catch (IOException) { /* 書き込み失敗は黙って中断 */ }
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = Localizer.Instance["HlImport"],
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("UwView highlighters") { Patterns = new[] { "*.uwvhl", "*.json" } },
            },
        });
        if (files.Count == 0) return;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            string json = await reader.ReadToEndAsync();
            var sets = Services.HighlighterIo.Import(json, new[] { _vm.Set.Id });
            if (sets.Count > 0)
                foreach (var r in sets[0].Rules)
                    _vm.AppendRule(r);
        }
        catch (IOException) { /* 読み込み失敗は黙って中断 */ }
    }
}
