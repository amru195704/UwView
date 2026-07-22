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

    /// <summary>ファイル指定起動の引数（ダブルクリック/D&D/CLI）。V1.1.1: あればそれのみ開き復元しない。</summary>
    public static string[]? LaunchFileArgs { get; private set; }

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
        UwView.Services.AppSettingsRef.Current = Settings; // 共有VMからの参照先
        Localizer.Instance.SetLanguage(ResolveLanguage(Settings));

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            LaunchFileArgs = desktop.Args; // ファイル指定起動の判定用
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };
            desktop.ShutdownRequested += (_, _) => Settings.Save(); // 終了時に確定保存
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

    // ── File / Help メニューの動作（File 操作は現在のメイン画面へ委譲）────
    public static System.Action? RequestOpenFile;
    public static System.Action? RequestCloseTab;
    public static System.Action? RequestCloseAll;

    internal static void OpenExternal(string url)
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } w })
            _ = w.Launcher.LaunchUriAsync(new System.Uri(url));
    }

    private void OnAboutClick(object? sender, System.EventArgs e) => ShowAbout();

    internal static void ShowAbout()
    {
        bool ja = Localizer.Instance.Culture.TwoLetterISOLanguageName == "ja";
        string ver = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0";
        string build = BuildNo(typeof(App).Assembly);
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
                    new Avalonia.Controls.TextBlock
                    { Text = string.IsNullOrEmpty(build) ? $"Version {ver}" : $"Version {ver}  (build {build})" },
                    new Avalonia.Controls.TextBlock
                    {
                        Text = ja ? "巨大テキストファイルを一瞬で開く軽量ビューア"
                                  : "A lightweight viewer that opens huge text files instantly",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    },
                    new Avalonia.Controls.TextBlock
                    { Text = "© 2026 amru195704 (Y4U)", Margin = new Thickness(0, 10, 0, 0) },
                    MakeLink(ja ? "公式サイト（最新情報）" : "Official site / News", UwView.Core.SiteLinks.Official),
                    MakeLink(ja ? "概要（UwViewとは）" : "About UwView", UwView.Core.SiteLinks.About),
                    MakeLink(ja ? "お問い合わせ" : "Support / Contact", UwView.Core.SiteLinks.Support),
                    MakeLink("GitHub", UwView.Core.SiteLinks.GitHubRepo),
                },
            },
        };
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            win.ShowDialog(owner);
        else
            win.Show();
    }

    /// <summary>ビルド番号（コンパイル日時 yy.MM.dd.HH）を AssemblyMetadata から取得。</summary>
    private static string BuildNo(System.Reflection.Assembly asm)
    {
        foreach (var a in asm.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false))
            if (a is System.Reflection.AssemblyMetadataAttribute m && m.Key == "BuildNumber")
                return m.Value ?? "";
        return "";
    }

    /// <summary>ラベル付きリンク（表示文字は label、開く先は url）。</summary>
    private static Avalonia.Controls.TextBlock MakeLink(string label, string url)
    {
        var link = MakeLink(url);
        link.Text = label;
        return link;
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