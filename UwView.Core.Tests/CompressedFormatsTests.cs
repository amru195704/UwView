using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using UwView.Core.Cli;

namespace UwView.Core.Tests;

/// <summary>
/// bzip2・xz・lzma・zstd を読む（v1.7.1 第1弾・指示書_開発部_圧縮形式サポート_2026-09-24）。
///
/// 実物は<b>本物の道具</b>（bzip2 -1 / xz / xz --format=lzma / zstd）で作って TestData に置いた。
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
}
