using System.Reflection;
using System.Text;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using UwView.Controls;
using UwView.Core;
using UwView.Services;
using UwView.ViewModels;

namespace UwView.UiTests;

/// <summary>
/// ブックマークを結果の一覧に出す（v1.8.2 extFS E-2）。
/// 当たり＋ブックマーク／ブックマークだけ／当たりだけの切り替え・混ぜて並べる順・両方の行・Delete・保存。
/// </summary>
public class ResultListBookmarkTests : IDisposable
{
    private readonly string _path;

    public ResultListBookmarkTests()
    {
        UiHarness.IsolateSettings();
        // 「line N」を 30 行（3 の倍数の行だけ ERROR）
        _path = UiHarness.WriteTempFile(Enumerable.Range(1, 30).Select(i => $"line {i}{(i % 3 == 0 ? " ERROR" : "")}"));
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private long LineStart(int line)
        => File.ReadAllText(_path).Split('\n').Take(line - 1).Sum(l => Encoding.UTF8.GetByteCount(l) + 1);

    private async Task<DocumentSession> OpenAsync(bool search)
    {
        var session = DocumentSession.Open(_path);
        await session.BuildIndexAsync();
        if (search) await session.StartSearchAsync(new SearchOptions("ERROR"));
        session.SetBookmarks([LineStart(5), LineStart(9)]);              // 5 行目は当たりでない・9 行目は当たり
        return session;
    }

    private static List<int> Lines(FilterResultsViewModel vm)
        => [.. vm.Rows.Where(r => !r.IsSeparator).Select(r => (int)r.EffectiveLineIndex + 1)];

    [AvaloniaFact]
    public async Task 検索していないときはブックマークの一覧になる()
    {
        await using var session = await OpenAsync(search: false);
        using var vm = new FilterResultsViewModel(_ => { }, maxContext: 0);
        vm.SetSession(session);

        Assert.Equal([5, 9], Lines(vm));
        Assert.All(vm.Rows, r => Assert.True(r.IsBookmark && !r.IsHit));
        Assert.Equal("ブックマーク 2 件", vm.HitInfo);
    }

    [AvaloniaFact]
    public async Task 当たりとブックマークを行の順に混ぜて両方の行は1行に両方の印を付ける()
    {
        await using var session = await OpenAsync(search: true);
        using var vm = new FilterResultsViewModel(_ => { }, maxContext: 0);
        vm.SetSession(session);

        Assert.Equal(ResultListMode.HitsAndBookmarks, vm.ListMode);      // 既定
        Assert.Equal([3, 5, 6, 9, 12, 15, 18, 21, 24, 27, 30], Lines(vm));
        var row5 = vm.Rows[1];
        Assert.True(row5.IsBookmark && !row5.IsHit && row5.HitOrdinal < 0);
        var row9 = vm.Rows[3];
        Assert.True(row9.IsBookmark && row9.IsHit);
        Assert.Equal(3, row9.HitOrdinal);                                 // 当たりの通し番号はブックマークで崩れない
        Assert.Equal("10 件・ブックマーク 2", vm.HitInfo);
    }

    [AvaloniaFact]
    public async Task 切り替えで出すものが変わり設定に残る()
    {
        await using var session = await OpenAsync(search: true);
        using var vm = new FilterResultsViewModel(_ => { }, maxContext: 0);
        vm.SetSession(session);

        vm.ShowBookmarksOnly = true;
        Assert.Equal([5, 9], Lines(vm));
        Assert.Equal((int)ResultListMode.BookmarksOnly, AppSettingsRef.Current.ResultListMode);

        vm.ShowHitsOnly = true;
        Assert.Equal([3, 6, 9, 12, 15, 18, 21, 24, 27, 30], Lines(vm));
        Assert.True(vm.Rows[2].IsBookmark);                               // 当たりだけでも、ブックマークの行には印

        vm.CycleListMode();                                               // 当たりだけ → 当たり＋ブックマーク
        Assert.Equal(ResultListMode.HitsAndBookmarks, vm.ListMode);
        using var again = new FilterResultsViewModel(_ => { }, maxContext: 0);
        Assert.Equal(ResultListMode.HitsAndBookmarks, again.ListMode);    // 次に開いた一覧も同じ
    }

    [AvaloniaFact]
    public async Task 前後の行を出すときはブックマークの行にも前後が付く()
    {
        await using var session = DocumentSession.Open(_path);
        await session.BuildIndexAsync();
        await session.StartSearchAsync(new SearchOptions("line 30"));
        session.SetBookmarks([LineStart(10)]);
        using var vm = new FilterResultsViewModel(_ => { }, maxContext: 1);
        vm.SetSession(session);
        vm.ContextN = 1;
        await UiHarness.WaitUntil(() => vm.Rows.Any(r => r.IsSeparator), "前後の行の表示に替わる", 10_000);

        Assert.Equal([9, 10, 11, 29, 30], Lines(vm));
        Assert.True(vm.Rows.First(r => r.EffectiveLineIndex == 9).IsBookmark);
        Assert.False(vm.Rows.First(r => r.EffectiveLineIndex == 8).IsBookmark);
    }

    [AvaloniaFact]
    public async Task 一覧でブックマークの行を外すと本文のブックマークも外れる()
    {
        await using var session = await OpenAsync(search: true);
        using var vm = new FilterResultsViewModel(_ => { }, maxContext: 0);
        vm.SetSession(session);

        Assert.Equal(1, vm.RemoveBookmarks([vm.Rows[0], vm.Rows[1]]));   // 3 行目は当たりだけなので外すものは 5 行目だけ
        Assert.Equal([LineStart(9)], session.Bookmarks);
        Assert.Equal([3, 6, 9, 12, 15, 18, 21, 24, 27, 30], Lines(vm));   // 一覧も作り直される
    }

    [AvaloniaFact]
    public async Task 保存は今の切り替えで見えている行を書き文脈を外してもブックマークは残す()
    {
        await using var session = await OpenAsync(search: true);
        using var vm = new FilterResultsViewModel(_ => { }, maxContext: 0);
        vm.SetSession(session);
        vm.ShowBookmarksOnly = true;

        using (var ms = new MemoryStream())
        {
            await vm.SaveAsync(ms, new UTF8Encoding(false));
            Assert.Equal("5\tline 5\n9\tline 9 ERROR\n", Encoding.UTF8.GetString(ms.ToArray()).Replace("\r\n", "\n"));
        }

        vm.ShowHitsAndBookmarks = true;
        vm.IncludeContextOnSave = false;
        using (var ms = new MemoryStream())
        {
            await vm.SaveAsync(ms, new UTF8Encoding(false));
            var lines = Encoding.UTF8.GetString(ms.ToArray()).Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(11, lines.Length);
            Assert.Equal("5\tline 5", lines[1]);
        }
    }

    [AvaloniaFact]
    public async Task 本文で行の範囲を選んでCtrlBで全部に付き全部付いていれば全部外す()
    {
        var (w, v, vm) = UiHarness.OpenMainWindow();
        try
        {
            UiHarness.OpenFile(_path);
            await UiHarness.WaitUntil(() => vm.Tabs.Count == 1, "タブが開く");
            var session = vm.Tabs[0].Session;
            await UiHarness.WaitIndexed(session);
            await UiHarness.Pump();
            var text = UiHarness.Find<TextView>(v, "TextView");
            SelectLines(text, 1, 3);                                       // 2〜4 行目（0 始まりで 1〜3）
            text.Focus();
            var primary = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

            w.KeyPress(Key.B, primary, PhysicalKey.None, "");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal([LineStart(2), LineStart(3), LineStart(4)], session.Bookmarks);

            SelectLines(text, 1, 3);
            w.KeyPress(Key.B, primary, PhysicalKey.None, "");
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(session.Bookmarks);
        }
        finally { w.Close(); }
    }

    /// <summary>本文の行選択を作る（ドラッグの代わり。選択の起点と現在点は TextView の内側の状態）。</summary>
    private static void SelectLines(TextView text, long top, long bottom)
    {
        var t = typeof(TextView);
        t.GetField("_selAnchor", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(text, (long?)top);
        t.GetField("_selExtent", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(text, (long?)bottom);
    }
}
