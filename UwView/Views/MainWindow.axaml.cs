using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UwView.Core;

namespace UwView.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // macOS はシステムメニューバー（NativeMenu）を使うのでウィンドウ内メニューは隠す。
        WinMenu.IsVisible = !OperatingSystem.IsMacOS();
    }

    /// <summary>
    /// ウィンドウを閉じる時（× ボタン・Cmd+Q・OS シャットダウン）に、
    /// 現在のタブ構成と表示位置を保存する。ShutdownRequested だけでは
    /// × で閉じた場合に発火しないことがあるため、ここでも確実に保存する。
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        App.RequestSaveSession?.Invoke();   // LastSession（位置含む）を更新
        App.Settings.Save();                // JSON へ確定保存
        base.OnClosing(e);
    }

    // ── macOS: NativeMenu.Menu(File/Help) のハンドラ（EventArgs）────
    private void OnMenuOpen(object? s, EventArgs e) => App.RequestOpenFile?.Invoke();
    private void OnMenuClose(object? s, EventArgs e) => App.RequestCloseTab?.Invoke();
    private void OnMenuCloseAll(object? s, EventArgs e) => App.RequestCloseAll?.Invoke();
    private void OnMenuHowTo(object? s, EventArgs e) => App.OpenExternal(SiteLinks.Help);
    private void OnMenuNews(object? s, EventArgs e) => App.OpenExternal(SiteLinks.Official);
    private void OnMenuSupport(object? s, EventArgs e) => App.OpenExternal(SiteLinks.Support);
    private void OnMenuGitHubUwView(object? s, EventArgs e) => App.OpenExternal(SiteLinks.GitHubRepo);
    private void OnMenuGitHubProfile(object? s, EventArgs e) => App.OpenExternal(SiteLinks.GitHubProfile);

    // ── Windows/Linux: ウィンドウ内 Menu のハンドラ（RoutedEventArgs）────
    private void OnWinOpen(object? s, RoutedEventArgs e) => App.RequestOpenFile?.Invoke();
    private void OnWinClose(object? s, RoutedEventArgs e) => App.RequestCloseTab?.Invoke();
    private void OnWinCloseAll(object? s, RoutedEventArgs e) => App.RequestCloseAll?.Invoke();
    private void OnWinHowTo(object? s, RoutedEventArgs e) => App.OpenExternal(SiteLinks.Help);
    private void OnWinNews(object? s, RoutedEventArgs e) => App.OpenExternal(SiteLinks.Official);
    private void OnWinSupport(object? s, RoutedEventArgs e) => App.OpenExternal(SiteLinks.Support);
    private void OnWinGitHubUwView(object? s, RoutedEventArgs e) => App.OpenExternal(SiteLinks.GitHubRepo);
    private void OnWinGitHubProfile(object? s, RoutedEventArgs e) => App.OpenExternal(SiteLinks.GitHubProfile);
    private void OnWinAbout(object? s, RoutedEventArgs e) => App.ShowAbout();
}
