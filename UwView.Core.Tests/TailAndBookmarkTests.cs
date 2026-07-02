using System.Text;
using UwView.Core;

namespace UwView.Core.Tests;

public class TailAndBookmarkTests
{
    public TailAndBookmarkTests() => EncodingDetector.EnsureCodePagesRegistered();

    private static string NewTempPath() =>
        Path.Combine(Path.GetTempPath(), $"uwview_{Guid.NewGuid():N}.txt");

    // ── Tail（§11-③）──────────────────────────────────────────

    [Fact]
    public async Task Tail_AppendedLines_ExtendIndexAndRead()
    {
        string path = NewTempPath();
        await File.WriteAllTextAsync(path, "alpha\nbeta\n", new UTF8Encoding(false));
        try
        {
            await using var session = DocumentSession.Open(path);
            await session.BuildIndexAsync();
            Assert.Equal(2, session.Document.TotalLines);

            // 別ハンドルから追記（ログの追記を模擬）
            await using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                fs.Write(new UTF8Encoding(false).GetBytes("gamma\ndelta\n"));

            Assert.True(await session.PollTailAsync());

            Assert.Equal(4, session.Document.TotalLines);
            Assert.Equal("gamma", session.Document.GetLine(2));
            Assert.Equal("delta", session.Document.GetLine(3));
            Assert.False(await session.PollTailAsync()); // 変化なし
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Tail_PartialLastLine_CompletedByAppend()
    {
        string path = NewTempPath();
        await File.WriteAllTextAsync(path, "one\ntw", new UTF8Encoding(false)); // 末尾改行なし
        try
        {
            await using var session = DocumentSession.Open(path);
            await session.BuildIndexAsync();
            Assert.Equal(2, session.Document.TotalLines);
            Assert.Equal("tw", session.Document.GetLine(1));

            await using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                fs.Write(new UTF8Encoding(false).GetBytes("o\nthree\n"));

            Assert.True(await session.PollTailAsync());

            Assert.Equal(3, session.Document.TotalLines);
            Assert.Equal("two", session.Document.GetLine(1));   // 部分行が完成
            Assert.Equal("three", session.Document.GetLine(2)); // LRU破棄も効いている
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Tail_CrossesCheckpointBoundary()
    {
        // blockLines(256) を跨ぐ追記でチェックポイントが正しく増えること
        string path = NewTempPath();
        var sb = new StringBuilder();
        for (int i = 1; i <= 100; i++) sb.Append($"L{i}\n");
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(false));
        try
        {
            await using var session = DocumentSession.Open(path);
            await session.BuildIndexAsync();

            sb.Clear();
            for (int i = 101; i <= 600; i++) sb.Append($"L{i}\n");
            await using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                fs.Write(new UTF8Encoding(false).GetBytes(sb.ToString()));

            Assert.True(await session.PollTailAsync());

            Assert.Equal(600, session.Document.TotalLines);
            Assert.Equal("L1", session.Document.GetLine(0));
            Assert.Equal("L300", session.Document.GetLine(299)); // 256境界越えの行
            Assert.Equal("L600", session.Document.GetLine(599));
            Assert.True(session.Index!.CheckpointCount >= 2);   // 600行 ÷ 256 → 3件
        }
        finally { File.Delete(path); }
    }

    // ── ブックマーク（§11-④）─────────────────────────────────

    [Fact]
    public async Task Bookmarks_Toggle_Navigate()
    {
        string path = NewTempPath();
        var sb = new StringBuilder();
        for (int i = 1; i <= 100; i++) sb.Append($"line {i}\n");
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(false));
        try
        {
            await using var session = DocumentSession.Open(path);
            await session.BuildIndexAsync();

            long off10 = session.Document.LineStartOffset(9);
            long off50 = session.Document.LineStartOffset(49);

            Assert.True(session.ToggleBookmark(off50));
            Assert.True(session.ToggleBookmark(off10));
            Assert.Equal(new[] { off10, off50 }, session.Bookmarks); // 昇順維持
            Assert.True(session.HasBookmark(off10));

            Assert.Equal(off10, session.NextBookmark(-1));
            Assert.Equal(off50, session.NextBookmark(off10));
            Assert.Null(session.NextBookmark(off50));
            Assert.Equal(off10, session.PrevBookmark(off50));

            Assert.False(session.ToggleBookmark(off10)); // トグルで削除
            Assert.Single(session.Bookmarks);
        }
        finally { File.Delete(path); }
    }
}
