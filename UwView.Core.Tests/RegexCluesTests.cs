using System.Text;
using System.Text.RegularExpressions;

namespace UwView.Core.Tests;

/// <summary>短い手がかり（<see cref="RegexClues"/>）。当たる行が必ず手がかりを含むこと（取りこぼさないこと）が一番大事。</summary>
public class RegexCluesTests
{
    private static string[] Hex(IReadOnlyList<byte[]>? needles) => needles is null ? [] : needles.Select(Convert.ToHexString).ToArray();

    [Theory]
    [InlineData("[0-9]{4}-[0-9]{2}", new[] { "2D" })]                 // -
    [InlineData(@"\d+\.\d{6,}", new[] { "2E" })]                      // .
    [InlineData("[ぁ-ん]{3,}", new[] { "E381", "E382" })]              // ひらがなは E3 81 か E3 82 で始まる
    [InlineData("^ +<", new string[0])]                               // 空白も < もほぼ全行にある
    [InlineData("^ +<a-", new[] { "2D" })]                            // ありふれていない - を選ぶ
    [InlineData(@"[A-Z][a-z]+ [A-Z][a-z]+", new string[0])]          // 空白だけ・大きなクラスは使わない
    [InlineData("x?-", new[] { "2D" })]                               // x は 0 回でもよい
    [InlineData(@"(foo|bar)-\d", new[] { "2D" })]                     // グループは読み飛ばす
    [InlineData("[-_]x", new[] { "78" })]                              // 同じ長さなら候補の少ない x
    [InlineData("[0-3]", new string[0])]                              // 数字だけのクラス
    [InlineData("[-_]", new[] { "2D", "5F" })]                         // 文字が 2 つのクラス
    [InlineData("a|b", new string[0])]                                // 一番外の選択は取らない
    [InlineData("(?i)a-b", new string[0])]                            // オプションは取らない
    [InlineData("[^-]+", new string[0])]                              // 否定のクラス
    [InlineData(@"[\d]-?", new string[0])]                            // \d を含むクラス・0 回でもよい -
    [InlineData("東", new[] { "E69DB1" })]
    public void 取り出し(string pattern, string[] expected)
    {
        var got = RegexClues.Extract(pattern);
        if (expected.Length == 0) Assert.Null(got);
        else Assert.Equal(expected, Hex(got));
    }

    /// <summary>ランダムな式とランダムな行で、当たる行は必ず手がかりのどれかを含むこと（RegexLiteralsTests と同じ形）。</summary>
    [Fact]
    public void 当たる行は必ず手がかりを含む()
    {
        var rnd = new Random(20261004);
        string[] atoms = ["a", "b", "-", ".", "<", " ", "ぁ", "ん", "東", @"\d", @"\w", @"\.", @"\-", "[ab]", "[ぁ-ん]", "[-_]",
                          "[0-9]", "[^a]", ".", "(a|b)", "(?:x-)", "^", "$", "[東京]", "x"];
        string[] quants = ["", "", "", "?", "*", "+", "{2}", "{0,2}", "{1,3}"];
        string alphabet = "ab-.< _ぁんあ東京x0123456789";
        int checkedPatterns = 0, withClues = 0, matches = 0;
        for (int round = 0; round < 4000; round++)
        {
            var sb = new StringBuilder();
            for (int k = rnd.Next(1, 6); k > 0; k--) sb.Append(atoms[rnd.Next(atoms.Length)]).Append(quants[rnd.Next(quants.Length)]);
            string pattern = sb.ToString();
            Regex regex;
            try { regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)); }
            catch (ArgumentException) { continue; }
            checkedPatterns++;
            var clues = RegexClues.Extract(pattern);
            if (clues is null) continue;
            withClues++;
            for (int t = 0; t < 60; t++)
            {
                var line = new StringBuilder();
                for (int k = rnd.Next(0, 16); k > 0; k--) line.Append(alphabet[rnd.Next(alphabet.Length)]);
                string text = line.ToString();
                try { if (!regex.IsMatch(text)) continue; }
                catch (RegexMatchTimeoutException) { continue; }
                matches++;
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                Assert.True(clues.Any(c => bytes.AsSpan().IndexOf(c) >= 0),
                    $"式 {pattern} は「{text}」に当たるのに、手がかり [{string.Join(" | ", Hex(clues))}] をどれも含まない");
            }
        }
        Assert.True(checkedPatterns > 2500 && withClues > 500 && matches > 3000,
            $"試した数が少なすぎる: 式 {checkedPatterns}・手がかりあり {withClues}・当たり {matches}");
    }
}
