using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Avalonia.Media;
using UwView;
using UwView.Browser;

[assembly: SupportedOSPlatform("browser")]

internal sealed partial class Program
{
    // WASM にはシステムフォントが無いため、同梱の Noto Sans JP を既定＋フォールバックにする
    private const string JpFont = "avares://UwView.Browser/Assets/Fonts#Noto Sans JP";

    private static async Task Main(string[] args)
    {
        // blob.slice 用 JS モジュールを読み込み、オープナーを Blob 実装へ差し替え
        // （ImportAsync のパスは _framework/ 基準で解決されるため 1 つ上がる）
        await JSHost.ImportAsync("blobRead", "../blobRead.js");
        App.DocumentOpener = new BrowserDocumentOpener();

        await BuildAvaloniaApp()
            .WithInterFont()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = JpFont,
                FontFallbacks = [new FontFallback { FontFamily = new FontFamily(JpFont) }]
            })
#if DEBUG
            .WithDeveloperTools()
#endif
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
