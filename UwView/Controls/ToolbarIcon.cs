using System;
using System.Collections.Generic;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace UwView.Controls;

/// <summary>
/// 文字のボタンをアイコンにする（オーナー指示 2026-09-27「漢字で表現しているボタンを全てアイコンに」）。
/// はい・いいえ・開く・閉じる・保存（と取り消し）は文字のまま。
///
/// アイコンだけでは何のボタンか分からないので、名前はツールチップと読み上げ名に残す。
/// 画像は press-kit/assets/toolbar（build/make-toolbar-icons.py が作る Material Icons）。
/// </summary>
public static class ToolbarIcon
{
    /// <summary>ツールバーのアイコンの大きさ（本体のツールバーと同じ）。</summary>
    public const double Size = 22;

    private static readonly Dictionary<string, Bitmap> Cache = [];

    /// <summary>名前（拡張子なし）のアイコン画像。</summary>
    /// <param name="sized">大きさを決めるか（false なら大きさはスタイルに任せる）。</param>
    public static Image Of(string name, bool sized = true)
    {
        if (!Cache.TryGetValue(name, out var bitmap))
            Cache[name] = bitmap = new Bitmap(AssetLoader.Open(new Uri($"avares://UwView/Assets/Toolbar/{name}.png")));
        var image = new Image { Source = bitmap };
        if (sized)
        {
            image.Width = Size;
            image.Height = Size;
        }
        return image;
    }

    /// <summary>ボタンの中身をアイコンにし、名前をツールチップと読み上げ名にする。</summary>
    public static void Apply(ContentControl button, string name, string label)
    {
        // 本体のツールバーは大きさをスタイルでそろえている（Button.icon > Image）。
        // ここで大きさを入れるとスタイルより強くなるので、入れない
        button.Content = Of(name, sized: !button.Classes.Contains("icon"));
        ToolTip.SetTip(button, label);
        AutomationProperties.SetName(button, label);
    }
}
