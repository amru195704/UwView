using System;
using System.Linq;
using Avalonia;
using UwView.Core.Cli;

namespace UwView.Desktop;

sealed class Program
{
    /// <summary>先頭にこれが付いて起動されたら CLI（uvf）として動く。配布物の uvf スクリプトが付ける。</summary>
    public const string CliMarker = "--uvf";

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // CLI は画面を作る前に分岐する（Avalonia を初期化しないので Dock にも出ない）
        if (args.Length > 0 && args[0] == CliMarker)
            return RunCli(args[1..]);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static int RunCli(string[] args)
    {
        CliHost.PrepareConsole();
        UwView.Core.EncodingDetector.EnsureCodePagesRegistered();

        using var cts = new System.Threading.CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        return UvfCli.RunAsync(args, new UvfEnvironment
        {
            StdOut = Console.OpenStandardOutput(),
            StdErr = Console.Error,
            // -open: 自分自身を GUI として起動し直す（検索パターンとファイルを渡す）
            LaunchGui = (file, pattern) => CliHost.LaunchSelfAsGui(
                (pattern is not null && file is not null
                    ? new[] { UvfCli.SearchArgument, Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(pattern)) }
                    : Array.Empty<string>())
                .Concat(file is null ? Array.Empty<string>() : new[] { file })),
        }, cts.Token).GetAwaiter().GetResult();
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
