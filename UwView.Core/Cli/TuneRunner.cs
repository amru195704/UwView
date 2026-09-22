using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace UwView.Core.Cli;

/// <summary>
/// <c>--tune</c>: この機械で検索に何スレッド使うのが良いかを実測する（Wide Field v1.7.0 段階1-b）。
///
/// 決めごと（指示書 §3 段階1-b・変更不可）:
/// <list type="bullet">
///   <item><b>ホットのみ測る。</b>スレッド数が効くのは CPU 律速の区間で、コールドは媒体律速になり何本でも同じ</item>
///   <item><b>管理者権限を要求しない</b>（キャッシュ破棄を求めた時点で大半の人は走らせない）</item>
///   <item><b>各設定3回・ばらつきを併記</b>（1回だと利用者がノイズに合わせて設定する）</item>
///   <item><b>差が小さいときは「どれでも同じ」と言い切る</b></item>
///   <item><b>先頭 4GiB 程度で打ち切る</b>（全部読むと誰も走らせない）</item>
///   <item><b>媒体の速さも別に測って併記</b>（CPU と媒体のどちらが先に頭打ちかを示す）</item>
/// </list>
/// </summary>
public static class TuneRunner
{
    /// <summary>測る量の上限（先頭からこのバイト数まで）。</summary>
    public const long MaxBytes = 4L << 30;

    /// <summary>これより大きいファイルは、後ろ半分を媒体の速さの計測に取っておく。</summary>
    private const long KeepTailAbove = 512L << 20;

    /// <summary>対象が指定されなかったときに作る一時ファイルの大きさ。</summary>
    public const long GeneratedBytes = 1L << 30;

    /// <summary>測る量の下限（これより小さくすると1回が短すぎて測れない）。</summary>
    public const long MinBytes = 256L << 20;

    /// <summary>
    /// この機械で「メモリに載る」と見なせる大きさ。<b>空きメモリの半分</b>（上限 4GiB・下限 256MiB）。
    ///
    /// 載らない量を測ると、2回目も媒体から読み直すことになり、
    /// <b>ホットのつもりで媒体律速を測る</b>。そうなるとスレッドを増やしても伸びず、
    /// 「この機械は1本で十分」という誤った結論になる
    /// （オーナー報告 2026-09-22: Linux VM 2 vCPU・50GB で 1本 2.0GB/s → 2本 2.17GB/s）。
    ///
    /// 半分にするのは、走査そのもの以外（OS・ほかのアプリ）にも要るため。
    /// </summary>
    public static long MemoryBudget(long? availableBytes = null)
    {
        long available = availableBytes ?? AvailableMemory();
        return Math.Clamp(available / 2, MinBytes, MaxBytes);
    }

    /// <summary>いま使えるメモリ（バイト）。分からなければ控えめな既定値。</summary>
    private static long AvailableMemory()
    {
        // Linux は「いま実際に使える量」を見る（総量だと、ほかで使われている分まで載ると見なしてしまう）
        if (OperatingSystem.IsLinux())
        {
            try
            {
                foreach (string line in File.ReadLines("/proc/meminfo"))
                {
                    if (!line.StartsWith("MemAvailable:", StringComparison.Ordinal)) continue;
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && long.TryParse(parts[1], out long kb)) return kb * 1024;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        long total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;   // 物理メモリ（コンテナなら上限）
        return total > 0 ? total : 2L << 30;
    }

    private const int Block = 1 << 20;
    private const int Rounds = 3;

    /// <summary>1つのスレッド数での結果。</summary>
    /// <param name="Threads">スレッド数。</param>
    /// <param name="GbPerSec">中央値（GB/s）。</param>
    /// <param name="SpreadPercent">3回の幅（中央値に対する ±%）。</param>
    public readonly record struct Row(int Threads, double GbPerSec, double SpreadPercent);

    /// <summary>
    /// 1回ぶんの走査を実際に行う係（GB/s を返す）。製品ごとに<b>本番と同じ経路</b>を渡す:
    /// uvf は平文の走査、uvp は <c>.uwvz</c> の検索（zstd 展開を含む）。
    /// 経路が違うと1バイトあたりの手間が違い、勧める本数も変わる
    /// （Mac 実測: 平文 7.1 GB/s・4本で頭打ち／.uwvz 2.0 GB/s・8〜16本で頭打ち。2026-09-22）。
    /// </summary>
    /// <param name="path">測る対象。</param>
    /// <param name="length">先頭からこのバイト数だけ測る。</param>
    /// <param name="threads">同時に走らせる本数。</param>
    public delegate double Scan(string path, long length, int threads, CancellationToken ct);

    /// <param name="Rows">スレッド数ごとの結果。</param>
    /// <param name="Recommended">勧めるスレッド数。</param>
    /// <param name="AllSame">どの本数でも変わらなかったか（差が小さい）。</param>
    /// <param name="DiskGbPerSec">
    /// 媒体から読める速さを、走査するバイト数に換算した値（測れなければ null）。
    /// <c>.uwvz</c> を読む場合は「媒体の速さ × 圧縮率」になる（同じ土俵で CPU 側と比べるため）。
    /// </param>
    /// <param name="MeasuredBytes">1回あたりに読んだバイト数。</param>
    /// <param name="Busy">測っている間に速さが揺れたか（ほかの処理が走っている疑い）。</param>
    /// <param name="OnMemory">
    /// 測った範囲がメモリ（ページキャッシュ）に載っていたか。
    /// <b>載っていなければ媒体律速の値</b>で、スレッド数を増やしても伸びない——
    /// その状態の結果を「この機械は1本で十分」と読むと、本当は CPU 律速で効くはずの場面を取り逃がす。
    /// </param>
    /// <param name="MemoryBudget">この機械で「載る」と見なした大きさ（測る量の上限に使った値）。</param>
    public sealed record Result(IReadOnlyList<Row> Rows, int Recommended, bool AllSame,
                                double? DiskGbPerSec, long MeasuredBytes, bool Busy,
                                bool OnMemory = true, long MemoryBudget = 0);

    /// <summary>試すスレッド数（1・2・4・… と論理プロセッサ数）。</summary>
    public static int[] Ladder(int logicalProcessors)
    {
        var list = new List<int>();
        for (int n = 1; n <= logicalProcessors; n *= 2) list.Add(n);
        if (list.Count == 0 || list[^1] != logicalProcessors) list.Add(logicalProcessors);
        return [.. list];
    }

    /// <summary>
    /// 測る。<paramref name="path"/> が null なら一時ファイルを作って測り、終わったら消す。
    /// </summary>
    /// <param name="status">途中経過（1行ずつ）。</param>
    /// <param name="scan">1回ぶんの走査（null なら平文の走査＝uvf の経路）。</param>
    /// <param name="mediumRatio">
    /// 媒体から読むバイト数に対する、走査するバイト数の倍率。平文は 1。
    /// <c>.uwvz</c> は元の約 1/9 しか読まないので 9 前後になる（媒体で頭打ちかの判断に使う）。
    /// </param>
    public static Result Run(string? path, int logicalProcessors, Action<string>? status = null,
                             CancellationToken ct = default, Scan? scan = null, double mediumRatio = 1)
    {
        scan ??= ScanPlain;
        string target = path ?? GenerateSample();
        bool generated = path is null;
        try
        {
            long total = new FileInfo(target).Length;
            // 大きいファイルは前半だけ測り、後ろは媒体の速さの計測用に取っておく（まだキャッシュに載っていない）
            long length = total > KeepTailAbove ? Math.Min(MaxBytes, total / 2) : Math.Min(MaxBytes, total);
            // メモリに載らない量を測ると、ホットのつもりが媒体律速になり「何本でも同じ」に見える
            //（オーナー報告 2026-09-22: Linux VM・50GB で 1本 2.0GB/s・2本 2.17GB/s＝伸びない）。
            // 載る大きさまで下げてから測る
            long budget = MemoryBudget();
            bool trimmed = length > budget;
            if (trimmed) length = budget;
            if (length <= 0) throw new InvalidDataException("測る中身がありません（ファイルが空です）");

            // 媒体の速さは、こちらが何も読む前に測る（読んだあとだとキャッシュから返って速く見える）。
            // 一時ファイルは自分で書いた直後で全部載っているため、測っても媒体の値にならない
            double? disk = generated ? null : MeasureDisk(target, length, status, ct);

            status?.Invoke("キャッシュに載せています…");
            double coldGbPerSec = scan(target, length, 1, ct);   // 1回目＝まだ載っていない状態
            double warmGbPerSec = scan(target, length, 1, ct);   // 2回目＝載ったはずの状態（混み具合の基準でもある）
            // 載っているかの判定。媒体の速さが測れていれば、それとの比較がいちばん確か
            //（同じくらいの速さなら、2回目も媒体から読み直している）。
            // 測れていないときは「2回目が速くなったか」で見る——ただし最初から載っていた場合は
            // どちらも速いので、比が伸びなくても載っていると見なせる（媒体比較を優先するのはそのため）
            // 媒体の速さと同じくらいなら、2回目も媒体から読み直している＝載っていない。
            // 媒体が測れないときは<b>言わない</b>——1回目から載っていることもあり
            //（書いた直後・前回の実行で載ったまま）、「2回目が速くならない」だけでは決められない
            double mediumForScan = (disk ?? 0) * mediumRatio;
            bool onMemory = mediumForScan <= 0 || warmGbPerSec > mediumForScan * 1.3;
            _ = coldGbPerSec;

            var rows = new List<Row>();
            foreach (int threads in Ladder(logicalProcessors))
            {
                ct.ThrowIfCancellationRequested();
                var runs = new List<double>(Rounds);
                for (int i = 0; i < Rounds; i++) runs.Add(scan(target, length, threads, ct));
                runs.Sort();
                double median = runs[runs.Count / 2];
                double spread = median > 0 ? (runs[^1] - runs[0]) / 2 / median * 100 : 0;
                rows.Add(new Row(threads, median, spread));
                status?.Invoke($"  {threads,4} スレッド  {median:F2} GB/s  ±{spread:F0}%");
            }

            // 混んでいないか: 最後にもう一度 1 本で測り、最初と大きく違えば疑う
            double again = scan(target, length, 1, ct);
            bool busy = warmGbPerSec > 0 && Math.Abs(again - warmGbPerSec) / warmGbPerSec > 0.25;

            double best = rows.Max(r => r.GbPerSec);
            bool allSame = best > 0 && rows[0].GbPerSec / best > 0.8;   // 1本でも8割出るなら「どれでも同じ」
            // 一番速い値の 95% に届く、いちばん少ない本数を勧める（頭打ちの手前を選ぶ）
            int recommended = allSame ? 1 : rows.First(r => r.GbPerSec >= best * 0.95).Threads;

            return new Result(rows, recommended, allSame, disk * mediumRatio, length, busy, onMemory, budget);
        }
        finally
        {
            if (generated) { try { File.Delete(target); } catch (IOException) { } }
        }
    }

    /// <summary>
    /// 1回ぶんの走査（GB/s）。<b>本番の検索と同じ処理</b>（<see cref="RawGrep"/>）を、
    /// 範囲を分けて同時に走らせる。素のバイト探しだけを測ると本番の何倍も速い値が出て、
    /// 勧める本数を誤る（見つからない語を使い、最後まで走らせる）。
    /// </summary>
    public static double ScanPlain(string path, long length, int threads, CancellationToken ct)
    {
        var options = new SearchOptions("NOT_FOUND_TUNE_PATTERN_ZZ");
        var watch = Stopwatch.StartNew();
        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads, CancellationToken = ct }, i =>
        {
            long from = length * i / threads;
            long to = length * (i + 1) / threads;
            if (from >= to) return;
            using var slice = new SliceByteSource(path, from, to - from);
            RawGrep.RunAsync(slice, 0, Encoding.UTF8, options, invert: false,
                             (_, _, _) => { }, ct).GetAwaiter().GetResult();
        });
        watch.Stop();
        return length / 1024.0 / 1024 / 1024 / Math.Max(0.000001, watch.Elapsed.TotalSeconds);
    }

    /// <summary>ファイルの一部分だけを 0 から始まるように見せる読み口（走査を分担させるため）。</summary>
    private sealed class SliceByteSource(string path, long offset, long length) : IByteSource, IDisposable
    {
        private readonly Microsoft.Win32.SafeHandles.SafeFileHandle _handle =
            File.OpenHandle(path, FileMode.Open, FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete, FileOptions.SequentialScan);

        public long Length => length;

        public int Read(long at, Span<byte> buffer)
        {
            if (at < 0 || at >= length || buffer.IsEmpty) return 0;
            int want = (int)Math.Min(buffer.Length, length - at);
            return RandomAccess.Read(_handle, buffer[..want], offset + at);
        }

        public void Dispose() => _handle.Dispose();

        public ValueTask DisposeAsync() { _handle.Dispose(); return ValueTask.CompletedTask; }
    }

    /// <summary>
    /// 媒体の読み出し（GB/s）。キャッシュに載っていないところを読みたいので、
    /// 測った範囲より後ろが十分にあるファイルのときだけ測る（載っている範囲を測っても意味が無い）。
    /// </summary>
    private static double? MeasureDisk(string path, long warmedLength, Action<string>? status, CancellationToken ct)
    {
        long total = new FileInfo(path).Length;
        long from = warmedLength;
        long want = Math.Min(1L << 30, total - from);
        if (want < (128L << 20)) return null;   // 後ろが足りない＝測れない

        status?.Invoke("媒体の速さを測っています…");
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
                                           FileShare.ReadWrite | FileShare.Delete, FileOptions.SequentialScan);
        // macOS はキャッシュを使わない読みに切り替えられる（管理者権限は要らない）。
        // これをしないと、前に読んだところが載っていて「媒体が 16GB/s」のような値になる
        bool uncached = OperatingSystem.IsMacOS() && TrySetNoCache(handle);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(4 << 20);
        try
        {
            var watch = Stopwatch.StartNew();
            long pos = from, read = 0;
            while (read < want)
            {
                ct.ThrowIfCancellationRequested();
                int got = RandomAccess.Read(handle, buffer.AsSpan(0, (int)Math.Min(buffer.Length, want - read)), pos);
                if (got <= 0) break;
                pos += got;
                read += got;
            }
            watch.Stop();
            double gbPerSec = read / 1024.0 / 1024 / 1024 / Math.Max(0.000001, watch.Elapsed.TotalSeconds);
            // 明らかに媒体では出ない速さが出たら「測れなかった」とする
            //（F_NOCACHE を付けても、既にキャッシュに載っているページは速く返る）。
            // 2026-09-22: mac で一時ファイルを測ったら 14.29 GB/s になり、
            // それを媒体の速さとして見せたうえ「メモリに載っていない」と誤判定した
            _ = uncached;
            return gbPerSec > 8 ? null : gbPerSec;
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    /// <summary>macOS: このファイルの読みでページキャッシュを使わない（F_NOCACHE）。</summary>
    private static bool TrySetNoCache(Microsoft.Win32.SafeHandles.SafeFileHandle handle)
    {
        const int F_NOCACHE = 48;
        try { return fcntl(handle.DangerousGetHandle().ToInt32(), F_NOCACHE, 1) == 0; }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int fcntl(int fd, int cmd, int arg);

    /// <summary>対象が指定されなかったときの一時ファイル（ログらしい中身にする）。</summary>
    private static string GenerateSample()
    {
        string path = Path.Combine(Path.GetTempPath(), "uwview-tune-" + Guid.NewGuid().ToString("N")[..8] + ".log");
        var line = Encoding.UTF8.GetBytes(
            "2026-09-22T00:00:00Z INFO dev0 code=200 event=heartbeat seq=000000 payload=xxxxxxxxxxxxxxxxxxxx\n");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20);
        var block = new byte[1 << 20];
        for (int at = 0; at + line.Length <= block.Length; at += line.Length) line.CopyTo(block, at);
        for (long written = 0; written < GeneratedBytes; written += block.Length) file.Write(block);
        return path;
    }

    /// <summary>表にして返す（画面にも同じものを出せるようにするため、文字列を作るところは分けてある）。</summary>
    public static string Format(Result result, bool ja, string target, int logicalProcessors, string scanName = "")
    {
        var sb = new StringBuilder();
        double gib = result.MeasuredBytes / 1024.0 / 1024 / 1024;
        double budgetGib = result.MemoryBudget / 1024.0 / 1024 / 1024;
        sb.AppendLine(ja
            ? $"  対象: {target} の先頭 {gib:F1} GiB ／ 各{Rounds}回"
              + (result.MemoryBudget > 0 && result.MeasuredBytes >= result.MemoryBudget
                 ? $"（この機械のメモリに載る大きさ {budgetGib:F1} GiB に合わせました）" : "")
            : $"  Target: the first {gib:F1} GiB of {target} / {Rounds} runs each"
              + (result.MemoryBudget > 0 && result.MeasuredBytes >= result.MemoryBudget
                 ? $" (trimmed to {budgetGib:F1} GiB, what fits in this machine's memory)" : ""));
        sb.AppendLine(ja
            ? $"  論理プロセッサ: {logicalProcessors}"
            : $"  Logical processors: {logicalProcessors}");
        if (scanName.Length > 0)
            sb.AppendLine(ja ? $"  測った処理: {scanName}" : $"  Measured: {scanName}");
        sb.AppendLine();
        sb.AppendLine(ja ? "   スレッド   速度        ばらつき" : "   threads    speed       spread");
        double best = result.Rows.Max(r => r.GbPerSec);
        foreach (var row in result.Rows)
        {
            string mark = row.Threads == result.Recommended ? (ja ? "  ← 勧める本数" : "  <- recommended")
                        : row.GbPerSec >= best * 0.95 && row.Threads > result.Recommended ? (ja ? "  ← 頭打ち" : "  <- no further gain")
                        : "";
            sb.AppendLine($"   {row.Threads,7}  {row.GbPerSec,6:F2} GB/s   ±{row.SpreadPercent,2:F0}%{mark}");
        }
        sb.AppendLine();
        if (result.Busy)
            sb.AppendLine(ja
                ? "  ※ 測っている間に速さが揺れました。ほかの処理が走っていないときに測り直してください"
                : "  Note: the speed moved while measuring. Try again when the machine is idle.");
        // 載っていなければ媒体律速の値。ここを言わないと「1本で十分」と読まれてしまう
        if (!result.OnMemory)
            sb.AppendLine(ja
                ? "  ※ 測った範囲がメモリに載りませんでした。これは<媒体律速>の値で、スレッド数を増やしても伸びません。\n"
                  + "     メモリに載る大きさのファイルで測り直すと、CPU 側の伸び方が分かります"
                : "  Note: the measured span did not stay in memory, so this is a *medium-bound* result and more "
                  + "threads cannot help.\n     Measure a file that fits in memory to see how the CPU side scales.");
        if (result.AllSame)
            sb.AppendLine(ja
                ? "  この機械では、スレッド数を増やしても変わりません（どれでも同じです）"
                : "  On this machine the thread count does not matter (any value behaves the same).");
        else if (result.DiskGbPerSec is { } disk)
            sb.AppendLine(ja
                ? $"  勧める本数: {result.Recommended}（媒体は {disk:F2} GB/s・参考。"
                  + (disk < best ? "実際の検索は媒体の速さで頭打ちになります）" : "CPU 側が先に頭打ちになります）")
                : $"  Recommended: {result.Recommended} (the medium reads at {disk:F2} GB/s — "
                  + (disk < best ? "real searches will be limited by the medium)" : "the CPU side saturates first)"));
        else
            sb.AppendLine(ja
                ? $"  勧める本数: {result.Recommended}（媒体の速さは測れませんでした）"
                : $"  Recommended: {result.Recommended} (the medium speed could not be measured)");
        return sb.ToString();
    }
}
