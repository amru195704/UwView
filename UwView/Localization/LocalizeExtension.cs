using System;
using System.ComponentModel;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace UwView.Localization;

/// <summary>
/// XAML 用マークアップ拡張。 {loc:Localize Open} で現在言語の文字列にバインドし、
/// 言語切替に追従して更新される。
/// インデクサ変更通知の解釈差を避けるため、キーごとに単一プロパティ Value を持つ
/// 通知オブジェクトを介した標準プロパティバインドにしている。
/// </summary>
public sealed class LocalizeExtension : MarkupExtension
{
    public LocalizeExtension() { }
    public LocalizeExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
        => new Binding(nameof(LocalizedValue.Value))
        {
            Source = new LocalizedValue(Key),
            Mode = BindingMode.OneWay
        };
}

/// <summary>1 キー分のローカライズ値。Localizer の変更を購読し Value の変更を通知する。</summary>
public sealed class LocalizedValue : INotifyPropertyChanged
{
    private readonly string _key;

    public LocalizedValue(string key)
    {
        _key = key;
        Localizer.Instance.PropertyChanged += OnLocalizerChanged;
    }

    public string Value => Localizer.Instance[_key];

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnLocalizerChanged(object? sender, PropertyChangedEventArgs e)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
}
