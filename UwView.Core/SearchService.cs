using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;

namespace UwView.Core;

/// <param name="MaxHits">
/// 1回の検索で保持する最大ヒット数。null なら既定（<see cref="SearchService.DefaultMaxHits"/>・初期値は無制限）、
/// 0 以下なら無制限（指示書 2026-09-15「検索上限のパラメータ化」／2026-09-16「既定を無制限に」）。
/// </param>
public sealed record SearchOptions(string Pattern, bool UseRegex = false, bool IgnoreCase = false, int? MaxHits = null)
{
    /// <summary>実際に使う上限（無制限なら long.MaxValue）。</summary>
    public long HitLimit => MaxHits switch
    {
        null => SearchService.DefaultMaxHits <= 0 ? long.MaxValue : SearchService.DefaultMaxHits,
        <= 0 => long.MaxValue,
        { } n => n,
    };
}

public sealed record SearchOutcome(long TotalHits, bool Truncated, bool Completed);

/// <summary>
/// 文字列検索（§11-①）。索引と独立に IByteSource を直接スキャンする背景処理。
/// ヒットは「マッチを含む行の行頭バイトオフセット」（昇順・1行1件）。
/// バイトオフセット基準なのでエンコード切替・索引未完了（ページモード）でも一貫して有効。
/// - literal（大小区別あり）: エンコード済みバイト列の SIMD IndexOf 高速パス
/// - regex / 大小無視: 行単位デコード + Regex.IsMatch(Span) パス。
///   正規表現に必須リテラルがあれば（<see cref="RegexLiterals"/>）、先にそのバイト列で候補行を探し、候補行だけをデコードして当てる
/// </summary>
public static class SearchService
{
    /// <summary>
    /// v1.6.0 までの既定だった上限（100万件＝long で 8MB）。
    /// いまは既定＝無制限なので、<b>保存済みの設定がこの値なら「旧い既定」とみなして無制限に読み替える</b>ためだけに使う
    /// （オーナー裁定 2026-09-16。上限に当たった打ち切りで結果が変わる事故を既定では起こさない）。
    /// </summary>
    public const int LegacyDefaultMaxHits = 1_000_000;

    /// <summary>
    /// <see cref="SearchOptions.MaxHits"/> を指定しなかったときの上限（0 以下＝無制限）。**初期値は無制限**。
    /// UVP の画面は設定「1回の検索で保持する最大ヒット数」をここへ入れる（検索条件を作る場所が多いため）。
    /// 件数を絞りたいときは検索条件の <c>MaxHits</c>（CLI は -limit）で明示する。
    /// </summary>
    public static int DefaultMaxHits { get; set; }

    /// <summary>
    /// 正規表現の必須リテラルで候補行を先に絞るか（既定 true）。
    /// 効果の測定（切ったときと比べる）と、万一の不具合時に元の動きへ戻すための切り替え。
    /// </summary>
    public static bool UseLiteralPrefilter { get; set; } = true;
    private const int BufSize = 1 << 20;            // 1MB ブロック
    private const int MaxLineMatchBytes = 64 * 1024; // 長大行はこの範囲でマッチ判定

    public static Regex BuildRegex(SearchOptions options)
    {
        var opts = RegexOptions.CultureInvariant;
        if (options.IgnoreCase) opts |= RegexOptions.IgnoreCase;
        string pattern = options.UseRegex ? options.Pattern : Regex.Escape(options.Pattern);
        return new Regex(pattern, opts, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// 全文検索で全行に当てる正規表現。意味は <see cref="BuildRegex"/> と同じで、コンパイルする
    /// （3GB の実測で指定なしより最大 1.6 倍速く、NonBacktracking は 3 割遅かった）。ブラウザ（WASM）はコンパイルしない。
    /// </summary>
    private static Regex BuildScanRegex(SearchOptions options)
    {
        var regex = BuildRegex(options);
        return OperatingSystem.IsBrowser()
            ? regex
            : new Regex(regex.ToString(), regex.Options | RegexOptions.Compiled, regex.MatchTimeout);
    }

    /// <param name="hitBatches">ヒットのバッチ通知。背景スレッドから同期的に呼ばれる
    /// （完了 await 前にすべての呼び出しが終わることを保証）。UI へのマーシャリングは呼び出し側で行う。</param>
    public static Task<SearchOutcome> SearchAsync(
        IByteSource src, int bomLength, Encoding encoding, SearchOptions options,
        Action<IReadOnlyList<long>>? hitBatches = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
        => Task.Run(() => SearchCore(src, bomLength, encoding, options, hitBatches, progress, ct), ct);

    private static async Task<SearchOutcome> SearchCore(
        IByteSource src, int bomLength, Encoding encoding, SearchOptions options,
        Action<IReadOnlyList<long>>? hitBatches, IProgress<double>? progress, CancellationToken ct)
    {
        long fileLength = src.Length;
        long contentBytes = Math.Max(1, fileLength - bomLength);

        bool bytePath = options is { UseRegex: false, IgnoreCase: false } && options.Pattern.Length > 0;
        byte[] needle = bytePath ? encoding.GetBytes(options.Pattern) : [];
        if (bytePath && needle.Length == 0) bytePath = false;
        Regex? regex = bytePath ? null : BuildScanRegex(options);
        Decoder? decoder = bytePath ? null : encoding.GetDecoder();
        LiteralFinder? prefilter = CreatePrefilter(options, encoding);
        long limit = options.HitLimit;

        long totalHits = 0;
        bool truncated = false;
        var batch = new List<long>(256);

        // バッファは常に行頭から始まる（未完の末尾行を先頭へ繰り越す）
        // carry は最大 BufSize 弱 + 追加読み BufSize → 2 倍を確保
        byte[] buf = ArrayPool<byte>.Shared.Rent(BufSize * 2);
        char[] chars = bytePath ? [] : ArrayPool<char>.Shared.Rent(MaxLineMatchBytes + 16);
        try
        {
            long bufBase = bomLength;  // buf[0] のファイル上オフセット
            int carry = 0;             // 前ブロックから繰り越した未完行のバイト数
            long pos = bomLength;      // 次に読むファイル位置
            long lastReport = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int want = (int)Math.Min(BufSize, fileLength - pos);
                int got = want <= 0 ? 0 : await src.ReadAsync(pos, buf.AsMemory(carry, want), ct);
                pos += got;
                int filled = carry + got;
                if (filled == 0) break;
                bool isEof = pos >= fileLength;

                // 処理範囲＝完結行まで（最後の '\n'）。EOF なら残り全部
                var span = buf.AsSpan(0, filled);
                int lastNl = span.LastIndexOf((byte)'\n');
                int region;
                if (lastNl >= 0) region = lastNl + 1;
                else if (isEof) region = filled;
                else if (filled >= BufSize)
                {
                    // 1MB 内に改行なしの長大行: 先頭 MaxLineMatchBytes で判定 → 次の '\n' まで読み飛ばし
                    ProcessRegion(buf.AsSpan(0, Math.Min(filled, MaxLineMatchBytes)), bufBase, oversized: true);
                    long skipped = await SkipToNextLineAsync(src, bufBase + filled, fileLength, ct);
                    bufBase = skipped; pos = skipped; carry = 0;
                    if (truncated || pos >= fileLength) break;
                    continue;
                }
                else { carry = filled; continue; } // もう少し読めば行が完結する

                ProcessRegion(buf.AsSpan(0, region), bufBase, oversized: false);
                if (truncated) break;

                // 未完の末尾行を先頭へ
                carry = filled - region;
                if (carry > 0) buf.AsSpan(region, carry).CopyTo(buf);
                bufBase += region;

                if (batch.Count >= 256) Flush();
                if (progress is not null && pos - lastReport >= (16 << 20))
                {
                    lastReport = pos;
                    progress.Report((double)(pos - bomLength) / contentBytes);
                }
                if (isEof && carry == 0) break;
                if (isEof && carry > 0)
                {
                    ProcessRegion(buf.AsSpan(0, carry), bufBase, oversized: false); // 末尾改行なしの最終行
                    break;
                }
            }

            Flush();
            progress?.Report(1.0);
            return new SearchOutcome(totalHits, truncated, Completed: true);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
            if (chars.Length > 0) ArrayPool<char>.Shared.Return(chars);
        }

        void Flush()
        {
            if (batch.Count == 0) return;
            hitBatches?.Invoke(batch.ToArray());
            batch.Clear();
        }

        // region は行頭から始まる完結行の集まり（oversized 時のみ 1 行の先頭断片）
        void ProcessRegion(ReadOnlySpan<byte> region, long regionBase, bool oversized)
        {
            if (bytePath) ProcessBytes(region, regionBase);
            else ProcessLines(region, regionBase, oversized);
        }

        // literal 高速パス: バイト列 IndexOf。ヒットしたら行頭を逆走査→行末へスキップ（1行1件）
        void ProcessBytes(ReadOnlySpan<byte> region, long regionBase)
        {
            int searchFrom = 0;
            while (searchFrom < region.Length)
            {
                int rel = region[searchFrom..].IndexOf(needle);
                if (rel < 0) break;
                int hitAt = searchFrom + rel;

                int lineStart = region[..hitAt].LastIndexOf((byte)'\n') + 1; // 見つからなければ 0
                if (AddHit(regionBase + lineStart)) return;

                int nl = region[hitAt..].IndexOf((byte)'\n');
                if (nl < 0) break;
                searchFrom = hitAt + nl + 1;
            }
        }

        // regex パス: 行単位にデコードして Span マッチ（長大行は先頭 MaxLineMatchBytes で判定）
        void ProcessLines(ReadOnlySpan<byte> region, long regionBase, bool oversized)
        {
            if (prefilter is not null) { ProcessCandidateLines(region, regionBase); return; }
            int lineStart = 0;
            while (lineStart < region.Length)
            {
                int nl = region[lineStart..].IndexOf((byte)'\n');
                int lineEnd = nl < 0 ? region.Length : lineStart + nl;

                var line = region[lineStart..lineEnd];
                if (line.Length > 0 && line[^1] == (byte)'\r') line = line[..^1];
                if (line.Length > MaxLineMatchBytes) line = line[..MaxLineMatchBytes];

                decoder!.Reset();
                int charCount = decoder.GetChars(line, chars, flush: true);
                if (regex!.IsMatch(chars.AsSpan(0, charCount)))
                {
                    if (AddHit(regionBase + lineStart)) return;
                }

                if (nl < 0) break;
                lineStart = lineEnd + 1;
            }
        }

        // regex パス（必須リテラルあり）: リテラルの現れる行だけをデコードして当てる。判定は上と同じ
        void ProcessCandidateLines(ReadOnlySpan<byte> region, long regionBase)
        {
            prefilter!.Reset();
            int from = 0;
            while (from < region.Length)
            {
                int at = prefilter.IndexOf(region, from);
                if (at < 0) break;
                int lineStart = region[..at].LastIndexOf((byte)'\n') + 1;
                int nl = region[at..].IndexOf((byte)'\n');
                int lineEnd = nl < 0 ? region.Length : at + nl;

                var line = region[lineStart..lineEnd];
                if (line.Length > 0 && line[^1] == (byte)'\r') line = line[..^1];
                if (line.Length > MaxLineMatchBytes) line = line[..MaxLineMatchBytes];

                decoder!.Reset();
                int charCount = decoder.GetChars(line, chars, flush: true);
                if (regex!.IsMatch(chars.AsSpan(0, charCount)))
                {
                    if (AddHit(regionBase + lineStart)) return;
                }

                if (nl < 0) break;
                from = lineEnd + 1;
            }
        }

        bool AddHit(long lineOffset)
        {
            batch.Add(lineOffset);
            totalHits++;
            if (totalHits >= limit) { truncated = true; Flush(); return true; }
            return false;
        }
    }

    /// <summary>
    /// 正規表現の必須リテラルで候補行を探す係（使えないときは null＝全行を見る）。
    /// 大小無視・UTF-8 以外・リテラルが取れない式では使わない。
    /// </summary>
    public static LiteralFinder? CreatePrefilter(SearchOptions options, Encoding encoding)
    {
        if (!UseLiteralPrefilter || !options.UseRegex || options.IgnoreCase || !LiteralFinder.Supports(encoding))
            return null;
        return RegexLiterals.Extract(options.Pattern, ignoreCase: false) is { } literals
            ? new LiteralFinder(literals, encoding)
            : null;
    }

    /// <summary>from から次の '\n' の直後までファイル位置を進める（長大行の読み飛ばし）。</summary>
    private static async Task<long> SkipToNextLineAsync(IByteSource src, long from, long fileLength, CancellationToken ct)
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
                if (nl >= 0) return pos + nl + 1;
                pos += got;
            }
            return fileLength;
        }
        finally { ArrayPool<byte>.Shared.Return(buf); }
    }
}
