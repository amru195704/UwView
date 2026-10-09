using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.LogicalTree;
using UwView.Controls;

namespace UwView.UiTests;

/// <summary>
/// ステータスバーのファイルの名前（2026-10-10 オーナー依頼）。ふだんはファイル名だけ、押すとフルパスを出してコピーできる。
/// </summary>
public class PathLabelTests
{
    [AvaloniaFact]
    public async Task ステータスバーはファイル名だけで押すとフルパスを出しコピーできる()
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

            label.ShowFull();
            await UiHarness.Pump();
            Assert.Equal(path, label.FullText);

            await label.CopyAsync();
            Assert.Equal(path, await w.Clipboard!.TryGetTextAsync());
        }
        finally { w.Close(); File.Delete(path); }
    }
}
