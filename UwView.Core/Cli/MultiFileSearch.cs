using System.Text;

namespace UwView.Core.Cli;

/// <summary>
/// 複数ファイルの検索（Wide Field v1.7.0 段階3・指示書 §2.1〜§2.3）。
///
/// 守ること:
/// <list type="bullet">
///   <item><b>出力の並びは指定順。</b>同時に走らせても変えない（テストが <c>cmp</c> で突き合わせている）</item>
///   <item><b>単一ファイルの出力は1バイトも変えない。</b>そちらは従来の経路をそのまま通す（呼び出し側で分岐）</item>
///   <item>1つ読めなくても止めない。読めたものは出し、最後に知らせて exit 2</item>
/// </list>
///
/// 並びを保ちながら同時に走らせるため、各ファイルの出力はいったん手元に貯め、
/// 自分の順番が来たら吐く。貯めすぎないよう、一定量を超えたら順番待ちに入り、
/// 順番が来てからは直接書く（<c>-v</c> で巨大な出力になっても、メモリを食い尽くさない）。
/// </summary>
public static class MultiFileSearch
{
    /// <summary>手元に貯める上限。これを超えたら自分の順番を待って直接書く（預かる合計もこれまで）。</summary>
    private static int BufferLimit => BufferLimitForTests ?? 64 << 20;

    /// <summary>テスト用: 貯める上限を小さくして、直接書く道と、預かりすぎて待つ道を通す。</summary>
    public static int? BufferLimitForTests;

    /// <summary>
    /// テスト用: ファイル i が走り始める前に待たせる（到着順を入れ替えて、順番待ちの噛み合いを試す）。
    /// </summary>
    public static Func<int, Task>? ArrivalDelayForTests;

    /// <summary>
    /// 計測用（<c>UV_TRACE=1</c>）：スレッドをまたいで足した時間（Stopwatch の刻み）。実装指示書 2026-10-03
    /// 「多数ファイル検索の hot」§3.1。数えないときは分岐だけで、速さに響かない。
    /// </summary>
    private sealed class Trace
    {
        public long Open, Detect, Search, Wait, Files;
        public static long Now => System.Diagnostics.Stopwatch.GetTimestamp();
        public void Report(int threads, long allocatedBefore)
        {
            static double S(long ticks) => ticks / (double)System.Diagnostics.Stopwatch.Frequency;
            double total = (DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalSeconds;
            Console.Error.WriteLine(
                $"uv_trace: multi files={Files} threads={threads} t_open={S(Open):F3} t_detect={S(Detect):F3} "
                + $"t_search={S(Search):F3} t_wait={S(Wait):F3} t_total={total:F3} "
                + $"alloc_mb={(GC.GetTotalAllocatedBytes() - allocatedBefore) / 1048576.0:F0} gc0={GC.CollectionCount(0)} gc2={GC.CollectionCount(2)}");
        }
    }

    /// <param name="Hits">全ファイル合計のヒット行数。</param>
    /// <param name="Truncated">上限で打ち切ったファイルがあったか。</param>
    /// <param name="Failed">読めなかったファイルと理由。</param>
    /// <param name="Skipped">
    /// 開いてみたら平文でなかったので探さなかったもの（<c>verifyPlain</c> のときだけ。並びは指定順）。
    /// Pbf なら pbf、<c>Office</c> が Word・Excel・PDF なら UwView Pro の役目（v1.8.3）、そうでなければ <c>Probe</c> が zip か断る理由を持つ。
    /// </param>
    public readonly record struct Result(long Hits, bool Truncated, IReadOnlyList<(string File, string Reason)> Failed,
                                         IReadOnlyList<(string File, bool Pbf, CompressedProbe Probe, OfficeProbe Office)>? Skipped = null);

    /// <summary>
    /// これ以下のファイルは、作業役ごとの使い回しの領域に 1 回で読み、文字コードの判定・中身の確かめ・検索を
    /// すべてそこから行う（文字コードの判定は先頭 256KB を毎回新しい配列に読んでいて、平均 22KB の
    /// カーネルのファイルでは 2 回読みと 8.6 万回の確保になっていた。2026-10-03）。
    /// 文字コードの判定が読む量（256KB）と同じにする。それより大きいファイルは、手元に読むと検索の領域へ
    /// もう 1 回写すことになるので、従来どおり検索の領域へ直接読む（1MB のファイル 1,000 本で遅くなった）。
    /// </summary>
    private const int SmallFileBytes = 256 << 10;

    /// <param name="withFileName">各行の先頭にファイル名を付ける（grep 互換）。</param>
    /// <param name="threads">同時に走らせる本数（<see cref="ThreadBudget"/> で決めた値）。</param>
    public static async Task<Result> RunAsync(
        IReadOnlyList<string> files, SearchOptions options, bool invert, bool json, bool lineNumbers,
        bool withFileName, int threads, TextWriter output, CancellationToken ct,
        IReadOnlyList<CompressedKind>? kinds = null, bool verifyPlain = false,
        IReadOnlyList<OfficeKind>? offices = null, bool officeRaw = false)
    {
        // offices: Word・Excel・PDF は取り出した文字を探す（v1.8.3.2。下調べで分かったもの。開いたときに分かったものも同じ）
        // verifyPlain: kinds で平文としたものを、開いたときに先頭で確かめる（下調べで全部を開き直さないため。
        // 開くこと自体が重く、カーネル 8.6 万本を1回開くだけで約 2 秒かかる。2026-10-02）。
        // 中身が圧縮ならその場で展開して探し（並びは変えない）、pbf・zip・断るものは探さずに Skipped へ
        var skipped = new (bool Pbf, CompressedProbe Probe, OfficeProbe Office)?[files.Count];
        var trace = Environment.GetEnvironmentVariable("UV_TRACE") == "1" ? new Trace() : null;
        long allocatedBefore = trace is null ? 0 : GC.GetTotalAllocatedBytes();
        if (trace is not null) RawGrep.QuietTrace = true;

        // xz の並列展開に回せる本数は、xz の部品の数で割る。ほかの形式は1スレッドで展開するので数に入れない。
        // ファイル数で割ると、gz＋xz＋zst＋gz では xz が 2 本しかもらえず、ほかが先に終わったあとも
        // xz だけが 2 本で走り続けて全体を待たせた（管理部の混在テスト 2026-09-24: rg -z の 1/1.85）
        // 呼び手が下調べ済みなら、その結果を使う（1本ずつ開き直すと 8.6 万本で約 7 秒。2026-10-02）
        kinds ??= files.Select(f => CompressedInput.Probe(f).Kind).ToArray();
        int decodeThreads = DecodeThreadsFor(kinds, threads);

        // 決まった本数の作業役が、番号を前から順に取って探す（ファイルごとに Task を作らない）。
        // 探し終えたら結果を預けてすぐ次へ進み、出力は番号順にそろったところから吐く。
        // 以前はファイルごとに Task と順番待ちを作り、前のファイルの出力を待つ間も枠を持ったままだったので、
        // カーネル 8.6 万本で rg の 2.2 倍かかっていた（実装指示書 2026-10-03「多数ファイル検索の hot」）
        // 開くときは絶対パスにする（相対パスのままだと、.NET が開くたびに今いるフォルダーを OS に問い合わせて
        // 作り直す。カーネル 8.6 万本を 8 並列で開いて読むだけで 2.33 秒 → 1.59 秒。2026-10-03）。出力の名前は書いたとおり
        string root = Directory.GetCurrentDirectory();
        // 式の準備（正規表現のコンパイル・手がかり）は 1 回だけ。全ファイル・全作業役で使い回す
        var prepared = PreparedSearch.Create(options);
        var order = new OrderedOutput(output, files.Count);
        var results = new (long Hits, bool Truncated, string? Reason)[files.Count];
        int taken = -1;
        // 画面のダイアログから走らせたときだけ、進み具合を数える（コマンドでは null）
        var progress = CommandProgress.Current;
        long[]? sizes = progress is null ? null : files.Select(f => CommandProgress.SizeOf(Path.Join(root, f))).ToArray();
        if (sizes is not null) progress!.Plan(files.Count, sizes.Sum());
        int workers = Math.Max(1, Math.Min(threads, files.Count));
        var tasks = new Task[workers];
        for (int w = 0; w < workers; w++)
        {
            tasks[w] = Task.Run(async () =>
            {
                byte[] small = new byte[SmallFileBytes];   // 作業役ごとに使い回す
                while (true)
                {
                    int index = Interlocked.Increment(ref taken);
                    if (index >= files.Count) break;
                    ct.ThrowIfCancellationRequested();
                    if (ArrivalDelayForTests is { } delay) await delay(index);
                    long w0 = trace is null ? 0 : Trace.Now;
                    await order.WaitForRoomAsync(index, ct);    // 預けた出力が多すぎるときは、吐けるまで待つ
                    if (trace is not null) Interlocked.Add(ref trace.Wait, Trace.Now - w0);
                    string file = files[index];
                    string path = Path.IsPathRooted(file) ? file : Path.Join(root, file);
                    results[index] = await OneFileAsync(path, kinds[index], prepared, invert, json, lineNumbers,
                                                        withFileName ? file : null, index, order, ct, decodeThreads,
                                                        small, verifyPlain ? skipped : null, trace,
                                                        offices?[index] ?? OfficeKind.None, officeRaw);
                    if (sizes is not null) progress!.FileDone(sizes[index]);
                }
            }, ct);
        }

        await Task.WhenAll(tasks);
        await output.FlushAsync(ct);
        if (trace is not null) { trace.Files = files.Count; trace.Report(workers, allocatedBefore); RawGrep.QuietTrace = false; }

        long hits = 0;
        bool truncated = false;
        var failed = new List<(string, string)>();
        var notSearched = new List<(string File, bool Pbf, CompressedProbe Probe, OfficeProbe Office)>();
        for (int i = 0; i < files.Count; i++)
        {
            hits += results[i].Hits;
            truncated |= results[i].Truncated;
            if (results[i].Reason is { } reason) failed.Add((files[i], reason));
            if (skipped[i] is { } s) notSearched.Add((files[i], s.Pbf, s.Probe, s.Office));
        }
        return new Result(hits, truncated, failed, notSearched);
    }

    /// <summary>xz の部品1本あたりの展開スレッド数（全体の本数を、xz の部品の数で割る）。</summary>
    public static int DecodeThreadsFor(IReadOnlyList<CompressedKind> kinds, int threads)
        => CompressedFormats.DecodeThreadsFor(kinds, threads);

    /// <summary>
    /// 出力を番号順に吐く係。終わったファイルの出力を預かり、次に吐く番号の分がそろったら続けて吐く。
    /// 書くのは常に「今の番号」の分だけなので、出力は指定順のまま、混ざらない。
    /// 預かる量が <see cref="BufferLimit"/> を超えたら、後ろのファイルを始めるのを待たせる（メモリを食い尽くさない）。
    /// </summary>
    private sealed class OrderedOutput(TextWriter output, int count)
    {
        private readonly object _gate = new();
        private readonly string?[] _done = new string?[count];
        private int _next;              // 次に吐く番号
        private long _heldChars;        // 預かっている出力の合計（文字数）
        private TaskCompletionSource _progress = NewSignal();

        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>出力先。今の番号のファイルの持ち主だけが、自分の番の間に直接書いてよい（ほかは預けるだけ）。</summary>
        public TextWriter Output => output;

        /// <summary>自分の番が来るまで待つ（出力が上限を超えて、直接書きたいとき）。</summary>
        public async Task WaitTurnAsync(int index, CancellationToken ct)
        {
            while (true)
            {
                Task signal;
                lock (_gate)
                {
                    if (_next == index) return;
                    signal = _progress.Task;
                }
                await signal.WaitAsync(ct);
            }
        }

        /// <summary>預けた出力が多すぎるときは、今の番号の分が吐かれて減るまで待つ（今の番号は待たない）。</summary>
        public async Task WaitForRoomAsync(int index, CancellationToken ct)
        {
            while (true)
            {
                Task signal;
                lock (_gate)
                {
                    if (index == _next || _heldChars < BufferLimit) return;
                    signal = _progress.Task;
                }
                await signal.WaitAsync(ct);
            }
        }

        /// <summary>そのファイルが終わった。今の番号なら吐いて、続けてそろっている分も吐く。</summary>
        public void Complete(int index, string text)
        {
            TaskCompletionSource? signal = null;
            lock (_gate)
            {
                if (index != _next)
                {
                    _done[index] = text;
                    _heldChars += text.Length;
                    return;
                }
                if (text.Length > 0) output.Write(text);
                _next++;
                while (_next < _done.Length && _done[_next] is { } held)
                {
                    if (held.Length > 0) output.Write(held);
                    _heldChars -= held.Length;
                    _done[_next] = null;
                    _next++;
                }
                signal = _progress;
                _progress = NewSignal();
            }
            signal.TrySetResult();
        }
    }

    private static async Task<(long Hits, bool Truncated, string? Reason)> OneFileAsync(
        string file, CompressedKind kind, PreparedSearch prepared, bool invert, bool json, bool lineNumbers, string? name,
        int index, OrderedOutput order, CancellationToken ct, int decodeThreads, byte[] small,
        (bool Pbf, CompressedProbe Probe, OfficeProbe Office)?[]? skipped = null, Trace? trace = null,
        OfficeKind office = OfficeKind.None, bool officeRaw = false)
    {
        var buffer = new StringWriter { NewLine = "\n" };
        TextWriter writer = buffer;   // 自分の番が来るまでは手元に貯める
        bool direct = false;
        long hits = 0;
        bool truncated = false;
        string? reason = null;

        // 貯めすぎたら自分の番を待ち、以後は直接書く（-v などで巨大な出力になってもメモリを食わない）
        void GoDirect()
        {
            long w0 = trace is null ? 0 : Trace.Now;
            order.WaitTurnAsync(index, ct).GetAwaiter().GetResult();
            if (trace is not null) Interlocked.Add(ref trace.Wait, Trace.Now - w0);
            writer = order.Output;
            writer.Write(buffer.GetStringBuilder().ToString());
            buffer.GetStringBuilder().Clear();
            direct = true;
        }

        try
        {
            // 圧縮（gz・bz2・xz・lzma・zstd）は展開しながら探す（1ファイルのときと同じ読み口。
            // 切れていれば読み終えたところで気づく）
            long t0 = trace is null ? 0 : Trace.Now;
            IByteSource source = office != OfficeKind.None
                ? new CompressedStreamByteSource(file, office, officeRaw)
                : CompressedFormats.IsSingleStream(kind)
                ? new CompressedStreamByteSource(file, kind, decodeThreads)
                : new SequentialFileByteSource(file);
            // 小さい平文は 1 回で全部読み、以後は手元の領域から（開いたファイルはすぐ閉じる）
            int inMemory = -1;
            if (kind == CompressedKind.None && source is SequentialFileByteSource && source.Length <= small.Length)
            {
                int length = (int)source.Length, got = 0, n;
                while (got < length && (n = source.Read(got, small.AsSpan(got, length - got))) > 0) got += n;
                await source.DisposeAsync();
                inMemory = got;
                source = new BufferByteSource(small, got);
            }
            if (trace is not null) Interlocked.Add(ref trace.Open, Trace.Now - t0);
            if (skipped is not null && kind == CompressedKind.None && office == OfficeKind.None)
            {
                Span<byte> head = stackalloc byte[OsmPbfFile.HeadBytes];
                int got = source.Read(0, head);
                bool pbf = OsmPbfFile.IsHead(head[..got]);
                // Word・Excel・PDF（名前で分からなかったもの。v1.8.3）。PDF は先頭が %PDF-、.docx・.xlsx は中身が zip、古い形式は OLE
                bool maybeOffice = head[..got].StartsWith("%PDF-"u8) || head[..got].StartsWith("PK\u0003\u0004"u8)
                                   || head[..got].StartsWith((ReadOnlySpan<byte>)[0xD0, 0xCF, 0x11, 0xE0]);
                if (pbf || maybeOffice || CompressedInput.PlainFromHead(file, head[..got]) is null)
                {
                    await source.DisposeAsync();
                    inMemory = -1;
                    var found = pbf || !maybeOffice ? OfficeProbe.NotOffice : OfficeDocumentFile.Probe(file);
                    var probe = pbf || found.IsOffice ? CompressedProbe.Plain : CompressedInput.Probe(file);
                    if (found.IsDocument)
                        source = new CompressedStreamByteSource(file, found.Kind, officeRaw);   // 名前で分からなかった Word・Excel・PDF
                    else if (pbf || found.IsRejected || probe.Kind == CompressedKind.Zip || probe.IsRejected)
                    {
                        skipped[index] = (pbf, probe, found);
                        source = new EmptyByteSource();     // 探さない（空として順番だけ通す）
                    }
                    else source = CompressedFormats.IsSingleStream(probe.Kind)
                        ? new CompressedStreamByteSource(file, probe.Kind, decodeThreads)
                        : new SequentialFileByteSource(file);
                }
            }
            await using var _ = source;
            long t1 = trace is null ? 0 : Trace.Now;
            // 判定に読む先頭も、作業役の領域を使う（毎回 256KB の配列を確保しない）
            int sampled = inMemory >= 0 ? inMemory
                        : source is SequentialFileByteSource ? source.Read(0, small.AsSpan(0, (int)Math.Min(small.Length, source.Length))) : -1;
            var detected = sampled >= 0 ? EncodingDetector.Detect(small.AsSpan(0, sampled)) : EncodingDetector.Detect(source);
            if (trace is not null) Interlocked.Add(ref trace.Detect, Trace.Now - t1);
            var encoding = detected.Encoding;

            long t2 = trace is null ? 0 : Trace.Now;
            var lines = new HitLineWriter();
            var outcome = await RawGrep.RunAsync(
                source, detected.BomLength, encoding, prepared.Options, invert,
                (line, _, text) =>
                {
                    hits++;
                    if (json)
                    {
                        string body = encoding.GetString(text);
                        writer.WriteLine(lineNumbers ? JsonLines.Hit(name, line + 1, body) : JsonLines.HitWithoutNumber(name, body));
                    }
                    else lines.Write(writer, name, lineNumbers, line, text, encoding);
                    if (!direct && buffer.GetStringBuilder().Length >= BufferLimit) GoDirect();
                }, ct, prepared: prepared);
            if (trace is not null) Interlocked.Add(ref trace.Search, Trace.Now - t2);
            truncated = outcome.Truncated;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            reason = e.Message;   // 1つ読めなくても止めない（§2.3）
        }

        // 直接書いていたなら、残りはもう無い。どちらでも、終わったことを知らせて次の番号へ進める
        order.Complete(index, direct ? "" : buffer.GetStringBuilder().ToString());
        return (hits, truncated, reason);
    }

    /// <summary>手元の領域に読んだファイルの中身（小さいファイル用）。</summary>
    private sealed class BufferByteSource(byte[] data, int length) : IByteSource
    {
        public long Length => length;

        public int Read(long offset, Span<byte> destination)
        {
            if (offset >= length || destination.Length == 0) return 0;
            int n = (int)Math.Min(destination.Length, length - offset);
            data.AsSpan((int)offset, n).CopyTo(destination);
            return n;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>探さないと決めたファイルの代わり（開いたものは閉じてある）。</summary>
    private sealed class EmptyByteSource : IByteSource
    {
        public long Length => 0;
        public int Read(long offset, Span<byte> buffer) => 0;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

}
