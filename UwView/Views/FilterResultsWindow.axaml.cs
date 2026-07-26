using Avalonia.Controls;
using UwView.Localization;
using UwView.ViewModels;

namespace UwView.Views;

/// <summary>
/// フィルタ結果ポップアップ（デスクトップ用の Window ホスト）。
/// 中身は FilterResultsView。Window 固有機能（Close/Title）を View から受けて適用する。
/// ブラウザ(WASM)では Window を使えないため、MainView が FilterResultsView を直接オーバーレイ表示する。
/// </summary>
public partial class FilterResultsWindow : Window
{
    private readonly FilterResultsView _view;

    public FilterResultsWindow(FilterResultsViewModel vm)
    {
        InitializeComponent();
        _view = new FilterResultsView(vm);
        _view.CloseRequested += () => Close();
        _view.TitleChanged += t => Title = t;
        Title = _view.CurrentTitle.Length > 0 ? _view.CurrentTitle : Localizer.Instance["FilterResultsTitle"];
        Content = _view;
        Closed += (_, _) => _view.DisposeView();
    }

    // XAMLプレビュー用（実行時は上のコンストラクタのみ使用）
    public FilterResultsWindow() : this(new FilterResultsViewModel(_ => { }, maxContext: 0)) { }

    // ── Pro 拡張用フック（PopupRectSelection.Attach から利用）。内側 View へ委譲 ────
    public Avalonia.Controls.Panel ToolbarHost => _view.ToolbarHost;
    public Avalonia.Controls.Panel ListHost => _view.ListHost;
    public ListBox ResultsList => _view.ResultsList;
    public FilterResultsViewModel ViewModel => _view.ViewModel;
}
