using System.Text;
using System.Text.RegularExpressions;
using UwView.Core.Cli;

namespace UwView.Core.Tests;

/// <summary>
/// 検索の準備（<see cref="PreparedSearch"/>）・方式（<see cref="SearchPlan"/>）・字句の読み取り（<see cref="RegexSyntax"/>）。
/// 外部レビュー 2026-10-04「整理の進め方」2〜5 の作り替えの確かめ。
/// </summary>
public class SearchPlanTests : IDisposable
{
    public void Dispose()
    {
        SearchService.UseLiteralPrefilter = true;
        SearchService.UseShortClues = true;
    }

    private static SearchMethod MethodOf(string pattern, bool regex, bool icase, Encoding? encoding = null)
        => PreparedSearch.Create(new SearchOptions(pattern, regex, icase)).For(encoding ?? Encoding.UTF8).Method;

    // ── 方式の選び方（どの検索もここで決まる） ──────────────

    [Theory]
    [InlineData("東京", false, false, SearchMethod.Bytes)]
    [InlineData("tower", false, true, SearchMethod.AsciiFold)]
    [InlineData("spinlock", false, true, SearchMethod.CandidateLines)]   // k を含む：手がかり spinloc＋正規表現
    [InlineData("東京", false, true, SearchMethod.EveryLine)]            // ASCII の部分が無い
    [InlineData("k=\"highway[^\"]*\"", true, false, SearchMethod.CandidateLines)]
    [InlineData("[0-9]{4}-[0-9]{2}", true, false, SearchMethod.CandidateLines)]   // 短い手がかり
    [InlineData("^ +<", true, false, SearchMethod.EveryLine)]
    [InlineData("highway", true, true, SearchMethod.CandidateLines)]
    [InlineData("foo|bar", true, true, SearchMethod.EveryLine)]
    [InlineData("", false, false, SearchMethod.EveryLine)]
    public void 方式の選び方(string pattern, bool regex, bool icase, SearchMethod expected)
        => Assert.Equal(expected, MethodOf(pattern, regex, icase));

    [Fact]
    public void バイトで探せない文字コードではデコードして当てる()
    {
        EncodingDetector.EnsureCodePagesRegistered();
        var sjis = Encoding.GetEncoding(932);
        Assert.Equal(SearchMethod.EveryLine, MethodOf("東京", false, false, sjis));
        Assert.Equal(SearchMethod.EveryLine, MethodOf("tokyo", false, true, sjis));
        Assert.Equal(SearchMethod.EveryLine, MethodOf("k=\"highway", true, false, sjis));
        // Latin-1 は ASCII がそのまま現れるが、正規表現の手がかりは UTF-8 だけ
        Assert.Equal(SearchMethod.Bytes, MethodOf("abc", false, false, Encoding.Latin1));
        Assert.Equal(SearchMethod.EveryLine, MethodOf("k=\"highway", true, false, Encoding.Latin1));
    }

    [Fact]
    public void 手がかりを切ると正規表現は全行に当てる()
    {
        SearchService.UseLiteralPrefilter = false;
        Assert.Equal(SearchMethod.EveryLine, MethodOf("k=\"highway", true, false));
        Assert.Equal(SearchMethod.EveryLine, MethodOf("highway", true, true));
        Assert.Equal(SearchMethod.Bytes, MethodOf("highway", false, false));     // 素の文字列は変わらない
        SearchService.UseLiteralPrefilter = true;
        SearchService.UseShortClues = false;
        Assert.Equal(SearchMethod.EveryLine, MethodOf("[0-9]{4}-[0-9]{2}", true, false));
    }

    [Fact]
    public void 書き間違いの式は使うときに知らせる()
    {
        var prepared = PreparedSearch.Create(new SearchOptions("(abc", UseRegex: true));
        Assert.Equal(SearchMethod.EveryLine, prepared.For(Encoding.UTF8).Method);
        Assert.Throws<RegexParseException>(() => prepared.Regex);
    }

    [Fact]
    public void 作業役ごとに別の正規表現を貸して_返されたものを使い回す()
    {
        // 1 つの Regex を何本もの作業役で同時に使うと、.NET は呼ぶたびに照合の係を作り直す（uvp の -E -v が 8 倍遅くなった。2026-10-05）
        var prepared = PreparedSearch.Create(new SearchOptions("^ +<", UseRegex: true));
        var a = prepared.RentRegex();
        var b = prepared.RentRegex();
        Assert.Same(prepared.Regex, a);       // 最初の 1 つは共有のもの（コンパイルし直さない）
        Assert.NotSame(a, b);
        Assert.Equal(prepared.Regex.ToString(), a.ToString());
        Assert.Equal(prepared.Regex.Options, a.Options);
        prepared.ReturnRegex(a);
        Assert.Same(a, prepared.RentRegex());
        Assert.Throws<RegexParseException>(() => PreparedSearch.Create(new SearchOptions("(x", UseRegex: true)).RentRegex());
    }

    [Fact]
    public void MayMatch_は当たる行で必ず真()
    {
        var plan = PreparedSearch.Create(new SearchOptions("k=\"name:(en|ja)\"", UseRegex: true)).For(Encoding.UTF8);
        Assert.True(plan.MayMatch("<tag k=\"name:ja\" v=\"x\"/>"u8));
        Assert.False(plan.MayMatch("<tag k=\"highway\"/>"u8));
        var icase = PreparedSearch.Create(new SearchOptions("K=\"NAME", UseRegex: true, IgnoreCase: true)).For(Encoding.UTF8);
        Assert.True(icase.MayMatch("k=\"name"u8));
        Assert.True(icase.MayMatch("K=\"NaMe"u8.ToArray()));   // ケルビン記号の K（手がかりは ="name）
    }

    // ── 字句の読み取り（RegexDialect・RegexLiterals・RegexClues が共用） ──────

    [Theory]
    [InlineData("[abc]x", 0, 4)]
    [InlineData("[]a]x", 0, 3)]                // 先頭の ] は文字
    [InlineData("[^]a]x", 0, 4)]
    [InlineData(@"[\]a]x", 0, 4)]
    [InlineData("[a-z-[aeiou]]x", 0, 12)]      // .NET の減算
    [InlineData("[[:alpha:]]x", 0, 10)]
    [InlineData("[abc", 0, -1)]
    public void 文字クラスの終わり(string pattern, int at, int expected)
        => Assert.Equal(expected, RegexSyntax.ClassEnd(pattern, at));

    [Theory]
    [InlineData("(a)b", 3)]
    [InlineData("(?:a(b)c)d", 9)]
    [InlineData("(?<n>a)b", 7)]
    [InlineData("(?<=a)b", 6)]
    [InlineData("(a[)]b)c", 7)]                // クラスの中の ) は数えない
    [InlineData(@"(a\)b)c", 6)]
    [InlineData("(?:(?x)#)Z(\n)", -1)]          // 中にオプション（コメントの ) を数えられない）
    [InlineData("(?=(?#x)a)b", -1)]             // 先読みの中のコメントも同じ
    [InlineData("(?:(?i:a))b", -1)]
    [InlineData("(ab", -1)]
    public void グループの終わり(string pattern, int expected)
        => Assert.Equal(expected, RegexSyntax.SkipGroup(pattern, 0));

    [Theory]
    [InlineData("(a)", "Capturing", 1)]
    [InlineData("(?:a)", "NonCapturing", 3)]
    [InlineData("(?<n>a)", "Named", 5)]
    [InlineData("(?'n'a)", "Named", 5)]
    [InlineData("(?>a)", "Atomic", 3)]
    [InlineData("(?=a)", "Lookaround", 3)]
    [InlineData("(?<!a)", "Lookaround", 4)]
    [InlineData("(?i)a", "Unsupported", 1)]
    [InlineData("(?#c)a", "Unsupported", 1)]
    [InlineData("(?(a)b|c)", "Unsupported", 1)]
    public void グループの開き方(string pattern, string kind, int body)
    {
        Assert.Equal(kind, RegexSyntax.OpenGroup(pattern, 0, out int at).ToString());
        Assert.Equal(body, at);
    }

    [Theory]
    [InlineData(@"\.", "Literal", '.', 2)]
    [InlineData(@"\t", "Literal", '\t', 2)]
    [InlineData(@"\d", "Other", '\0', 2)]
    [InlineData(@"\x41b", "Other", '\0', 4)]   // 41 をリテラルと読まない
    [InlineData("\\u3042b", "Other", '\0', 6)]
    [InlineData(@"\p{L}b", "Other", '\0', 5)]
    [InlineData(@"\k<n>b", "Other", '\0', 5)]
    [InlineData(@"\12b", "Other", '\0', 3)]
    [InlineData(@"\😀", "Other", '\0', 2)]     // 絵文字の片側だけをリテラルにしない
    [InlineData(@"\", "Broken", '\0', 0)]
    public void 逃がしの読み方(string pattern, string kind, char literal, int next)
    {
        int i = 0;
        Assert.Equal(kind, RegexSyntax.ReadEscape(pattern, ref i, out char got).ToString());
        if (kind == "Broken") return;
        Assert.Equal(literal, got);
        Assert.Equal(next, i);
    }

    [Theory]
    [InlineData("a", 1, false)]
    [InlineData("?", 0, true)]
    [InlineData("*?", 0, true)]
    [InlineData("+", 1, true)]
    [InlineData("{3}", 3, true)]
    [InlineData("{0,2}", 0, true)]
    [InlineData("{2,}+", 2, true)]
    [InlineData("{x}", -1, false)]
    public void 量指定子の読み方(string text, int min, bool quantified)
    {
        int i = 0;
        var got = RegexSyntax.ReadQuantifier(text, ref i);
        Assert.Equal(min, got.Min);
        if (min >= 0) Assert.Equal(quantified, got.Quantified);
    }

    [Theory]
    [InlineData("abc", true)]
    [InlineData("a😀", true)]
    [InlineData("a\uD83D", false)]
    [InlineData("\uDE00a", false)]
    [InlineData("a�", false)]
    public void バイト列の手がかりに使えるか(string text, bool expected)
        => Assert.Equal(expected, RegexSyntax.IsByteSafe(text));

    // ── 先に絞る処理の有無で答えが変わらないこと（ランダム） ─────────────
    //
    // レビューの指摘（置換文字・入れ子のオプション・絵文字の量指定子・文字クラスの中の (?P<）は、
    // どれも「手がかりの取り出し」と「.NET の正規表現の意味」がずれたもの。今までのランダムテストは式の部品に
    // それらを含まなかったので、部品とデータの両方に混ぜて、3 つの検索（uvf の通常・-v、画面の検索）で突き合わせる。

    private static readonly string[] Atoms =
    [
        "a", "b", "k", "K", "-", ".", @"\.", @"\-", "ぁ", "東", "京", "😀", "�", " ", "x", "<",
        "[ab]", "[ぁ-ん]", "[東京]", "[ｦ-￿]", "[(?P<]", "[-_]", "[0-9]", "[^a]", "[a-z-[aeiou]]",
        "(?P<n>ab)", "(?<m>東)", "(a|b)", "(?:x-)", "(?=a)", "(?<!b)", "(?>ab)",
        "(?i)", "(?:(?i)a)", "(?#c)", "(?:(?x)#)", @"\d", @"\w", @"\t", @"\x41", "\\u6771", "^", "$",
    ];
    private static readonly string[] Quantifiers = ["", "", "", "?", "*", "+", "{2}", "{0,2}", "{1,3}", "??"];

    private static readonly byte[][] DataPieces =
    [
        "a"u8.ToArray(), "b"u8.ToArray(), "k"u8.ToArray(), "K"u8.ToArray(), "-"u8.ToArray(), "."u8.ToArray(),
        "ぁ"u8.ToArray(), "東"u8.ToArray(), "京"u8.ToArray(), "😀"u8.ToArray(), "x"u8.ToArray(), " "u8.ToArray(),
        "(?P<"u8.ToArray(), "A"u8.ToArray(), "e"u8.ToArray(), "1"u8.ToArray(), "\t"u8.ToArray(),
        "K"u8.ToArray(),          // ケルビン記号（-i の k）
        "ｦ"u8.ToArray(), "�"u8.ToArray(),
        [0xFF], [0xC3], [0xED, 0xA0, 0x80], [0xF0, 0x9F],   // 壊れたバイト（デコードで U+FFFD）
    ];

    private static byte[] RandomData(Random rnd)
    {
        var data = new List<byte>();
        for (int line = rnd.Next(1, 12); line > 0; line--)
        {
            for (int k = rnd.Next(0, 10); k > 0; k--) data.AddRange(DataPieces[rnd.Next(DataPieces.Length)]);
            if (rnd.Next(8) == 0) data.Add((byte)'\r');
            data.Add((byte)'\n');
        }
        if (rnd.Next(3) == 0) data.AddRange(DataPieces[rnd.Next(DataPieces.Length)]);   // 改行なしの最終行
        return data.ToArray();
    }

    private static async Task<(long Raw, long Inverted, long Search)> CountAsync(byte[] data, SearchOptions options)
    {
        var raw = await RawGrep.RunAsync(new MemoryByteSource(data), 0, Encoding.UTF8, options, false, (_, _, _) => { });
        var inv = await RawGrep.RunAsync(new MemoryByteSource(data), 0, Encoding.UTF8, options, true, (_, _, _) => { });
        var search = await SearchService.SearchAsync(new MemoryByteSource(data), 0, Encoding.UTF8, options);
        return (raw.Hits, inv.Hits, search.TotalHits);
    }

    [Fact]
    public async Task ランダムな式とデータで_先に絞る処理の有無で結果が同じ()
    {
        var rnd = new Random(20261004);
        int patterns = 0, narrowed = 0, hits = 0;
        for (int round = 0; round < 1500; round++)
        {
            var sb = new StringBuilder();
            for (int k = rnd.Next(1, 5); k > 0; k--) sb.Append(Atoms[rnd.Next(Atoms.Length)]).Append(Quantifiers[rnd.Next(Quantifiers.Length)]);
            string pattern = sb.ToString();
            bool icase = rnd.Next(3) == 0;
            var options = new SearchOptions(pattern, UseRegex: true, IgnoreCase: icase, MaxHits: 0);
            try { _ = SearchService.BuildRegex(options); }
            catch (ArgumentException) { continue; }
            patterns++;
            if (PreparedSearch.Create(options).For(Encoding.UTF8).Method == SearchMethod.CandidateLines) narrowed++;

            byte[] data = RandomData(rnd);
            SearchService.UseLiteralPrefilter = false;
            var off = await CountAsync(data, options);
            SearchService.UseLiteralPrefilter = true;
            var on = await CountAsync(data, options);
            Assert.True(off == on, $"式 {Escape(pattern)}（-i {icase}）・データ {Convert.ToHexString(data)}: OFF {off} / ON {on}");
            hits += (int)off.Raw;
        }
        Assert.True(patterns > 1000 && narrowed > 300 && hits > 1000,
            $"試した数が少なすぎる: 式 {patterns}・絞れた式 {narrowed}・当たり {hits}");
    }

    // レビューの4件の形（とその近く）を部品にして、ふつうの部品と混ぜる。データも、その部品が当たりそうなものに寄せる。
    // 直した箇所を1つずつ元に戻すと、この試験がそれぞれ落ちることを確かめてある（2026-10-04）
    private static readonly string[] TrickyAtoms =
    [
        "�", "[�]", "[ｦ-￿]", @"\�", "😀", "a😀?", "x😀*", "😀+", @"\😀", "[(?P<]", "[^(?P<]", "(?P<n>a)",
        "(?:(?x)#)Z(\n)", "(?:(?x)#)a(\n)", "(?=(?#)a))", "(?:(?#a)b)", "(?:(?i)z)", "(?:(?i:a)b)",
        "a", "b", "x", "Z", "P", "東", "-", "[ab]", "(a|b)", ".",
    ];

    private static readonly byte[][] TrickyData =
    [
        [0xFF], [0xC3], [0xED, 0xA0, 0x80], [0xF0, 0x9F], "�"u8.ToArray(), "ｦ"u8.ToArray(), "😀"u8.ToArray(),
        "a"u8.ToArray(), "b"u8.ToArray(), "x"u8.ToArray(), "z"u8.ToArray(), "Z"u8.ToArray(), "P"u8.ToArray(),
        "("u8.ToArray(), "?"u8.ToArray(), "<"u8.ToArray(), "東"u8.ToArray(), "-"u8.ToArray(),
    ];

    [Fact]
    public async Task 指摘の形を混ぜたランダムな式とデータで_先に絞る処理の有無で結果が同じ()
    {
        var rnd = new Random(1004);
        int patterns = 0, narrowed = 0, hits = 0;
        for (int round = 0; round < 6000; round++)
        {
            var sb = new StringBuilder();
            for (int k = rnd.Next(1, 4); k > 0; k--)
                sb.Append(TrickyAtoms[rnd.Next(TrickyAtoms.Length)]).Append(rnd.Next(4) == 0 ? Quantifiers[rnd.Next(Quantifiers.Length)] : "");
            string pattern = sb.ToString();
            var options = new SearchOptions(pattern, UseRegex: true, IgnoreCase: rnd.Next(5) == 0, MaxHits: 0);
            try { _ = SearchService.BuildRegex(options); }
            catch (ArgumentException) { continue; }
            patterns++;
            if (PreparedSearch.Create(options).For(Encoding.UTF8).Method == SearchMethod.CandidateLines) narrowed++;

            var data = new List<byte>();
            for (int line = rnd.Next(1, 6); line > 0; line--)
            {
                for (int k = rnd.Next(0, 5); k > 0; k--) data.AddRange(TrickyData[rnd.Next(TrickyData.Length)]);
                data.Add((byte)'\n');
            }
            byte[] bytes = data.ToArray();
            SearchService.UseLiteralPrefilter = false;
            var off = await CountAsync(bytes, options);
            SearchService.UseLiteralPrefilter = true;
            var on = await CountAsync(bytes, options);
            Assert.True(off == on, $"式 {Escape(pattern)}（-i {options.IgnoreCase}）・データ {Convert.ToHexString(bytes)}: OFF {off} / ON {on}");
            hits += (int)off.Raw;

            // 読み替え（RegexDialect）は OFF と ON の両方に効くので、元の式のまま .NET が読めるなら、元の式とも突き合わせる
            //（.NET は (?P< をグループとして読めないので、読めた式は (?P< をグループとして使っていない）
            Regex original;
            try { original = new Regex(pattern, RegexOptions.CultureInvariant | (options.IgnoreCase ? RegexOptions.IgnoreCase : 0)); }
            catch (ArgumentException) { continue; }
            var lines = Encoding.UTF8.GetString(bytes).Split('\n')[..^1];
            Assert.True(lines.Count(original.IsMatch) == off.Raw, $"式 {Escape(pattern)}: 元の式 {lines.Count(original.IsMatch)} 行 / 検索 {off.Raw} 行");
        }
        Assert.True(patterns > 4000 && narrowed > 1000 && hits > 1500,
            $"試した数が少なすぎる: 式 {patterns}・絞れた式 {narrowed}・当たり {hits}");
    }

    private static string Escape(string s) => s.Replace("\n", "\\n");
}
