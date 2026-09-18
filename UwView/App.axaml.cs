using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
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

    /// <summary>
    /// CLI（uvf -open）から渡された検索パターン。開いたファイルでこの語を検索する。
    /// 画面ができたら <see cref="Views.MainView"/> が取り出して使う。
    /// </summary>
    public static string? PendingCliSearch { get; internal set; }

    /// <summary>uvf -open が渡してきた検索結果の置き場所（一度使ったら消える）。</summary>
    public static string? PendingCliHits { get; internal set; }

    /// <summary>uvf -open が渡してきた検索の種類（i/E/v の並び）。</summary>
    public static string? PendingCliOptions { get; internal set; }

    /// <summary>uvf が GUI を起動するときに付ける引数名（uvf 側と同じ）。</summary>
    public const string CliSearchArgument = "--uvf-search";

    /// <summary>
    /// 起動引数から <c>--uvf-search &lt;base64&gt;</c> を取り除き、残り（ファイル指定）を返す。
    /// 壊れていたら検索だけ諦める（ファイルは普通に開く。起動は止めない）。
    /// </summary>
    internal static string[] ExtractCliSearch(string[] args, out string? pattern)
        => ExtractCliSearch(args, out pattern, out _);

    /// <param name="hitsPath">CLI が見つけた結果の置き場所（<see cref="UwView.Core.Cli.CliHandoff"/>）。</param>
    internal static string[] ExtractCliSearch(string[] args, out string? pattern, out string? hitsPath)
        => ExtractCliSearch(args, out pattern, out hitsPath, out _);

    /// <param name="options">検索の種類（i/E/v の並び）。</param>
    internal static string[] ExtractCliSearch(string[] args, out string? pattern, out string? hitsPath,
                                              out string? options)
    {
        pattern = null;
        hitsPath = null;
        options = null;
        var rest = new System.Collections.Generic.List<string>(args.Length);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == CliSearchArgument && i + 1 < args.Length)
            {
                try { pattern = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(args[++i])); }
                catch (System.FormatException) { pattern = null; }
                continue;
            }
            if (args[i] == UwView.Core.Cli.CliHandoff.Argument && i + 1 < args.Length)
            {
                hitsPath = args[++i];
                continue;
            }
            if (args[i] == UwView.Core.Cli.UvfCli.OptionsArgument && i + 1 < args.Length)
            {
                options = args[++i];
                continue;
            }
            rest.Add(args[i]);
        }
        return [.. rest];
    }

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

    /// <summary>
    /// Finder の「このアプリケーションで開く」・アイコンへのドラッグ&ドロップ・
    /// <c>open -a UwView file.log</c> で渡されたファイル。
    ///
    /// macOS ではこの3つは<b>いずれも同じ Apple Event</b>（開くドキュメント）として届く。
    /// argv ではないので、起動引数とは別に受ける必要がある。
    /// </summary>
    private static readonly List<string> _pendingOpen = [];

    /// <summary>ファイルを開く係（メイン画面が起動時に差し込む）。</summary>
    public static Action<string[]>? RequestOpenFiles;

    /// <summary>
    /// OS から渡されたファイルを開く。
    /// 画面がまだ無い（＝ダブルクリックでの起動直後）なら溜めておき、
    /// 画面ができたときに <see cref="TakePendingOpen"/> で引き取ってもらう。
    /// </summary>
    public static void OpenFromOs(IEnumerable<string> paths)
    {
        var list = paths.Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p)).ToArray();
        if (list.Length == 0) return;

        if (RequestOpenFiles is { } open) open(list);
        else _pendingOpen.AddRange(list);
    }

    /// <summary>溜めてあった分を引き取る（引き取ったら空にする）。</summary>
    public static string[] TakePendingOpen()
    {
        var a = _pendingOpen.ToArray();
        _pendingOpen.Clear();
        return a;
    }

    /// <summary>OS からのファイル通知を購読する（対応していないプラットフォームでは何もしない）。</summary>
    private void HookFileActivation()
    {
        if (ApplicationLifetime is not IActivatableLifetime activatable) return;

        activatable.Activated += (_, e) =>
        {
            if (e is not FileActivatedEventArgs f) return;
            OpenFromOs(f.Files.Select(i => i.TryGetLocalPath()).Where(p => p is not null)!);
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 言語を VM 生成より先に適用（EncodingOptions 等の初期ラベルを正しい言語に）
        Settings = AppSettings.Load();
        UwView.Services.AppSettingsRef.Current = Settings; // 共有VMからの参照先
        Localizer.Instance.SetLanguage(ResolveLanguage(Settings));

        HookFileActivation();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // uvf -open から来た検索パターンを抜く（残りがファイル指定）
            LaunchFileArgs = ExtractCliSearch(desktop.Args ?? [], out var pattern, out var hitsPath, out var opts);
            PendingCliSearch = pattern;
            PendingCliHits = hitsPath;
            PendingCliOptions = opts;
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };
            // 終了時: 現在の表示位置までを記録してから確定保存
            //（SaveSession を呼ばないと、タブを開いた時点の位置しか残らず復元がずれる）
            desktop.ShutdownRequested += (_, _) =>
            {
                RequestSaveSession?.Invoke();
                Settings.Save();
            };
            SetupAppMenu();
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = () => new MainView { DataContext = new MainViewModel() };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            var view = new MainView { DataContext = new MainViewModel() };
            // ブラウザ版は扱える大きさに限りがあるので、先にお断りを出してから使ってもらう。
            // WASM にはウィンドウが無く ShowDialog が使えないため、画面に重ねる（2026-09-09 指示）
            singleViewPlatform.MainView = System.OperatingSystem.IsBrowser()
                ? new BrowserStartupNotice(view)
                : view;
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
    /// <summary>終了時に現在のタブ構成・表示位置を LastSession へ記録する（MainView が設定）。</summary>
    public static System.Action? RequestSaveSession;

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
                    MakeLink(ja ? "公式サイト（最新情報）" : "Official site / News", UwView.Core.SiteLinks.OfficialLink),
                    MakeLink(ja ? "概要（UwViewとは）" : "About UwView", UwView.Core.SiteLinks.AboutLink),
                    MakeLink(ja ? "お問い合わせ" : "Support / Contact", UwView.Core.SiteLinks.SupportLink),
                    MakeLink("GitHub", UwView.Core.SiteLinks.GitHubRepoLink),
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