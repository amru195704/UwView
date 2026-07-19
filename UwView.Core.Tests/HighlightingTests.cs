using UwView.Core;
using Xunit;

namespace UwView.Core.Tests;

/// <summary>色分けハイライタのマッチャ（CompiledHighlighter）テスト。実装指示書 §8-1/2。</summary>
public class HighlightingTests
{
    private static HlSet Set(params HlRule[] rules) => new("id", "test", new List<HlRule>(rules));

    [Fact]
    public void ParseColor_HandlesFormats()
    {
        Assert.Equal(0xFFFF0000u, CompiledHighlighter.ParseColor("#FF0000"));
        Assert.Equal(0xFFFF0000u, CompiledHighlighter.ParseColor("FF0000"));
        Assert.Equal(0x80FF0000u, CompiledHighlighter.ParseColor("#80FF0000"));
        Assert.Equal(0u, CompiledHighlighter.ParseColor(null));
        Assert.Equal(0u, CompiledHighlighter.ParseColor(""));
        Assert.Equal(0u, CompiledHighlighter.ParseColor("xyz"));
    }

    [Fact]
    public void Empty_WhenNoUsableRules()
    {
        Assert.True(CompiledHighlighter.Build(null).IsEmpty);
        Assert.True(CompiledHighlighter.Build(Set()).IsEmpty);
        // 色指定なしは無視
        Assert.True(CompiledHighlighter.Build(Set(new HlRule("x"))).IsEmpty);
        // 無効な規則は無視
        Assert.True(CompiledHighlighter.Build(Set(new HlRule("x", background: "#FF0000", enabled: false))).IsEmpty);
    }

    [Fact]
    public void Literal_MatchPart_ColorsOnlyMatch()
    {
        var hl = CompiledHighlighter.Build(Set(new HlRule("ERROR", background: "#FF0000")));
        var spans = hl.Highlight("2026 ERROR boom");
        Assert.Single(spans);
        Assert.Equal(5, spans[0].Start);
        Assert.Equal(5, spans[0].Length);
        Assert.Equal(0xFFFF0000u, spans[0].Bg);
    }

    [Fact]
    public void WholeLine_ColorsEntireLine()
    {
        var hl = CompiledHighlighter.Build(Set(new HlRule("WARN", background: "#00FF00", wholeLine: true)));
        var spans = hl.Highlight("abc WARN xyz");
        Assert.Single(spans);
        Assert.Equal(0, spans[0].Start);
        Assert.Equal(12, spans[0].Length);
    }

    [Fact]
    public void IgnoreCase_Works()
    {
        var on = CompiledHighlighter.Build(Set(new HlRule("error", ignoreCase: true, background: "#FF0000")));
        Assert.Single(on.Highlight("ERROR here"));
        var off = CompiledHighlighter.Build(Set(new HlRule("error", ignoreCase: false, background: "#FF0000")));
        Assert.Empty(off.Highlight("ERROR here"));
    }

    [Fact]
    public void TopRule_WinsOnOverlap()
    {
        // 先頭規則（緑）が下の規則（赤）に上書き勝ちする（§2・下→上・後勝ち）
        var hl = CompiledHighlighter.Build(Set(
            new HlRule("ERROR", background: "#00FF00"),  // 先頭＝優先
            new HlRule("ERR", background: "#FF0000")));   // 下
        var spans = hl.Highlight("ERROR");
        // "ERR" は緑で上書き、残り "OR" も緑（先頭規則が5文字マッチ）
        Assert.Single(spans);
        Assert.Equal(0xFF00FF00u, spans[0].Bg);
    }

    [Fact]
    public void Regex_CaptureGroup_ColorsOnlyCapture()
    {
        var hl = CompiledHighlighter.Build(Set(
            new HlRule(@"id=(\d+)", isRegex: true, foreground: "#0000FF")));
        var spans = hl.Highlight("user id=1234 end");
        // "1234" のみ着色（id= は着色しない）
        Assert.Single(spans);
        Assert.Equal("user id=".Length, spans[0].Start);
        Assert.Equal(4, spans[0].Length);
        Assert.Equal(0xFF0000FFu, spans[0].Fg);
    }

    [Fact]
    public void FgAndBg_Independent()
    {
        var hl = CompiledHighlighter.Build(Set(
            new HlRule("A", foreground: "#0000FF"),
            new HlRule("AB", background: "#FF0000")));
        // "AB": A は fg=青(先頭勝ち)＋bg=赤(下), B は bg=赤のみ → 2区間
        var spans = hl.Highlight("AB");
        Assert.Equal(2, spans.Count);
        Assert.Equal(0xFF0000FFu, spans[0].Fg);
        Assert.Equal(0xFFFF0000u, spans[0].Bg);
        Assert.Equal(0u, spans[1].Fg);
        Assert.Equal(0xFFFF0000u, spans[1].Bg);
    }

    [Fact]
    public void MultipleMatches_SameRule()
    {
        var hl = CompiledHighlighter.Build(Set(new HlRule("x", background: "#FF0000")));
        var spans = hl.Highlight("x_x_x");
        Assert.Equal(3, spans.Count);
        Assert.Equal(0, spans[0].Start);
        Assert.Equal(2, spans[1].Start);
        Assert.Equal(4, spans[2].Start);
    }

    [Fact]
    public void DefaultPalette_Has32()
    {
        Assert.Equal(32, HighlightDefaults.Palette.Length);
        Assert.Equal(32, HighlightDefaults.DefaultColorLabels().Count);
    }
}
