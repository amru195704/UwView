using System;
using Avalonia.Controls;
using UwView.ViewModels;

namespace UwView.Views;

/// <summary>
/// 色分けハイライタ管理ダイアログ（デスクトップ用の Window ホスト）。
/// 中身は HighlighterView。ブラウザ(WASM)では Window を使えないため、
/// MainView が HighlighterView を直接オーバーレイ表示する。
/// </summary>
public partial class HighlighterWindow : Window
{
    private readonly HighlighterView _view;

    /// <summary>「保存」「設定」で確定したとき（呼び出し側で App.Settings.Save して適用）。</summary>
    public Action? Applied { get => _view.Applied; set => _view.Applied = value; }
    /// <summary>「キャンセル」または閉じるで破棄したとき（呼び出し側で編集前へ戻す）。</summary>
    public Action? Cancelled { get => _view.Cancelled; set => _view.Cancelled = value; }

    public HighlighterWindow(HighlighterViewModel vm)
    {
        InitializeComponent();
        _view = new HighlighterView(vm);
        _view.CloseRequested += () => Close();
        Content = _view;
        Closed += (_, _) => _view.NotifyClosed();
    }

    // XAMLプレビュー用
    public HighlighterWindow() : this(new HighlighterViewModel(new Core.HlSet())) { }
}
