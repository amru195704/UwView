using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using UwView.Core;
using UwView.Services;
using UwView.Views;

namespace UwView.UiTests;

/// <summary>
/// 「開く」ボタンを実際に押す（ファイル選択画面だけ差し替える）。
///
/// UVP で「開く」を押しても何も起きない状態で配布しかけた（2026-09-14）。
/// 開くボタンを「パスを選ばせる」方式に変えたとき、選択画面の実装を足し忘れたため。
/// 既存のテストはファイル選択画面を出せないのでボタンを押しておらず、見逃していた。
/// </summary>
public class OpenButtonTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "uvf-openbtn-" + Guid.NewGuid().ToString("N"));
    private readonly IDocumentOpener _original = UwView.App.DocumentOpener;

    public OpenButtonTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        UwView.App.DocumentOpener = _original;
        CompressedOpenDialog.NoticeOverride = null;
        CompressedOpenDialog.AskOverride = null;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>選択画面の代わりに、決めたパスを返す（それ以外は本物のデスクトップ実装）。</summary>
    private sealed class PickerStub(params string[] paths) : IDocumentOpener
    {
        private readonly DesktopDocumentOpener _real = new();
        public Task<IReadOnlyList<DocumentSession>> PickFilesAsync(TopLevel topLevel) => _real.PickFilesAsync(topLevel);
        public DocumentSession? OpenLocalPath(string path) => _real.OpenLocalPath(path);
        public bool SupportsPathPicking => _real.SupportsPathPicking;
        public Task<IReadOnlyList<string>> PickPathsAsync(TopLevel topLevel) => Task.FromResult<IReadOnlyList<string>>(paths);
    }

    [AvaloniaFact]
    public async Task 開くボタンで選んだファイルが開く()
    {
        string path = Path.Combine(_dir, "picked.log");
        File.WriteAllText(path, "one\ntwo\nthree\n");
        var (window, view, vm) = UiHarness.OpenMainWindow();
        UwView.App.DocumentOpener = new PickerStub(path);

        UiHarness.Click(UiHarness.Find<Button>(view, "OpenButton"));
        await UiHarness.WaitUntil(() => vm.ActiveTab is not null, "タブが開く");
        Assert.Equal(path, vm.ActiveTab!.FilePath);
    }

    [AvaloniaFact]
    public void デスクトップの選択画面はパスを扱える()
    {
        // 既定の実装を持たない形にしたので、ここが false だと「開く」が何もしない
        Assert.True(new DesktopDocumentOpener().SupportsPathPicking);
    }

    [AvaloniaFact]
    public async Task zipは理由を出して開かず壊れているとは言わない()
    {
        string zip = Path.Combine(_dir, "logs.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var w = new StreamWriter(archive.CreateEntry("server.log").Open()))
            w.Write("line\n");

        string message = "";
        CompressedOpenDialog.NoticeOverride = m => message = m;
        bool asked = false;
        CompressedOpenDialog.AskOverride = (_, _) => { asked = true; return Task.FromResult(CompressedOpenMethod.Expand); };
        var (window, view, vm) = UiHarness.OpenMainWindow();

        await view.OpenPathsAsync(new[] { zip });
        await UiHarness.Pump();

        Assert.False(asked, "zip なのに開き方を尋ねている（展開に進んでしまう）");
        Assert.Contains("zip", message);
        Assert.DoesNotContain("壊れて", message);
        Assert.Null(vm.ActiveTab);
    }
}
