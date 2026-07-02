using System.Text;
using UwView.Core;

namespace UwView.Core.Tests;

public class SearchServiceTests
{
    public SearchServiceTests() => EncodingDetector.EnsureCodePagesRegistered();

    private static async Task<(List<long> hits, SearchOutcome outcome)> RunAsync(
        byte[] data, SearchOptions options, Encoding? enc = null, int bomLength = 0)
    {
        var src = new MemoryByteSource(data);
        var hits = new List<long>();
        var outcome = await SearchService.SearchAsync(
            src, bomLength, enc ?? new UTF8Encoding(false), options,
            b => { lock (hits) hits.AddRange(b); });
        return (hits, outcome);
    }

    [Fact]
    public async Task Literal_FindsLineOffsets()
    {
        var sb = new StringBuilder();
        for (int i = 1; i <= 1000; i++)
            sb.Append(i is 10 or 500 or 999 ? $"line {i} NEEDLE here\n" : $"line {i} plain\n");
        var bytes = new UTF8Encoding(false).GetBytes(sb.ToString());

        var (hits, outcome) = await RunAsync(bytes, new SearchOptions("NEEDLE"));

        Assert.Equal(3, outcome.TotalHits);
        Assert.False(outcome.Truncated);
        // 行頭オフセットであること＝その位置の直前は '\n'（先頭以外）
        foreach (var off in hits)
            Assert.True(off == 0 || bytes[off - 1] == (byte)'\n');
        // 各ヒット行に NEEDLE が含まれる
        foreach (var off in hits)
        {
            int end = Array.IndexOf(bytes, (byte)'\n', (int)off);
            string line = Encoding.UTF8.GetString(bytes, (int)off, end - (int)off);
            Assert.Contains("NEEDLE", line);
        }
    }

    [Fact]
    public async Task Literal_OneHitPerLine()
    {
        var bytes = Encoding.UTF8.GetBytes("aaa aaa aaa\nbbb\naaa\n");
        var (hits, outcome) = await RunAsync(bytes, new SearchOptions("aaa"));
        Assert.Equal(2, outcome.TotalHits); // 1行目に3回出現しても1件
        Assert.Equal(0, hits[0]);
    }

    [Fact]
    public async Task Regex_And_IgnoreCase_Work()
    {
        var bytes = Encoding.UTF8.GetBytes("Error: disk full\nwarning: slow\nERROR timeout\nok\n");

        var (_, rx) = await RunAsync(bytes, new SearchOptions(@"^error", UseRegex: true, IgnoreCase: true));
        Assert.Equal(2, rx.TotalHits);

        var (_, ic) = await RunAsync(bytes, new SearchOptions("error", IgnoreCase: true));
        Assert.Equal(2, ic.TotalHits);
    }

    [Fact]
    public async Task Japanese_Utf8_And_ShiftJis()
    {
        string text = "一行目テスト\n検索対象の行\n三行目\n検索対象ふたたび\n";

        var utf8 = new UTF8Encoding(false).GetBytes(text);
        var (_, u) = await RunAsync(utf8, new SearchOptions("検索対象"));
        Assert.Equal(2, u.TotalHits);

        var sjis = Encoding.GetEncoding(932).GetBytes(text);
        var (_, s) = await RunAsync(sjis, new SearchOptions("検索対象"), Encoding.GetEncoding(932));
        Assert.Equal(2, s.TotalHits);

        // regex パス（SJIS デコード後にマッチ）
        var (_, sr) = await RunAsync(sjis, new SearchOptions("検索.象", UseRegex: true), Encoding.GetEncoding(932));
        Assert.Equal(2, sr.TotalHits);
    }

    [Fact]
    public async Task CrossBlockBoundary_HitIsFound()
    {
        // 1MB ブロック境界をまたぐ行にヒットがあるケース
        var sb = new StringBuilder();
        string filler = new string('x', 4093) + "\n"; // 4094B/行
        while (sb.Length < (1 << 20) - 2000) sb.Append(filler);
        sb.Append(new string('y', 3000)).Append("NEEDLE").Append(new string('y', 3000)).Append('\n'); // 境界またぎ
        sb.Append("tail\n");
        var bytes = Encoding.ASCII.GetBytes(sb.ToString());

        var (hits, outcome) = await RunAsync(bytes, new SearchOptions("NEEDLE"));
        Assert.Equal(1, outcome.TotalHits);
        Assert.True(bytes[hits[0] - 1] == (byte)'\n');
    }

    [Fact]
    public async Task NoTrailingNewline_LastLineSearched()
    {
        var bytes = Encoding.UTF8.GetBytes("first\nlast NEEDLE");
        var (hits, outcome) = await RunAsync(bytes, new SearchOptions("NEEDLE"));
        Assert.Equal(1, outcome.TotalHits);
        Assert.Equal(6, hits[0]); // "first\n" の直後
    }

    [Fact]
    public async Task Session_NextPrevHit_Navigate()
    {
        string path = Path.Combine(Path.GetTempPath(), $"uwview_{Guid.NewGuid():N}.txt");
        var sb = new StringBuilder();
        for (int i = 1; i <= 2000; i++)
            sb.Append(i % 100 == 0 ? $"line {i} MATCH\n" : $"line {i}\n");
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(false));
        try
        {
            await using var session = DocumentSession.Open(path);
            await session.BuildIndexAsync();
            await session.StartSearchAsync(new SearchOptions("MATCH"));

            Assert.Equal(20, session.SearchHits.Count);
            Assert.False(session.IsSearching);

            long first = session.NextHit(-1)!.Value;
            Assert.Equal(99, session.Document.OffsetToLineIndex(first)); // 100行目（0始まり99）

            long second = session.NextHit(first)!.Value;
            Assert.Equal(199, session.Document.OffsetToLineIndex(second));

            Assert.Equal(first, session.PrevHit(second));
            Assert.Null(session.PrevHit(first));
            Assert.Null(session.NextHit(session.SearchHits[^1]));
        }
        finally { File.Delete(path); }
    }
}
