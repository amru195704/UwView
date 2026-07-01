using System.Text;
using UwView.Core;

namespace UwView.Core.Tests;

/// <summary>10 万行の既知ファイルを 1 度だけ生成して共有するフィクスチャ。</summary>
public sealed class BigFileFixture : IDisposable
{
    public const int LineCount = 100_000;
    public string Path { get; }

    public BigFileFixture()
    {
        EncodingDetector.EnsureCodePagesRegistered();
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"uwview_{Guid.NewGuid():N}.txt");
        var sb = new StringBuilder(LineCount * 20);
        for (int n = 1; n <= LineCount; n++)
            sb.Append("line ").Append(n).Append(" テスト行\n");
        File.WriteAllText(Path, sb.ToString(), new UTF8Encoding(false));
    }

    public static string Expected(long lineIndex) => $"line {lineIndex + 1} テスト行";

    public void Dispose()
    {
        try { File.Delete(Path); } catch { /* best effort */ }
    }
}

public class LineDocumentTests : IClassFixture<BigFileFixture>
{
    private readonly BigFileFixture _big;
    public LineDocumentTests(BigFileFixture big) => _big = big;

    private static async Task<LineDocument> OpenAsync(string path, int blockLines = 256)
    {
        var src = new MmapByteSource(path);
        var enc = EncodingDetector.Detect(src);
        var nl = EncodingDetector.DetectNewline(src);
        var doc = new LineDocument(src, enc, nl, blockLines);
        await doc.BuildIndexAsync();
        return doc;
    }

    private static async Task<string> WriteTempAsync(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"uwview_{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    [Fact]
    public async Task TotalLines_Matches()
    {
        await using var doc = await OpenAsync(_big.Path);
        Assert.Equal(BigFileFixture.LineCount, doc.TotalLines);
    }

    [Fact]
    public async Task GetLine_First_Last_And_Random_AreCorrect()
    {
        await using var doc = await OpenAsync(_big.Path);

        Assert.Equal(BigFileFixture.Expected(0), doc.GetLine(0));
        Assert.Equal(BigFileFixture.Expected(BigFileFixture.LineCount - 1), doc.GetLine(BigFileFixture.LineCount - 1));

        var rnd = new Random(12345);
        for (int i = 0; i < 200; i++)
        {
            long idx = rnd.Next(BigFileFixture.LineCount);
            Assert.Equal(BigFileFixture.Expected(idx), doc.GetLine(idx));
        }
    }

    [Fact]
    public async Task GetLine_OutOfRange_Throws()
    {
        await using var doc = await OpenAsync(_big.Path);
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.GetLine(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.GetLine(BigFileFixture.LineCount));
    }

    [Fact]
    public async Task GetPage_ReturnsAlignedLines()
    {
        await using var doc = await OpenAsync(_big.Path);

        var head = doc.GetPage(0, 5);
        Assert.Equal(5, head.Count);
        Assert.Equal(BigFileFixture.Expected(0), head[0]);
        Assert.Equal(BigFileFixture.Expected(4), head[4]);

        // 行の途中のオフセットからでも次の行頭に揃う
        long mid = doc.LineStartOffset(1000) + 3;
        var page = doc.GetPage(mid, 3);
        Assert.Equal(BigFileFixture.Expected(1001), page[0]);
    }

    [Fact]
    public async Task OffsetToLineIndex_RoundTrips()
    {
        await using var doc = await OpenAsync(_big.Path);
        foreach (long idx in new long[] { 0, 1, 255, 256, 257, 1000, 50_000, 99_999 })
        {
            long off = doc.LineStartOffset(idx);
            Assert.Equal(idx, doc.OffsetToLineIndex(off));
        }
    }

    [Fact]
    public async Task Crlf_TrailingCr_IsStripped()
    {
        string path = await WriteTempAsync(Encoding.UTF8.GetBytes("alpha\r\nbeta\r\ngamma\r\n"));
        try
        {
            await using var doc = await OpenAsync(path);
            Assert.Equal(3, doc.TotalLines);
            Assert.Equal("alpha", doc.GetLine(0));
            Assert.Equal("beta", doc.GetLine(1));
            Assert.Equal("gamma", doc.GetLine(2));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task NoTrailingNewline_CountsLastLine()
    {
        string path = await WriteTempAsync(Encoding.UTF8.GetBytes("a\nb\nc"));
        try
        {
            await using var doc = await OpenAsync(path);
            Assert.Equal(3, doc.TotalLines);
            Assert.Equal("c", doc.GetLine(2));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EmptyFile_HasZeroLines()
    {
        string path = await WriteTempAsync(Array.Empty<byte>());
        try
        {
            await using var doc = await OpenAsync(path);
            Assert.Equal(0, doc.TotalLines);
            Assert.Equal(0, doc.Length);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ShiftJis_File_DecodesJapanese()
    {
        var sjis = Encoding.GetEncoding(932);
        var content = "一行目です\n二行目です\n三行目です\n";
        string path = await WriteTempAsync(sjis.GetBytes(content));
        try
        {
            await using var doc = await OpenAsync(path);
            Assert.Equal(932, doc.Encoding.CodePage);
            Assert.Equal("一行目です", doc.GetLine(0));
            Assert.Equal("三行目です", doc.GetLine(2));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EncodingSwitch_DoesNotRebuildIndex()
    {
        // UTF-8 の日本語ファイルを Shift-JIS として誤読 → 再度 UTF-8 に戻して正しく読める
        string path = await WriteTempAsync(new UTF8Encoding(false).GetBytes("日本語\nテスト\n"));
        try
        {
            await using var doc = await OpenAsync(path);
            var indexBefore = doc.Index;
            long totalBefore = doc.TotalLines!.Value;

            Assert.Equal("日本語", doc.GetLine(0));

            doc.Encoding = EncodingDetector.ShiftJis; // 差し替え（索引はそのまま）
            Assert.Same(indexBefore, doc.Index);
            Assert.Equal(totalBefore, doc.TotalLines!.Value);
            _ = doc.GetLine(0); // 例外なく読める（内容は化けるが index 再構築不要）

            doc.Encoding = new UTF8Encoding(false);
            Assert.Equal("日本語", doc.GetLine(0)); // 元に戻る
        }
        finally { File.Delete(path); }
    }
}
