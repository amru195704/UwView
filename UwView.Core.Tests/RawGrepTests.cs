using System.Text;
using System.Text.RegularExpressions;
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

    /// <summary>uvf の検索を実行して stdout を返す（options は -i / -E / -v）。</summary>
    private static async Task<(int Exit, string Out)> Search(string path, string pattern, params string[] options)
    {
        using var stdout = new MemoryStream();
        var env = new UvfEnvironment
        {
            StdOut = stdout, StdErr = new StringWriter(), Japanese = false, LaunchGui = (_, _) => true,
        };
        int code = await UvfCli.RunAsync([path, pattern, .. options], env);
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
    public async Task 長大行の64KBより後ろの一致も見つける_索引の有無で変わらない()
    {
        // 以前は先頭 64KB だけを見ていたため、索引あり（SearchService・1MB バッファ）と
        // 索引なし（RawGrep・4MB バッファ）で同じファイルの答えが変わっていた
        //（ソースレビュー 2026-09-19 の指摘7）。いまはどちらも読んだ範囲すべてを見る。
        string huge = new string('w', 100 * 1024) + "HIT" + new string('w', 6 << 20);
        File.WriteAllText(P("far.log"), $"{huge}\nafter HIT\n");

        var (_, output) = await Search(P("far.log"), "HIT");
        string[] lineNumbers = output.TrimEnd('\n').Split('\n').Select(l => l.Split('\t')[0]).ToArray();
        Assert.Equal(["1", "2"], lineNumbers);
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

    // ── -i / -E / -v（2026-09-17 オーナー指示で uvf にも入れた）────────────

    /// <summary>
    /// 参照実装（-i / -E / -v つき）。
    ///
    /// -i は <c>OrdinalIgnoreCase</c> ではなく<b>正規表現の IgnoreCase</b> で書く。この2つは同じではなく、
    /// 正規表現のほうは U+212A（ケルビン記号）を 'k' と同じに扱う（ripgrep の -i も畳む）。
    /// uvf は画面の検索と同じ規則＝正規表現側に合わせているので、参照もそちらで書く。
    /// </summary>
    private static string Reference(string path, string pattern, bool icase, bool regex, bool invert)
    {
        var options = regex || icase
            ? new Regex(regex ? pattern : Regex.Escape(pattern),
                        RegexOptions.CultureInvariant | (icase ? RegexOptions.IgnoreCase : RegexOptions.None))
            : null;
        var sb = new StringBuilder();
        long n = 0;
        foreach (string text in File.ReadLines(path))
        {
            n++;
            bool hit = options is not null ? options.IsMatch(text) : text.Contains(pattern, StringComparison.Ordinal);
            if (hit != invert) sb.Append(n).Append('\t').Append(text).Append('\n');
        }
        return sb.ToString();
    }

    private string Mixed()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 3_000; i++)
            sb.Append($"{i:D5} {(i % 3 == 0 ? "ERROR" : i % 3 == 1 ? "error" : "info")} dev{i % 4} code={i % 7}\n");
        File.WriteAllText(P("mixed.log"), sb.ToString());
        return P("mixed.log");
    }

    [Fact]
    public async Task 大小無視で探す()
    {
        string log = Mixed();
        var (exit, output) = await Search(log, "error", "-i");
        Assert.Equal(UvfExit.Found, exit);
        Assert.Equal(Reference(log, "error", icase: true, regex: false, invert: false), output);

        var (_, sensitive) = await Search(log, "error");     // -i なしと違うこと（取りこぼしの確認）
        Assert.NotEqual(sensitive, output);
    }

    [Fact]
    public async Task 正規表現で探す()
    {
        string log = Mixed();
        foreach (string pattern in new[] { @"code=[13]$", @"^0000\d ERROR", @"dev[02] code=\d" })
        {
            var (_, output) = await Search(log, pattern, "-E");
            Assert.Equal(Reference(log, pattern, icase: false, regex: true, invert: false), output);
        }
    }

    [Fact]
    public async Task 正規表現と大小無視を一緒に使う()
    {
        string log = Mixed();
        var (_, output) = await Search(log, @"^\d+ error", "-E", "-i");
        Assert.Equal(Reference(log, @"^\d+ error", icase: true, regex: true, invert: false), output);
        Assert.NotEqual("", output);
    }

    [Fact]
    public async Task 当てはまらない行を出す()
    {
        string log = Mixed();
        var (_, output) = await Search(log, "ERROR", "-v");
        Assert.Equal(Reference(log, "ERROR", icase: false, regex: false, invert: true), output);

        var (_, hit) = await Search(log, "ERROR");
        // -v と素の検索を足すと全行になる
        Assert.Equal(3_000, output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length
                          + hit.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task 正規表現と_v_を一緒に使う()
    {
        string log = Mixed();
        var (_, output) = await Search(log, @"code=[0-3]$", "-E", "-v");
        Assert.Equal(Reference(log, @"code=[0-3]$", icase: false, regex: true, invert: true), output);
    }

    [Fact]
    public async Task 全行が当てはまらないときは見つからない扱い()
    {
        File.WriteAllText(P("all.log"), "HIT a\nHIT b\n");
        var (exit, output) = await Search(P("all.log"), "HIT", "-v");
        Assert.Equal(UvfExit.NotFound, exit);
        Assert.Equal("", output);
    }

    [Fact]
    public async Task ブロックをまたいでも_v_と正規表現の行番号が合う()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 60_000; i++)
            sb.Append($"{i:D8} {new string('x', 70)} {(i % 997 == 0 ? "HIT" : "---")} {i}\n");
        File.WriteAllText(P("big2.log"), sb.ToString());
        Assert.True(new FileInfo(P("big2.log")).Length > 5 << 20);

        var (_, inverted) = await Search(P("big2.log"), "HIT", "-v");
        Assert.Equal(Reference(P("big2.log"), "HIT", icase: false, regex: false, invert: true), inverted);

        var (_, byRegex) = await Search(P("big2.log"), @"HIT \d+$", "-E");
        Assert.Equal(Reference(P("big2.log"), @"HIT \d+$", icase: false, regex: true, invert: false), byRegex);
    }

    [Fact]
    public async Task 長大行も_v_と正規表現で同じ扱いになる()
    {
        string huge = new('z', 6 << 20);
        File.WriteAllText(P("skip2.log"), $"one\n{huge}\nthree HIT\nfour\n");

        var (_, inverted) = await Search(P("skip2.log"), "HIT", "-v");
        Assert.Equal($"1\tone\n2\t{huge}\n4\tfour\n", inverted);

        var (_, byRegex) = await Search(P("skip2.log"), "^z+$", "-E");
        Assert.Equal($"2\t{huge}\n", byRegex);
    }

    [Theory]
    [InlineData("-i")]
    [InlineData("-E")]
    [InlineData("-v")]
    public void openと並べても受け付ける(string option)
    {
        // オーナー指示 2026-09-18 で併用可にした（それまではエラーだった）
        var (inv, ja, _) = UvfCli.Parse([P("x.log"), "HIT", option, "-open"]);
        Assert.True(inv is not null, ja);
        Assert.Equal(UvfMode.SearchInGui, inv!.Mode);
        Assert.Equal(option, "-" + UvfCli.OptionLetters(inv));
    }

    [Fact]
    public void オプションは書く場所を選ばない()
    {
        var (inv, _, _) = UvfCli.Parse(["-i", "a.log", "-E", "HIT", "-v"]);
        Assert.Equal(new UvfInvocation(UvfMode.Search, "a.log", "HIT", true, true, true), inv);
    }

    // ── -i の前チェックとバイト折り畳み（オーナー指摘 2026-09-17）────────

    [Fact]
    public async Task 大小無視はケルビン記号も従来どおり拾う()
    {
        // .NET の IgnoreCase は U+212A（ケルビン記号）を 'k' と同じに扱う。
        // バイトのまま畳む速い道は k を含む語では使わないので、ここが崩れないことを見る
        File.WriteAllText(P("kelvin.log"), "1 kilo\n2 KILO\n3 Kilo\n4 other\n");

        var (_, output) = await Search(P("kelvin.log"), "kilo", "-i");
        Assert.Equal("1\t1 kilo\n2\t2 KILO\n3\t3 Kilo\n", output);
    }

    [Fact]
    public async Task 大小無視の前チェックは当たる行を落とさない()
    {
        // k を含む語は前チェック（k 以外の1文字）で絞ってから正規表現に当てる。
        // 絞り込みで取りこぼしが出ないことを、記号つき・大小混在で確かめる
        File.WriteAllText(P("anchor.log"), string.Concat(
            Enumerable.Range(0, 500).Select(i => (i % 5) switch
            {
                0 => $"{i} key=ok\n",
                1 => $"{i} KEY=OK\n",
                2 => $"{i} Key=ok\n",     // ケルビン記号の k
                3 => $"{i} Key=Ok\n",
                _ => $"{i} nothing here\n",
            })));

        var (_, output) = await Search(P("anchor.log"), "key=ok", "-i");
        Assert.Equal(Reference(P("anchor.log"), "key=ok", icase: true, regex: false, invert: false), output);
        // 5行のうち4行（key=ok / KEY=OK / ケルビン記号の k / Key=Ok）が当たる
        Assert.Equal(400, output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Theory]
    [InlineData("highway")]        // 英字だけ（バイト折り畳みの道）
    [InlineData("K=\"NAME\"")]     // k を含む（前チェック＋正規表現の道）
    [InlineData("東京")]            // 非 ASCII（従来の道）
    [InlineData("a")]              // 1文字
    public async Task 大小無視はどの語でも参照実装と一致する(string pattern)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 4_000; i++)
            sb.Append((i % 4) switch
            {
                0 => $"{i} HIGHWAY K=\"NAME\" 東京 A\n",
                1 => $"{i} highway k=\"name\" 大阪 a\n",
                2 => $"{i} HighWay K=\"Name\" 東京タワー A\n",
                _ => $"{i} nothing\n",
            });
        File.WriteAllText(P("icase.log"), sb.ToString());

        var (_, output) = await Search(P("icase.log"), pattern, "-i");
        Assert.Equal(Reference(P("icase.log"), pattern, icase: true, regex: false, invert: false), output);
        Assert.NotEqual("", output);
    }

    [Fact]
    public async Task 大小無視の_v_も参照実装と一致する()
    {
        File.WriteAllText(P("iv.log"), "1 HIGHWAY\n2 highway\n3 other\n");
        var (_, output) = await Search(P("iv.log"), "highway", "-i", "-v");
        Assert.Equal("3\t3 other\n", output);
        Assert.Equal(Reference(P("iv.log"), "highway", icase: true, regex: false, invert: true), output);
    }

    // ── -open の受け渡し（オーナー指示 2026-09-18）────────────────

    [Fact]
    public async Task openでは検索してから結果を画面へ渡す()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 2_000; i++) sb.Append($"{i} {(i % 7 == 0 ? "ERROR" : "info")} x\n");
        File.WriteAllText(P("open.log"), sb.ToString());

        string? handoff = null;
        var env = new UvfEnvironment
        {
            StdOut = new MemoryStream(), StdErr = new StringWriter(), Japanese = false,
            LaunchGui = (_, _) => true,
        };
        // LaunchGui が呼ばれた時点で受け渡し先が入っていること（順序が逆だと画面へ渡らない）
        var withCapture = new UvfEnvironment
        {
            StdOut = env.StdOut, StdErr = env.StdErr, Japanese = false,
            LaunchGui = (_, _) => { handoff = null; return true; },
        };
        Assert.Equal(UvfExit.Found, await UvfCli.RunAsync([P("open.log"), "ERROR", "-open"], withCapture));
        handoff = withCapture.HandoffPath;

        Assert.NotNull(handoff);
        var taken = CliHandoff.TakeFrom(handoff!);
        Assert.NotNull(taken);
        Assert.False(File.Exists(handoff!));                       // 一度きり（読んだら消える）
        Assert.Equal("ERROR", taken!.Pattern);
        Assert.True(taken.Matches(new FileInfo(P("open.log")).Length));
        Assert.False(taken.Truncated);

        // 渡したのは行頭の位置。stdout に出る行番号と同じ行を指していること
        var (_, output) = await Search(P("open.log"), "ERROR");
        Assert.Equal(output.TrimEnd('\n').Split('\n').Length, taken.Hits.Length);

        var text = File.ReadAllBytes(P("open.log"));
        foreach (long at in taken.Hits)
        {
            Assert.True(at == 0 || text[at - 1] == (byte)'\n');    // 行頭を指している
            Assert.Contains("ERROR", Encoding.UTF8.GetString(text, (int)at, 20));
        }
    }

    [Fact]
    public async Task 中身が変わっていたら渡された結果は使わない()
    {
        File.WriteAllText(P("chg.log"), "1 ERROR\n2 info\n");
        var env = new UvfEnvironment
        {
            StdOut = new MemoryStream(), StdErr = new StringWriter(), Japanese = false,
            LaunchGui = (_, _) => true,
        };
        await UvfCli.RunAsync([P("chg.log"), "ERROR", "-open"], env);

        File.AppendAllText(P("chg.log"), "3 ERROR\n");             // 渡したあとに増えた
        var taken = CliHandoff.TakeFrom(env.HandoffPath!);
        Assert.NotNull(taken);
        Assert.False(taken!.Matches(new FileInfo(P("chg.log")).Length));
    }

    [Fact]
    public async Task 壊れた受け渡しは黙って捨てる()
    {
        File.WriteAllText(P("broken.uvfh"), "これは受け渡しファイルではない");
        Assert.Null(CliHandoff.TakeFrom(P("broken.uvfh")));
        Assert.False(File.Exists(P("broken.uvfh")));               // 消えている
        Assert.Null(CliHandoff.TakeFrom(P("no-such-file.uvfh")));
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData(new[] { "-i" }, "i")]
    [InlineData(new[] { "-E" }, "E")]
    [InlineData(new[] { "-v" }, "v")]
    [InlineData(new[] { "-E", "-i" }, "iE")]
    public async Task openは_i_E_v_と併用できる(string[] opts, string letters)
    {
        // オーナー指示 2026-09-18: -i/-E/-v を付けた検索でも -open が効くこと。
        // CLI 側で解決して渡すので、画面は検索し直さない
        File.WriteAllText(P("o2.log"), "1 ERROR a\n2 error b\n3 plain c\n");
        var env = new UvfEnvironment
        {
            StdOut = new MemoryStream(), StdErr = new StringWriter(), Japanese = false,
            LaunchGui = (_, _) => true,
        };
        Assert.Equal(UvfExit.Found, await UvfCli.RunAsync([P("o2.log"), "ERROR", .. opts, "-open"], env));

        Assert.Equal(letters, env.SearchOptionLetters);          // 画面へ渡す種類
        Assert.NotNull(env.HandoffPath);
        var taken = CliHandoff.TakeFrom(env.HandoffPath!);
        Assert.NotNull(taken);

        // 渡した件数は、同じ条件で stdout に出したときと同じ
        var (_, output) = await Search(P("o2.log"), "ERROR", opts);
        Assert.Equal(output.TrimEnd('\n').Split('\n').Length, taken!.Hits.Length);
        Assert.Equal(opts.Contains("-i"), taken.IgnoreCase);
        Assert.Equal(opts.Contains("-E"), taken.Regex);
        Assert.Equal(opts.Contains("-v"), taken.Invert);
    }

    // ── 検索と同時に索引も作る（オーナー指示 2026-09-18）────────────

    [Theory]
    [InlineData(20_000)]        // 目印が何度も出る
    [InlineData(300)]           // 目印が1つも出ない小さいファイル
    public async Task 検索のついでに作った索引は画面が作るものと同じ(int lines)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < lines; i++) sb.Append($"{i:D6} {(i % 9 == 0 ? "ERROR" : "info")} x\n");
        File.WriteAllText(P("idx.log"), sb.ToString());

        var env = new UvfEnvironment
        {
            StdOut = new MemoryStream(), StdErr = new StringWriter(), Japanese = false,
            LaunchGui = (_, _) => true,
        };
        await UvfCli.RunAsync([P("idx.log"), "ERROR", "-open"], env);
        var handoff = CliHandoff.TakeFrom(env.HandoffPath!);
        Assert.NotNull(handoff);

        await using var session = DocumentSession.Open(P("idx.log"));
        var mine = handoff!.BuildIndex(session.Document.BomLength, session.Newline);
        Assert.NotNull(mine);

        // 画面が自分で作った索引と突き合わせる
        await session.BuildIndexAsync();
        var theirs = session.Document.Index!;
        Assert.Equal(theirs.TotalLines, mine!.TotalLines);
        Assert.Equal(theirs.CheckpointCount, mine.CheckpointCount);
        for (int k = 0; k < theirs.CheckpointCount; k++)
            Assert.Equal(theirs.GetCheckpoint(k), mine.GetCheckpoint(k));

        // 目印の間隔・BOM・改行スタイルもそろっている
        Assert.Equal(theirs.BlockLines, mine.BlockLines);
        Assert.Equal(theirs.BomLength, mine.BomLength);
        Assert.Equal(theirs.Newline, mine.Newline);
        Assert.Equal(theirs.FileLength, mine.FileLength);
    }

    [Fact]
    public async Task 末尾に改行が無いファイルでも索引が合う()
    {
        File.WriteAllText(P("idx2.log"), "1 ERROR a\n2 info b\n3 ERROR c");   // 最後に改行なし
        var env = new UvfEnvironment
        {
            StdOut = new MemoryStream(), StdErr = new StringWriter(), Japanese = false,
            LaunchGui = (_, _) => true,
        };
        await UvfCli.RunAsync([P("idx2.log"), "ERROR", "-open"], env);
        var handoff = CliHandoff.TakeFrom(env.HandoffPath!)!;

        await using var session = DocumentSession.Open(P("idx2.log"));
        var mine = handoff.BuildIndex(session.Document.BomLength, session.Newline)!;
        await session.BuildIndexAsync();
        Assert.Equal(session.Document.Index!.TotalLines, mine.TotalLines);
        Assert.Equal(3, mine.TotalLines);
    }

    // ── 画面の検索が索引も同時に作る（オーナー指示 2026-09-18 A・B-1）────

    [Fact]
    public async Task 索引が無いまま検索すると検索と同時に索引もできる()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 20_000; i++) sb.Append($"{i:D6} {(i % 11 == 0 ? "ERROR" : "info")} x\n");
        File.WriteAllText(P("both.log"), sb.ToString());

        await using var session = DocumentSession.Open(P("both.log"));
        Assert.False(session.IsIndexed);                       // 索引はまだ

        bool indexed = false;
        session.IndexCompleted += (_, _) => indexed = true;
        await session.StartSearchAsync(new SearchOptions("ERROR"));

        Assert.True(session.IsIndexed, "検索だけで終わり、索引ができていない");
        Assert.True(indexed, "行モードへの昇格が知らされていない");

        // 索引を別に作ったときと同じ結果になる
        await using var plain = DocumentSession.Open(P("both.log"));
        await plain.BuildIndexAsync();
        Assert.Equal(plain.Document.TotalLines, session.Document.TotalLines);
        for (long line = 0; line < 500; line += 43)
            Assert.Equal(plain.Document.LineStartOffset(line), session.Document.LineStartOffset(line));

        // ヒットも従来の検索と同じ
        await plain.StartSearchAsync(new SearchOptions("ERROR"));
        Assert.Equal(plain.SearchHits, session.SearchHits);
        Assert.Equal(20_000 / 11 + 1, session.SearchHits.Count);
    }

    [Fact]
    public async Task 上限で打ち切ったときは索引を採らない()
    {
        File.WriteAllText(P("cut.log"), string.Concat(Enumerable.Range(0, 200).Select(i => $"ERROR {i}\n")));

        int saved = SearchService.DefaultMaxHits;
        try
        {
            SearchService.DefaultMaxHits = 10;
            await using var session = DocumentSession.Open(P("cut.log"));
            await session.StartSearchAsync(new SearchOptions("ERROR"));

            Assert.True(session.SearchTruncated);
            Assert.False(session.IsIndexed, "最後まで読んでいないのに索引を採っている");
            Assert.Equal(10, session.SearchHits.Count);
        }
        finally { SearchService.DefaultMaxHits = saved; }
    }

    [Fact]
    public async Task 索引ができたあとの検索は索引を作り直さない()
    {
        File.WriteAllText(P("again.log"), "1 ERROR a\n2 info b\n3 ERROR c\n");
        await using var session = DocumentSession.Open(P("again.log"));
        await session.BuildIndexAsync();
        Assert.True(session.IsIndexed);

        int completed = 0;
        session.IndexCompleted += (_, _) => completed++;
        await session.StartSearchAsync(new SearchOptions("ERROR"));

        Assert.Equal(0, completed);                            // 作り直していない
        Assert.Equal(2, session.SearchHits.Count);
    }

    [Fact]
    public async Task 索引と同時の検索は呼び出し元のスレッドを塞がない()
    {
        // オーナー報告 2026-09-19「索引作成中の検索でハングした」。
        // 統合パスが呼び出し元（画面では UI スレッド）でそのまま回っていたのが原因。
        // ヒットの通知が<b>別スレッド</b>から来ることで、背景へ逃がせていることを確かめる
        var sb = new StringBuilder();
        for (int i = 0; i < 50_000; i++) sb.Append($"{i:D6} {(i % 10 == 0 ? "ERROR" : "info")} x\n");
        File.WriteAllText(P("thread.log"), sb.ToString());

        await using var session = DocumentSession.Open(P("thread.log"));
        Assert.False(session.IsIndexed);                     // 統合パスへ入る条件

        // 最初にヒットが届いたときのスレッドだけを見る。
        // 完了通知（finally）は待っている側のスレッドで上がるので、最後の1回で上書きすると
        // 走査スレッドの判定にならない（レビュー 2026-09-19 でこのテストの不安定さを指摘された）
        int caller = Environment.CurrentManagedThreadId;
        int scanThread = 0;
        session.SearchUpdated += (_, _) =>
        {
            if (scanThread == 0 && session.SearchHits.Count > 0)
                scanThread = Environment.CurrentManagedThreadId;
        };

        await session.StartSearchAsync(new SearchOptions("ERROR"));

        Assert.NotEqual(0, scanThread);                      // ヒットの通知が来ている
        Assert.NotEqual(caller, scanThread);                 // 呼び出し元で回していない
        Assert.True(session.IsIndexed);
        Assert.Equal(5_000, session.SearchHits.Count);
    }
}
