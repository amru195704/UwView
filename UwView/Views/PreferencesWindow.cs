using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using UwView.Localization;

namespace UwView.Views;

/// <summary>
/// uvf の設定の画面（v1.8.2 extFS E-3 §4.1。メニュー：Windows・Linux は「ツール → 設定…」、Mac はアプリ名の「設定…」・⌘,）。
/// 中身は共通の部品（<see cref="KeyBindingsPanel"/> ほか）。以後の設定もここに足す。
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
        Background = UwView.Services.ThemeColors.Surface;

        Tabs = new TabControl
        {
            Name = "SettingsTabs",
            Margin = new Thickness(12),
            Items =
            {
                new TabItem { Header = L["SettingsTabGeneral"], Content = GeneralPanel() },
                new TabItem { Header = L["SettingsTabKeys"], Content = new KeyBindingsPanel() },
                new TabItem { Header = L["SettingsTabAppearance"], Content = new AppearancePanel() },
            },
        };
        Content = Tabs;
        KeyDown += (_, e) => { if (e.Key == Key.Escape && e.Source is not Border) { Close(); e.Handled = true; } };
        Opened += (_, _) => Current = this;
        Closed += (_, _) => { if (ReferenceEquals(Current, this)) Current = null; };
    }

    /// <summary>全般: 変換して開いたファイルの元を見張るか（2026-10-14）。</summary>
    private static Control GeneralPanel()
    {
        var L = Localizer.Instance;
        var settings = UwView.Services.AppSettingsRef.Current;
        var box = new CheckBox
        {
            Name = "WatchOriginalBox", Content = L["WatchOriginalFile"], IsChecked = settings.WatchOriginalFile,
            Foreground = UwView.Services.ThemeColors.Text,
        };
        ToolTip.SetTip(box, L["TipWatchOriginalFile"]);
        box.IsCheckedChanged += (_, _) =>
        {
            var s = UwView.Services.AppSettingsRef.Current;
            s.WatchOriginalFile = box.IsChecked == true;
            s.Save();
        };
        var panel = new StackPanel
        {
            Margin = new Thickness(6, 12, 6, 0), Spacing = 6,
            Children =
            {
                box,
                new TextBlock { Text = L["WatchOriginalFileHint"], TextWrapping = TextWrapping.Wrap, Foreground = UwView.Services.ThemeColors.Text },
            },
        };
        if (UwView.Core.SupportPrompt.Available) AddSupportSetting(panel);
        return panel;
    }

    /// <summary>
    /// 全般: 支援の案内を表示しない（帯の［もう表示しない］と同じ項目 <c>SupportBanner.Disabled</c>。指示-20261113-05）。
    /// 無料版の本体だけに出す。
    /// </summary>
    private static void AddSupportSetting(StackPanel panel)
    {
        bool ja = Localizer.Instance.Culture.TwoLetterISOLanguageName == "ja";
        var settings = UwView.Services.AppSettingsRef.Current;
        var box = new CheckBox
        {
            Name = "SupportBannerOffBox",
            Content = UwView.Core.SupportPromptText.SettingLabel(ja),
            IsChecked = settings.SupportBanner?.Disabled == true,
            Foreground = UwView.Services.ThemeColors.Text,
            Margin = new Thickness(0, 12, 0, 0),
        };
        box.IsCheckedChanged += (_, _) =>
        {
            var s = UwView.Services.AppSettingsRef.Current;
            s.SupportBanner ??= new UwView.Core.SupportPromptState();
            s.SupportBanner.Disabled = box.IsChecked == true;
            s.Save();
            if (s.SupportBanner.Disabled) SupportBannerClosed?.Invoke();
        };
        panel.Children.Add(box);
        panel.Children.Add(new TextBlock
        {
            Text = UwView.Core.SupportPromptText.SettingHint(ja),
            TextWrapping = TextWrapping.Wrap,
            Foreground = UwView.Services.ThemeColors.Text,
        });
    }

    /// <summary>「支援の案内を表示しない」をオンにしたとき（出ている帯を畳む。メイン画面が差し込む）。</summary>
    internal static Action? SupportBannerClosed { get; set; }

    /// <summary>開く（開いていれば前に出す）。</summary>
    public static void Open(Window? owner)
    {
        if (Current is { } open) { open.Activate(); return; }
        var win = new PreferencesWindow();
        if (owner is not null) win.Show(owner);
        else win.Show();
    }
}
