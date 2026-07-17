using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace UwView.Controls;

/// <summary>
/// マッチ部をハイライトして表示する TextBlock（フィルタ結果ポップアップ用）。
/// Text と HighlightRegex から Inlines（前・マッチ・後…）を組み立てる。
/// 仮想化リストの行テンプレートから使う想定なので、プロパティ変更時のみ再構築する。
/// </summary>
public sealed class HighlightTextBlock : TextBlock
{
    public static readonly StyledProperty<Regex?> HighlightRegexProperty =
        AvaloniaProperty.Register<HighlightTextBlock, Regex?>(nameof(HighlightRegex));

    public static readonly StyledProperty<IBrush?> HighlightBrushProperty =
        AvaloniaProperty.Register<HighlightTextBlock, IBrush?>(nameof(HighlightBrush),
            new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xD5, 0x00)));

    public Regex? HighlightRegex
    {
        get => GetValue(HighlightRegexProperty);
        set => SetValue(HighlightRegexProperty, value);
    }

    public IBrush? HighlightBrush
    {
        get => GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == HighlightRegexProperty)
            RebuildInlines();
    }

    private void RebuildInlines()
    {
        string text = Text ?? "";
        var regex = HighlightRegex;
        if (regex is null || text.Length == 0)
        {
            Inlines = null; // 通常の Text 描画
            return;
        }

        var inlines = new InlineCollection();
        int pos = 0;
        try
        {
            foreach (Match m in regex.Matches(text))
            {
                if (m.Length == 0) break; // 空マッチの無限分割を防ぐ
                if (m.Index > pos)
                    inlines.Add(new Run(text[pos..m.Index]));
                inlines.Add(new Run(m.Value) { Background = HighlightBrush, FontWeight = FontWeight.Bold });
                pos = m.Index + m.Length;
            }
        }
        catch (RegexMatchTimeoutException) { /* 部分ハイライトのまま */ }

        if (pos == 0) { Inlines = null; return; } // マッチなし
        if (pos < text.Length)
            inlines.Add(new Run(text[pos..]));
        Inlines = inlines;
    }
}
