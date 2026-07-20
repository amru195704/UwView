using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace UwView.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // macOS はシステムメニューバー（NativeMenu）を使うのでウィンドウ内メニューは隠す。
        WinMenu.IsVisible = !OperatingSystem.IsMacOS();
    }

    // ── macOS: NativeMenu.Menu(File/Help) のハンドラ（EventArgs）────
    private void OnMenuOpen(object? s, EventArgs e) => App.RequestOpenFile?.Invoke();
    private void OnMenuClose(object? s, EventArgs e) => App.RequestCloseTab?.Invoke();
    private void OnMenuCloseAll(object? s, EventArgs e) => App.RequestCloseAll?.Invoke();
    private void OnMenuHowTo(object? s, EventArgs e) { /* 使い方は別途用意（今は形だけ） */ }
    private void OnMenuGitHubUwView(object? s, EventArgs e) => App.OpenExternal("https://github.com/amru195704/UwView");
    private void OnMenuGitHubProfile(object? s, EventArgs e) => App.OpenExternal("https://github.com/amru195704");

    // ── Windows/Linux: ウィンドウ内 Menu のハンドラ（RoutedEventArgs）────
    private void OnWinOpen(object? s, RoutedEventArgs e) => App.RequestOpenFile?.Invoke();
    private void OnWinClose(object? s, RoutedEventArgs e) => App.RequestCloseTab?.Invoke();
    private void OnWinCloseAll(object? s, RoutedEventArgs e) => App.RequestCloseAll?.Invoke();
    private void OnWinHowTo(object? s, RoutedEventArgs e) { /* 使い方は別途用意（今は形だけ） */ }
    private void OnWinGitHubUwView(object? s, RoutedEventArgs e) => App.OpenExternal("https://github.com/amru195704/UwView");
    private void OnWinGitHubProfile(object? s, RoutedEventArgs e) => App.OpenExternal("https://github.com/amru195704");
    private void OnWinAbout(object? s, RoutedEventArgs e) => App.ShowAbout();
}
