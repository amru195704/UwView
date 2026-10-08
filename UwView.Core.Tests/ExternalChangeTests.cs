using System.Text;
using UwView.Core;

namespace UwView.Core.Tests;

/// <summary>
/// 開いているファイルが外で切り詰め・作り直し・ローテーション・削除されても落ちず、何が起きたかを見分ける
///（v1.8.2 extFS E-0 §1.2）。1.8.1 までは表示が mmap で、切り詰められた所を読むと SIGBUS で落ちていた。
/// </summary>
public class ExternalChangeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "uwview-change-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public ExternalChangeTests()
    {
        EncodingDetector.EnsureCodePagesRegistered();
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "app.log");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static string Lines(string prefix, int count)
        => string.Concat(Enumerable.Range(1, count).Select(i => $"{prefix} {i}\n"));

    private void Write(string text) => File.WriteAllText(_path, text, new UTF8Encoding(false));

    private void Append(string text)
    {
        using var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        fs.Write(Encoding.UTF8.GetBytes(text));
    }

    private void Truncate(long length)
    {
        using var fs = new FileStream(_path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        fs.SetLength(length);
    }

    [Fact]
    public async Task 切り詰められても落ちずに無くなった所は空に読める()
    {
        Write(Lines("line", 2000));
        await using var session = DocumentSession.Open(_path);
        await session.BuildIndexAsync();

        Truncate(0);

        Assert.Equal(FileChange.Truncated, session.CheckExternalChange());
        Assert.Equal(FileChange.Truncated, session.ExternalChange);
        Assert.Equal("", session.Document.GetLine(1999));               // 索引はそのまま・本文は空
        var buf = new byte[4096];
        Assert.Equal(0, session.Source.Read(1000, buf));
    }

    /// <summary>長さは元のまま、途中から先は読めない（探している最中に切り詰められたのと同じ）。</summary>
    private sealed class CutSource(byte[] data, int readable) : IByteSource
    {
        public long Length => data.Length;
        public int Read(long offset, Span<byte> buffer)
        {
            if (offset >= readable) return 0;
            int n = (int)Math.Min(buffer.Length, readable - offset);
            data.AsSpan((int)offset, n).CopyTo(buffer);
            return n;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task 探している最中に行の途中で切り詰められても検索は終わる()
    {
        var data = Encoding.UTF8.GetBytes(Lines("line ERROR", 50_000));
        await using var src = new CutSource(data, data.Length / 2 + 3);  // 行の途中から先が無い
        var separator = LineSeparator.For(Encoding.UTF8, NewlineStyle.Lf);

        var search = SearchService.SearchAsync(src, 0, Encoding.UTF8, new SearchOptions("ERROR"), separator: separator);
        Assert.Same(search, await Task.WhenAny(search, Task.Delay(10_000)));   // 以前はここで回り続けた
    }

    [Fact]
    public async Task 追記は伸びたと見て書き換えではない()
    {
        Write("a\n");
        await using var session = DocumentSession.Open(_path);
        Append(Lines("b", 3000));                                         // 先頭 4KB にかかる長さまで伸ばす

        Assert.Equal(FileChange.Grew, session.CheckExternalChange());
        Assert.Equal(FileChange.None, session.ExternalChange);
        Assert.True(await session.PollTailAsync());
        Assert.Equal(FileChange.None, session.CheckExternalChange());
    }

    [Fact]
    public async Task 同じ長さ以上で中身が変わったら作り直されたと見る()
    {
        Write(Lines("old", 100));
        await using var session = DocumentSession.Open(_path);
        using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            fs.Write(Encoding.UTF8.GetBytes(Lines("new", 200)));          // 同じファイルに上書き（長くなる）

        Assert.Equal(FileChange.Rewritten, session.CheckExternalChange());
    }

    [Fact]
    public async Task 名前を変えて同じ名前で作り直したらローテーションと見て開いている方は読める()
    {
        Write(Lines("old", 100));
        await using var session = DocumentSession.Open(_path);
        await session.BuildIndexAsync();
        File.Move(_path, _path + ".1");
        Write("");

        Assert.Equal(FileChange.Replaced, session.CheckExternalChange());
        Assert.Equal("old 100", session.Document.GetLine(99));           // 名前を変えられた方をそのまま見られる
    }

    [Fact]
    public async Task 空で開いたファイルが別のファイルに置き換わったら気づく()
    {
        Write("");
        await using var session = DocumentSession.Open(_path);
        File.WriteAllText(_path + ".new", "x\n");
        File.Move(_path + ".new", _path, overwrite: true);

        Assert.Equal(FileChange.Replaced, session.CheckExternalChange());
    }

    [Fact]
    public async Task 消されたら消されたと見て同じ名前でできたら置き換わったと見る()
    {
        Write(Lines("line", 10));
        await using var session = DocumentSession.Open(_path);
        await session.BuildIndexAsync();
        File.Delete(_path);

        Assert.Equal(FileChange.Deleted, session.CheckExternalChange());
        Assert.Equal("line 10", session.Document.GetLine(9));            // 開いている内容はそのまま見られる

        Write(Lines("line", 10));                                         // 前と同じ中身で作り直しても
        session.CheckExternalChange();
        Assert.Equal(FileChange.Replaced, session.ExternalChange);
    }

    [Fact]
    public async Task 切り詰めのあとは後の変化で知らせを替えない()
    {
        Write(Lines("line", 10));
        await using var session = DocumentSession.Open(_path);
        Truncate(0);
        session.CheckExternalChange();
        File.Delete(_path);
        session.CheckExternalChange();

        Assert.Equal(FileChange.Truncated, session.ExternalChange);
    }

    [Fact]
    public async Task 変わったあとは追記されても伸ばさない()
    {
        Write(Lines("line", 10));
        await using var session = DocumentSession.Open(_path);
        Truncate(5);
        session.CheckExternalChange();
        Append(Lines("more", 100));

        Assert.False(await session.PollTailAsync());                     // 古い索引のまま伸ばすと行の位置が食い違う
    }
}
