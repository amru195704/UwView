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
}