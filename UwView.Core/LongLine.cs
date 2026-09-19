using System.Buffers;

namespace UwView.Core;

/// <summary>
/// 1回の読みに収まらない長い行の<b>残り</b>を読み進める係。
///
/// 検索は長い行を先頭の1回ぶんだけで判定し、残りを改行まで読み飛ばしていた。
/// 固定文字列（大小無視を含む）はバイトを見るだけなので、行の後ろにあっても見落とす理由がない
/// （再々レビュー 2026-09-19 の指摘6。UVF の索引あり／なし・Pro の通常／除外で見落としていた）。
/// 読み飛ばすついでに探し、読みの境目をまたぐ一致も拾う。
///
/// 固定文字列の道は ASCII 互換の文字コードに限っているので、区切りは1バイトとして扱う。
/// </summary>
public static class LongLine
{
    /// <summary>span の中で探す（見つからなければ -1）。</summary>
    public delegate int Finder(ReadOnlySpan<byte> span);

    /// <summary>読んだバイトを受け取る（.uwvz への追記など）。span はこの呼び出しの間だけ有効。</summary>
    public delegate void Observer(long fileOffset, ReadOnlySpan<byte> bytes);

    /// <summary>
    /// <paramref name="from"/> から次の区切りまで読み、その間に <paramref name="finder"/> が当たるかも調べる。
    /// </summary>
    /// <param name="head">
    /// <paramref name="from"/> の直前にある、同じ行の終わりの部分（語の長さ − 1 バイトあれば足りる）。
    /// 読みの境目をまたいだ一致を拾うために使う。探さないときは空でよい。
    /// </param>
    /// <param name="finder">探す係（null なら行の終わりだけ求める）。</param>
    /// <returns>(区切りの位置＝行の本文の終わり（無ければ <paramref name="end"/>）, 見つかったか)</returns>
    public static async Task<(long ContentEnd, bool Found)> ScanRestAsync(
        IByteSource src, long from, long end, byte separator,
        ReadOnlyMemory<byte> head, Finder? finder, Observer? observer, CancellationToken ct)
    {
        const int Chunk = 1 << 20;
        byte[] buf = ArrayPool<byte>.Shared.Rent(Chunk + head.Length);
        try
        {
            head.Span.CopyTo(buf);
            int keep = head.Length;
            int overlap = head.Length;
            bool found = false;
            long pos = from;
            while (pos < end)
            {
                ct.ThrowIfCancellationRequested();
                int want = (int)Math.Min(Chunk, end - pos);
                int got = await src.ReadAsync(pos, buf.AsMemory(keep, want), ct);
                if (got <= 0) break;
                observer?.Invoke(pos, buf.AsSpan(keep, got));

                var span = buf.AsSpan(0, keep + got);
                int sepAt = span[keep..].IndexOf(separator);          // 新しく読んだぶんだけ見る
                int limit = sepAt >= 0 ? keep + sepAt : span.Length;
                if (!found && finder is not null && finder(span[..limit]) >= 0) found = true;
                if (sepAt >= 0) return (pos + sepAt, found);

                // 境目をまたぐ一致のために、終わりの部分を次の読みの頭へ残す
                int k = Math.Min(overlap, span.Length);
                span[^k..].CopyTo(buf);
                keep = k;
                pos += got;
            }
            return (end, found);
        }
        finally { ArrayPool<byte>.Shared.Return(buf); }
    }
}
