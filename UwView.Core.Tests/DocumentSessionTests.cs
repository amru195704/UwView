using System.Text;
using UwView.Core;

namespace UwView.Core.Tests;

public class DocumentSessionTests
{
    public DocumentSessionTests() => EncodingDetector.EnsureCodePagesRegistered();

    private static async Task<string> WriteTempAsync(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"uwview_{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    [Fact]
    public async Task Open_StartsInPageMode_ThenPromotesToLineMode()
    {
        var content = new StringBuilder();
        for (int i = 1; i <= 5000; i++) content.Append("行 ").Append(i).Append('\n');
        string path = await WriteTempAsync(new UTF8Encoding(false).GetBytes(content.ToString()));
        try
        {
            await using var session = DocumentSession.Open(path);
            Assert.Equal(ViewMode.Page, session.Mode);
            Assert.False(session.IsIndexed);

            // ページモードで即読める（索引不要）
            var page = session.Document.GetPage(session.TopByteOffset, 3);
            Assert.Equal("行 1", page[0]);

            await session.BuildIndexAsync();

            Assert.True(session.IsIndexed);
            Assert.Equal(ViewMode.Line, session.Mode); // 昇格
            Assert.Equal(5000, session.Document.TotalLines);
            Assert.Equal("行 1", session.Document.GetLine(0));
            Assert.Equal("行 5000", session.Document.GetLine(4999));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SetEncoding_ChangesDecodingWithoutReindex()
    {
        string path = await WriteTempAsync(new UTF8Encoding(false).GetBytes("日本語\nテスト\n"));
        try
        {
            await using var session = DocumentSession.Open(path);
            await session.BuildIndexAsync();
            var indexBefore = session.Index;

            Assert.Equal("日本語", session.Document.GetLine(0));

            session.SetEncoding(EncodingDetector.ShiftJis);
            Assert.Same(indexBefore, session.Index); // 索引は再構築されない

            session.SetEncoding(new UTF8Encoding(false));
            Assert.Equal("日本語", session.Document.GetLine(0));
        }
        finally { File.Delete(path); }
    }
}
