using System.IO.Compression;
using UwView.Core.Cli;

namespace UwView.Core.Tests;

/// <summary>
/// NativeAOT の CLI が、正規表現の検索を JIT の本体に任せるかの判断（<see cref="CompiledRegexRoute"/>）。
/// 任せるのは「必須の文字列で絞れない正規表現」を「大きな入力」に当てるときだけ。
/// </summary>
public class CompiledRegexRouteTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "uv-route-" + Guid.NewGuid().ToString("N"));

    public CompiledRegexRouteTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("^ +<", true)]                 // 必須の文字列が1文字（ほぼ全行に当たる）
    [InlineData("[0-9]{4}-[0-9]{2}", true)]
    [InlineData(".*", true)]
    [InlineData("k=\"highway[^\"]*\"", false)] // k="highway で先に絞れる
    [InlineData("^ *<tag k=\"name\"", false)]
    [InlineData("k=\"(name|addr:city)\"", false)]
    public void 必須の文字列で絞れない正規表現だけが全行に当たる(string pattern, bool expected)
        => Assert.Equal(expected, CompiledRegexRoute.ScansEveryLine(pattern));

    [Fact]
    public void 大きさの目安は平文はそのまま圧縮は8倍で複数ファイルは足す()
    {
        string plain = Path.Combine(_dir, "a.log");
        File.WriteAllBytes(plain, new byte[1000]);
        string gz = Path.Combine(_dir, "b.log.gz");
        using (var fs = File.Create(gz))
        using (var z = new GZipStream(fs, CompressionLevel.Fastest))
            z.Write(System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("line 東京\n", 10_000))));
        long gzSize = new FileInfo(gz).Length;

        Assert.Equal(1000, CompiledRegexRoute.Estimate(plain));
        Assert.Equal(gzSize * 8, CompiledRegexRoute.Estimate(gz));
        Assert.Equal(0, CompiledRegexRoute.Estimate(Path.Combine(_dir, "none.log")));
        Assert.Equal(1000 + gzSize * 8, CompiledRegexRoute.TextBytes($"{plain},{gz}"));
        Assert.Equal(123, CompiledRegexRoute.TextBytes(plain, _ => 123));
    }

    [Fact]
    public void JITで動いているときは任せない()
    {
        // テストは JIT で動く（正規表現をコンパイルできる）ので、どんな指定でも自分で探す
        Assert.False(CompiledRegexRoute.Interpreted);
        Assert.False(CompiledRegexRoute.ForUvf(["big.log", "^ +<", "-E", "-v"]));
    }
}
