using System.Buffers;
using System.Text;

namespace UwView.Core;

/// <summary>
/// 大文字小文字を無視する検索を、<b>デコードせずバイトのまま</b>行うための道具
/// （オーナー指示 2026-09-17「uvf に揃える」。uvf の CLI と UwView Pro の並列検索が共通で使う）。
///
/// <b>なぜ「ASCII だけ」で済むのか</b>:
/// .NET の <c>IgnoreCase | CultureInvariant</c> が ASCII 英字と一致させる非 ASCII 文字を全 BMP で
/// 調べたところ、<c>k</c> ← U+212A（ケルビン記号）<b>だけ</b>だった（2026-09-17 実測）。
/// したがって <c>k</c>/<c>K</c> を含まない ASCII だけの語なら、ASCII の大小を畳むだけで
/// 正規表現とまったく同じ結果になる。<c>ripgrep</c> の <c>-i</c> も U+212A を畳むので、
/// この線引きなら画面・CLI・ripgrep の三者が一致する。
///
/// <c>k</c>/<c>K</c> を含む語では、この道を使わずに正規表現へ回すこと
/// （<see cref="LongestFoldableRun"/> で「候補行を絞る手がかり」だけ取り出すのは安全）。
/// </summary>
public static class AsciiCaseFold
{
    /// <summary>バイトと文字が1対1に対応し、ASCII 部分がそのまま現れる文字コードか。</summary>
    public static bool IsAsciiCompatible(Encoding encoding) =>
        encoding.CodePage is 65001 or 20127 or 28591;   // UTF-8 / US-ASCII / Latin-1

    /// <summary>この語なら、バイトのまま大小を畳んで探しても正規表現と同じ結果になるか。</summary>
    public static bool IsFoldable(string pattern) =>
        pattern.Length > 0 && Ascii.IsValid(pattern) && !pattern.AsSpan().ContainsAny('k', 'K');

    /// <summary>
    /// バイトのまま畳んで探せる、いちばん長い連続部分（<c>k</c>/<c>K</c> と非 ASCII で切る）。
    /// 必須の文字列の<b>連続した一部</b>もまた必須なので、候補行を絞る手がかりに使える。
    /// 例: <c>K="NAME</c> → <c>="NAME</c>。取れなければ空文字列。
    /// </summary>
    public static string LongestFoldableRun(string text)
    {
        int bestAt = 0, bestLen = 0, at = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] < 0x80 && text[i] is not ('k' or 'K')) continue;
            if (i - at > bestLen) { bestAt = at; bestLen = i - at; }
            at = i + 1;
        }
        return bestLen == 0 ? "" : text.Substring(bestAt, bestLen);
    }

    /// <summary>探す語を小文字のバイト列にする（比較の基準）。</summary>
    public static byte[] ToLowerBytes(string pattern) => Encoding.ASCII.GetBytes(pattern.ToLowerInvariant());

    /// <summary>英語・XML での出現頻度が低い順（希少が先頭）。ここに無いバイト（記号・数字）は中程度とみなす。</summary>
    private static ReadOnlySpan<byte> RarityOrder => "zqxjkvbwpygfmucldrhsnioate"u8;

    /// <summary>
    /// 語の中で<b>いちばん珍しいバイト</b>の位置。そこを目印に探すと、拾う候補が減って照合の手間が下がる
    /// （よく出る 'e' や 't' を目印にすると候補だらけになる）。
    /// </summary>
    public static int PickRarestIndex(ReadOnlySpan<byte> lower)
    {
        const int NonLetterRank = 8;   // 'p' 相当の希少さとみなす
        int best = 0, bestRank = int.MaxValue;
        for (int i = 0; i < lower.Length; i++)
        {
            int idx = RarityOrder.IndexOf(lower[i]);
            int rank = idx >= 0 ? idx : NonLetterRank;
            if (rank < bestRank) { bestRank = rank; best = i; }
        }
        return best;
    }

    /// <summary>目印のバイト（小文字・大文字の両方）を作る。<paramref name="index"/> は語の中での位置。</summary>
    public static SearchValues<byte> Anchor(ReadOnlySpan<byte> lower, out int index)
    {
        index = PickRarestIndex(lower);
        byte lo = lower[index];
        byte up = (byte)char.ToUpperInvariant((char)lo);
        return SearchValues.Create(lo == up ? [lo] : [lo, up]);
    }

    /// <summary>data の先頭 lower.Length バイトを、ASCII の大小を無視して比べる。</summary>
    public static bool EqualsFolded(ReadOnlySpan<byte> data, ReadOnlySpan<byte> lower)
    {
        for (int i = 0; i < lower.Length; i++)
        {
            byte b = data[i];
            if (b is >= (byte)'A' and <= (byte)'Z') b |= 0x20;
            if (b != lower[i]) return false;
        }
        return true;
    }

    /// <summary>
    /// ASCII の大小を無視して語を探す（見つからなければ -1）。
    /// 目印のバイトを SIMD で拾い、その前後が語に合うかだけを確かめる。
    /// </summary>
    public static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> lower,
                              SearchValues<byte> anchor, int anchorIndex)
    {
        int from = anchorIndex;
        while (from <= haystack.Length - (lower.Length - anchorIndex))
        {
            int rel = haystack[from..].IndexOfAny(anchor);
            if (rel < 0) return -1;
            int at = from + rel - anchorIndex;
            if (at >= 0 && at + lower.Length <= haystack.Length && EqualsFolded(haystack.Slice(at, lower.Length), lower))
                return at;
            from += rel + 1;
        }
        return -1;
    }

    /// <summary>文字列版（本文をすでに読み込んである場合）。</summary>
    public static bool Contains(string text, string lower)
    {
        for (int i = 0; i + lower.Length <= text.Length; i++)
        {
            int k = 0;
            for (; k < lower.Length; k++)
            {
                char c = text[i + k];
                if (c is >= 'A' and <= 'Z') c = (char)(c | 0x20);
                if (c != lower[k]) break;
            }
            if (k == lower.Length) return true;
        }
        return false;
    }
}
