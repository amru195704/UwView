using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using UwView.Localization;

namespace UwView.Views;

/// <summary>
/// uvf の設定の画面（v1.8.2 extFS E-3 §4.1。メニュー：Windows・Linux は「ツール → 設定…」、Mac はアプリ名の「設定…」・⌘,）。
/// 中身は Pro の設定の画面と同じ部品（<see cref="KeyBindingsPanel"/> ほか）。以後の設定もここに足す。
/// </summary>
public sealed class PreferencesWindow : Window
{
    /// <summary>自動テスト用: いま開いている設定の画面（無ければ null）。</summary>
    internal static PreferencesWindow? Current { get; private set; }

    internal TabControl Tabs { get; }

    public PreferencesWindow()
    {
        var L = Localizer.Instance;
        Title = L["SettingsTitle"];
        Width = 760;
        Height = 620;
        MinWidth = 600;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;

        Tabs = new TabControl
        {
            Name = "SettingsTabs",
            Margin = new Thickness(12),
            Items =
            {
                new TabItem { Header = L["SettingsTabKeys"], Content = new KeyBindingsPanel() },
            },
        };
        Content = Tabs;
        KeyDown += (_, e) => { if (e.Key == Key.Escape && e.Source is not Border) { Close(); e.Handled = true; } };
        Opened += (_, _) => Current = this;
        Closed += (_, _) => { if (ReferenceEquals(Current, this)) Current = null; };
    }

    /// <summary>開く（開いていれば前に出す）。</summary>
    public static void Open(Window? owner)
    {
        if (Current is { } open) { open.Activate(); return; }
        var win = new PreferencesWindow();
        if (owner is not null) win.Show(owner);
        else win.Show();
    }
}
