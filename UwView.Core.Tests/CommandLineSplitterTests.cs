using System.Diagnostics;
using System.Text;
using UwView.Core.Cli;

namespace UwView.Core.Tests;

/// <summary>
/// 「コマンドライン」ダイアログの入力欄を引数に分ける係（v1.8.0 Finder Scope §7.2・完了条件 #4）。
/// </summary>
public class CommandLineSplitterTests
{
    [Theory]
    [InlineData("ERROR", new[] { "ERROR" })]
    [InlineData("  ERROR   -i  ", new[] { "ERROR", "-i" })]
    [InlineData("'k=\"highway[^\"]*\"' -E", new[] { "k=\"highway[^\"]*\"", "-E" })]
    [InlineData("\"a b\" c", new[] { "a b", "c" })]
    [InlineData("a\\ b", new[] { "a b" })]
    [InlineData("'ERROR | uniq code'", new[] { "ERROR | uniq code" })]
    [InlineData("東京 -grep 駅", new[] { "東京", "-grep", "駅" })]
    [InlineData("東京　駅", new[] { "東京　駅" })]                 // 全角空白では区切らない（シェルと同じ）
    [InlineData("\"say \\\"hi\\\"\"", new[] { "say \"hi\"" })]
    [InlineData("\"\\$HOME\\\\x\"", new[] { "$HOME\\x" })]
    [InlineData("\"\\n\"", new[] { "\\n" })]                      // "…" の中で \n は逃がしにならない
    [InlineData("'it'\\''s'", new[] { "it's" })]
    [InlineData("''", new[] { "" })]
    [InlineData("a''b", new[] { "ab" })]
    [InlineData("**/*.log,old/*.gz", new[] { "**/*.log,old/*.gz" })]   // 展開しない
    [InlineData("a\\\nb", new[] { "ab" })]                          // 行の続き
    [InlineData("", new string[0])]
    public void シェルと同じに分ける(string text, string[] expected)
    {
        var r = CommandLineSplitter.Split(text);
        Assert.Null(r.Error);
        Assert.Equal(expected, r.Args);
    }

    [Theory]
    [InlineData("'abc")]
    [InlineData("\"abc")]
    [InlineData("a \"b\\\"")]
    public void 閉じていない引用符は誤り(string text)
    {
        var r = CommandLineSplitter.Split(text);
        Assert.NotNull(r.Error);
        Assert.Contains("閉じていません", r.Error!.Value.Ja);
    }

    [Theory]
    [InlineData("ERROR", "ERROR")]
    [InlineData("a b", "'a b'")]
    [InlineData("it's", "'it'\\''s'")]
    [InlineData("", "''")]
    [InlineData("**/*.log", "'**/*.log'")]
    [InlineData("東京", "東京")]
    [InlineData("k=\"x\"", "'k=\"x\"'")]
    [InlineData("-C", "-C")]
    public void 必要なときだけ囲む(string arg, string expected)
        => Assert.Equal(expected, CommandLineSplitter.Quote(arg));

    private static readonly string[] Pieces =
        ["a", "Z", "0", " ", "  ", "\t", "'", "\"", "\\", "|", "*", "?", "[", "]", "{", "}", "$", "~", "#", "!", "&", ";",
         "<", ">", "(", ")", "`", "=", ",", ":", "-", "_", ".", "/", "東", "京", "　", "😀", "\n", "%", "+", "^"];

    [Fact]
    public void 分けて組み立てて分けると元に戻る_200通り()
    {
        var rnd = new Random(1005);
        for (int round = 0; round < 200; round++)
        {
            var args = new List<string>();
            for (int k = rnd.Next(1, 6); k > 0; k--)
            {
                var sb = new StringBuilder();
                for (int n = rnd.Next(0, 7); n > 0; n--) sb.Append(Pieces[rnd.Next(Pieces.Length)]);
                args.Add(sb.ToString());
            }
            string line = CommandLineSplitter.Join(args);
            var back = CommandLineSplitter.Split(line);
            Assert.Null(back.Error);
            Assert.True(args.SequenceEqual(back.Args), $"{string.Join(" | ", args)} → {line} → {string.Join(" | ", back.Args)}");
            Assert.Equal(line, CommandLineSplitter.Join(back.Args));
        }
    }

    // 本物のシェルで分けた結果と同じか（bash があるときだけ。展開を止めて比べる）
    [Theory]
    [InlineData("ERROR -C 5 timeout -uniq 'code=(\\d+)'")]
    [InlineData("\"a b\" 'c d' e\\ f")]
    [InlineData("'it'\\''s' \"say \\\"hi\\\"\"")]
    [InlineData("東京 '東京　駅' -grep \"k=\\\"name\\\"\"")]
    [InlineData("'ERROR | uniq code' --json")]
    public void 本物のbashと同じに分ける(string text)
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/bash")) return;
        var psi = new ProcessStartInfo("/bin/bash")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("set -f; eval \"set -- $1\"; for a in \"$@\"; do printf '%s\\0' \"$a\"; done");
        psi.ArgumentList.Add("_");
        psi.ArgumentList.Add(text);
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        var bash = output.Split('\0')[..^1];
        Assert.Equal(bash, CommandLineSplitter.Split(text).Args);
    }

    [Theory]
    [InlineData("ERROR", "ERROR")]
    [InlineData("**/*.log", "**/*.log")]
    [InlineData("*.log,old/*.gz", "'*.log,old/*.gz'")]                  // カンマは PowerShell では配列になる
    [InlineData("a b", "'a b'")]
    [InlineData("it's", "'it''s'")]
    [InlineData("k=\"x\"", "'k=\"x\"'")]
    [InlineData("$HOME", "'$HOME'")]
    [InlineData("@x", "'@x'")]
    [InlineData("東京", "東京")]
    [InlineData("\u2019q", "'\u2019\u2019q'")]                              // ’ も ' と同じに読まれる
    [InlineData("", "''")]
    public void PowerShell向けの引用(string arg, string expected)
        => Assert.Equal(expected, CommandLineSplitter.QuotePowerShell(arg));

    // Windows の画面の入力欄は PowerShell と同じに分ける（オーナー裁定 2026-10-07）
    [Theory]
    [InlineData("\\d+ -E", new[] { "\\d+", "-E" })]                          // \ は文字のまま（POSIX なら d+）
    [InlineData("C:\\logs\\*.log", new[] { "C:\\logs\\*.log" })]
    [InlineData("'it''s'", new[] { "it's" })]
    [InlineData("'k=\"x\"' -E", new[] { "k=\"x\"", "-E" })]
    [InlineData("\"say \"\"hi\"\"\"", new[] { "say \"hi\"" })]
    [InlineData("\"a`\"b\"", new[] { "a\"b" })]
    [InlineData("a` b c", new[] { "a b", "c" })]
    [InlineData("\u2018k=\"x\"\u2019", new[] { "k=\"x\"" })]               // ‘ ’ も ' と同じ
    [InlineData("'^(?=.*User: (\\S+))' -format '$1' --csv", new[] { "^(?=.*User: (\\S+))", "-format", "$1", "--csv" })]
    [InlineData("$1,$2", new[] { "$1,$2" })]                                    // 展開・配列にしない
    [InlineData("東京　駅 -i", new[] { "東京　駅", "-i" })]
    [InlineData("''", new[] { "" })]
    [InlineData("", new string[0])]
    public void PowerShellと同じに分ける(string text, string[] expected)
    {
        var r = CommandLineSplitter.Split(text, CommandLineSplitter.ShellStyle.PowerShell);
        Assert.Null(r.Error);
        Assert.Equal(expected, r.Args);
    }

    [Theory]
    [InlineData("'abc")]
    [InlineData("\"abc")]
    [InlineData("'it''s")]
    public void PowerShellでも閉じていない引用符は誤り(string text)
        => Assert.NotNull(CommandLineSplitter.Split(text, CommandLineSplitter.ShellStyle.PowerShell).Error);

    [Fact]
    public void PowerShell向けに組み立てて分けると元に戻る_200通り()
    {
        var rnd = new Random(1007);
        for (int round = 0; round < 200; round++)
        {
            var args = new List<string>();
            for (int k = rnd.Next(1, 6); k > 0; k--)
            {
                var sb = new StringBuilder();
                for (int n = rnd.Next(0, 7); n > 0; n--) sb.Append(Pieces[rnd.Next(Pieces.Length)]);
                args.Add(sb.ToString());
            }
            string line = CommandLineSplitter.JoinPowerShell(args);
            var back = CommandLineSplitter.Split(line, CommandLineSplitter.ShellStyle.PowerShell);
            Assert.Null(back.Error);
            Assert.True(args.SequenceEqual(back.Args), $"{string.Join(" | ", args)} → {line} → {string.Join(" | ", back.Args)}");
        }
    }

    [Fact]
    public void 画面の分け方はOSのターミナルと同じ()
        => Assert.Equal(OperatingSystem.IsWindows() ? CommandLineSplitter.ShellStyle.PowerShell : CommandLineSplitter.ShellStyle.Posix,
                        CommandLineSplitter.Native);
}
