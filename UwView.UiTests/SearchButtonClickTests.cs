using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace UwView.UiTests;

/// <summary>
/// 検索語を打ってから虫眼鏡を押すと、1回目は検索が始まらなかった（オーナー指摘 2026-09-14）。
///
/// 検索履歴があると、入力中に候補の一覧が開く。一覧の外を押すと、透明な層がそのクリックを受け止めて
/// 一覧を閉じるだけで終わり、虫眼鏡まで届いていなかった（Enter は層を通らないので効いていた）。
/// ボタンを直接叩くテスト（Click イベントを送る）では層を通らないので見逃す。本物のマウス操作で押す。
/// </summary>
public class SearchButtonClickTests
{
    private static Point Center(Visual target, Visual root)
        => target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), root)!.Value;

    [AvaloniaFact]
    public async Task 候補の一覧が開いていても虫眼鏡の1回目で検索が始まる()
    {
        string path = UiHarness.WriteTempFile(Enumerable.Range(0, 2000).Select(i => i % 10 == 0 ? $"{i} ERROR x" : $"{i} ok"));
        var (w, v, vm) = UiHarness.OpenMainWindow();
        try
        {
            UiHarness.OpenFile(path);
            await UiHarness.WaitIndexed(vm.ActiveTab!.Session);

            UwView.App.Settings.PushSearchHistory("ERROR x");
            vm.SearchHistory.Clear();
            foreach (var h in UwView.App.Settings.SearchHistory) vm.SearchHistory.Add(h);

            var box = UiHarness.Find<AutoCompleteBox>(v, "SearchBox");
            var p = Center(box, w);
            w.MouseDown(p, MouseButton.Left);
            w.MouseUp(p, MouseButton.Left);
            w.KeyTextInput("ERROR");
            await UiHarness.Pump();
            Assert.True(box.IsDropDownOpen, "前提: 入力中に候補の一覧が開いている");

            var b = Center(UiHarness.Find<Button>(v, "SearchButton"), w);
            w.MouseDown(b, MouseButton.Left);
            w.MouseUp(b, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            await UiHarness.WaitSearchDone(vm.ActiveTab.Session);
            Assert.Equal(200, vm.ActiveTab.Session.SearchHits.Count);
            Assert.False(box.IsDropDownOpen);
        }
        finally { w.Close(); File.Delete(path); }
    }
}
