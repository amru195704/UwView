using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;

namespace UwView.Core;

/// <summary>
/// 大文字小文字を無視する検索を、<b>デコードせずバイトのまま</b>行うための道具
/// （9.17修正「uvf に揃える」。uvf の CLI と UwView Pro の並列検索が共通で使う）。
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
    /// <summary>
    /// バイトと文字が1対1に対応し、ASCII 部分がそのまま現れる文字コードか。
    ///
    /// <b>バイト列のまま探してよいかの判定にも使う。</b>Shift-JIS や EUC-JP では ASCII の値が
    /// 2バイト文字の<b>後半</b>にも現れる（`ソ` は 83 5C なので `\` の検索に当たってしまう）。
    /// UTF-16/32 は1文字が複数バイトで、そもそもバイト単位の一致が文字の一致にならない。
    /// そういう文字コードでは、デコードしてから判定する経路へ回す（ソースレビュー 2026-09-19 の指摘5）。
    /// </summary>
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

    /// <summary>
    /// 計測用（<c>UV_TRACE=1</c>）：目印が合って語全体を比べた回数を数えるか、とその数。
    /// 数えないときは分岐1つだけで、速さに響かない。
    /// </summary>
    public static bool CountChecks;
    public static long Checks;

    /// <summary>探す語を小文字のバイト列にする（比較の基準）。</summary>
    public static byte[] ToLowerBytes(string pattern) => Encoding.ASCII.GetBytes(pattern.ToLowerInvariant());

    /// <summary>英語・XML での出現頻度が低い順（希少が先頭）。</summary>
    private static ReadOnlySpan<byte> RarityOrder => "zqxjkvbwpygfmucldrhsnioate"u8;

    /// <summary>
    /// XML・ログ・ソースコードで<b>どの英字よりもよく出る</b>記号と数字。目印にすると候補だらけになる。
    /// 以前は記号をすべて「p 並みに珍しい」とみなしていたので、<c>K="NAME</c> の手がかり <c>="name</c> では
    /// <c>=</c> と <c>"</c> が目印になり、OSM のほぼすべての属性 <c>="</c> に当たって、3G で 1 億 2,600 万回も
    /// 語全体を比べていた（rg の 1.84 倍遅い。実装指示書 2026-10-03）。
    /// </summary>
    private static ReadOnlySpan<byte> CommonSymbols => " =\"<>/:.,-_'\t;()[]0123456789"u8;

    /// <summary>目印としての珍しさ（小さいほど珍しい）。</summary>
    private static int Rank(byte b)
    {
        int idx = RarityOrder.IndexOf(b);
        if (idx >= 0) return idx;
        int common = CommonSymbols.IndexOf(b);
        if (common >= 0) return RarityOrder.Length + CommonSymbols.Length - common;   // どの英字より後ろ
        return 8;   // ほかの記号（@ # $ % など）は 'p' 並みに珍しいとみなす
    }

    /// <summary>
    /// 語の中で<b>いちばん珍しいバイト</b>の位置。そこを目印に探すと、拾う候補が減って照合の手間が下がる
    /// （よく出る 'e' や 't' を目印にすると候補だらけになる）。
    /// </summary>
    public static int PickRarestIndex(ReadOnlySpan<byte> lower)
    {
        int best = 0, bestRank = int.MaxValue;
        for (int i = 0; i < lower.Length; i++)
        {
            int rank = Rank(lower[i]);
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
        if (lower.Length >= 2 && haystack.Length >= 64 && Vector128.IsHardwareAccelerated)
            return IndexOfTwoAnchors(haystack, lower, anchorIndex);
        int from = anchorIndex;
        while (from <= haystack.Length - (lower.Length - anchorIndex))
        {
            int rel = haystack[from..].IndexOfAny(anchor);
            if (rel < 0) return -1;
            int at = from + rel - anchorIndex;
            if (CountChecks) Interlocked.Increment(ref Checks);
            if (at >= 0 && at + lower.Length <= haystack.Length && EqualsFolded(haystack.Slice(at, lower.Length), lower))
                return at;
            from += rel + 1;
        }
        return -1;
    }

    /// <summary>
    /// 目印を2つ（いちばん珍しい位置と、次に珍しい別の位置）にして、16 バイトずつ同時に確かめる。
    /// 目印1つだと、ふつうの英単語（spinlock の p など）はソースの中で何度も当たり、そのたびに語全体を比べて
    /// 大小を区別する検索の約3倍かかった（Linux カーネル・2026-10-02）。2つ同時に合う位置はずっと少ない。
    /// 返すのは<b>いちばん前の一致</b>（目印1つの道と同じ）。
    /// </summary>
    private static int IndexOfTwoAnchors(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> lower, int a)
    {
        int b = SecondRarestIndex(lower, a);
        var aLo = Vector128.Create(lower[a]);
        var aUp = Vector128.Create(UpperOf(lower[a]));
        var bLo = Vector128.Create(lower[b]);
        var bUp = Vector128.Create(UpperOf(lower[b]));
        ref byte start = ref MemoryMarshal.GetReference(haystack);

        int lastStart = haystack.Length - lower.Length;   // 語が始まれる最後の位置
        int i = 0;
        // 16 個の始まり位置 i..i+15 を一度に見る。読むのは i+a・i+b から 16 バイトずつ（語の内側なので範囲に収まる）
        for (; i <= lastStart - 15; i += 16)
        {
            var va = Vector128.LoadUnsafe(ref start, (nuint)(i + a));
            var vb = Vector128.LoadUnsafe(ref start, (nuint)(i + b));
            var both = (Vector128.Equals(va, aLo) | Vector128.Equals(va, aUp))
                     & (Vector128.Equals(vb, bLo) | Vector128.Equals(vb, bUp));
            uint bits = both.ExtractMostSignificantBits();
            while (bits != 0)
            {
                int at = i + BitOperations.TrailingZeroCount(bits);
                if (CountChecks) Interlocked.Increment(ref Checks);
                if (EqualsFolded(haystack.Slice(at, lower.Length), lower)) return at;
                bits &= bits - 1;
            }
        }
        for (; i <= lastStart; i++)
            if (EqualsFolded(haystack.Slice(i, lower.Length), lower)) return i;
        return -1;
    }

    /// <summary>計測用：目印 2 つの位置（<see cref="IndexOf"/> が SIMD で使うもの）。</summary>
    public static (int First, int Second) AnchorPositions(ReadOnlySpan<byte> lower)
    {
        int a = PickRarestIndex(lower);
        return (a, lower.Length >= 2 ? SecondRarestIndex(lower, a) : a);
    }

    /// <summary><paramref name="first"/> 以外で、いちばん珍しいバイトの位置。</summary>
    private static int SecondRarestIndex(ReadOnlySpan<byte> lower, int first)
    {
        int best = first == 0 ? 1 : 0, bestRank = int.MaxValue;
        for (int i = 0; i < lower.Length; i++)
        {
            if (i == first) continue;
            int rank = Rank(lower[i]);
            // 同じ文字なら、目印1つ目から離れた位置を選ぶ（隣り合う同じ文字はあまり絞り込めない）
            if (rank < bestRank || (rank == bestRank && Math.Abs(i - first) > Math.Abs(best - first)))
            { bestRank = rank; best = i; }
        }
        return best;
    }

    private static byte UpperOf(byte lower) => lower is >= (byte)'a' and <= (byte)'z' ? (byte)(lower & ~0x20) : lower;

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
