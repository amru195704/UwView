using System.Text;

namespace UwView.Core.Tests;

/// <summary>
/// 無料版の検索（<see cref="SearchService"/>）の、上限の指定と、正規表現の前置フィルタ（指示書 2026-09-15）。
/// </summary>
public class SearchLimitAndPrefilterTests : IDisposable
{
    public void Dispose()
    {
        SearchService.UseLiteralPrefilter = true;
        SearchService.DefaultMaxHits = 0;
    }

    private static byte[] Tricky()
    {
        var rnd = new Random(7);
        var sb = new StringBuilder();
        for (int i = 0; i < 30_000; i++)
        {
            if (i % 997 == 0) { sb.Append("long ").Append('x', 70_000).Append(" v=\"bus_stop\"\n"); continue; }
            if (i % 499 == 0) { sb.Append("crlf 東京駅 k=\"name:ja\"\r\n"); continue; }
            sb.Append(i).Append(' ');
            sb.Append(rnd.Next(5) switch { 0 => "v=\"bus_stop\"", 1 => "v=\"traffic_signals\"", 2 => "東京都", 3 => "k=\"name:en\"", _ => "k=\"name\"" });
            sb.Append(' ').Append(rnd.Next(1000, 9999)).Append('-').Append(rnd.Next(10, 99)).Append('\n');
        }
        sb.Append("last 東京駅 no newline");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static async Task<(List<long> Hits, SearchOutcome Outcome)> Run(byte[] data, SearchOptions options)
    {
        var hits = new List<long>();
        var outcome = await SearchService.SearchAsync(new MemoryByteSource(data), 0, Encoding.UTF8, options, b => hits.AddRange(b));
        return (hits, outcome);
    }

    [Theory]
    [InlineData("v=\"(bus_stop|traffic_signals)\"")]
    [InlineData("k=\"name:(en|ja)\"")]
    [InlineData("東京(都|駅)")]
    [InlineData(@"\d{4}-\d{2}")]
    [InlineData(@"^\d+ 東京")]
    [InlineData(@"(bus_stop""|東京都) \d{4}-\d{2}$")]
    public async Task 前置フィルタの有無で結果が同じ(string pattern)
    {
        byte[] data = Tricky();
        SearchService.UseLiteralPrefilter = false;
        var off = await Run(data, new SearchOptions(pattern, UseRegex: true, MaxHits: 0));
        SearchService.UseLiteralPrefilter = true;
        var on = await Run(data, new SearchOptions(pattern, UseRegex: true, MaxHits: 0));
        Assert.NotEmpty(off.Hits);
        Assert.Equal(off.Hits, on.Hits);
    }

    [Fact]
    public async Task 上限は指定どおりに効き_0なら無制限()
    {
        byte[] data = Tricky();
        var all = await Run(data, new SearchOptions("東京", MaxHits: 0));
        Assert.False(all.Outcome.Truncated);
        Assert.True(all.Hits.Count > 100);

        var cut = await Run(data, new SearchOptions("東京", MaxHits: 50));
        Assert.True(cut.Outcome.Truncated);
        Assert.Equal(all.Hits.Take(50), cut.Hits);

        // 指定しなければ既定値（画面の設定を入れる場所）
        SearchService.DefaultMaxHits = 10;
        var byDefault = await Run(data, new SearchOptions("東京"));
        Assert.Equal(10, byDefault.Hits.Count);
        Assert.True(byDefault.Outcome.Truncated);
    }
}
