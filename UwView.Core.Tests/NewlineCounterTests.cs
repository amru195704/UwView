namespace UwView.Core.Tests;

/// <summary>改行の数え方（v1.7.3.6.8 で SIMD の足し込みにした）。素朴に数えた数と一致すること。</summary>
public class NewlineCounterTests
{
    private static long Naive(byte[] data) => data.LongCount(b => b == (byte)'\n');

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(127)]
    [InlineData(64 * 255)]
    [InlineData(64 * 255 + 1)]
    [InlineData(64 * 256 + 17)]
    [InlineData(1 << 20)]
    public void MatchesNaiveCount(int length)
    {
        var random = new Random(length + 20261003);
        byte[] data = new byte[length];
        for (int i = 0; i < length; i++) data[i] = random.Next(4) == 0 ? (byte)'\n' : (byte)random.Next(256);
        Assert.Equal(Naive(data), NewlineCounter.Count(data));
    }

    [Fact]
    public void CountsEveryByteWhenAllAreNewlines()
    {
        // 足し込みが 255 回でもあふれないこと（全部が改行のとき、各レーンが毎回 1 ずつ増える）
        byte[] data = Enumerable.Repeat((byte)'\n', 64 * 255 * 3 + 5).ToArray();
        Assert.Equal(data.Length, NewlineCounter.Count(data));
    }

    [Fact]
    public void CountsOnlyNewlineNotCarriageReturn()
    {
        byte[] data = System.Text.Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("a\r\nb\rc\n", 50)));
        Assert.Equal(100, NewlineCounter.Count(data));
    }
}
