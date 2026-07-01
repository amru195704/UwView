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
}
