using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Globalization;
using System.Linq;
using Avalonia.Markup.Xaml;
using UwView.Localization;
using UwView.Services;
using UwView.ViewModels;
using UwView.Views;

namespace UwView;

public partial class App : Application
{
    /// <summary>head ごとのファイルオープン実装。Browser head は起動前に差し替える。</summary>
    public static IDocumentOpener DocumentOpener { get; set; } = new DesktopDocumentOpener();

    public static AppSettings Settings { get; private set; } = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>保存済み言語 → 無ければ OS の UI カルチャ（ja 以外は en）。</summary>
    private static string ResolveLanguage(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Language))
            return settings.Language!;
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? "ja" : "en";
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 言語を VM 生成より先に適用（EncodingOptions 等の初期ラベルを正しい言語に）
        Settings = AppSettings.Load();
        Localizer.Instance.SetLanguage(ResolveLanguage(Settings));

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };
            SetupAppMenu();
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = () => new MainView { DataContext = new MainViewModel() };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>macOS アプリメニュー（App.axaml の NativeMenu）の About 文言を UI 言語へ合わせる。</summary>
    private void SetupAppMenu()
    {
        bool ja = Localizer.Instance.Culture.TwoLetterISOLanguageName == "ja";
        if (Avalonia.Controls.NativeMenu.GetMenu(this) is { Items: [Avalonia.Controls.NativeMenuItem about, ..] })
            about.Header = ja ? "UwView について" : "About UwView";
    }

    private void OnAboutClick(object? sender, System.EventArgs e) => ShowAbout();

    private static void ShowAbout()
    {
        bool ja = Localizer.Instance.Culture.TwoLetterISOLanguageName == "ja";
        string ver = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0";
        var win = new Avalonia.Controls.Window
        {
            Title = ja ? "UwView について" : "About UwView",
            Width = 400, SizeToContent = Avalonia.Controls.SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            Content = new Avalonia.Controls.StackPanel
            {
                Margin = new Thickness(28, 24),
                Spacing = 6,
                Children =
                {
                    new Avalonia.Controls.TextBlock
                    { Text = "UwView", FontSize = 26, FontWeight = Avalonia.Media.FontWeight.Bold },
                    new Avalonia.Controls.TextBlock { Text = $"Version {ver}" },
                    new Avalonia.Controls.TextBlock
                    {
                        Text = ja ? "巨大テキストファイルを一瞬で開く軽量ビューア"
                                  : "A lightweight viewer that opens huge text files instantly",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    },
                    new Avalonia.Controls.TextBlock
                    { Text = "© 2026 amru195704 (Y4U)", Margin = new Thickness(0, 10, 0, 0) },
                    MakeLink("https://github.com/amru195704"),
                },
            },
        };
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            win.ShowDialog(owner);
        else
            win.Show();
    }

    /// <summary>クリックで既定ブラウザを開くリンク風 TextBlock。</summary>
    private static Avalonia.Controls.TextBlock MakeLink(string url)
    {
        var link = new Avalonia.Controls.TextBlock
        {
            Text = url,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x1A, 0x6F, 0xE8)),
            TextDecorations = Avalonia.Media.TextDecorations.Underline,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        link.PointerPressed += (sender, args) =>
        {
            if (sender is Avalonia.Controls.TextBlock tb
                && Avalonia.Controls.TopLevel.GetTopLevel(tb) is { } top)
                _ = top.Launcher.LaunchUriAsync(new System.Uri(url));
        };
        return link;
    }
}