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

/// <summary>スタート画面の1項目（最近使ったファイル・お気に入り）。</summary>
public sealed record StartItem(string Path)
{
    public string DisplayName => System.IO.Path.GetFileName(Path);
    public override string ToString() => DisplayName;
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

    // Ver1.1-A: 検索履歴（オートコンプリート）・定義済みフィルタ
    public ObservableCollection<string> SearchHistory { get; } = [];
    public ObservableCollection<Services.PredefinedFilter> PredefinedFilters { get; } = [];
    [ObservableProperty] private Services.PredefinedFilter? _selectedPredefinedFilter;

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

    // V1.1.1: スタート画面（最近使ったファイル・お気に入り）＋一時通知
    public ObservableCollection<StartItem> StartRecent { get; } = [];
    public ObservableCollection<StartItem> StartFavorites { get; } = [];
    [ObservableProperty] private string _notice = "";

    /// <summary>AppSettings の Recent/Favorites をスタート画面用コレクションへ反映。</summary>
    public void RefreshStartLists()
    {
        StartRecent.Clear();
        foreach (var e in Services.AppSettingsRef.Current.RecentFiles)
            StartRecent.Add(new StartItem(e.Path));
        StartFavorites.Clear();
        foreach (var p in Services.AppSettingsRef.Current.Favorites)
            StartFavorites.Add(new StartItem(p));
    }

    public bool HasTabs => Tabs.Count > 0;
    public bool IsEmpty => Tabs.Count == 0;
    public string FilePath => ActiveTab?.FilePath ?? Localizer.Instance["NoFile"];

    /// <summary>タブの × ボタンから発火。実際の破棄は View 側で行う。</summary>
    public event EventHandler<DocumentTabViewModel>? CloseTabRequested;
    public void RequestClose(DocumentTabViewModel tab) => CloseTabRequested?.Invoke(this, tab);

    public MainViewModel()
    {
        _selectedEncoding = EncodingOptions[0];
        var cur = Localizer.Instance.Culture.TwoLetterISOLanguageName;
        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == cur) ?? Languages[1];
        Tabs.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasTabs));
            OnPropertyChanged(nameof(IsEmpty));
        };
    }

    partial void OnActiveTabChanged(DocumentTabViewModel? value)
    {
        OnPropertyChanged(nameof(FilePath));
    }

    /// <summary>言語切替時にコード生成の派生プロパティを更新する。</summary>
    public void RaiseFilePathChanged() => OnPropertyChanged(nameof(FilePath));
}
