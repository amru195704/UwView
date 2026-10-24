using System.Text;
using UwView.Core;

namespace UwView.Core.Tests;

public class EncodingDetectorTests
{
    public EncodingDetectorTests() => EncodingDetector.EnsureCodePagesRegistered();

    private static DetectedEncoding Detect(byte[] bytes)
        => EncodingDetector.Detect(new MemoryByteSource(bytes));

    [Fact]
    public void Detects_Utf8_NoBom()
    {
        var d = Detect(Encoding.UTF8.GetBytes("これは日本語のテストです。ABC123"));
        Assert.Equal("UTF-8", d.DisplayName);
        Assert.False(d.HasBom);
        Assert.Equal(0, d.BomLength);
    }

    [Fact]
    public void Detects_Utf8_Bom()
    {
        var body = Encoding.UTF8.GetBytes("日本語");
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray();
        var d = Detect(bytes);
        Assert.Equal("UTF-8 (BOM)", d.DisplayName);
        Assert.True(d.HasBom);
        Assert.Equal(3, d.BomLength);
    }

    [Fact]
    public void Detects_PureAscii_As_Utf8()
    {
        var d = Detect(Encoding.ASCII.GetBytes("plain ascii only 12345"));
        Assert.Equal("UTF-8", d.DisplayName);
    }

    [Fact]
    public void Detects_ShiftJis()
    {
        var bytes = Encoding.GetEncoding(932).GetBytes("これはシフトJISの日本語テスト文字列です。");
        var d = Detect(bytes);
        Assert.Equal("Shift-JIS", d.DisplayName);
    }

    [Fact]
    public void Detects_EucJp()
    {
        var bytes = Encoding.GetEncoding(51932).GetBytes("これはEUC-JPの日本語テスト文字列です。");
        var d = Detect(bytes);
        Assert.Equal("EUC-JP", d.DisplayName);
    }

    [Fact]
    public void Detects_Utf16Le_Bom()
    {
        var bytes = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("日本語")).ToArray();
        var d = Detect(bytes);
        Assert.Equal("UTF-16LE", d.DisplayName);
        Assert.Equal(2, d.BomLength);
    }

    [Fact]
    public void Detects_Utf16Be_Bom()
    {
        var bytes = new byte[] { 0xFE, 0xFF }.Concat(Encoding.BigEndianUnicode.GetBytes("日本語")).ToArray();
        var d = Detect(bytes);
        Assert.Equal("UTF-16BE", d.DisplayName);
        Assert.Equal(2, d.BomLength);
    }

    [Fact]
    public void DetectNewline_Lf_Crlf_Cr()
    {
        Assert.Equal(NewlineStyle.Lf, EncodingDetector.DetectNewline(new MemoryByteSource(Encoding.ASCII.GetBytes("a\nb\nc"))));
        Assert.Equal(NewlineStyle.CrLf, EncodingDetector.DetectNewline(new MemoryByteSource(Encoding.ASCII.GetBytes("a\r\nb\r\n"))));
        Assert.Equal(NewlineStyle.Cr, EncodingDetector.DetectNewline(new MemoryByteSource(Encoding.ASCII.GetBytes("a\rb\rc"))));
    }

    [Theory]
    // ASCII を SIMD で読み飛ばすようにした（v1.7.3.6.9）。長い ASCII の後ろに来るものでも、判定は従来の規則どおり
    [InlineData(new byte[] { 0xE6, 0x9D, 0xB1 }, "UTF-8")]        // 東（UTF-8）
    [InlineData(new byte[] { 0x93, 0x8C }, "Shift-JIS")]          // 東（Shift-JIS）→ UTF-8 として不正
    [InlineData(new byte[] { 0xC0, 0x80 }, "Shift-JIS")]          // 長すぎる符号化（overlong）は UTF-8 ではない
    [InlineData(new byte[] { 0xE6, 0x9D }, "UTF-8")]              // 末尾で切れた多バイト文字は「切れただけ」
    public void LongAsciiThenNonAscii(byte[] tail, string expected)
    {
        byte[] data = [.. System.Text.Encoding.ASCII.GetBytes(new string('a', 100_000) + "\n"), .. tail];
        Assert.Equal(expected, EncodingDetector.Detect(data.AsSpan()).DisplayName);
    }

    /// <summary>UTF-32 は断るために BOM で見分ける（LE・BE とも。2026-10-24）。</summary>
    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFE, 0, 0, 0x41, 0, 0, 0 }, "UTF-32LE")]
    [InlineData(new byte[] { 0, 0, 0xFE, 0xFF, 0, 0, 0, 0x41 }, "UTF-32BE")]
    public void Detects_Utf32_Bom_For_Rejection(byte[] data, string expected)
    {
        var d = Detect(data);
        Assert.Equal(expected, d.DisplayName);
        Assert.True(Utf32Rejected.IsUtf32(d));
        Assert.True(Utf32Rejected.IsUtf32Bom(data));
        Assert.Throws<Utf32RejectedException>(() => Utf32Rejected.ThrowIf(d, "x.txt"));
    }

    /// <summary>本物の UTF-32（漢字・絵文字・CJK 拡張 B・改行）は、続きを見ても UTF-32 のまま。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Real_Utf32_Text_Is_Still_Utf32(bool bigEndian)
    {
        var e = new System.Text.UTF32Encoding(bigEndian, byteOrderMark: true);
        byte[] data = [.. e.GetPreamble(), .. e.GetBytes("東京 ERROR 😀 𠮷\r\nA\nB\n"), .. e.GetBytes(char.ConvertFromUtf32(0x40000))];   // まだ使われていない面の字も
        Assert.True(Utf32Rejected.IsUtf32(Detect(data)));
        Assert.True(Utf32Rejected.IsUtf32Bom(data));
    }

    /// <summary>
    /// UTF-16LE で 1 字目が U+0000 だと、先頭 4 バイトが UTF-32LE の BOM と同じ（FF FE 00 00）。続きを見て UTF-16LE と判定する（検査課 2026-10-24 §4-2）。
    /// </summary>
    [Theory]
    [InlineData("東京\n")]
    [InlineData("AB\n")]
    [InlineData("A\r\nB\r\n")]
    [InlineData("ERROR 404\n")]
    public void Utf16Le_Starting_With_Nul_Is_Not_Utf32(string text)
    {
        byte[] data = [0xFF, 0xFE, .. System.Text.Encoding.Unicode.GetBytes("\0" + text)];
        var d = Detect(data);
        Assert.Equal("UTF-16LE", d.DisplayName);
        Assert.False(Utf32Rejected.IsUtf32(d));
        Assert.False(Utf32Rejected.IsUtf32Bom(data));
    }

    /// <summary>
    /// 見分けきれない残り: UTF-16LE で 2 字に 1 字が U+0000〜U+0010 のものは、決まり（UTF-32 を優先）で UTF-32 とみなす（レビュー 2026-10-24 P1-2）。
    /// </summary>
    [Theory]
    [InlineData("A\0B\0")]
    [InlineData("A\nB\nC\n")]
    public void Utf16Le_With_Every_Other_Char_A_Control_Is_Taken_As_Utf32(string text)
    {
        byte[] data = [0xFF, 0xFE, .. System.Text.Encoding.Unicode.GetBytes("\0" + text)];
        Assert.True(Utf32Rejected.IsUtf32(Detect(data)));
    }
}
