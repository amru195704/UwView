using UwView.Core.Cli;

namespace UwView.Core.Tests;

/// <summary>CLI はアプリの設定ファイルを読むだけ（最大ヒット数など）。読めなければ既定値。</summary>
public class CliSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "uvf-clisettings-" + Guid.NewGuid().ToString("N"));
    public CliSettingsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string Write(string json)
    {
        string p = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(p, json);
        return p;
    }

    [Fact]
    public void 数値の設定を読み_無い壊れた型違いは既定値()
    {
        Assert.Equal(0, CliSettings.ReadIntFrom(Write("""{ "SearchMaxHits": 0 }"""), CliSettings.SearchMaxHitsKey, 1_000_000));
        Assert.Equal(5_000_000, CliSettings.ReadIntFrom(Write("""{ "SearchMaxHits": 5000000 }"""), CliSettings.SearchMaxHitsKey, 1));
        Assert.Equal(7, CliSettings.ReadIntFrom(Write("""{ "Language": "ja" }"""), CliSettings.SearchMaxHitsKey, 7));
        Assert.Equal(7, CliSettings.ReadIntFrom(Write("""{ "SearchMaxHits": "many" }"""), CliSettings.SearchMaxHitsKey, 7));
        Assert.Equal(7, CliSettings.ReadIntFrom(Write("{ broken"), CliSettings.SearchMaxHitsKey, 7));
        Assert.Equal(7, CliSettings.ReadIntFrom(Path.Combine(_dir, "none.json"), CliSettings.SearchMaxHitsKey, 7));
    }
}
