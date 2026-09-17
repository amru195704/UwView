using System.Buffers;
using System.Text;

namespace UwView.Core.Cli;

/// <param name="Hits">出した行数。</param>
/// <param name="Truncated">上限で打ち切ったか。</param>
public readonly record struct RawGrepOutcome(long Hits, bool Truncated);

/// <summary>
/// CLI の生ファイル検索を<b>1回読みで済ませる</b>係（指示書 2026-09-17「uvf 生検索の速度調査と改善」）。
///
/// これまでの CLI は
/// <list type="number">
///   <item>索引を作る（ファイル全体を読む）… 行番号を出すため</item>
///   <item>検索する（ファイル全体をもう一度読む）</item>
///   <item>ヒット行ごとにファイルを読み直す（本文を出すため）</item>
/// </list>
/// と<b>2回半</b>読んでいた。50GB の実測 203 秒の内訳は 索引 約100秒 ＋ 検索 約82秒 ＋ 出力 約20秒。
///
/// ここでは通しの1回読みの最中に「改行を数える（＝行番号）」「一致を探す」「その行の本文をそのまま渡す」
/// を同時に行う。読むのは <see cref="SequentialFileByteSource"/>（pread）なので媒体の帯域に近い速度が出る。
///
/// 一致の判定規則は <see cref="SearchService"/> のバイト高速パスと同じにしてある
/// （大小を区別する素の文字列・1行1件・改行なしの長大行は先頭 64KB で判定）。
/// 正規表現・大小無視は扱わない（<see cref="CanUse"/> が false を返し、呼び出し側が従来経路へ戻る）。
///
/// 行番号は「その行より前にある '\n' の数」で数える。<see cref="SparseLineIndex"/> も改行スタイルに
/// 関わらず '\n' だけを数えているので、索引を作ったときと同じ番号になる。
/// </summary>
public static class RawGrep
{
    private const int BufSize = 4 << 20;              // 4MB ブロック（1MB より 1 割ほど速い）
    private const int MaxLineMatchBytes = 64 * 1024; // 長大行はこの範囲でマッチ判定（SearchService と同じ）

    /// <summary>ヒット行を受け取る係（行は 0 始まり・末尾の改行は取り除いてある）。</summary>
    public delegate void LineSink(long lineIndex, ReadOnlySpan<byte> line);

    /// <summary>この経路で同じ結果が出せる条件か。外れたら従来の「索引＋検索」へ戻す。</summary>
    public static bool CanUse(SearchOptions options)
        => options is { UseRegex: false, IgnoreCase: false } && options.Pattern.Length > 0;

    public static async Task<RawGrepOutcome> RunAsync(
        IByteSource src, int bomLength, Encoding encoding, SearchOptions options,
        LineSink sink, CancellationToken ct = default)
    {
        byte[] needle = encoding.GetBytes(options.Pattern);
        if (needle.Length == 0) return new RawGrepOutcome(0, false);

        long fileLength = src.Length;
        long limit = options.HitLimit;
        long hits = 0;
        bool truncated = false;

        // 行が途中で切れたぶんを先頭へ繰り越すので、バッファは 2 倍取る（SearchService と同じ作り）
        byte[] buf = ArrayPool<byte>.Shared.Rent(BufSize * 2);
        try
        {
            long bufBase = bomLength;   // buf[0] のファイル上の位置
            long pos = bomLength;       // 次に読む位置
            long lineNo = 0;            // buf[0] が何行目か（0 始まり）
            int carry = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int want = (int)Math.Min(BufSize, fileLength - pos);
                int got = want <= 0 ? 0 : await src.ReadAsync(pos, buf.AsMemory(carry, want), ct);
                pos += got;
                int filled = carry + got;
                if (filled == 0) break;
                bool isEof = pos >= fileLength;

                var span = buf.AsSpan(0, filled);
                int lastNl = span.LastIndexOf((byte)'\n');
                int region;
                if (lastNl >= 0) region = lastNl + 1;
                else if (isEof) region = filled;
                else if (filled >= BufSize)
                {
                    // 1 ブロック内に改行がない長大行: 先頭 64KB だけで判定し、次の改行まで読み飛ばす
                    if (span[..Math.Min(filled, MaxLineMatchBytes)].IndexOf(needle) >= 0)
                    {
                        long end = await LineEndAsync(src, bufBase + filled, fileLength, ct);
                        await EmitLongLineAsync(src, bufBase, end, lineNo, sink, ct);
                        if (++hits >= limit) { truncated = true; break; }
                        bufBase = pos = end + 1;
                    }
                    else
                    {
                        long next = await LineEndAsync(src, bufBase + filled, fileLength, ct);
                        bufBase = pos = next + 1;
                    }
                    lineNo++;                       // 読み飛ばした長大行ぶん
                    carry = 0;
                    if (pos >= fileLength) break;
                    continue;
                }
                else { carry = filled; continue; }  // もう少し読めば行が完結する

                lineNo = Scan(span[..region], bufBase, lineNo);
                if (truncated) break;

                carry = filled - region;
                if (carry > 0) span[region..].CopyTo(buf);
                bufBase += region;

                if (isEof && carry == 0) break;
                if (isEof && carry > 0)
                {
                    lineNo = Scan(buf.AsSpan(0, carry), bufBase, lineNo);  // 末尾に改行がない最終行
                    break;
                }
            }

            return new RawGrepOutcome(hits, truncated);

            // region は行頭から始まる完結行の集まり。改行を数えながら一致行を出し、次の行番号を返す
            long Scan(ReadOnlySpan<byte> region, long regionBase, long firstLine)
            {
                int cursor = 0;            // まだ見ていない範囲の先頭（必ず行頭）
                long cursorLine = firstLine;

                while (cursor < region.Length)
                {
                    int rel = region[cursor..].IndexOf(needle);
                    if (rel < 0) break;
                    int hitAt = cursor + rel;

                    // cursor から一致位置までの改行を数えて、その行の先頭を求める
                    int lineStart = cursor;
                    long line = cursorLine;
                    for (int i = cursor; i < hitAt; )
                    {
                        int nl = region[i..hitAt].IndexOf((byte)'\n');
                        if (nl < 0) break;
                        i += nl + 1;
                        line++;
                        lineStart = i;
                    }

                    int nlAfter = region[hitAt..].IndexOf((byte)'\n');
                    int lineEnd = nlAfter < 0 ? region.Length : hitAt + nlAfter;

                    var text = region[lineStart..lineEnd];
                    if (text.Length > 0 && text[^1] == (byte)'\r') text = text[..^1];
                    sink(line, text);

                    if (++hits >= limit) { truncated = true; return line + 1; }

                    if (nlAfter < 0) { cursor = region.Length; cursorLine = line; break; }
                    cursor = lineEnd + 1;
                    cursorLine = line + 1;
                }

                return cursorLine + CountNewlines(region[cursor..]);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buf); }
    }

    private static long CountNewlines(ReadOnlySpan<byte> span)
    {
        long n = 0;
        for (int i = 0; i < span.Length; )
        {
            int nl = span[i..].IndexOf((byte)'\n');
            if (nl < 0) break;
            n++; i += nl + 1;
        }
        return n;
    }

    /// <summary>from 以降で最初の '\n' の位置（無ければファイル末尾）。</summary>
    private static async Task<long> LineEndAsync(IByteSource src, long from, long fileLength, CancellationToken ct)
    {
        byte[] buf = ArrayPool<byte>.Shared.Rent(1 << 16);
        try
        {
            long pos = from;
            while (pos < fileLength)
            {
                ct.ThrowIfCancellationRequested();
                int want = (int)Math.Min(buf.Length, fileLength - pos);
                int got = await src.ReadAsync(pos, buf.AsMemory(0, want), ct);
                if (got <= 0) break;
                int nl = buf.AsSpan(0, got).IndexOf((byte)'\n');
                if (nl >= 0) return pos + nl;
                pos += got;
            }
            return fileLength;
        }
        finally { ArrayPool<byte>.Shared.Return(buf); }
    }

    /// <summary>バッファに収まらない長大行を、省略せずに読み直して渡す（まれな経路）。</summary>
    private static async Task EmitLongLineAsync(
        IByteSource src, long start, long end, long lineIndex, LineSink sink, CancellationToken ct)
    {
        long size = Math.Max(0, end - start);
        byte[] line = new byte[size];
        long got = 0;
        while (got < size)
        {
            ct.ThrowIfCancellationRequested();
            int n = await src.ReadAsync(start + got, line.AsMemory((int)got, (int)Math.Min(int.MaxValue, size - got)), ct);
            if (n <= 0) break;
            got += n;
        }
        var text = line.AsSpan(0, (int)got);
        if (text.Length > 0 && text[^1] == (byte)'\r') text = text[..^1];
        sink(lineIndex, text);
    }
}
