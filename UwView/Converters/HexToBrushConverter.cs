using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using UwView.Core;

namespace UwView.Converters;

/// <summary>
/// "#RRGGBB"（/"#AARRGGBB"）文字列 → SolidColorBrush。空・不正は null（既定色にフォールバック）。
/// ハイライタ管理の色欄を、入力したコードの色そのもので塗って見せるための変換。
/// </summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        uint argb = CompiledHighlighter.ParseColor(value as string);
        if (argb == 0)
            // 未指定: 文字色用途(parameter="fg")は黒を返す（null にすると文字が描かれず消える）。
            // 背景用途は null（＝既定の白）でよい。
            return (parameter as string) == "fg" ? Brushes.Black : null;
        return new SolidColorBrush(Color.FromArgb(
            (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
