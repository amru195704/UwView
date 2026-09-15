using System.Text;
using System.Text.RegularExpressions;

namespace UwView.Core.Tests;

/// <summary>
/// 正規表現の必須リテラル（指示書 2026-09-15「正規表現検索の高速化」A）。
///
/// 一番大事なのは<b>ヒットが消えないこと</b>: 「正規表現に当たる行は、取り出したリテラルのどれかを必ず含む」。
/// 取り損ねても遅いだけだが、必須でないものを取ると検索結果が黙って減る。
/// 決まった形の確認に加えて、ランダムに作った式と行でこの性質を大量に突き合わせる。
/// </summary>
public class RegexLiteralsTests
{
    [Theory]
    [InlineData("v=\"(bus_stop|traffic_signals)\"", "v=\"bus_stop\"|v=\"traffic_signals\"")]
    [InlineData("k=\"name:(en|ja)\"", "k=\"name:en\"|k=\"name:ja\"")]
    [InlineData("東京(都|駅)", "東京都|東京駅")]
    [InlineData(@"^\s+<tag k=""railway"" v=""(station|halt)""/>$", "<tag k=\"railway\" v=\"station\"/>|<tag k=\"railway\" v=\"halt\"/>")]
    [InlineData("[Tt]okyo", "okyo")]
    [InlineData("colou?r", "colo")]
    [InlineData(@"ERROR\s+code=\d+", "ERROR")]
    [InlineData("foo|barbaz", "foo|barbaz")]
    [InlineData(@"a\.b\.c", "a.b.c")]
    [InlineData("ab+c", "ab")]
    [InlineData("(?:abc|abd)x", "abcx|abdx")]
    [InlineData("(?<word>hello) world", "hello world")]
    [InlineData(@"x(ab)+y", "ab")]
    public void 必須リテラルを取り出す(string pattern, string expected)
    {
        var got = RegexLiterals.Extract(pattern, ignoreCase: false);
        Assert.NotNull(got);
        Assert.Equal(expected.Split('|').Order(StringComparer.Ordinal), got!.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(@"\d{4}-\d{2}")]           // リテラルが無い
    [InlineData(@"[0-9]{3}-[0-9]{4}""")]   // 1文字しか取れない
    [InlineData("(?i)tokyo")]              // インラインの大小無視
    [InlineData("abc|.*")]                 // 取れない枝がある
    [InlineData("(abc)?def?")]             // どれも無くてよい（de は取れるが2文字未満にはならない → de）
    [InlineData(@"\x41BC")]                // 引数付きエスケープの引数をリテラルと読まない（BC だけ）
    [InlineData("a(?=bcd)")]               // 先読みは本文を消費しない
    [InlineData("")]
    public void 取れない式やあやしい式では絞り込まない_または安全な部分だけ(string pattern)
    {
        var got = RegexLiterals.Extract(pattern, ignoreCase: false);
        if (got is null) return;
        // 取れた場合でも、下の性質（当たる行は必ず含む）は守る
        foreach (var text in new[] { "abcd", "def", "ABC", "xxBCyy", "de", "tokyo 2026-09 123-4567\"" })
            if (Regex.IsMatch(text, pattern))
                Assert.Contains(got, l => text.Contains(l, StringComparison.Ordinal));
    }

    [Fact]
    public void 大小無視では取り出さない()
        => Assert.Null(RegexLiterals.Extract("tokyo", ignoreCase: true));

    [Fact]
    public void 当たる行は必ず取り出したリテラルを含む_ランダムな式と行で確かめる()
    {
        var rnd = new Random(20260915);
        const string alphabet = "abcxy-\"=:東京";
        string Atom(int depth)
        {
            switch (rnd.Next(depth > 2 ? 6 : 9))
            {
                case 0: return alphabet[rnd.Next(alphabet.Length)].ToString();
                case 1: return new string(alphabet[rnd.Next(alphabet.Length)], 1) + alphabet[rnd.Next(alphabet.Length)];
                case 2: return "[abc]";
                case 3: return ".";
                case 4: return @"\d";
                case 5: return Regex.Escape(alphabet[rnd.Next(alphabet.Length)].ToString());
                case 6: return "(" + Seq(depth + 1) + "|" + Seq(depth + 1) + ")";
                case 7: return "(?:" + Seq(depth + 1) + ")";
                default: return "(" + Seq(depth + 1) + ")";
            }
        }
        string Quant() => rnd.Next(8) switch { 0 => "?", 1 => "*", 2 => "+", 3 => "{0,2}", 4 => "{1,3}", 5 => "{2}", _ => "" };
        string Seq(int depth)
        {
            var sb = new StringBuilder();
            for (int n = rnd.Next(1, 5); n > 0; n--) sb.Append(Atom(depth)).Append(Quant());
            return sb.ToString();
        }

        int checkedPatterns = 0, withLiterals = 0, matches = 0;
        for (int round = 0; round < 3000; round++)
        {
            string pattern = rnd.Next(5) == 0 ? Seq(0) + "|" + Seq(0) : (rnd.Next(4) == 0 ? "^" : "") + Seq(0);
            Regex regex;
            // ランダムな式には入れ子の量指定子（(a+)+ など）が混ざり、バックトラックで止まらなくなることがある
            try { regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)); }
            catch (ArgumentException) { continue; }
            checkedPatterns++;

            var literals = RegexLiterals.Extract(pattern, ignoreCase: false);
            if (literals is null) continue;
            withLiterals++;

            for (int t = 0; t < 60; t++)
            {
                var line = new StringBuilder();
                for (int k = rnd.Next(0, 24); k > 0; k--)
                    line.Append(rnd.Next(4) == 0 ? "0123456789"[rnd.Next(10)] : alphabet[rnd.Next(alphabet.Length)]);
                // 当たりやすくするため、式から作った文字列を混ぜる
                if (t % 3 == 0) line.Insert(rnd.Next(line.Length + 1), string.Concat(literals[rnd.Next(literals.Count)]));
                string text = line.ToString();
                try { if (!regex.IsMatch(text)) continue; }
                catch (RegexMatchTimeoutException) { continue; }
                matches++;
                Assert.True(literals.Any(l => text.Contains(l, StringComparison.Ordinal)),
                    $"式 {pattern} は「{text}」に当たるのに、リテラル [{string.Join(" | ", literals)}] をどれも含まない");
            }
        }
        Assert.True(checkedPatterns > 2000 && withLiterals > 300 && matches > 3000,
            $"試した数が少なすぎる: 式 {checkedPatterns}・リテラルあり {withLiterals}・当たり {matches}");
    }

    [Fact]
    public void 候補の位置はいちばん手前を返す()
    {
        var finder = new LiteralFinder(["ccc", "bb", "a"], Encoding.UTF8);
        byte[] text = Encoding.UTF8.GetBytes("xxbbxxaxxccc");
        finder.Reset();
        Assert.Equal(2, finder.IndexOf(text, 0));
        Assert.Equal(6, finder.IndexOf(text, 3));
        Assert.Equal(9, finder.IndexOf(text, 7));
        Assert.Equal(-1, finder.IndexOf(text, 10));
    }
}
