using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;

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
/// ここでは通しの1回読みの最中に「改行を数える（＝行番号）」「一致を判定する」「その行の本文をそのまま渡す」
/// を同時に行う。読むのは <see cref="SequentialFileByteSource"/>（pread）なので媒体の帯域に近い速度が出る。
///
/// 判定の規則は <see cref="SearchService"/> に合わせてある
/// （1行1件・改行なしの長大行は先頭 64KB で判定・正規表現は必須リテラルで候補行を絞る）。
/// 行番号は「その行より前にある '\n' の数」で数える。<see cref="SparseLineIndex"/> も改行スタイルに
/// 関わらず '\n' だけを数えているので、索引を作ったときと同じ番号になる。
/// </summary>
public static class RawGrep
{
    private const int BufSize = 4 << 20;              // 4MB ブロック（1MB より 1 割ほど速い）
    private const int MaxLineMatchBytes = 64 * 1024; // 長大行はこの範囲でマッチ判定（SearchService と同じ）

    /// <summary>
    /// 検索と索引作成を1回読みにまとめてよい条件か。
    /// <c>-v</c>（当てはまらない行）は画面の検索に無いので対象外。
    /// </summary>
    /// <param name="separator">行の区切り。<b>1バイトの LF のときだけ</b>この経路に乗せる。
    /// ここの走査は 0x0A を1バイトで数えるので、UTF-16 や CR 単独では行も索引も誤る
    /// （再レビュー 2026-09-19 の指摘A。それ以外は SearchService＋通常の索引作成へ回す）。</param>
    public static bool CanCombineWithIndex(SearchOptions options, LineSeparator separator)
        => options.Pattern.Length > 0 && separator.IsSingleByteLf;

    /// <summary>ヒット行を受け取る係（行は 0 始まり・末尾の改行は取り除いてある）。</summary>
    /// <param name="lineStart">その行の行頭バイト位置（-open で画面へ渡すときに使う）。</param>
    public delegate void LineSink(long lineIndex, long lineStart, ReadOnlySpan<byte> line);

    /// <param name="invert">当てはまら<b>ない</b>行を出す（grep -v / uvf -v）。</param>
    /// <param name="index">
    /// これを渡すと、検索のついでに<b>索引の目印も集める</b>（N 行ごとの行頭位置）。
    /// どのみち全部読んでいるので、画面がもう一度読み直さずに済む
    /// （オーナー指示 2026-09-18「-open が最後にある場合、検索時に index も同時に作成する」）。
    /// </param>
    /// <param name="observer">
    /// 読んだバイトを<b>順番どおり・隙間なく</b>受け取る係（Pro の CLI が .uwvz を同時に作るのに使う）。
    /// 一度渡したところをもう一度渡すことはある（受け手が重なりを捨てる約束。<c>SidecarAppender</c> と同じ規約）。
    /// </param>
    public static async Task<RawGrepOutcome> RunAsync(
        IByteSource src, int bomLength, Encoding encoding, SearchOptions options, bool invert,
        LineSink sink, CancellationToken ct = default, IndexMarks? index = null,
        LongLine.Observer? observer = null)
    {
        // 判定の道具立て（素の文字列は SearchService と同じ選び方）
        // バイト列のまま探せる文字コードに限る（同 指摘5）
        bool bytePath = options is { UseRegex: false, IgnoreCase: false } && options.Pattern.Length > 0
                        && AsciiCaseFold.IsAsciiCompatible(encoding);
        byte[] needle = bytePath ? encoding.GetBytes(options.Pattern) : [];
        if (bytePath && needle.Length == 0) bytePath = false;

        // -i の素の文字列は、条件が合えばバイトのまま大小無視で探す（デコードも正規表現も要らない）
        bool byteIcase = !bytePath
            && options is { UseRegex: false, IgnoreCase: true }
            && AsciiCaseFold.IsFoldable(options.Pattern)
            && AsciiCaseFold.IsAsciiCompatible(encoding);
        byte[] folded = byteIcase ? AsciiCaseFold.ToLowerBytes(options.Pattern) : [];
        int foldAt = 0;
        var foldAnchor = byteIcase ? AsciiCaseFold.Anchor(folded, out foldAt) : null;

        bool literal = bytePath || byteIcase;
        Regex? regex = literal ? null : BuildRegex(options);
        Decoder? decoder = literal ? null : encoding.GetDecoder();
        LiteralFinder? prefilter = SearchService.CreatePrefilter(options, encoding);

        // -i（正規表現でも素の文字列でも）の手がかり。`RegexLiterals` は大小無視だと必須リテラルを
        // 出さない（`LiteralFinder` がバイト一致でしか探せないため）ので、こちらで用意する。
        byte[] icaseClue = prefilter is null && !literal ? IcaseClue(options, encoding) : [];
        int clueAt = 0;
        var clueAnchor = icaseClue.Length > 0 ? AsciiCaseFold.Anchor(icaseClue, out clueAt) : null;

        bool hasClue = prefilter is not null || icaseClue.Length > 0;

        long fileLength = src.Length;
        long limit = options.HitLimit;
        long hits = 0;
        bool truncated = false;

        // 行が途中で切れたぶんを先頭へ繰り越すので、バッファは 2 倍取る（SearchService と同じ作り）
        byte[] buf = ArrayPool<byte>.Shared.Rent(BufSize * 2);
        char[] chars = literal ? [] : ArrayPool<char>.Shared.Rent(MaxLineMatchBytes + 16);
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
                if (got > 0) observer?.Invoke(pos, buf.AsSpan(carry, got));   // 読んだそばから渡す
                pos += got;
                int filled = carry + got;
                if (filled == 0) break;
                // 長さが先に分からない入力（gz を展開しながら読む）は、読めなくなったところで終わり
                bool isEof = pos >= fileLength || (want > 0 && got == 0);

                var span = buf.AsSpan(0, filled);
                int lastNl = span.LastIndexOf((byte)'\n');
                int region;
                if (lastNl >= 0) region = lastNl + 1;
                else if (isEof) region = filled;
                else if (filled >= BufSize)
                {
                    // 1 ブロック内に改行が無い長大行: 次の改行まで読み飛ばす。
                    // 判定に使う範囲は SearchService と同じ規則（素の文字列は全部・正規表現は先頭 64KB）
                    bool hit;
                    long end;
                    if (literal)
                    {
                        // 素の文字列は行の最後まで探す（先頭の1回ぶんで諦めると、後ろの一致を見落とす。
                        // 再々レビュー 2026-09-19 の指摘6）。読みの境目をまたぐ一致も拾う
                        bool found = FindFrom(span[..filled]) >= 0;
                        int overlap = Math.Min(filled, (bytePath ? needle.Length : folded.Length) - 1);
                        var (contentEnd, foundRest) = await LongLine.ScanRestAsync(
                            src, bufBase + filled, fileLength, (byte)'\n',
                            found ? ReadOnlyMemory<byte>.Empty : buf.AsMemory(filled - overlap, overlap),
                            found ? null : FindFrom, observer, ct);
                        hit = (found || foundRest) != invert;
                        end = contentEnd;
                    }
                    else
                    {
                        // 正規表現は1行をデコードする都合で先頭 64KB まで
                        hit = Matches(span[..Math.Min(filled, MaxLineMatchBytes)]) != invert;
                        end = await LineEndAsync(src, bufBase + filled, fileLength, ct, observer);
                    }
                    if (hit)
                    {
                        await EmitLongLineAsync(src, bufBase, end, lineNo, sink, ct);   // 行頭＝bufBase
                        if (++hits >= limit) { truncated = true; break; }
                    }
                    bufBase = pos = end + 1;
                    index?.Skip(lineNo, end + 1);   // 読み飛ばした長大行の次の行頭
                    lineNo++;                       // 読み飛ばした長大行ぶん
                    carry = 0;
                    if (pos >= fileLength) break;
                    continue;
                }
                else { carry = filled; continue; }  // もう少し読めば行が完結する

                index?.Take(span[..region], bufBase, lineNo);
                lineNo = Scan(span[..region], bufBase, lineNo);
                if (truncated) break;

                carry = filled - region;
                if (carry > 0) span[region..].CopyTo(buf);
                bufBase += region;

                if (isEof && carry == 0) break;
                if (isEof && carry > 0)
                {
                    index?.Take(buf.AsSpan(0, carry), bufBase, lineNo);
                    lineNo = Scan(buf.AsSpan(0, carry), bufBase, lineNo);  // 末尾に改行がない最終行
                    break;
                }
            }

            index?.Finish(fileLength, lineNo);
            return new RawGrepOutcome(hits, truncated);

            // region は行頭から始まる完結行の集まり。改行を数えながら出すべき行を出し、次の行番号を返す
            //
            // 「手がかり」（素の文字列そのもの／正規表現の必須リテラル）がある限り、そこへ飛びながら見る。
            // 手がかりの無い区間は改行をまとめて数えるだけで済み、行の切り出しもデコードもしない。
            // -v は全行を出す判断が要るので、1行ずつ見る道（ScanLines）へ回す。
            long Scan(ReadOnlySpan<byte> region, long regionBase, long firstLine)
                => !invert && (literal || hasClue)
                    ? ScanByClue(region, regionBase, firstLine)
                    : ScanLines(region, regionBase, firstLine);

            long ScanByClue(ReadOnlySpan<byte> region, long regionBase, long firstLine)
            {
                prefilter?.Reset();
                int cursor = 0;            // まだ見ていない範囲の先頭（必ず行頭）
                long cursorLine = firstLine;

                while (cursor < region.Length)
                {
                    int clueAt = FindClue(region, cursor);
                    if (clueAt < 0) break;

                    // cursor から手がかりの位置までの改行を数えて、その行の先頭を求める。
                    // 1本ずつ IndexOf で進むのではなく、まとめて数える（SIMD が効いて 5 倍速い）
                    var before = region[cursor..clueAt];
                    long line = cursorLine + before.Count((byte)'\n');
                    int lastNlBefore = before.LastIndexOf((byte)'\n');
                    int lineStart = lastNlBefore < 0 ? cursor : cursor + lastNlBefore + 1;

                    int nlAfter = region[clueAt..].IndexOf((byte)'\n');
                    int lineEnd = nlAfter < 0 ? region.Length : clueAt + nlAfter;

                    var text = region[lineStart..lineEnd];
                    if (text.Length > 0 && text[^1] == (byte)'\r') text = text[..^1];

                    // 素の文字列なら手がかり＝一致そのもの。正規表現はこの行に当ててみる
                    if (literal || Matches(text))
                    {
                        Emit(line, regionBase + lineStart, text, stripped: true);
                        if (truncated) return line + 1;
                    }

                    if (nlAfter < 0) { cursor = region.Length; cursorLine = line + 1; break; }
                    cursor = lineEnd + 1;
                    cursorLine = line + 1;
                }

                return cursorLine + CountNewlines(region[cursor..]);
            }

            // cursor 以降で次の手がかりの位置（無ければ -1）
            int FindClue(ReadOnlySpan<byte> region, int cursor)
            {
                if (literal)
                {
                    int rel = FindFrom(region[cursor..]);
                    return rel < 0 ? -1 : cursor + rel;
                }
                if (prefilter is not null) return prefilter.IndexOf(region, cursor);
                if (icaseClue.Length > 0)
                {
                    int rel = AsciiCaseFold.IndexOf(region[cursor..], icaseClue, clueAnchor!, clueAt);
                    return rel < 0 ? -1 : cursor + rel;
                }
                return -1;
            }

            // -v・手がかりの無い正規表現: 1行ずつ見る
            long ScanLines(ReadOnlySpan<byte> region, long regionBase, long firstLine)
            {
                prefilter?.Reset();
                int candidate = prefilter is null ? -1 : prefilter.IndexOf(region, 0);

                long line = firstLine;
                int start = 0;
                while (start < region.Length)
                {
                    int nl = region[start..].IndexOf((byte)'\n');
                    int end = nl < 0 ? region.Length : start + nl;

                    bool maybe = true;
                    if (prefilter is not null)
                    {
                        while (candidate >= 0 && candidate < start)
                            candidate = prefilter.IndexOf(region, candidate + 1);
                        maybe = candidate >= 0 && candidate < end;
                    }

                    var text = region[start..end];
                    if (text.Length > 0 && text[^1] == (byte)'\r') text = text[..^1];

                    if ((maybe && Matches(text)) != invert)
                    {
                        Emit(line, regionBase + start, text, stripped: true);
                        if (truncated) return line + 1;
                    }

                    line++;
                    if (nl < 0) break;
                    start = end + 1;
                }

                return line;
            }

            void Emit(long line, long lineStart, ReadOnlySpan<byte> text, bool stripped = false)
            {
                if (!stripped && text.Length > 0 && text[^1] == (byte)'\r') text = text[..^1];
                sink(line, lineStart, text);
                if (++hits >= limit) truncated = true;
            }

            // 1行が当てはまるか（長大行は先頭 64KB だけで見る＝SearchService と同じ約束）
            bool Matches(ReadOnlySpan<byte> line)
            {
                // 素の文字列はバイトを見るだけなので切らない。デコードが要る経路だけ先頭 64KB に切る
                if (literal) return FindFrom(line) >= 0;
                var probe = line.Length > MaxLineMatchBytes ? line[..MaxLineMatchBytes] : line;

                // 前チェック: 当たるなら必ず入っている手がかりが無ければ、デコードせずに外す
                // （-v はここを通る。手がかりで飛ぶ道が使えないため）
                if (icaseClue.Length > 0 && AsciiCaseFold.IndexOf(probe, icaseClue, clueAnchor!, clueAt) < 0)
                    return false;

                decoder!.Reset();
                int n = decoder.GetChars(probe, chars, flush: true);
                return regex!.IsMatch(chars.AsSpan(0, n));
            }

            // 素の文字列（-i も）を探す。見つからなければ -1
            int FindFrom(ReadOnlySpan<byte> hay)
                => bytePath ? hay.IndexOf(needle) : AsciiCaseFold.IndexOf(hay, folded, foldAnchor!, foldAt);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
            if (chars.Length > 0) ArrayPool<char>.Shared.Return(chars);
        }
    }

    /// <summary>
    /// <c>-E -i</c> の手がかり（当たる行には必ず入っている文字列を、ASCII の大小を畳んだ小文字で返す）。
    ///
    /// <see cref="RegexLiterals.Extract"/> は大小無視のとき null を返す（<see cref="LiteralFinder"/> が
    /// バイト一致しかできないため）。ここでは同じ抽出を大小を区別する形で1回行い、得られた必須リテラルを
    /// <see cref="AsciiCaseFold.IndexOf"/> で大小を畳んで探す。当たる行は必ずそのリテラルの大小どれかを含むので
    /// 取りこぼしは無い。ただし <c>k</c>/<c>K</c> を含むリテラルは U+212A とも一致しうるので使わない。
    /// 候補が複数（選択肢）のときは、どれが入るか決められないので手がかりにしない。
    /// </summary>
    private static byte[] IcaseClue(SearchOptions options, Encoding encoding)
    {
        if (options is not { IgnoreCase: true } || !AsciiCaseFold.IsAsciiCompatible(encoding)) return [];

        // 当たる行に必ず入っている文字列。素の文字列ならそれ自身、正規表現なら必須リテラル
        string? required = options.UseRegex
            ? RegexLiterals.Extract(options.Pattern, ignoreCase: false) is [string only] ? only : null
            : options.Pattern;
        if (required is null) return [];

        string run = AsciiCaseFold.LongestFoldableRun(required);
        return run.Length == 0 ? [] : AsciiCaseFold.ToLowerBytes(run);
    }


    /// <summary>全行に当てる正規表現（<see cref="SearchService"/> の全文検索と同じ作り方）。</summary>
    private static Regex BuildRegex(SearchOptions options)
    {
        var regex = SearchService.BuildRegex(options);
        return OperatingSystem.IsBrowser()
            ? regex
            : new Regex(regex.ToString(), regex.Options | RegexOptions.Compiled, regex.MatchTimeout);
    }

    /// <summary>
    /// 改行の本数（行番号づけの土台）。<see cref="MemoryExtensions.Count{T}"/> は SIMD で数えるので、
    /// 1本ずつ IndexOf で進むより 5 倍以上速い（1GB・改行3,350万本で 0.302秒 → 0.055秒）。
    /// 索引を持たない uvf はここを全バイトに対して通るため、この差がそのまま検索時間に出る。
    /// </summary>
    private static long CountNewlines(ReadOnlySpan<byte> span) => span.Count((byte)'\n');

    /// <summary>from 以降で最初の '\n' の位置（無ければファイル末尾）。</summary>
    private static async Task<long> LineEndAsync(IByteSource src, long from, long fileLength, CancellationToken ct,
                                                 LongLine.Observer? observer = null)
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
                observer?.Invoke(pos, buf.AsSpan(0, got));
                int nl = buf.AsSpan(0, got).IndexOf((byte)'\n');
                if (nl >= 0) return pos + nl;
                pos += got;
            }
            return pos;
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
        sink(lineIndex, start, text);
    }

    /// <summary>
    /// 検索のついでに集める索引の目印（<paramref name="blockLines"/> 行ごとの行頭位置）。
    /// 数え方は <see cref="SparseLineIndex"/> と同じ——'\n' を数え、その本数が区切りに達した位置を控える。
    /// </summary>
    public sealed class IndexMarks(int blockLines)
    {
        private readonly List<long> _marks = [];

        /// <summary>控えた位置（先頭の BOM 位置は含まない。索引側が入れる）。</summary>
        public IReadOnlyList<long> Marks => _marks;

        /// <summary>ファイル全体の '\n' の数。</summary>
        public long NewlineCount { get; private set; }

        /// <summary>ファイルの最後のバイト（末尾に改行があるかの判定に使う）。</summary>
        public byte LastByte { get; private set; }

        internal void Take(ReadOnlySpan<byte> region, long regionBase, long firstLine)
        {
            long line = firstLine;
            for (int i = 0; i < region.Length; )
            {
                int nl = region[i..].IndexOf((byte)'\n');
                if (nl < 0) break;
                i += nl + 1;
                line++;
                NewlineCount++;
                if (line % blockLines == 0) _marks.Add(regionBase + i);
            }
            if (region.Length > 0) LastByte = region[^1];
        }

        /// <summary>バッファに収まらない長大行を読み飛ばしたとき（その行の改行1本を数える）。</summary>
        internal void Skip(long lineOfSkipped, long nextLineStart)
        {
            NewlineCount++;
            LastByte = (byte)'\n';
            if ((lineOfSkipped + 1) % blockLines == 0) _marks.Add(nextLineStart);
        }

        internal void Finish(long fileLength, long totalLines) { FileLength = fileLength; TotalLines = totalLines; }

        public long FileLength { get; private set; }
        public long TotalLines { get; private set; }
    }
}
