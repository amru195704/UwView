using System.Text;
using UwView.Core;
using UwView.Core.Cli;

namespace UwView.Core.Tests;

/// <summary>
/// 生ファイル検索の1回読み経路（指示書 2026-09-17）。索引を作らずに
/// 行番号・一致・本文を同時に出すので、<b>従来と1バイトも違わない</b>ことをここで押さえる。
///
/// 気にするところは境目: 読みブロック（4MB）をまたぐ行、4MB に収まらない長大行、
/// 長大行を読み飛ばしたあとの行番号、BOM、CRLF、UTF-8 以外の文字コード、末尾に改行がない場合。
/// </summary>
public class RawGrepTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "uvf-raw-" + Guid.NewGuid().ToString("N"));

    public RawGrepTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string P(string name) => Path.Combine(_dir, name);

    /// <summary>uvf の検索を実行して stdout を返す。</summary>
    private static async Task<(int Exit, string Out)> Search(string path, string pattern)
    {
        using var stdout = new MemoryStream();
        var env = new UvfEnvironment
        {
            StdOut = stdout, StdErr = new StringWriter(), Japanese = false, LaunchGui = (_, _) => true,
        };
        int code = await UvfCli.RunAsync([path, pattern], env);
        return (code, Encoding.UTF8.GetString(stdout.ToArray()));
    }

    /// <summary>素朴な参照実装（1行ずつ読んで含むかを見るだけ）。</summary>
    private static string Reference(string path, string pattern, Encoding encoding)
    {
        var sb = new StringBuilder();
        using var reader = new StreamReader(path, encoding, detectEncodingFromByteOrderMarks: true);
        long n = 0;
        while (reader.ReadLine() is { } text)
        {
            n++;
            if (text.Contains(pattern, StringComparison.Ordinal)) sb.Append(n).Append('\t').Append(text).Append('\n');
        }
        return sb.ToString();
    }

    private static string Reference(string path, string pattern) => Reference(path, pattern, Encoding.UTF8);

    [Fact]
    public async Task 普通のログで参照実装と一致する()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 20_000; i++)
            sb.Append($"{i:D6} {(i % 10 == 0 ? "ERROR" : "INFO")} dev{i % 4} 東京 seq={i}\n");
        File.WriteAllText(P("a.log"), sb.ToString());

        var (exit, output) = await Search(P("a.log"), "ERROR");
        Assert.Equal(UvfExit.Found, exit);
        Assert.Equal(Reference(P("a.log"), "ERROR"), output);
    }

    [Fact]
    public async Task 読みブロックの境目をまたいでも行番号と本文が合う()
    {
        // 4MB ブロックの境目に一致行が乗るよう、5MB ぶんを 100 バイト前後の行で作る
        var sb = new StringBuilder();
        for (int i = 0; i < 60_000; i++)
            sb.Append($"{i:D8} {new string('x', 70)} {(i % 997 == 0 ? "HIT" : "---")} {i}\n");
        File.WriteAllText(P("big.log"), sb.ToString());
        Assert.True(new FileInfo(P("big.log")).Length > 5 << 20);

        var (_, output) = await Search(P("big.log"), "HIT");
        Assert.Equal(Reference(P("big.log"), "HIT"), output);
    }

    [Fact]
    public async Task 末尾に改行がなくても最終行を出す()
    {
        File.WriteAllText(P("tail.log"), "a HIT 1\nb 2\nc HIT 3");   // 最後に改行なし
        var (_, output) = await Search(P("tail.log"), "HIT");
        Assert.Equal("1\ta HIT 1\n3\tc HIT 3\n", output);
        Assert.Equal(Reference(P("tail.log"), "HIT"), output);
    }

    [Fact]
    public async Task CRLF_と_BOM_でも同じ結果になる()
    {
        File.WriteAllText(P("crlf.log"), "one HIT\r\ntwo\r\nthree HIT\r\n", new UTF8Encoding(true));
        var (_, output) = await Search(P("crlf.log"), "HIT");
        Assert.Equal("1\tone HIT\n3\tthree HIT\n", output);   // \r は落として出す（従来と同じ）
        Assert.Equal(Reference(P("crlf.log"), "HIT"), output);
    }

    [Fact]
    public async Task UTF8以外の文字コードでも探せる()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var sjis = Encoding.GetEncoding("shift_jis");
        File.WriteAllText(P("sjis.log"), "一行目 東京\n二行目 大阪\n三行目 東京タワー\n", sjis);

        var (_, output) = await Search(P("sjis.log"), "東京");
        Assert.Equal("1\t一行目 東京\n3\t三行目 東京タワー\n", output);
        Assert.Equal(Reference(P("sjis.log"), "東京", sjis), output);
    }

    [Fact]
    public async Task 同じ行に2回あっても1件()
    {
        File.WriteAllText(P("dup.log"), "HIT and HIT again\nplain\nHIT\n");
        var (_, output) = await Search(P("dup.log"), "HIT");
        Assert.Equal("1\tHIT and HIT again\n3\tHIT\n", output);
    }

    [Fact]
    public async Task ブロックに収まらない長大行も省略せずに出す()
    {
        // 4MB ブロックに入らない 1 行（一致は先頭 64KB の中）＋そのあとの行
        string huge = "HIT " + new string('y', 6 << 20);
        File.WriteAllText(P("huge.log"), $"first\n{huge}\nlast HIT\n");

        var (_, output) = await Search(P("huge.log"), "HIT");
        string[] lines = output.TrimEnd('\n').Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal($"2\t{huge}", lines[0]);      // 省略せずそのまま
        Assert.Equal("3\tlast HIT", lines[1]);     // 読み飛ばしたあとも行番号が合う
    }

    [Fact]
    public async Task 一致しない長大行を読み飛ばしても行番号が狂わない()
    {
        string huge = new('z', 6 << 20);
        File.WriteAllText(P("skip.log"), $"one\n{huge}\nthree HIT\nfour\nfive HIT\n");

        var (_, output) = await Search(P("skip.log"), "HIT");
        Assert.Equal("3\tthree HIT\n5\tfive HIT\n", output);
    }

    [Fact]
    public async Task 長大行の64KBより後ろの一致は見つけない_従来と同じ()
    {
        // SearchService のバイト高速パスと同じ約束（長大行は先頭 64KB だけで判定する）
        string huge = new string('w', 100 * 1024) + "HIT" + new string('w', 6 << 20);
        File.WriteAllText(P("far.log"), $"{huge}\nafter HIT\n");

        var (_, output) = await Search(P("far.log"), "HIT");
        Assert.Equal("2\tafter HIT\n", output);
    }

    [Fact]
    public async Task 見つからなければ何も出さずに終了コードで伝える()
    {
        File.WriteAllText(P("none.log"), "a\nb\nc\n");
        var (exit, output) = await Search(P("none.log"), "HIT");
        Assert.Equal(UvfExit.NotFound, exit);
        Assert.Equal("", output);
    }

    [Fact]
    public async Task 上限を超えたら打ち切って不完全だと伝える()
    {
        File.WriteAllText(P("many.log"), string.Concat(Enumerable.Range(0, 100).Select(i => $"HIT {i}\n")));

        int saved = SearchService.DefaultMaxHits;
        try
        {
            SearchService.DefaultMaxHits = 10;
            var (exit, output) = await Search(P("many.log"), "HIT");
            Assert.Equal(UvfExit.Error, exit);                                  // 不完全なので成功にしない
            Assert.Equal(10, output.TrimEnd('\n').Split('\n').Length);
            Assert.StartsWith("1\tHIT 0\n", output);
        }
        finally { SearchService.DefaultMaxHits = saved; }
    }

    [Fact]
    public async Task 検索そのものは従来の索引経路と同じ行を選ぶ()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 5_000; i++) sb.Append($"line {i} {(i % 13 == 0 ? "東京" : "大阪")}\n");
        File.WriteAllText(P("cmp.log"), sb.ToString());

        // 従来経路（索引＋SearchService）でヒット行の行頭オフセットを取り、行番号へ直す
        await using var session = DocumentSession.Open(P("cmp.log"));
        await session.BuildIndexAsync();
        await session.StartSearchAsync(new SearchOptions("東京"));
        var expected = session.SearchHits.Order()
            .Select(o => session.Document.OffsetToLineIndex(o) + 1)
            .Distinct().ToArray();

        var (_, output) = await Search(P("cmp.log"), "東京");
        var actual = output.TrimEnd('\n').Split('\n').Select(l => long.Parse(l.Split('\t')[0])).ToArray();
        Assert.Equal(expected, actual);
    }
}
