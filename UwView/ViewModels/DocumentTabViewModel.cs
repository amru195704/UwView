using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UwView.Core;

namespace UwView.ViewModels;

/// <summary>1 タブ = 1 <see cref="DocumentSession"/> のラッパ（表示名・索引中インジケータ・選択文字コードを保持）。</summary>
public partial class DocumentTabViewModel : ObservableObject, IAsyncDisposable
{
    private readonly Action<DocumentTabViewModel> _onClose;

    public DocumentSession Session { get; }
    public string DisplayName => TitlePrefix + Session.DisplayName;
    public string FilePath => Session.FilePath;

    /// <summary>タブに出す名前の長さの上限（番号を含む。越えたら末尾を … にする）。</summary>
    public const int MaxTabTitleLength = 27;

    /// <summary>タブに出す名前（<c>番号:ファイル名</c> を 27 文字まで。オーナー指示 2026-09-27）。</summary>
    public string TabTitle => DisplayName.Length <= MaxTabTitleLength
        ? DisplayName
        : DisplayName[..(MaxTabTitleLength - 3)] + "...";

    /// <summary>タブにマウスを重ねたときの説明（ファイル名。切らずに出す。2行目は場所）。</summary>
    public string TabToolTip => string.IsNullOrEmpty(FilePath) ? Session.DisplayName : $"{Session.DisplayName}\n{FilePath}";

    /// <summary>タブ名の前に付ける番号（ファイル一覧から開いたタブは <c>3:</c>。どのファイルか分かるように）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(TabTitle))]
    private string _titlePrefix = "";

    /// <summary>
    /// 閉じられるか。結果セット・束ねた索引のメインは false（閉じるボタンを出さない。
    /// 実装指示書_uvf複数ファイルGUI表示とタブ §5.5）。
    /// </summary>
    [ObservableProperty] private bool _canClose = true;

    [ObservableProperty] private double _indexProgress;
    [ObservableProperty] private bool _isIndexing;

    /// <summary>このタブで選択中の文字コード（タブ切替時にツールバーへ復元）。</summary>
    public EncodingChoice SelectedEncoding { get; set; } = EncodingChoice.Auto;

    /// <summary>このタブに適用する色分けハイライタのセットID（V1.1.1: タブ別ハイライタ。null=既定）。</summary>
    public string? HighlighterSetId { get; set; }

    public IRelayCommand CloseCommand { get; }

    public DocumentTabViewModel(DocumentSession session, Action<DocumentTabViewModel> onClose)
    {
        Session = session;
        _onClose = onClose;
        IsIndexing = session.IsIndexing;
        Session.IndexProgressChanged += OnIndexProgress;
        CloseCommand = new RelayCommand(() => { if (CanClose) _onClose(this); });
    }

    private void OnIndexProgress(object? sender, EventArgs e)
    {
        IndexProgress = Session.IndexProgress;
        IsIndexing = Session.IsIndexing;
    }

    public ValueTask DisposeAsync()
    {
        Session.IndexProgressChanged -= OnIndexProgress;
        return Session.DisposeAsync();
    }
}
