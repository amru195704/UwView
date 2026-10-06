using System.Text;
using UwView.Core.Cli;

namespace UwView.Core.Tests;

/// <summary>
/// 当たった行の書き出し（<see cref="HitLineWriter"/>）が、作り替える前の書き方（1 行ごとに文字列を作る）と同じ文字を出すこと。
/// 2026-10-07：ほとんどの行が当たる式で確保が 28GB になっていたのを、使い回す領域に書く形にした。
/// </summary>
public class HitLineWriterTests
{
    static HitLineWriterTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    // 作り替える前の書き方（MultiFileSearch.Text と UvfCli の単一ファイル）
    private static string Before(string? name, bool lineNumbers, long line, byte[] text, Encoding encoding)
    {
        var sb = new StringBuilder();
        if (name is not null) { sb.Append(name); sb.Append(':'); }
        if (lineNumbers) { sb.Append(line + 1); sb.Append('\t'); }
        sb.Append(encoding.GetString(text));
        return sb.ToString() + "\n";
    }

    private static string After(HitLineWriter w, string? name, bool lineNumbers, long line, byte[] text, Encoding encoding)
    {
        var sw = new StringWriter { NewLine = "\n" };
        w.Write(sw, name, lineNumbers, line, text, encoding);
        return sw.ToString();
    }

    public static TheoryData<string?, bool, long> Shapes => new()
    {
        { "src/a.c", true, 0 },
        { "src/a.c", true, 9 },
        { "日本/東京.osm", true, 123456789 },
        { null, true, 41 },
        { "x", false, 7 },
        { null, false, long.MaxValue - 1 },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void 作り替える前と同じ文字を出す_UTF8(string? name, bool lineNumbers, long line)
    {
        var w = new HitLineWriter();
        foreach (string body in new[] { "", "abc", "    <tag k=\"name\" v=\"東京駅\"/>", "😀 絵文字 ｶﾅ", new string('あ', 5000) })
        {
            byte[] text = Encoding.UTF8.GetBytes(body);
            Assert.Equal(Before(name, lineNumbers, line, text, Encoding.UTF8), After(w, name, lineNumbers, line, text, Encoding.UTF8));
        }
    }

    [Fact]
    public void 壊れたUTF8も作り替える前と同じ置き換えになる()
    {
        var w = new HitLineWriter();
        var enc = new UTF8Encoding(false);
        foreach (byte[] text in new[] { new byte[] { 0x41, 0xE3, 0x81 }, new byte[] { 0xFF, 0xFE, 0x41 }, new byte[] { 0xC0, 0xAF } })
            Assert.Equal(Before("f", true, 3, text, enc), After(w, "f", true, 3, text, enc));
    }

    [Fact]
    public void ShiftJISも作り替える前と同じ文字を出す()
    {
        var w = new HitLineWriter();
        var sjis = Encoding.GetEncoding(932);
        byte[] text = sjis.GetBytes("東京都 ｶﾀｶﾅ ABC");
        Assert.Equal(Before("a.txt", true, 0, text, sjis), After(w, "a.txt", true, 0, text, sjis));
    }

    [Fact]
    public void 長い行のあとに短い行を書いても前の行の残りが出ない()
    {
        var w = new HitLineWriter();
        byte[] longLine = Encoding.UTF8.GetBytes(new string('x', 10000));
        byte[] shortLine = Encoding.UTF8.GetBytes("yz");
        After(w, null, true, 0, longLine, Encoding.UTF8);
        Assert.Equal("2\tyz\n", After(w, null, true, 1, shortLine, Encoding.UTF8));
    }
}
