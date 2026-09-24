using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using UwView.Core.Cli;

namespace UwView.Core.Tests;

/// <summary>
/// bzip2・xz・lzma・zstd を読む（v1.7.1 第1弾・指示書_開発部_圧縮形式サポート_2026-09-24）。
///
/// 実物は<b>本物の道具</b>（bzip2 -1 / xz / xz --format=lzma / zstd / lz4 / brotli）で作って TestData に置いた。
/// 自前の書き手と読み手だけで閉じると、同じ思い違いを両側でして見逃す。
/// 中身は sample.log（3000 行・「東京」は 384 行）。bzip2 は -1（100KB ブロック）で2ブロックにしてある。
/// </summary>
public class CompressedFormatsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "uv-formats-" + Guid.NewGuid().ToString("N"));

    public CompressedFormatsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);

    private string Copy(string fixture, string? as_ = null)
    {
        string to = Path.Combine(_dir, as_ ?? fixture);
        File.Copy(Fixture(fixture), to, overwrite: true);
        return to;
    }

    private static byte[] Plain => File.ReadAllBytes(Fixture("sample.log"));

    private static byte[] ReadAll(Stream stream)
    {
        using var all = new MemoryStream();
        stream.CopyTo(all);
        return all.ToArray();
    }

    public static TheoryData<string, CompressedKind> Samples => new()
    {
        { "sample.log.bz2", CompressedKind.Bzip2 },
        { "sample.log.xz", CompressedKind.Xz },
        { "sample.log.lzma", CompressedKind.Lzma },
        { "sample.log.zst", CompressedKind.Zstd },
        { "sample.log.lz4", CompressedKind.Lz4 },
        { "sample.log.br", CompressedKind.Brotli },
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void 中身で形式を見分けて元どおりに展開できる(string fixture, CompressedKind kind)
    {
        string path = Copy(fixture);

        Assert.Equal(kind, CompressedInput.Probe(path).Kind);
        using var stream = CompressedFormats.Open(path, kind);
        Assert.Equal(Plain, ReadAll(stream));
    }

    [Theory]
    [InlineData("sample.log.bz2", CompressedKind.Bzip2)]
    [InlineData("sample.log.xz", CompressedKind.Xz)]
    [InlineData("sample.log.zst", CompressedKind.Zstd)]
    [InlineData("sample.log.lz4", CompressedKind.Lz4)]
    public void 名前が違っても中身で見分ける(string fixture, CompressedKind kind)
    {
        // ログの回転で app.log.1 のような名前の圧縮ファイルは普通にある
        string path = Copy(fixture, "app.log.1");
        Assert.Equal(kind, CompressedInput.Probe(path).Kind);
    }

    [Theory]
    [InlineData("concat.log.bz2", CompressedKind.Bzip2)]   // pbzip2 などが作る「連結した bz2」
    [InlineData("concat.log.zst", CompressedKind.Zstd)]    // 複数フレーム
    public void 連結されたものも最後まで読む(string fixture, CompressedKind kind)
    {
        using var stream = CompressedFormats.Open(Copy(fixture), kind);
        Assert.Equal(Plain, ReadAll(stream));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void 途中で切れていれば読みの途中で知らせる(string fixture, CompressedKind kind)
    {
        string path = Copy(fixture);
        byte[] whole = File.ReadAllBytes(path);
        File.WriteAllBytes(path, whole[..(whole.Length * 2 / 3)]);

        Assert.Throws<InvalidDataException>(() =>
        {
            using var stream = CompressedFormats.Open(path, kind);
            _ = ReadAll(stream);
        });
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task uvfで平文と同じ結果になる(string fixture, CompressedKind kind)
    {
        _ = kind;
        string plain = Path.Combine(_dir, "sample.log");
        File.WriteAllBytes(plain, Plain);
        string packed = Copy(fixture);

        var want = await Uvf(plain, "東京");
        var got = await Uvf(packed, "東京");

        Assert.Equal(UvfExit.Found, got.Exit);
        Assert.Equal(want.Out, got.Out);
        Assert.Equal(384, got.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task uvfは切れたものを最後まで読めなかったと言う(string fixture, CompressedKind kind)
    {
        string path = Copy(fixture);
        byte[] whole = File.ReadAllBytes(path);
        File.WriteAllBytes(path, whole[..(whole.Length * 2 / 3)]);

        var run = await Uvf(path, "東京");

        Assert.Equal(UvfExit.Error, run.Exit);
        Assert.Contains(CompressedFormats.Name(kind), run.Err);
        Assert.Contains("keep the original file", run.Err);
    }

    /// <summary>
    /// mac・Linux では bzip2・xz・lzma を OS のライブラリで展開する（オーナー決定 2026-09-24）。
    /// 黙って .NET 側へ落ちると速さが 1/2.4〜1/2.8 になるので、落ちていないことを確かめる。
    /// </summary>
    [Theory]
    [InlineData(CompressedKind.Bzip2)]
    [InlineData(CompressedKind.Xz)]
    [InlineData(CompressedKind.Lzma)]
    public void macとLinuxではOSのライブラリで展開する(CompressedKind kind)
    {
        if (!(OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())) return;
        if (Environment.GetEnvironmentVariable("UWVIEW_SYSTEM_DECODERS") == "0") return;
        Assert.StartsWith("OS", CompressedFormats.DecoderOf(kind));
    }

    // ── xz の並列展開（実装指示書_xz並列展開_2026-09-24）──────────────────

    /// <summary>
    /// 複数ブロックの xz（xz -T4 --block-size=16KiB で作った 9 ブロック）を、並列でも単スレッドでも
    /// 読んで、<b>バイト単位で同じ</b>になること（完了条件2）。出力はブロック順に揃っていること。
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void 複数ブロックのxzは並列でも単スレッドでも同じ中身になる(int threads)
    {
        string path = Copy("sample.multiblock.log.xz");
        using var file = File.OpenRead(path);
        using var stream = CompressedFormats.Open(file, CompressedKind.Xz, path, threads);
        Assert.Equal(Plain, ReadAll(stream));
    }

    [Fact]
    public void macとLinuxではxzを並列のデコーダで展開する()
    {
        // 並列の口（liblzma 5.4 以降）が無い環境では単スレッドに落ちる。mac の OS 標準は 5.4.3
        if (!OperatingSystem.IsMacOS()) return;
        if (Environment.GetEnvironmentVariable("UWVIEW_SYSTEM_DECODERS") == "0") return;
        int saved = CompressedFormats.DecodeThreads;
        try
        {
            CompressedFormats.DecodeThreads = 4;
            Assert.Equal("OS×4", CompressedFormats.DecoderOf(CompressedKind.Xz));
            CompressedFormats.DecodeThreads = 1;
            Assert.Equal("OS", CompressedFormats.DecoderOf(CompressedKind.Xz));   // 1 本なら単スレッドのデコーダ
        }
        finally { CompressedFormats.DecodeThreads = saved; }
    }

    [Fact]
    public void 複数ブロックのxzも途中で切れていれば知らせる()
    {
        string path = Copy("sample.multiblock.log.xz");
        byte[] whole = File.ReadAllBytes(path);
        File.WriteAllBytes(path, whole[..(whole.Length * 2 / 3)]);

        Assert.Throws<InvalidDataException>(() =>
        {
            using var file = File.OpenRead(path);
            using var stream = CompressedFormats.Open(file, CompressedKind.Xz, path, 8);
            _ = ReadAll(stream);
        });
    }

    [Fact]
    public async Task 複数ブロックのxzをuvfで探すと平文と同じ結果になる()
    {
        string plain = Path.Combine(_dir, "sample.log");
        File.WriteAllBytes(plain, Plain);
        string packed = Copy("sample.multiblock.log.xz");

        Assert.Equal((await Uvf(plain, "東京")).Out, (await Uvf(packed, "東京")).Out);
    }

    [Fact]
    public void brotliはマジックが無いので名前が違えば平文として扱う()
    {
        // brotli には先頭の目印が無い。.br でないものを brotli と決めつけると、普通のファイルを壊して読む
        string path = Copy("sample.log.br", "app.log.1");
        Assert.Equal(CompressedKind.None, CompressedInput.Probe(path).Kind);
    }

    [Fact]
    public void 名前はbrなのに中身がbrotliでなければ読めないと言う()
    {
        string path = Path.Combine(_dir, "fake.br");
        File.WriteAllText(path, "plain text 東京 but named .br\n");
        var probe = CompressedInput.Probe(path);
        Assert.True(probe.IsRejected);
    }

    [Fact]
    public void 名前はbz2なのに中身が違えば断る()
    {
        string path = Path.Combine(_dir, "fake.bz2");
        File.WriteAllText(path, "plain text 東京\n");
        Assert.Equal(CompressedReject.WrongFormat, CompressedInput.Probe(path).Reject);
    }

    [Fact]
    public void テキストでない中身は断る()
    {
        string path = Path.Combine(_dir, "blob.bin.bz2");
        WriteBzip2(path, [0x00, 0x01, 0x02, 0x00, 0xFF, .. new byte[600]]);
        Assert.Equal(CompressedReject.NotText, CompressedInput.Probe(path).Reject);
    }

    [Fact]
    public void 二重に圧縮したものは断る()
    {
        using var gz = new MemoryStream();
        using (var z = new GZipStream(gz, CompressionLevel.Fastest, leaveOpen: true)) z.Write(Plain);
        string path = Path.Combine(_dir, "nested.log.gz.bz2");
        WriteBzip2(path, gz.ToArray());
        Assert.Equal(CompressedReject.NestedCompression, CompressedInput.Probe(path).Reject);
    }

    [Fact]
    public void tarを圧縮したものは書庫として断る()
    {
        // tar はまだ（第3弾）。黙って1本のテキストとして探すと、tar の見出しまで本文に混ざる
        using var tar = new MemoryStream();
        using (var writer = new TarWriter(tar, leaveOpen: true))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "a.log")
            {
                DataStream = new MemoryStream("x 東京\n"u8.ToArray()),
            };
            writer.WriteEntry(entry);
        }
        string path = Path.Combine(_dir, "pack.tar.bz2");
        WriteBzip2(path, tar.ToArray());
        Assert.Equal(CompressedReject.TarArchive, CompressedInput.Probe(path).Reject);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task 画面の展開でも元どおりになり拡張子を外した名前で置く(string fixture, CompressedKind kind)
    {
        string path = Copy(fixture);
        string dst = CompressedFormats.PlainPath(path, kind);

        long written = await CompressedInput.ExpandAsync(path, kind, dst);

        Assert.Equal(Path.Combine(_dir, "sample.log"), dst);
        Assert.Equal(Plain.Length, written);
        Assert.Equal(Plain, File.ReadAllBytes(dst));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task 画面の展開で切れていたら何も残さない(string fixture, CompressedKind kind)
    {
        string path = Copy(fixture);
        byte[] whole = File.ReadAllBytes(path);
        File.WriteAllBytes(path, whole[..(whole.Length * 2 / 3)]);
        string dst = CompressedFormats.PlainPath(path, kind);

        await Assert.ThrowsAsync<InvalidDataException>(() => CompressedInput.ExpandAsync(path, kind, dst));
        Assert.False(File.Exists(dst));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp-*"));
    }

    private static void WriteBzip2(string path, byte[] contents)
    {
        using var file = File.Create(path);
        using var bz = SharpCompress.Compressors.BZip2.BZip2Stream.Create(
            file, SharpCompress.Compressors.CompressionMode.Compress, false, false, false);
        bz.Write(contents);
    }

    private sealed record Result(int Exit, string Out, string Err);

    private static async Task<Result> Uvf(params string[] args)
    {
        using var stdout = new MemoryStream();
        var stderr = new StringWriter();
        var env = new UvfEnvironment { StdOut = stdout, StdErr = stderr, Japanese = false };
        int code = await UvfCli.RunAsync(args, env);
        return new Result(code, Encoding.UTF8.GetString(stdout.ToArray()), stderr.ToString());
    }

    [Fact]
    public void Xz_parts_share_decode_threads_only_among_xz_parts()
    {
        const CompressedKind gz = CompressedKind.Gzip, xz = CompressedKind.Xz, zst = CompressedKind.Zstd;
        // gz＋xz＋zst＋gz: xz は1本だけなので全部を回す（ファイル数で割ると 2 本になり、xz だけが残って待たせた）
        Assert.Equal(8, MultiFileSearch.DecodeThreadsFor([gz, xz, zst, gz], 8));
        Assert.Equal(8, CompressedFormats.DecodeThreadsFor([gz, xz, zst, gz], 8));
        Assert.Equal(4, MultiFileSearch.DecodeThreadsFor([xz, gz, xz, zst], 8));
        Assert.Equal(1, MultiFileSearch.DecodeThreadsFor([xz, xz, xz, xz, xz, xz, xz, xz, xz, xz], 8));
        Assert.Equal(8, MultiFileSearch.DecodeThreadsFor([gz, zst], 8));
        Assert.Equal(1, MultiFileSearch.DecodeThreadsFor([xz, xz], 1));
    }
}
