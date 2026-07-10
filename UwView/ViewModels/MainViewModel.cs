using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UwView.Localization;

namespace UwView.ViewModels;

public enum EncodingChoice { Auto, Utf8, Utf8Bom, ShiftJis, EucJp, Utf16Le, Utf16Be }

public sealed record EncodingOption(EncodingChoice Choice, string Label)
{
    public override string ToString() => Label;
}

/// <summary>UI 言語の選択肢。Label は各言語の自称（切替後も判別できるよう固定表記）。</summary>
public sealed record LanguageOption(string Code, string Label)
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

    // 検索（§11-①②）
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _searchIsRegex;
    [ObservableProperty] private bool _searchIgnoreCase;
    [ObservableProperty] private string _searchInfo = "";

    public ObservableCollection<EncodingOption> EncodingOptions { get; } =
    [
        new(EncodingChoice.Auto, Localizer.Instance["EncodingAuto"]),
        new(EncodingChoice.Utf8, "UTF-8"),
        new(EncodingChoice.Utf8Bom, "UTF-8 (BOM)"),
        new(EncodingChoice.ShiftJis, "Shift-JIS"),
        new(EncodingChoice.EucJp, "EUC-JP"),
        new(EncodingChoice.Utf16Le, "UTF-16LE"),
        new(EncodingChoice.Utf16Be, "UTF-16BE"),
    ];

    [ObservableProperty] private EncodingOption _selectedEncoding = null!;

    // UI 言語（§販売戦略 §4）
    public ObservableCollection<LanguageOption> Languages { get; } =
    [
        new("ja", "日本語"),
        new("en", "English"),
    ];

    [ObservableProperty] private LanguageOption _selectedLanguage = null!;

    public bool HasTabs => Tabs.Count > 0;
    public string FilePath => ActiveTab?.FilePath ?? Localizer.Instance["NoFile"];

    /// <summary>タブの × ボタンから発火。実際の破棄は View 側で行う。</summary>
    public event EventHandler<DocumentTabViewModel>? CloseTabRequested;
    public void RequestClose(DocumentTabViewModel tab) => CloseTabRequested?.Invoke(this, tab);

    public MainViewModel()
    {
        _selectedEncoding = EncodingOptions[0];
        var cur = Localizer.Instance.Culture.TwoLetterISOLanguageName;
        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == cur) ?? Languages[1];
        Tabs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTabs));
    }

    partial void OnActiveTabChanged(DocumentTabViewModel? value)
    {
        OnPropertyChanged(nameof(FilePath));
    }

    /// <summary>言語切替時にコード生成の派生プロパティを更新する。</summary>
    public void RaiseFilePathChanged() => OnPropertyChanged(nameof(FilePath));
}
