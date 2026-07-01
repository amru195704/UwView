using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UwView.ViewModels;

public enum EncodingChoice { Auto, Utf8, Utf8Bom, ShiftJis, EucJp, Utf16Le, Utf16Be }

public sealed record EncodingOption(EncodingChoice Choice, string Label)
{
    public override string ToString() => Label;
}

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty] private string _filePath = "（ファイル未選択）";
    [ObservableProperty] private string _positionInfo = "";
    [ObservableProperty] private string _encodingInfo = "";
    [ObservableProperty] private string _modeInfo = "";

    [ObservableProperty] private double _indexProgress;
    [ObservableProperty] private bool _isIndexing;

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

    public MainViewModel()
    {
        _selectedEncoding = EncodingOptions[0];
    }
}
