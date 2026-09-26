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
        Assert.Contains(@"\p{L}", invalid.Value.Ja);
        Assert.Contains("POSIX", invalid.Value.En);
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
