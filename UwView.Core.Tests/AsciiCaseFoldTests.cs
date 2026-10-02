using System.Text;

namespace UwView.Core.Tests;

/// <summary>
/// 大小を無視したバイト検索（v1.7.3.6.3 で目印2つの SIMD 版を追加）。
/// 素朴な走査と同じ「いちばん前の一致」を返すことを、境界と乱数データで確かめる。
/// </summary>
public class AsciiCaseFoldTests
{
    private static int Naive(byte[] hay, byte[] lower)
    {
        for (int i = 0; i + lower.Length <= hay.Length; i++)
            if (AsciiCaseFold.EqualsFolded(hay.AsSpan(i, lower.Length), lower)) return i;
        return -1;
    }

    private static int Find(byte[] hay, string word)
    {
        byte[] lower = AsciiCaseFold.ToLowerBytes(word);
        var anchor = AsciiCaseFold.Anchor(lower, out int at);
        return AsciiCaseFold.IndexOf(hay, lower, anchor, at);
    }

    [Theory]
    [InlineData("spinlock")]
    [InlineData("mutex_lock")]
    [InlineData("ab")]
    [InlineData("zz")]
    [InlineData("a1_")]
    public void MatchesNaiveScanOnRandomText(string word)
    {
        var random = new Random(20261002);
        byte[] alphabet = Encoding.ASCII.GetBytes("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_1 \n");
        byte[] lower = AsciiCaseFold.ToLowerBytes(word);
        for (int round = 0; round < 300; round++)
        {
            byte[] hay = new byte[random.Next(0, 400)];
            for (int i = 0; i < hay.Length; i++) hay[i] = alphabet[random.Next(alphabet.Length)];
            // 半分は語を大小混ぜて埋め込む（途中・末尾・先頭）
            if (round % 2 == 0 && hay.Length >= word.Length)
            {
                int at = random.Next(hay.Length - word.Length + 1);
                for (int i = 0; i < word.Length; i++)
                    hay[at + i] = random.Next(2) == 0 ? (byte)char.ToUpperInvariant(word[i]) : lower[i];
            }
            Assert.Equal(Naive(hay, lower), Find(hay, word));
        }
    }

    [Fact]
    public void ReturnsEarliestOfSeveralMatchesInOneBlock()
    {
        byte[] hay = Encoding.ASCII.GetBytes(new string('.', 70) + "SpInLoCk..spinlock" + new string('.', 30));
        Assert.Equal(70, Find(hay, "spinlock"));
    }

    [Fact]
    public void FindsMatchEndingAtLastByte()
    {
        byte[] hay = Encoding.ASCII.GetBytes(new string('x', 100) + "MUTEX_LOCK");
        Assert.Equal(100, Find(hay, "mutex_lock"));
        Assert.Equal(-1, Find(hay[..^1], "mutex_lock"));
    }

    [Fact]
    public void DoesNotMatchNonLetterBytesThatDifferOnlyInBit5()
    {
        // '_'(0x5F) と DEL(0x7F)、'@'(0x40) と '`'(0x60) は大小の関係ではない
        byte[] hay = Encoding.ASCII.GetBytes(new string(' ', 80) + "a\u007fb a`b");
        Assert.Equal(-1, Find(hay, "a_b"));
        Assert.Equal(-1, Find(hay, "a@b"));
    }
}
