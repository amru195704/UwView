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
    /// <summary>
    /// 読み込み単位。<see cref="Cli.RawGrep"/> と<b>同じ値にしておく</b>——
    /// 改行の無い長大行の扱いがこの大きさで決まるので、違うと同じ検索でも結果が変わる
    /// （索引の有無で経路が分かれるため。ソースレビュー 2026-09-19 の指摘7）。
    /// </summary>
    private const int BufSize = 4 << 20;            // 4MB ブロック
    private const int MaxLineMatchBytes = 64 * 1024; // 長大行はこの範囲でマッチ判定（デコードが要る経路のみ）

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
    /// <param name="separator">行の区切り（文字コード・改行種別で決まる）。省略時は 1バイトの LF。
    /// 索引・表示と同じものを渡すこと（再レビュー 2026-09-19 の指摘A。UTF-16 や CR 単独で結果がずれていた）。</param>
    public static Task<SearchOutcome> SearchAsync(
        IByteSource src, int bomLength, Encoding encoding, SearchOptions options,
        Action<IReadOnlyList<long>>? hitBatches = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default,
        LineSeparator? separator = null)
        => Task.Run(() => SearchCore(src, bomLength, encoding, options, hitBatches, progress, ct, separator), ct);

    private static async Task<SearchOutcome> SearchCore(
        IByteSource src, int bomLength, Encoding encoding, SearchOptions options,
        Action<IReadOnlyList<long>>? hitBatches, IProgress<double>? progress, CancellationToken ct,
        LineSeparator? separator = null)
    {
        var sep = separator ?? new LineSeparator((byte)'\n', 1, 0);
        long fileLength = src.Length;
        long contentBytes = Math.Max(1, fileLength - bomLength);

        // バイト列のまま探せるのは、ASCII がそのまま現れて1対1に対応する文字コードだけ
        //（Shift-JIS 等は2バイト文字の後半に当たる。ソースレビュー 2026-09-19 の指摘5）
        bool bytePath = options is { UseRegex: false, IgnoreCase: false } && options.Pattern.Length > 0
                        && AsciiCaseFold.IsAsciiCompatible(encoding);
        byte[] needle = bytePath ? encoding.GetBytes(options.Pattern) : [];
        if (bytePath && needle.Length == 0) bytePath = false;

        // -i の素の文字列も、条件が合えばバイトのまま大小無視で探す（RawGrep と同じ選び方）。
        // デコード＋正規表現だと長大行を先頭 64KB までしか見られず、索引ありと索引なしで
        // 同じファイルの答えが変わっていた（再レビュー 2026-09-19 の指摘E）
        bool byteIcase = !bytePath
            && options is { UseRegex: false, IgnoreCase: true }
            && AsciiCaseFold.IsFoldable(options.Pattern)
            && AsciiCaseFold.IsAsciiCompatible(encoding);
        byte[] folded = byteIcase ? AsciiCaseFold.ToLowerBytes(options.Pattern) : [];
        int foldAt = 0;
        var foldAnchor = byteIcase ? AsciiCaseFold.Anchor(folded, out foldAt) : null;

        bool literal = bytePath || byteIcase;
        int literalLength = bytePath ? needle.Length : folded.Length;
        Regex? regex = literal ? null : BuildScanRegex(options);
        Decoder? decoder = literal ? null : encoding.GetDecoder();
        LiteralFinder? prefilter = CreatePrefilter(options, encoding);
        long limit = options.HitLimit;

        long totalHits = 0;
        bool truncated = false;
        var batch = new List<long>(256);

        // バッファは常に行頭から始まる（未完の末尾行を先頭へ繰り越す）
        // carry は最大 BufSize 弱 + 追加読み BufSize → 2 倍を確保
        byte[] buf = ArrayPool<byte>.Shared.Rent(BufSize * 2);
        char[] chars = literal ? [] : ArrayPool<char>.Shared.Rent(MaxLineMatchBytes + 16);
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
                int region = sep.LineStartBefore(span, filled);   // 最後の区切りの直後まで＝完結行の集まり
                if (region > 0) { }
                else if (isEof) region = filled;
                else if (filled >= BufSize)
                {
                    // 1ブロック内に改行が無い長大行: 次の '\n' まで読み飛ばす。
                    // 判定に使う範囲は、素の文字列なら<b>読んだぶん全部</b>（バイトを見るだけなので切る理由がない）、
                    // 正規表現・大小無視は1行をデコードする都合で先頭 64KB まで（同 指摘7）
                    long skipped;
                    if (literal && sep.UnitSize == 1)
                    {
                        // 素の文字列は行の最後まで探す（先頭の1回ぶんで諦めると、後ろの一致を見落とす。
                        // 再々レビュー 2026-09-19 の指摘6）。読みの境目をまたぐ一致も拾う
                        bool found = FindLiteral(buf.AsSpan(0, filled)) >= 0;
                        int overlap = Math.Min(filled, literalLength - 1);
                        var (contentEnd, foundRest) = await LongLine.ScanRestAsync(
                            src, bufBase + filled, fileLength, sep.Value,
                            found ? ReadOnlyMemory<byte>.Empty : buf.AsMemory(filled - overlap, overlap),
                            found ? null : FindLiteral, null, ct);
                        if (found || foundRest) AddHit(bufBase);
                        skipped = contentEnd < fileLength ? contentEnd + 1 : fileLength;
                    }
                    else
                    {
                        // 正規表現は1行をデコードする都合で先頭 64KB まで（同 指摘7）
                        ProcessRegion(buf.AsSpan(0, Math.Min(filled, MaxLineMatchBytes)), bufBase, oversized: true);
                        skipped = await SkipToNextLineAsync(src, bufBase + filled, fileLength, ct, sep);
                    }
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
            if (literal) ProcessBytes(region, regionBase);
            else ProcessLines(region, regionBase, oversized);
        }

        // literal 高速パス: バイト列 IndexOf。ヒットしたら行頭を逆走査→行末へスキップ（1行1件）
        void ProcessBytes(ReadOnlySpan<byte> region, long regionBase)
        {
            int searchFrom = 0;
            while (searchFrom < region.Length)
            {
                int rel = bytePath
                    ? region[searchFrom..].IndexOf(needle)
                    : AsciiCaseFold.IndexOf(region[searchFrom..], folded, foldAnchor!, foldAt);
                if (rel < 0) break;
                int hitAt = searchFrom + rel;

                int lineStart = sep.LineStartBefore(region, hitAt); // 見つからなければ 0
                if (AddHit(regionBase + lineStart)) return;

                int sepAt = sep.IndexOfSeparator(region, hitAt);
                if (sepAt < 0) break;
                searchFrom = sepAt + sep.UnitSize;
            }
        }

        // regex パス: 行単位にデコードして Span マッチ（長大行は先頭 MaxLineMatchBytes で判定）
        void ProcessLines(ReadOnlySpan<byte> region, long regionBase, bool oversized)
        {
            if (prefilter is not null) { ProcessCandidateLines(region, regionBase); return; }
            int lineStart = 0;
            while (lineStart < region.Length)
            {
                int sepAt = sep.IndexOfSeparator(region, lineStart);
                int lineEnd = sepAt < 0 ? region.Length : sepAt;

                var line = TrimCr(region[lineStart..lineEnd]);
                if (line.Length > MaxLineMatchBytes) line = line[..MaxLineMatchBytes];

                decoder!.Reset();
                int charCount = decoder.GetChars(line, chars, flush: true);
                if (regex!.IsMatch(chars.AsSpan(0, charCount)))
                {
                    if (AddHit(regionBase + lineStart)) return;
                }

                if (sepAt < 0) break;
                lineStart = lineEnd + sep.UnitSize;
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
                int lineStart = sep.LineStartBefore(region, at);
                int sepAt = sep.IndexOfSeparator(region, at);
                int lineEnd = sepAt < 0 ? region.Length : sepAt;

                var line = TrimCr(region[lineStart..lineEnd]);
                if (line.Length > MaxLineMatchBytes) line = line[..MaxLineMatchBytes];

                decoder!.Reset();
                int charCount = decoder.GetChars(line, chars, flush: true);
                if (regex!.IsMatch(chars.AsSpan(0, charCount)))
                {
                    if (AddHit(regionBase + lineStart)) return;
                }

                if (sepAt < 0) break;
                from = lineEnd + sep.UnitSize;
            }
        }

        /// <summary>CRLF 対策: 末尾の '\r' を1文字ぶん落とす（UTF-16 なら 0D 00 の2バイト）。</summary>
        ReadOnlySpan<byte> TrimCr(ReadOnlySpan<byte> line)
            => sep.EndsWithCarriageReturn(line) ? line[..^sep.UnitSize] : line;

        // 素の文字列を探す（大小を区別する／ASCII の大小を畳む）。長大行の残りを読むときにも使う
        int FindLiteral(ReadOnlySpan<byte> hay) => bytePath
            ? hay.IndexOf(needle)
            : AsciiCaseFold.IndexOf(hay, folded, foldAnchor!, foldAt);

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

    /// <summary>from から次の区切りの直後までファイル位置を進める（長大行の読み飛ばし）。</summary>
    private static async Task<long> SkipToNextLineAsync(IByteSource src, long from, long fileLength,
                                                       CancellationToken ct, LineSeparator sep)
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
                int nl = sep.IndexOfSeparator(buf.AsSpan(0, got), 0);
                if (nl >= 0) return pos + nl + sep.UnitSize;
                pos += got;
            }
            return fileLength;
        }
        finally { ArrayPool<byte>.Shared.Return(buf); }
    }
}
