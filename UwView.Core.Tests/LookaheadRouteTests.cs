using System.Text;
using UwView.Core.Cli;

namespace UwView.Core.Tests;

/// <summary>
/// 肯定の先読みの中身で絞る式を、候補の行が多いときだけ本体（JIT）に任せる（2026-10-06）。
/// 先読みは解釈実行で特に遅いので、候補の数（先頭を数えてファイル全体に引き延ばした見積もり）で決める。
/// </summary>
public class LookaheadRouteTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"uv_ahead_{Guid.NewGuid():N}.log");

    public void Dispose()
    {
        File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    private void Write(int lines, int recordEvery)
    {
        using var w = new StreamWriter(_path, false, new UTF8Encoding(false), 1 << 20);
        for (int i = 0; i < lines; i++)
            w.Write(i % recordEvery == 0 ? "rec User: u1 Action: a2 Time: 12:00\n" : "noise item=12345 value=678 xx\n");
    }

    private const string Ahead = @"^(?=.*User: (\S+))(?=.*Action: (\S+))(?=.*Time: (.+?)\s*$)";

    [Fact]
    public void 候補が多ければ任せる()
    {
        Write(1_200_000, recordEvery: 1);
        Assert.True(CompiledRegexRoute.LookaheadCluesAreDense(_path, Ahead, sampleBytes: 1 << 20));
    }

    [Fact]
    public void 候補が少なければ任せない()
    {
        Write(1_200_000, recordEvery: 100);
        Assert.False(CompiledRegexRoute.LookaheadCluesAreDense(_path, Ahead, sampleBytes: 1 << 20));
    }

    [Fact]
    public void 先読みの無い式は候補が多くても任せない()
    {
        Write(1_200_000, recordEvery: 1);
        Assert.False(CompiledRegexRoute.LookaheadCluesAreDense(_path, @"Action: (\S+)", sampleBytes: 1 << 20));
    }
}
