using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.LogicalTree;
using UwView.Controls;
using UwView.Services;

namespace UwView.UiTests;

/// <summary>
/// ステータスバーのファイルの名前と、共通の操作の窓（2026-10-10 オーナー依頼）。
/// ふだんはファイル名だけ。押すとフルパスと［パスをコピー］［フォルダーを開く］［ターミナルで開く］［アプリで開く］。
/// </summary>
public class PathLabelTests : IDisposable
{
    private readonly List<(string Action, string Path)> _done = [];

    public PathLabelTests()
    {
        FileActions.Override = (action, path) => { _done.Add((action, path)); return true; };
        FileActions.LaunchOverride = path => { _done.Add(("app", path)); return Task.FromResult(true); };
    }

    public void Dispose()
    {
        FileActions.Override = null;
        FileActions.LaunchOverride = null;
        GC.SuppressFinalize(this);
    }

    [AvaloniaFact]
    public async Task ステータスバーはファイル名だけで押すと操作の窓を出す()
    {
        string path = UiHarness.WriteTempFile(["line 1", "line 2"]);
        var (w, v, vm) = UiHarness.OpenMainWindow();
        try
        {
            var label = v.GetLogicalDescendants().OfType<PathLabel>().Single(l => l.Name == "StatusPath");
            Assert.False(Path.IsPathRooted(label.ShownText));          // 開く前は「（ファイル未選択）」

            UiHarness.OpenFile(path);
            await UiHarness.WaitUntil(() => vm.Tabs.Count == 1, "タブが開く");
            await UiHarness.Pump();
            Assert.Equal(Path.GetFileName(path), label.ShownText);

            label.ShowActions();
            await UiHarness.Pump();
            var actions = label.Actions!;

            await actions.CopyAsync();
            Assert.Equal(path, await w.Clipboard!.TryGetTextAsync());

            actions.Reveal();
            actions.Terminal();
            await actions.OpenAppAsync();
            Assert.Equal([("reveal", path), ("terminal", Path.GetDirectoryName(path)!), ("app", path)], _done);
            Assert.Contains(Path.GetFileName(path), actions.ResultText);
        }
        finally { w.Close(); File.Delete(path); }
    }

    /// <summary>窓の幅は親の窓に収め、長いパスはその幅で折り返す（右が切れて見えなかった。2026-10-10 オーナー指摘）。</summary>
    [AvaloniaFact]
    public void 長いパスは窓の幅で折り返し親の窓からはみ出さない()
    {
        string path = "/Volumes/BIWIN/26work/cmc296data/2608/20260805result/cmcOut0805_296_4/依頼②-⑤_調査と修正案.pdf";
        var window = new Avalonia.Controls.Window { Width = 400, Height = 300 };
        window.Show();
        try
        {
            var panel = new FileActionsPanel(path, path);
            panel.FitTo(window);
            Assert.True(panel.Width <= 400 - 48);
            window.Content = panel;
            window.UpdateLayout();
            var box = panel.GetLogicalDescendants().OfType<Avalonia.Controls.TextBox>().Single(b => b.Name == "FileActionsPath");
            Assert.True(box.Bounds.Width <= panel.Width + 0.5);
            Assert.True(box.Bounds.Height > 40, $"折り返していない（高さ {box.Bounds.Height}）");
        }
        finally { window.Close(); }
    }

    /// <summary>取り出した索引（report.pdf.uwvz）の［アプリで開く］は、隣の元のファイル（report.pdf）を開く。元が無ければ押せない。</summary>
    [AvaloniaFact]
    public void 索引のアプリで開くは元のファイルを開き元が無ければ押せない()
    {
        string dir = Path.Combine(Path.GetTempPath(), "uvf-actions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string pdf = Path.Combine(dir, "report.pdf"), index = pdf + ".uwvz", bundle = Path.Combine(dir, "uw-x.uwvz");
            File.WriteAllText(pdf, "%PDF-1.7");
            File.WriteAllText(index, "x");
            File.WriteAllText(bundle, "x");

            Assert.Equal(pdf, new FileActionsPanel(index, index).AppTargetPath);
            var only = new FileActionsPanel(bundle, bundle);
            Assert.False(only.AppEnabled);
            Assert.Null(only.AppTargetPath);
            Assert.False(new FileActionsPanel(Path.Combine(dir, "gone.log"), Path.Combine(dir, "gone.log")).AppEnabled);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
