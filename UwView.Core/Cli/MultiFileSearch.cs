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
    /// <summary>手元に貯める上限。これを超えたら自分の順番を待って直接書く。</summary>
    private const int BufferLimit = 64 << 20;

    /// <summary>
    /// テスト用: ファイル i が走り始める前に待たせる（到着順を入れ替えて、順番待ちの噛み合いを試す）。
    /// </summary>
    public static Func<int, Task>? ArrivalDelayForTests;

    /// <param name="Hits">全ファイル合計のヒット行数。</param>
    /// <param name="Truncated">上限で打ち切ったファイルがあったか。</param>
    /// <param name="Failed">読めなかったファイルと理由。</param>
    /// <param name="Skipped">
    /// 開いてみたら平文でなかったので探さなかったもの（<c>verifyPlain</c> のときだけ。並びは指定順）。
    /// Pbf なら pbf、そうでなければ <c>Probe</c> が zip か断る理由を持つ。
    /// </param>
    public readonly record struct Result(long Hits, bool Truncated, IReadOnlyList<(string File, string Reason)> Failed,
                                         IReadOnlyList<(string File, bool Pbf, CompressedProbe Probe)>? Skipped = null);

    /// <param name="withFileName">各行の先頭にファイル名を付ける（grep 互換）。</param>
    /// <param name="threads">同時に走らせる本数（<see cref="ThreadBudget"/> で決めた値）。</param>
    public static async Task<Result> RunAsync(
        IReadOnlyList<string> files, SearchOptions options, bool invert, bool json, bool lineNumbers,
        bool withFileName, int threads, TextWriter output, CancellationToken ct,
        IReadOnlyList<CompressedKind>? kinds = null, bool verifyPlain = false)
    {
        // verifyPlain: kinds で平文としたものを、開いたときに先頭で確かめる（下調べで全部を開き直さないため。
        // 開くこと自体が重く、カーネル 8.6 万本を1回開くだけで約 2 秒かかる。2026-10-02）。
        // 中身が圧縮ならその場で展開して探し（並びは変えない）、pbf・zip・断るものは探さずに Skipped へ
        var skipped = new (bool Pbf, CompressedProbe Probe)?[files.Count];
        long hits = 0;
        bool truncated = false;
        var failed = new List<(string, string)>();
        var failedLock = new object();

        // 順番に吐くための「自分の番が来た」合図。i 番目は i-1 番目の完了を待つ
        var turn = new TaskCompletionSource[files.Count + 1];
        for (int i = 0; i <= files.Count; i++) turn[i] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        turn[0].SetResult();

        using var slots = new SemaphoreSlim(Math.Max(1, threads));
        // xz の並列展開に回せる本数は、xz の部品の数で割る。ほかの形式は1スレッドで展開するので数に入れない。
        // ファイル数で割ると、gz＋xz＋zst＋gz では xz が 2 本しかもらえず、ほかが先に終わったあとも
        // xz だけが 2 本で走り続けて全体を待たせた（管理部の混在テスト 2026-09-24: rg -z の 1/1.85）
        // 呼び手が下調べ済みなら、その結果を使う（1本ずつ開き直すと 8.6 万本で約 7 秒。2026-10-02）
        kinds ??= files.Select(f => CompressedInput.Probe(f).Kind).ToArray();
        int decodeThreads = DecodeThreadsFor(kinds, threads);

        // 枠は<b>指定順に</b>取る。順番待ち（turn）は前のファイルの完了を待つので、
        // 後ろのファイルが先に枠を取ると、前のファイルが枠を取れず永久に待ち合う
        //（オーナーの比較テストで発覚 2026-09-22: UVF_MAX_THREADS=1 で6ファイルを検索すると止まった）。
        // i 番目は「i-1 番目が枠を取った」合図を待ってから枠を取りに行く
        var admitted = new TaskCompletionSource[files.Count + 1];
        for (int i = 0; i <= files.Count; i++) admitted[i] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        admitted[0].SetResult();

        var tasks = new Task[files.Count];
        for (int i = 0; i < files.Count; i++)
        {
            int index = i;
            string file = files[i];
            tasks[i] = Task.Run(async () =>
            {
                if (ArrivalDelayForTests is { } delay) await delay(index);
                await admitted[index].Task.WaitAsync(ct);   // 前のファイルが枠を取ってから
                await slots.WaitAsync(ct);
                admitted[index + 1].TrySetResult();         // 次のファイルを枠取りへ進ませる
                try
                {
                    var one = await OneFileAsync(file, kinds[index], options, invert, json, lineNumbers,
                                                 withFileName ? file : null, index, turn, output, ct, decodeThreads,
                                                 verifyPlain ? skipped : null);
                    Interlocked.Add(ref hits, one.Hits);
                    if (one.Truncated) truncated = true;
                    if (one.Reason is { } reason) lock (failedLock) failed.Add((file, reason));
                }
                finally { slots.Release(); }
            }, ct);
        }

        await Task.WhenAll(tasks);
        var notSearched = new List<(string File, bool Pbf, CompressedProbe Probe)>();
        for (int i = 0; i < files.Count; i++)
            if (skipped[i] is { } s) notSearched.Add((files[i], s.Pbf, s.Probe));
        return new Result(hits, truncated, failed, notSearched);
    }

    /// <summary>xz の部品1本あたりの展開スレッド数（全体の本数を、xz の部品の数で割る）。</summary>
    public static int DecodeThreadsFor(IReadOnlyList<CompressedKind> kinds, int threads)
        => CompressedFormats.DecodeThreadsFor(kinds, threads);

    private static async Task<(long Hits, bool Truncated, string? Reason)> OneFileAsync(
        string file, CompressedKind kind, SearchOptions options, bool invert, bool json, bool lineNumbers, string? name,
        int index, TaskCompletionSource[] turn, TextWriter output, CancellationToken ct, int decodeThreads,
        (bool Pbf, CompressedProbe Probe)?[]? skipped = null)
    {
        var buffer = new StringWriter { NewLine = "\n" };
        TextWriter writer = buffer;   // 自分の番が来るまでは手元に貯める
        bool direct = false;
        long hits = 0;
        bool truncated = false;
        string? reason = null;

        async Task TakeTurnAsync()
        {
            if (direct) return;
            await turn[index].Task;              // 前のファイルが出し終わるまで待つ
            await output.WriteAsync(buffer.GetStringBuilder().ToString());
            buffer.GetStringBuilder().Clear();
            writer = output;
            direct = true;
        }

        try
        {
            // 圧縮（gz・bz2・xz・lzma・zstd）は展開しながら探す（1ファイルのときと同じ読み口。
            // 切れていれば読み終えたところで気づく）
            IByteSource source = CompressedFormats.IsSingleStream(kind)
                ? new CompressedStreamByteSource(file, kind, decodeThreads)
                : new SequentialFileByteSource(file);
            if (skipped is not null && kind == CompressedKind.None)
            {
                Span<byte> head = stackalloc byte[OsmPbfFile.HeadBytes];
                int got = source.Read(0, head);
                bool pbf = OsmPbfFile.IsHead(head[..got]);
                if (pbf || CompressedInput.PlainFromHead(file, head[..got]) is null)
                {
                    await source.DisposeAsync();
                    var probe = pbf ? CompressedProbe.Plain : CompressedInput.Probe(file);
                    if (pbf || probe.Kind == CompressedKind.Zip || probe.IsRejected)
                    {
                        skipped[index] = (pbf, probe);
                        source = new EmptyByteSource();     // 探さない（空として順番だけ通す）
                    }
                    else source = CompressedFormats.IsSingleStream(probe.Kind)
                        ? new CompressedStreamByteSource(file, probe.Kind, decodeThreads)
                        : new SequentialFileByteSource(file);
                }
            }
            await using var _ = source;
            var detected = EncodingDetector.Detect(source);
            var encoding = detected.Encoding;

            var outcome = await RawGrep.RunAsync(
                source, detected.BomLength, encoding, options, invert,
                (line, _, text) =>
                {
                    hits++;
                    string body = encoding.GetString(text);
                    writer.WriteLine(json
                        ? (lineNumbers ? JsonLines.Hit(name, line + 1, body) : JsonLines.HitWithoutNumber(name, body))
                        : Text(name, lineNumbers, line, body));
                    // 貯めすぎたら順番待ちに入り、以後は直接書く
                    if (!direct && buffer.GetStringBuilder().Length >= BufferLimit)
                        TakeTurnAsync().GetAwaiter().GetResult();
                }, ct);
            truncated = outcome.Truncated;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            reason = e.Message;   // 1つ読めなくても止めない（§2.3）
        }

        await TakeTurnAsync();      // 自分の番で、残りを吐く
        await output.FlushAsync(ct);
        turn[index + 1].TrySetResult();
        return (hits, truncated, reason);
    }

    /// <summary>探さないと決めたファイルの代わり（開いたものは閉じてある）。</summary>
    private sealed class EmptyByteSource : IByteSource
    {
        public long Length => 0;
        public int Read(long offset, Span<byte> buffer) => 0;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>テキスト1行（grep 互換: 複数ファイルのときだけファイル名を前置する）。</summary>
    private static string Text(string? name, bool lineNumbers, long line, string body)
    {
        var sb = new StringBuilder();
        if (name is not null) { sb.Append(name); sb.Append(':'); }
        if (lineNumbers) { sb.Append(line + 1); sb.Append('\t'); }
        sb.Append(body);
        return sb.ToString();
    }
}
