using UwView.Core;
using UwView.Core.Cli;

namespace UwView.Core.Tests;

using static ThreadBudgetTestNames;

internal static class ThreadBudgetTestNames
{
    public const string HeavyMeasurement = "全コアを使う計測";
}

/// <summary>
/// スレッド数の決め方（Wide Field v1.7.0 段階1-a）と、<c>--tune</c> の組み立て（段階1-b）。
/// 「環境変数 &gt; 設定ファイル &gt; 既定」「論理プロセッサ数で丸める」「丸めたら知らせる」を守ること。
/// </summary>
[CollectionDefinition(HeavyMeasurement, DisableParallelization = true)]
public sealed class HeavyMeasurementCollection
{
    public const string Name = "全コアを使う計測";
}

/// <remarks>
/// 実測は全コアを使うので、ほかのテストと同時に走らせない
/// （重なると別のテストの正規表現が時間切れになり、関係ない失敗が出る）。
/// </remarks>
[Collection(HeavyMeasurement)]
public class ThreadBudgetTests : IDisposable
{
    private readonly int _saved = ThreadBudget.LogicalProcessors;
    private readonly string _dir = Directory.CreateTempSubdirectory("uvf_threads_").FullName;

    public ThreadBudgetTests() => ThreadBudget.LogicalProcessors = 16;

    public void Dispose()
    {
        ThreadBudget.LogicalProcessors = _saved;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void 何も指定しなければ既定の8()
        => Assert.Equal(ThreadBudget.Default, ThreadBudget.Resolve(environmentValue: null, configured: 0));

    [Fact]
    public void 設定ファイルの値を使う()
        => Assert.Equal(4, ThreadBudget.Resolve(environmentValue: null, configured: 4));

    [Fact]
    public void 環境変数が設定ファイルより優先される()
        => Assert.Equal(2, ThreadBudget.Resolve("2", configured: 4));

    [Fact]
    public void 論理プロセッサ数を超える指定は丸めて知らせる()
    {
        var said = new List<string>();
        Assert.Equal(16, ThreadBudget.Resolve("99", configured: 0, said.Add));
        Assert.Contains(said, m => m.Contains("16"));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-3")]
    public void 読めない指定は知らせて既定に戻す(string value)
    {
        var said = new List<string>();
        Assert.Equal(ThreadBudget.Default, ThreadBudget.Resolve(value, configured: 0, said.Add));
        Assert.Single(said);
    }

    [Fact]
    public void 読めない指定でも設定ファイルの値があればそちらを使う()
        => Assert.Equal(4, ThreadBudget.Resolve("abc", configured: 4));

    // ── --tune ────────────────────────────────────────────

    [Theory]
    [InlineData(1, new[] { 1 })]
    [InlineData(2, new[] { 1, 2 })]
    [InlineData(10, new[] { 1, 2, 4, 8, 10 })]
    [InlineData(16, new[] { 1, 2, 4, 8, 16 })]
    [InlineData(64, new[] { 1, 2, 4, 8, 16, 32, 64 })]
    public void 試す本数は1から倍々と論理プロセッサ数(int logical, int[] expected)
        => Assert.Equal(expected, TuneRunner.Ladder(logical));

    [Fact]
    public void 設定の保存はほかのキーを壊さない()
    {
        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{"SearchMaxHits": 1234, "Language": "ja"}""");

        Assert.True(CliSettings.WriteIntTo(path, ThreadBudget.SettingsKey, 8));

        Assert.Equal(8, CliSettings.ReadIntFrom(path, ThreadBudget.SettingsKey, 0));
        Assert.Equal(1234, CliSettings.ReadIntFrom(path, "SearchMaxHits", 0));
        Assert.Contains("\"Language\"", File.ReadAllText(path));
    }

    [Fact]
    public void 設定ファイルがまだ無ければ作る()
    {
        string path = Path.Combine(_dir, "none", "settings.json");
        Assert.True(CliSettings.WriteIntTo(path, ThreadBudget.SettingsKey, 3));
        Assert.Equal(3, CliSettings.ReadIntFrom(path, ThreadBudget.SettingsKey, 0));
    }

    [Fact]
    public void 実測して勧める本数を出す()
    {
        // 小さい中身でも、表・勧める本数・読んだ量が揃って返ること（速さの値そのものは機械しだい）
        string path = Path.Combine(_dir, "sample.log");
        File.WriteAllText(path, string.Concat(Enumerable.Repeat("2026-09-22 INFO dev0 seq=1 x\n", 20_000)));

        var result = TuneRunner.Run(path, logicalProcessors: 2);

        Assert.Equal([1, 2], result.Rows.Select(r => r.Threads));
        Assert.All(result.Rows, r => Assert.True(r.GbPerSec > 0));
        Assert.InRange(result.Recommended, 1, 2);
        Assert.Equal(new FileInfo(path).Length, result.MeasuredBytes);
        Assert.Null(result.DiskGbPerSec);       // 小さすぎて媒体は測れない

        string text = TuneRunner.Format(result, ja: true, path, logicalProcessors: 2);
        Assert.Contains("勧める本数", text);
        Assert.Contains("ばらつき", text);
    }

    [Theory]
    [InlineData(512L << 20, 256L << 20)]      // 空き 512MB → 半分。下限に張り付く
    [InlineData(8L << 30, 4L << 30)]          // 空き 8GB → 半分は 4GB。上限どおり
    [InlineData(16L << 30, 4L << 30)]         // 空きが多くても 4GiB で打ち切る（測るのに時間がかかりすぎる）
    [InlineData(64L << 20, 256L << 20)]       // 極端に少なくても下限は割らない（短すぎて測れないため）
    public void メモリに載る大きさで測る量を決める(long available, long expected)
        => Assert.Equal(expected, TuneRunner.MemoryBudget(available));

    [Fact]
    public void 載らなかったときは媒体律速だと言う()
    {
        // オーナー報告 2026-09-22（Linux VM 2 vCPU・50GB）: 1本 2.0GB/s → 2本 2.17GB/s で伸びない。
        // 原因はメモリに載っていないこと。黙って「どれでも同じ」と言うと、
        // 本当は CPU 律速で効く場面まで「1本で十分」と読まれてしまう
        var result = new TuneRunner.Result(
            [new TuneRunner.Row(1, 2.00, 2), new TuneRunner.Row(2, 2.17, 3)],
            Recommended: 1, AllSame: true, DiskGbPerSec: null, MeasuredBytes: 4L << 30, Busy: false,
            OnMemory: false, MemoryBudget: 1L << 30);

        string ja = TuneRunner.Format(result, ja: true, "big.log", logicalProcessors: 2);
        Assert.Contains("メモリに載りませんでした", ja);
        Assert.Contains("媒体律速", ja);

        string en = TuneRunner.Format(result, ja: false, "big.log", logicalProcessors: 2);
        Assert.Contains("did not stay in memory", en);
    }

    [Fact]
    public void 載ったときは余計なことを言わない()
    {
        var result = new TuneRunner.Result(
            [new TuneRunner.Row(1, 0.8, 2), new TuneRunner.Row(2, 1.6, 3)],
            Recommended: 2, AllSame: false, DiskGbPerSec: 0.5, MeasuredBytes: 1L << 30, Busy: false,
            OnMemory: true, MemoryBudget: 2L << 30);

        string text = TuneRunner.Format(result, ja: true, "fits.log", logicalProcessors: 2);
        Assert.DoesNotContain("メモリに載りませんでした", text);
        Assert.Contains("勧める本数: 2", text);
    }

    [Fact]
    public void 対象を指定しなければ一時ファイルで測り後片付けする()
    {
        var before = Directory.GetFiles(Path.GetTempPath(), "uwview-tune-*");
        var result = TuneRunner.Run(null, logicalProcessors: 1);
        Assert.Single(result.Rows);
        // 大きいファイルは前半だけ測る（後ろは媒体の速さの計測用に取っておく）
        Assert.Equal(TuneRunner.GeneratedBytes / 2, result.MeasuredBytes);
        Assert.Equal(before.Length, Directory.GetFiles(Path.GetTempPath(), "uwview-tune-*").Length);
    }
}
