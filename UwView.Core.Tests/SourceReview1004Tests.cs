using System.Text;
using System.Text.RegularExpressions;
using UwView.Core.Cli;

namespace UwView.Core.Tests;

/// <summary>
/// 外部レビュー（2026-10-04・v1.7.3.6.9 / 922a616）で指摘された不具合の再発防止テスト。
/// 期待値は ripgrep ではなく、.NET の Regex と前置フィルタ OFF の結果。
/// </summary>
public class SourceReview1004Tests : IDisposable
{
    public void Dispose() => SearchService.UseLiteralPrefilter = true;

    private static async Task<(long Raw, long Inverted, long Search)> RunAsync(byte[] data, string pattern, bool filter)
    {
        SearchService.UseLiteralPrefilter = filter;
        var options = new SearchOptions(pattern, UseRegex: true, MaxHits: 0);
        var raw = await RawGrep.RunAsync(new MemoryByteSource(data), 0, Encoding.UTF8, options, false, (_, _, _) => { });
        var inv = await RawGrep.RunAsync(new MemoryByteSource(data), 0, Encoding.UTF8, options, true, (_, _, _) => { });
        var search = await SearchService.SearchAsync(new MemoryByteSource(data), 0, Encoding.UTF8, options);
        return (raw.Hits, inv.Hits, search.TotalHits);
    }

    // 指摘1: 壊れたバイト（FF）はデコードで U+FFFD になる。EF BF BD を手がかりにすると行を捨てる
    // 指摘2: 入れ子の (?x) のコメント #)Z( の Z を必須と取り違える
    // 指摘3: a😀? の必須リテラルを a＋上位サロゲートだけと取り違える
    [Theory]
    [InlineData("�", new byte[] { 0xFF, 0x0A })]
    [InlineData("[�]", new byte[] { 0xFF, 0x0A })]
    [InlineData("[ｦ-￿]", new byte[] { 0xFF, 0x0A })]
    [InlineData(@"\�", new byte[] { 0xFF, 0x0A })]
    [InlineData("(?:(?x)#)Z(\n)", new byte[] { (byte)'a', (byte)'b', (byte)'c', 0x0A })]
    [InlineData("(?:(?i)z)", new byte[] { (byte)'Z', 0x0A })]
    [InlineData("a😀?", new byte[] { (byte)'a', 0xF0, 0x9F, 0x98, 0x80, 0x0A })]
    [InlineData("xa😀*", new byte[] { (byte)'x', (byte)'a', 0xF0, 0x9F, 0x98, 0x80, 0x0A })]
    public async Task 前置フィルタの有無で結果が同じ(string pattern, byte[] data)
    {
        string line = Encoding.UTF8.GetString(data).TrimEnd('\n');
        Assert.Matches(new Regex(pattern, RegexOptions.CultureInvariant), line);

        var off = await RunAsync(data, pattern, filter: false);
        var on = await RunAsync(data, pattern, filter: true);
        Assert.Equal((1L, 0L, 1L), off);
        Assert.Equal(off, on);
    }

    [Fact]
    public void 指摘1_置換文字は短い手がかりにしない()
    {
        Assert.Null(RegexClues.Extract("�"));
        Assert.Null(RegexClues.Extract("[�]"));
        Assert.Null(RegexClues.Extract("[ｦ-￿]"));
    }

    [Fact]
    public void 指摘2_グループの中のオプションとコメントは読まない()
    {
        Assert.Null(RegexClues.Extract("(?:(?x)#)Z(\n)"));
        Assert.Null(RegexClues.Extract("(?:(?#x)y)Z(?:(?i)q)"));
        // 名前付き・先読みのグループは今までどおり読み飛ばして、外の手がかりを取る
        Assert.NotNull(RegexClues.Extract("(?<y>[0-9]{4})-(?=[0-9])"));
    }

    [Fact]
    public void 指摘3_片側のサロゲートを必須リテラルにしない()
    {
        Assert.Null(RegexLiterals.Extract("a😀?", ignoreCase: false));
        Assert.Equal(["a😀"], RegexLiterals.Extract("a😀", ignoreCase: false));
        Assert.Equal(["xyz"], RegexLiterals.Extract("😀+xyz", ignoreCase: false));
    }

    // 指摘4: (?P< の読み替えが文字クラスの中まで書き換える
    [Theory]
    [InlineData("[(?P<]", "P")]
    [InlineData("[^(?P<]", "Q")]
    [InlineData("[](?P<]", "P")]
    [InlineData("[a-z-[(?P<]]", "P")]
    [InlineData(@"[\](?P<]", "P")]
    public void 指摘4_文字クラスの中は読み替えない(string pattern, string input)
    {
        Assert.Equal(pattern, RegexDialect.Normalize(pattern));
        bool expected = new Regex(pattern, RegexOptions.CultureInvariant).IsMatch(input);
        Assert.Equal(expected, SearchService.BuildRegex(new SearchOptions(pattern, UseRegex: true)).IsMatch(input));
    }

    [Fact]
    public void 指摘4_クラスの外は今までどおり読み替える()
    {
        Assert.Equal("[(?P<](?<n>x)", RegexDialect.Normalize("[(?P<](?P<n>x)"));
        Assert.Equal("(?<n>[a-z-[aeiou]])(?<m>y)", RegexDialect.Normalize("(?P<n>[a-z-[aeiou]])(?P<m>y)"));
    }

    // 指摘5: 大小無視の「絞れる」判断を、実際の検索が使う方式で行う
    [Theory]
    [InlineData("foo|bar", true)]                  // 候補が2つ：-i の手がかりにならない
    [InlineData("東京", true)]                      // ASCII の部分が無い
    [InlineData("k=\"highway[^\"]*\"", false)]
    [InlineData("highway", false)]
    public void 指摘5_大小無視の判断が実際の検索と同じ(string pattern, bool scansEveryLine)
    {
        Assert.Equal(scansEveryLine, CompiledRegexRoute.ScansEveryLine(pattern, ignoreCase: true));
        var plan = PreparedSearch.Create(new SearchOptions(pattern, UseRegex: true, IgnoreCase: true)).For(Encoding.UTF8);
        Assert.Equal(scansEveryLine, plan.Method == SearchMethod.EveryLine);
    }

    // 指摘6: 同じ検索の準備を、ファイルごとに作り直さない（探した位置を覚える係だけ新しく作る）
    [Fact]
    public void 指摘6_同じ検索なら準備を使い回す()
    {
        var prepared = PreparedSearch.Create(new SearchOptions("[ぁ-ん]{3,}", UseRegex: true));
        var plan = prepared.For(Encoding.UTF8);
        Assert.Same(plan, prepared.For(new UTF8Encoding(true)));   // 同じ文字コードなら同じ方式
        Assert.Same(prepared.Regex, prepared.Regex);               // 正規表現のコンパイルは 1 回
        Assert.Equal(SearchMethod.CandidateLines, plan.Method);

        var first = plan.NewClueFinder();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) prepared.For(Encoding.UTF8).NewClueFinder();
        long perFile = (GC.GetAllocatedBytesForCurrentThread() - before) / 100;
        Assert.NotSame(first, plan.NewClueFinder());
        Assert.True(perFile < 512, $"1 ファイル {perFile} バイト");
    }
}
