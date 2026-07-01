using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace UwView.ViewModels;

public enum EncodingChoice { Auto, Utf8, Utf8Bom, ShiftJis, EucJp, Utf16Le, Utf16Be }

public sealed record EncodingOption(EncodingChoice Choice, string Label)
{
    public override string ToString() => Label;
}

public partial class MainViewModel : ViewModelBase
{
    // タブ集合＋Active（§4.7）
    public ObservableCollection<DocumentTabViewModel> Tabs { get; } = [];

    [ObservableProperty] private DocumentTabViewModel? _activeTab;

    // ステータス（Active タブを反映）
    [ObservableProperty] private string _positionInfo = "";
    [ObservableProperty] private string _encodingInfo = "";
    [ObservableProperty] private string _modeInfo = "";
    [ObservableProperty] private bool _isIndexing;
    [ObservableProperty] private double _indexProgress;

    [ObservableProperty] private string _jumpText = "";

    public ObservableCollection<EncodingOption> EncodingOptions { get; } =
    [
        new(EncodingChoice.Auto, "自動判定"),
        new(EncodingChoice.Utf8, "UTF-8"),
        new(EncodingChoice.Utf8Bom, "UTF-8 (BOM)"),
        new(EncodingChoice.ShiftJis, "Shift-JIS"),
        new(EncodingChoice.EucJp, "EUC-JP"),
        new(EncodingChoice.Utf16Le, "UTF-16LE"),
        new(EncodingChoice.Utf16Be, "UTF-16BE"),
    ];

    [ObservableProperty] private EncodingOption _selectedEncoding = null!;

    public bool HasTabs => Tabs.Count > 0;
    public string FilePath => ActiveTab?.FilePath ?? "（ファイル未選択）";

    /// <summary>タブの × ボタンから発火。実際の破棄は View 側で行う。</summary>
    public event EventHandler<DocumentTabViewModel>? CloseTabRequested;
    public void RequestClose(DocumentTabViewModel tab) => CloseTabRequested?.Invoke(this, tab);

    public MainViewModel()
    {
        _selectedEncoding = EncodingOptions[0];
        Tabs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTabs));
    }

    partial void OnActiveTabChanged(DocumentTabViewModel? value)
    {
        OnPropertyChanged(nameof(FilePath));
    }
}
