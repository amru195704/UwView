using UwView.Core;

namespace UwView.Core.Tests;

/// <summary>支援の案内（帯）を出す決まり（指示-20261112-07）。</summary>
public class SupportPromptTests
{
    private static readonly DateOnly Start = new(2026, 3, 1);

    /// <summary>Start から n 日、毎日 1 回使った状態。</summary>
    private static SupportPromptState UsedEveryDay(int days)
    {
        var s = new SupportPromptState();
        for (int i = 0; i < days; i++) SupportPrompt.RecordUse(s, Start.AddDays(i));
        return s;
    }

    [Fact]
    public void 同じ日に何度起動しても使った日は1日()
    {
        var s = new SupportPromptState();
        Assert.True(SupportPrompt.RecordUse(s, Start));
        Assert.False(SupportPrompt.RecordUse(s, Start));
        Assert.True(SupportPrompt.RecordUse(s, Start.AddDays(1)));
        Assert.Equal(2, s.UsedDays);
        Assert.Equal(Start.DayNumber, s.FirstUseDay);
    }

    [Fact]
    public void 初めて使ってから30日未満なら出さない()
    {
        var s = UsedEveryDay(20);
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(s, Start.AddDays(29)));
        Assert.Equal(SupportPromptKind.First, SupportPrompt.Decide(s, Start.AddDays(30)));
    }

    [Fact]
    public void 使った日が10日未満なら出さない()
    {
        var s = UsedEveryDay(9);
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(s, Start.AddDays(60)));
        SupportPrompt.RecordUse(s, Start.AddDays(60));
        Assert.Equal(SupportPromptKind.First, SupportPrompt.Decide(s, Start.AddDays(60)));
    }

    [Fact]
    public void 後でなら90日後に1回だけ出しその後は出さない()
    {
        var s = UsedEveryDay(10);
        var first = Start.AddDays(30);
        Assert.Equal(SupportPromptKind.First, SupportPrompt.Decide(s, first));
        SupportPrompt.MarkShown(s, SupportPromptKind.First, first);

        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(s, first));
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(s, first.AddDays(89)));
        var again = first.AddDays(90);   // 2026-06-29（12/15〜1/10 の外）
        Assert.Equal(SupportPromptKind.Reminder, SupportPrompt.Decide(s, again));
        SupportPrompt.MarkShown(s, SupportPromptKind.Reminder, again);

        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(s, again.AddDays(100)));   // 10/7・期間の外
    }

    [Fact]
    public void 支援するを押したら年に1回の呼びかけも出さない()
    {
        var s = UsedEveryDay(10);
        SupportPrompt.MarkShown(s, SupportPromptKind.First, Start.AddDays(30));
        SupportPrompt.MarkSupported(s);
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(s, new DateOnly(2026, 12, 20)));
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(s, Start.AddDays(200)));
    }

    [Fact]
    public void もう表示しないを押したら設定の表示しないと同じで出さない()
    {
        var s = UsedEveryDay(10);
        SupportPrompt.MarkNeverShow(s);
        Assert.True(s.Disabled);
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(s, new DateOnly(2026, 12, 20)));
    }

    [Theory]
    [InlineData(2026, 12, 14, 0)]
    [InlineData(2026, 12, 15, 2026)]
    [InlineData(2026, 12, 31, 2026)]
    [InlineData(2027, 1, 1, 2026)]
    [InlineData(2027, 1, 10, 2026)]
    [InlineData(2027, 1, 11, 0)]
    public void 年に1回の呼びかけの期間は12月15日から1月10日(int y, int m, int d, int season)
        => Assert.Equal(season, SupportPrompt.SeasonYear(new DateOnly(y, m, d)));

    [Fact]
    public void 呼びかけは期間中の最初の起動で1回だけ()
    {
        var s = UsedEveryDay(10);
        SupportPrompt.MarkShown(s, SupportPromptKind.First, Start.AddDays(30));   // 3/31
        var day = new DateOnly(2026, 12, 16);
        Assert.Equal(SupportPromptKind.Season, SupportPrompt.Decide(s, day));
        SupportPrompt.MarkShown(s, SupportPromptKind.Season, day);
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(s, new DateOnly(2027, 1, 5)));
        Assert.Equal(SupportPromptKind.Season, SupportPrompt.Decide(s, new DateOnly(2027, 12, 20)));   // 次の年
    }

    [Fact]
    public void 前の帯から90日以内なら呼びかけも出さない()
    {
        var s = UsedEveryDay(10);
        SupportPrompt.MarkShown(s, SupportPromptKind.First, new DateOnly(2026, 11, 1));
        Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(s, new DateOnly(2026, 12, 20)));
    }

    [Fact]
    public void 呼びかけが最初の帯を兼ねる()
    {
        var s = UsedEveryDay(10);
        var day = new DateOnly(2026, 12, 15);
        SupportPrompt.MarkShown(s, SupportPromptKind.Season, day);
        Assert.Equal(1, s.ShownCount);
        Assert.Equal(SupportPromptKind.Reminder, SupportPrompt.Decide(s, day.AddDays(90)));
    }

    [Fact]
    public void 記録が無ければ出さない()
        => Assert.Equal(SupportPromptKind.None, SupportPrompt.Decide(new SupportPromptState(), new DateOnly(2026, 12, 20)));
}
