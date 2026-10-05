using System.Text;
using System.Text.RegularExpressions;
using UwView.Core.Cli;

namespace UwView.Core.Tests;

/// <summary>
/// 外部再レビュー（2026-10-05・HEAD 66665c0）で指摘された2件の再発防止テスト。
/// 期待値は .NET の Regex と前置フィルタ OFF の結果。
/// </summary>
public class SourceReview1005Tests : IDisposable
{
    public void Dispose() => SearchService.UseLiteralPrefilter = true;

    private static async Task<(long Raw, long Inverted)> RunAsync(byte[] data, SearchOptions options, bool filter)
    {
        SearchService.UseLiteralPrefilter = filter;
        var raw = await RawGrep.RunAsync(new MemoryByteSource(data), 0, Encoding.UTF8, options, false, (_, _, _) => { });
        var inv = await RawGrep.RunAsync(new MemoryByteSource(data), 0, Encoding.UTF8, options, true, (_, _, _) => { });
        return (raw.Hits, inv.Hits);
    }

    // 指摘1: -v（1行ずつ見る道）で、前の行に残った手がかりを1つずつたどり直していた。
    // 答えは変わらない（遅いだけ）ので、手がかりの多い行・候補が複数・一致なし・改行なしの最終行で、OFF と同じことを確かめる
    [Theory]
    [InlineData("ka", true, 1024, 0)]           // 手がかり a が全行に大量・どの行にも当たらない
    [InlineData("ka", true, 1024, 3)]           // 当たる行を混ぜる
    [InlineData("k=\"(name|ref)\"", false, 64, 5)]  // 大小を区別する・候補が複数
    public async Task 指摘1_手がかりの多い行が続いても_除外検索の答えが同じ(string pattern, bool icase, int lines, int hitsEvery)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < lines; i++)
        {
            sb.Append('a', 4096);
            if (hitsEvery > 0 && i % hitsEvery == 0) sb.Append(icase ? " KA" : " k=\"ref\"");
            sb.Append(i % 7 == 0 ? "\r\n" : "\n");
        }
        sb.Append("aaaa no newline");
        byte[] data = Encoding.UTF8.GetBytes(sb.ToString());
        var options = new SearchOptions(pattern, UseRegex: true, IgnoreCase: icase, MaxHits: 0);

        var off = await RunAsync(data, options, filter: false);
        var on = await RunAsync(data, options, filter: true);
        Assert.Equal(off, on);
        Assert.Equal(lines + 1, off.Raw + off.Inverted);
    }

    // 指摘2: コメントの中の [ を文字クラスの始まりと読み、後ろの (?P< を読み替えなかった
    [Theory]
    [InlineData("(?# [)(?P<n>a)]", "(?# [)(?<n>a)]", "a]")]
    [InlineData("(?#(?P<x>)(?P<n>a)", "(?#(?P<x>)(?<n>a)", "a")]            // コメントの中はそのまま
    [InlineData("(?# [)[(?P<](?P<n>b)", "(?# [)[(?P<](?<n>b)", "Pb")]        // コメントの直後に本物の文字クラス
    [InlineData("(?x)a # [ (?P<x>\n(?P<n>b)", "(?x)a # [ (?P<x>\n(?<n>b)", "ab")]   // x の # コメントは行末まで
    [InlineData("(?x:# [\n)(?P<n>c)", "(?x:# [\n)(?<n>c)", "c")]               // (?x:…) は閉じるまで
    [InlineData("(?x)[#](?P<n>d)", "(?x)[#](?<n>d)", "#d")]                   // 文字クラスの中の # はコメントでない
    [InlineData("#[(?P<](?P<n>e)", "#[(?P<](?<n>e)", "#Pe")]                  // x でなければ # はただの文字
    [InlineData("(?x)(?-x)#[(?P<](?P<n>f)", "(?x)(?-x)#[(?P<](?<n>f)", "#Pf")] // (?-x) で戻す
    public void 指摘2_コメントの中は読み替えず_後ろの名前付きグループは読み替える(string pattern, string normalized, string input)
    {
        Assert.Equal(normalized, RegexDialect.Normalize(pattern));
        var regex = SearchService.BuildRegex(new SearchOptions(pattern, UseRegex: true));
        Assert.Matches(regex, input);
        Assert.Null(RegexDialect.Check(pattern));
    }

    // テスト一式（2026-10-05）で遅くなった 2: 短い手がかりのある行が多いと、絞れずに遅い。
    // 途中から全行に当てる（ClueWatch）。答えは変わらないことを、手がかりが 3 割を超える 20 万行で確かめる
    [Fact]
    public async Task 短い手がかりのある行が多ければ途中から全行に当て_答えは同じ()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 200_000; i++)
            sb.Append(i % 2 == 0 ? $"<node ts=\"2019-05-{i % 28:00}\"/>" : "<tag k=\"x\"/>")
              .Append(i % 1000 == 0 ? $" tel=\"{i % 900 + 100}-{i % 9000 + 1000}\"" : "").Append('\n');
        byte[] data = Encoding.UTF8.GetBytes(sb.ToString());
        var options = new SearchOptions("[0-9]{3}-[0-9]{4}\"", UseRegex: true, MaxHits: 0);
        Assert.True(PreparedSearch.Create(options).For(Encoding.UTF8).UsesShortClue);

        var off = await RunAsync(data, options, filter: false);
        var on = await RunAsync(data, options, filter: true);
        Assert.Equal(off, on);
        Assert.Equal(200, off.Raw);
        SearchService.UseLiteralPrefilter = false;
        var expected = await SearchService.SearchAsync(new MemoryByteSource(data), 0, Encoding.UTF8, options);
        SearchService.UseLiteralPrefilter = true;
        var actual = await SearchService.SearchAsync(new MemoryByteSource(data), 0, Encoding.UTF8, options);
        Assert.Equal(expected.TotalHits, actual.TotalHits);
    }

    [Fact]
    public void 見張りは割合を超えたときだけ_1回だけ手がかりをやめる()
    {
        var shortClue = PreparedSearch.Create(new SearchOptions("[0-9]{4}-[0-9]{2}", UseRegex: true)).For(Encoding.UTF8);
        var dense = new ClueWatch(shortClue);
        for (int i = 0; i < 30_000; i++) dense.Candidate();
        Assert.False(dense.GiveUp(50_000));                 // まだ数が足りない
        Assert.True(dense.GiveUp(50_000));                  // 3 万 / 10 万 = 30% > 25%
        Assert.False(dense.Watching);
        Assert.False(dense.GiveUp(100_000));                // 決めるのは 1 回だけ

        var sparse = new ClueWatch(shortClue);
        for (int i = 0; i < 10_000; i++) sparse.Candidate();
        Assert.False(sparse.GiveUp(100_000));               // 10%

        var literal = new ClueWatch(PreparedSearch.Create(new SearchOptions("k=\"highway", UseRegex: true)).For(Encoding.UTF8));
        Assert.False(literal.Watching);                     // 必須リテラルは見張らない
    }

    // 遅くなった 2（続き）: 1 本の大きな入力で短い手がかりが絞れない（＝全行に当てる）ときだけ本体（JIT）に任せる。
    // 先頭を読んで割合を数える（絞れる式まで任せると、本体の起動の分だけ遅かった。grix テスト 2026-10-05）
    [Theory]
    [InlineData("[0-9]{3}-[0-9]{4}\"", 2, true)]       // 半分の行に - がある
    [InlineData("[0-9]{3}-[0-9]{4}\"", 50, false)]     // 2% の行だけ
    [InlineData("[ぁ-ん]{3,}", 2, false)]                // ひらがなの行は無い
    [InlineData("k=\"x\" tel", 2, false)]              // 必須リテラルで絞れる式は数えない
    public void 短い手がかりが絞れないときだけ任せる(string pattern, int dashEvery, bool expected)
    {
        string path = Path.Combine(Path.GetTempPath(), $"uv_dense_{Guid.NewGuid():N}.txt");
        try
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 20_000; i++)
                sb.Append(i % dashEvery == 0 ? "<node ts=\"2019-05-01\"/>" : "<tag k=\"x\" v=\"y\"/>").Append('\n');
            File.WriteAllText(path, sb.ToString());
            var plan = PreparedSearch.Create(new SearchOptions(pattern, UseRegex: true)).For(Encoding.UTF8);
            Assert.Equal(expected, CompiledRegexRoute.ShortClueIsDense(path, plan));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void 短すぎるファイルと圧縮ファイルは数えない()
    {
        string path = Path.Combine(Path.GetTempPath(), $"uv_dense_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, string.Concat(Enumerable.Repeat("2019-05-01\n", 500)));
            var plan = PreparedSearch.Create(new SearchOptions("[0-9]{3}-[0-9]{4}", UseRegex: true)).For(Encoding.UTF8);
            Assert.False(CompiledRegexRoute.ShortClueIsDense(path, plan));
            using (var gz = new System.IO.Compression.GZipStream(File.Create(path + ".gz"), System.IO.Compression.CompressionLevel.Fastest))
                gz.Write(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("2019-05-01\n", 5000))));
            Assert.False(CompiledRegexRoute.ShortClueIsDense(path + ".gz", plan));
        }
        finally { File.Delete(path); File.Delete(path + ".gz"); }
    }
}
