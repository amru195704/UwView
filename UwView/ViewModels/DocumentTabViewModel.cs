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
    public string DisplayName => Session.DisplayName;
    public string FilePath => Session.FilePath;

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
        CloseCommand = new RelayCommand(() => _onClose(this));
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
