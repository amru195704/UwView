using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace UwView.Controls;

/// <summary>
/// 文字のボタンをアイコンにする（9.27修正「漢字で表現しているボタンを全てアイコンに」）。
/// はい・いいえ・開く・閉じる・保存（と取り消し）は文字のまま。
///
/// アイコンだけでは何のボタンか分からないので、名前はツールチップと読み上げ名に残す。
/// 画像は press-kit/assets/toolbar（build/make-toolbar-icons.py が作る Material Icons）。
/// ダークでは黒い線が見えないので、明るくしたもの（toolbar-dark。build/make-dark-toolbar-icons.py）に替える
///（v1.8.2 extFS E-4）。画面からは <c>{DynamicResource Icon_名前}</c> で引く。
/// </summary>
public static class ToolbarIcon
{
    /// <summary>ツールバーのアイコンの大きさ（本体のツールバーと同じ）。</summary>
    public const double Size = 22;

    /// <summary>
    /// アイコンを、ライト／ダークで替わる資源（<c>Icon_名前</c>）として入れる。App の初期化で 1 回呼ぶ。
    /// </summary>
    public static void RegisterThemeIcons(Application app)
    {
        var light = new ResourceDictionary();
        var dark = new ResourceDictionary();
        foreach (var uri in AssetLoader.GetAssets(new Uri("avares://UwView/Assets/Toolbar/"), null))
        {
            string name = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
            var lightBitmap = new Bitmap(AssetLoader.Open(uri));
            var darkUri = new Uri($"avares://UwView/Assets/ToolbarDark/{name}.png");
            light[$"Icon_{name}"] = lightBitmap;
            dark[$"Icon_{name}"] = AssetLoader.Exists(darkUri) ? new Bitmap(AssetLoader.Open(darkUri)) : lightBitmap;
        }
        app.Resources.ThemeDictionaries[ThemeVariant.Light] = light;
        app.Resources.ThemeDictionaries[ThemeVariant.Dark] = dark;
    }

    /// <summary>名前（拡張子なし）のアイコン画像（テーマが替わると絵も替わる）。</summary>
    /// <param name="sized">大きさを決めるか（false なら大きさはスタイルに任せる）。</param>
    public static Image Of(string name, bool sized = true)
    {
        var image = new Image();
        image.Bind(Image.SourceProperty, image.GetResourceObservable($"Icon_{name}"));
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
