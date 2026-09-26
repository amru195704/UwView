namespace UwView.Core.Tests;

/// <summary>正規表現の書き方の違い（指示書 WideField v1.7 §10.2）。</summary>
public class RegexDialectTests
{
    [Theory]
    [InlineData("(?P<n>a+)b", "(?<n>a+)b")]
    [InlineData(@"\(?P<x", @"\(?P<x")]                 // 逃がした括弧は触らない
    [InlineData("abc", "abc")]
    public void 名前付きグループのPの形を読み替える(string pattern, string expected)
        => Assert.Equal(expected, RegexDialect.Normalize(pattern));

    [Fact]
    public void POSIX文字クラスは書き換えを案内して止める()
    {
        var invalid = RegexDialect.Check("[[:alpha:]]+");
        Assert.NotNull(invalid);
        Assert.Contains("[A-Za-z]", invalid.Value.Ja);      // ripgrep と同じ ASCII の範囲を先に
        Assert.Contains(@"\p{L}", invalid.Value.Ja);        // 日本語なども含めるときの書き方も添える
        Assert.Contains("POSIX", invalid.Value.En);
        Assert.Contains("[0-9]", RegexDialect.Check("[[:digit:]]")!.Value.Ja);
    }

    /// <summary>
    /// 案内する ASCII の書き方は ripgrep の POSIX 文字クラスと同じ範囲（全角数字・é・日本語には当たらない）。
    /// .NET の \d は全角数字にも当たるので、範囲をそろえるなら [0-9]（外部レビュー 2026-09-27）。
    /// </summary>
    [Theory]
    [InlineData("[A-Za-z]", "abc", true)]
    [InlineData("[A-Za-z]", "é", false)]
    [InlineData("[A-Za-z]", "東京", false)]
    [InlineData("[0-9]", "12", true)]
    [InlineData("[0-9]", "１２", false)]
    [InlineData(@"\d", "１２", true)]
    public void 案内する書き方の範囲(string pattern, string text, bool matches)
        => Assert.Equal(matches, System.Text.RegularExpressions.Regex.IsMatch(text, pattern));

    [Fact]
    public void 漢字の近い書き方は々と〇と拡張Aにも当たる()
    {
        string hint = RegexDialect.Check(@"\p{Han}+")!.Value.Ja;
        string wide = hint[hint.IndexOf('[')..(hint.IndexOf(']') + 1)];
        Assert.Null(RegexDialect.Check(wide));
        foreach (string c in new[] { "漢", "々", "〇", "㐀" }) Assert.Matches(wide, c);
        Assert.DoesNotMatch(@"\p{IsCJKUnifiedIdeographs}", "々");
    }

    [Fact]
    public void 用字名は書き換え先を添える()
    {
        var invalid = RegexDialect.Check(@"\p{Han}+");
        Assert.NotNull(invalid);
        Assert.Contains("IsCJKUnifiedIdeographs", invalid.Value.Ja);
        Assert.Contains("IsCJKUnifiedIdeographs", invalid.Value.En);
    }

    [Theory]
    [InlineData(@"\p{IsCJKUnifiedIdeographs}+")]
    [InlineData("(?P<n>a)")]
    [InlineData(@"(a)(?<=a)b\1")]
    public void 使える書き方は通す(string pattern) => Assert.Null(RegexDialect.Check(pattern));

    [Fact]
    public void 必須の文字列はPの形を読み替えてから取り出す()
        => Assert.Equal(["ERROR"], RegexLiterals.Extract("(?P<k>ERROR)", ignoreCase: false)!);
}
