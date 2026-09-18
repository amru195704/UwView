using System.Text;
using UwView.Core;

namespace UwView.Core.Tests;

/// <summary>
/// ソースレビュー（2026-09-19）で指摘された8件の再発防止テスト。
/// 指摘3（取り消した検索の通知）と指摘8（不正な正規表現）は
/// DocumentSessionTests / UvfCliTests 側に置いてある。
/// </summary>
public class SourceReview0919Tests
{
    private static async Task<string> WriteTempAsync(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), $"uwview_review_{Guid.NewGuid():N}.txt");
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    private static async Task<LineDocument> OpenAsync(string path)
    {
        EncodingDetector.EnsureCodePagesRegistered();
        var src = new MmapByteSource(path);
        var enc = EncodingDetector.Detect(src);
        var doc = new LineDocument(src, enc, EncodingDetector.DetectNewline(src));
        await doc.BuildIndexAsync();
        return doc;
    }

    // ── 指摘1: Tail の追記分を検索できる ──────────────────────

    [Fact]
    public async Task 指摘1_Tailで伸びたぶんも検索できる()
    {
        string path = await WriteTempAsync("alpha\nbeta\n"u8.ToArray());
        try
        {
            await using var session = DocumentSession.Open(path);
            await session.BuildIndexAsync();

            await using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                fs.Write("gamma\n"u8);
            Assert.True(await session.PollTailAsync());

            await session.StartSearchAsync(new SearchOptions("gamma"));
            Assert.Single(session.SearchHits);
        }
        finally { File.Delete(path); }
    }

    // ── 指摘2: 行の区切りを文字コード・改行種別に合わせる ──────

    [Fact]
    public async Task 指摘2_UTF16LEの2行を2行として数え本文も読める()
    {
        var bytes = new List<byte> { 0xFF, 0xFE };
        bytes.AddRange(Encoding.Unicode.GetBytes("alpha\nbeta\n"));
        string path = await WriteTempAsync(bytes.ToArray());
        try
        {
            await using var doc = await OpenAsync(path);
            Assert.Equal(2L, doc.TotalLines);
            Assert.Equal("alpha", doc.GetLine(0));
            Assert.Equal("beta", doc.GetLine(1));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task 指摘2_CR改行の2行を2行として数える()
    {
        string path = await WriteTempAsync("alpha\rbeta\r"u8.ToArray());
        try
        {
            await using var doc = await OpenAsync(path);
            Assert.Equal(NewlineStyle.Cr, doc.Newline);
            Assert.Equal(2L, doc.TotalLines);
            Assert.Equal("alpha", doc.GetLine(0));
        }
        finally { File.Delete(path); }
    }

    // ── 指摘4: 未取得の本文を待ってから読む ────────────────────

    [Fact]
    public async Task 指摘4_同期読みでは取れない本文も非同期なら読める()
    {
        var src = new AsyncOnlySource("alpha\nbeta\n"u8.ToArray());
        var doc = new LineDocument(src, new DetectedEncoding(new UTF8Encoding(false), "UTF-8", false, 0), NewlineStyle.Lf);
        await using (doc)
        {
            Assert.Equal("", doc.GetLineAtOffset(0));                    // 同期は未取得＝空
            Assert.Equal("alpha", await doc.GetLineAtOffsetAsync(0));    // 非同期は取得を待つ
            Assert.Equal("beta", await doc.GetLineAtOffsetAsync(6));
        }
    }

    /// <summary>同期 Read が常に未取得を返す読み取り元（ブラウザー版の契約を模す）。</summary>
    private sealed class AsyncOnlySource(byte[] data) : IByteSource
    {
        public long Length => data.Length;
        public int Read(long offset, Span<byte> buffer) => 0;
        public ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken ct = default)
        {
            if (offset >= data.Length || buffer.Length == 0) return new(0);
            int n = (int)Math.Min(data.Length - offset, buffer.Length);
            data.AsSpan((int)offset, n).CopyTo(buffer.Span);
            return new(n);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // ── 指摘5: バイト検索は文字境界が保証される文字コードだけ ──

    [Fact]
    public async Task 指摘5_ShiftJISのソはバックスラッシュ検索に当たらない()
    {
        EncodingDetector.EnsureCodePagesRegistered();
        byte[] sjis = Encoding.GetEncoding(932).GetBytes("ソソソ\nC:\\temp\n");
        string path = await WriteTempAsync(sjis);
        try
        {
            await using var session = DocumentSession.Open(path);
            await session.BuildIndexAsync();
            await session.StartSearchAsync(new SearchOptions("\\"));
            Assert.Single(session.SearchHits);   // 2行目だけ
        }
        finally { File.Delete(path); }
    }

    // ── 再レビュー: 退行C（Tail の未完行）────────────────────────

    [Fact]
    public async Task 退行C_改行なしの追記もTailで1行として見える()
    {
        string path = await WriteTempAsync("alpha\n"u8.ToArray());
        try
        {
            await using var session = DocumentSession.Open(path);
            await session.BuildIndexAsync();
            Assert.Equal(1, session.Document.TotalLines);

            await using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                fs.Write("beta"u8);                       // 末尾に改行が無い追記
            Assert.True(await session.PollTailAsync());

            Assert.Equal(2, session.Document.TotalLines);
            Assert.Equal("beta", session.Document.GetLine(1));
        }
        finally { File.Delete(path); }
    }

    // ── 再レビュー: 指摘B（UTF-16 の文字全体で改行を判定）────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 指摘B_0Aを含む文字を改行と誤認しない(bool bigEndian)
    {
        Encoding enc = bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode;
        byte[] data = [.. enc.GetPreamble(), .. enc.GetBytes("A\u010AB\nC\n")];   // Ċ は 0A 01 / 01 0A
        string path = await WriteTempAsync(data);
        try
        {
            await using var doc = await OpenAsync(path);
            Assert.Equal(2L, doc.TotalLines);
            Assert.Equal("A\u010AB", doc.GetLine(0));
        }
        finally { File.Delete(path); }
    }

    // ── 再レビュー: 指摘A（検索も同じ行区切りを使う）──────────────

    [Theory]
    [InlineData("UTF16LE", false)]
    [InlineData("UTF16LE", true)]
    [InlineData("CR", false)]
    [InlineData("CR", true)]
    public async Task 指摘A_文字コードと改行に合わせて検索が行を切る(string format, bool indexed)
    {
        Encoding enc = format == "UTF16LE" ? Encoding.Unicode : new UTF8Encoding(false);
        string nl = format == "CR" ? "\r" : "\n";
        byte[] data = [.. enc.GetPreamble(), .. enc.GetBytes($"alpha{nl}beta{nl}")];
        string path = await WriteTempAsync(data);
        try
        {
            await using var session = DocumentSession.Open(path);
            if (indexed) await session.BuildIndexAsync();
            await session.StartSearchAsync(new SearchOptions("^beta$", UseRegex: true));

            long expected = enc.GetPreamble().Length + enc.GetByteCount($"alpha{nl}");
            Assert.Equal([expected], session.SearchHits);
            Assert.Equal(2, session.Document.TotalLines);
        }
        finally { File.Delete(path); }
    }

    // ── 再レビュー: 指摘E（大小無視の長大行も索引の有無で変わらない）──

    [Fact]
    public async Task 指摘E_大小無視の長大行検索も索引の有無で変わらない()
    {
        byte[] data = Encoding.UTF8.GetBytes(new string('a', 100_000) + "TARGET" + new string('a', 2_000_000) + "\n");
        string path = await WriteTempAsync(data);
        try
        {
            await using var combined = DocumentSession.Open(path);
            await using var indexed = DocumentSession.Open(path);
            await indexed.BuildIndexAsync();

            var options = new SearchOptions("target", IgnoreCase: true);
            await combined.StartSearchAsync(options);
            await indexed.StartSearchAsync(options);

            Assert.Single(combined.SearchHits);
            Assert.Equal(combined.SearchHits, indexed.SearchHits);
        }
        finally { File.Delete(path); }
    }

    // ── 指摘6: 文字クラス減算で候補行を捨てない ────────────────

    [Fact]
    public void 指摘6_文字クラス減算から誤った必須文字列を取り出さない()
    {
        var literals = RegexLiterals.Extract("[a-z-[aeiou]]XYZ", false) ?? [];
        Assert.DoesNotContain(literals, s => s.Contains(']'));
    }
}
