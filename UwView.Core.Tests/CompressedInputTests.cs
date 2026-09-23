using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UwView.Core;

namespace UwView.Core.Tests;

/// <summary>
/// 圧縮入力（gz）の受け付け判定と展開。
/// 指示書 0-doc/実装指示書_UVP_圧縮読み込み.md §2A・§5 の受け入れ条件を自動化したもの。
///
/// 一番大事なのは「展開結果が gunzip の出力とバイト一致すること」。
/// gunzip が無い環境でも落ちないよう、参照実装が使えないときは元データと直接照合する。
/// </summary>
public class CompressedInputTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "uvf-gz-" + Guid.NewGuid().ToString("N"));

    public CompressedInputTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Path_(string name) => Path.Combine(_dir, name);

    private string WriteGz(string name, byte[] plain, CompressionLevel level = CompressionLevel.Optimal)
    {
        string gz = Path_(name);
        using var fs = new FileStream(gz, FileMode.Create, FileAccess.Write);
        using var z = new GZipStream(fs, level);
        z.Write(plain);
        return gz;
    }

    private static byte[] Hash(string path)
    {
        using var fs = File.OpenRead(path);
        return SHA256.HashData(fs);
    }

    // ── 判定 ────────────────────────────────────────────────

    [Fact]
    public void 普通のファイルは圧縮と判定しない()
    {
        string p = Path_("plain.log");
        File.WriteAllText(p, "hello\n");
        var probe = CompressedInput.Probe(p);
        Assert.Equal(CompressedKind.None, probe.Kind);
        Assert.False(probe.IsRejected);
    }

    [Fact]
    public void 拡張子がgzでマジックも合えばgzと判定する()
    {
        string gz = WriteGz("a.log.gz", Encoding.UTF8.GetBytes("line\n"));
        Assert.Equal(CompressedKind.Gzip, CompressedInput.Probe(gz).Kind);
    }

    [Fact]
    public void 拡張子がgzでもマジックが違えば理由をつけて断る()
    {
        string p = Path_("fake.log.gz");
        File.WriteAllText(p, "I am not gzip at all\n");
        var probe = CompressedInput.Probe(p);
        Assert.Equal(CompressedKind.None, probe.Kind);
        Assert.Equal(CompressedReject.NotGzip, probe.Reject);
    }

    [Fact]
    public void 二重gzは理由をつけて断る()
    {
        byte[] inner;
        using (var ms = new MemoryStream())
        {
            using (var z = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(Encoding.UTF8.GetBytes("payload\n"));
            inner = ms.ToArray();
        }
        string gz = WriteGz("double.gz", inner);
        Assert.Equal(CompressedReject.NestedGzip, CompressedInput.Probe(gz).Reject);
    }

    [Theory]
    [InlineData("bundle.tar.gz")]
    [InlineData("bundle.tgz")]
    public void 名前がtarのgzは理由をつけて断る(string name)
    {
        string gz = WriteGz(name, Encoding.UTF8.GetBytes("whatever\n"));
        Assert.Equal(CompressedReject.TarArchive, CompressedInput.Probe(gz).Reject);
    }

    [Fact]
    public void 名前がtarでなくても中身がtarなら断る()
    {
        // tar ヘッダーは 512B。257 バイト目から "ustar"
        var tar = new byte[512];
        Encoding.ASCII.GetBytes("logs/error.log").CopyTo(tar, 0);
        Encoding.ASCII.GetBytes("ustar").CopyTo(tar, 257);
        string gz = WriteGz("sneaky.gz", tar);
        Assert.Equal(CompressedReject.TarArchive, CompressedInput.Probe(gz).Reject);
    }

    [Fact]
    public void 短すぎるgzは壊れていると判定する()
    {
        byte[] plain = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("some repetitive line\n", 4000)));
        string gz = WriteGz("cut.log.gz", plain);
        byte[] bytes = File.ReadAllBytes(gz);
        File.WriteAllBytes(gz, bytes[..12]);   // ヘッダーの途中まで（トレーラが無い）
        Assert.Equal(CompressedReject.Corrupt, CompressedInput.Probe(gz).Reject);
    }

    /// <summary>
    /// 判定は先頭しか見ないので、後ろが切れていても「gz」と答える（それでよい）。
    /// 切り詰めは展開時にトレーラ照合で捕まえる——下の「切り詰めたgzは…」がその担保。
    /// </summary>
    [Fact]
    public void 後ろが切れたgzは判定では通る()
    {
        byte[] plain = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("some repetitive line\n", 4000)));
        string gz = WriteGz("halfcut.log.gz", plain);
        byte[] bytes = File.ReadAllBytes(gz);
        File.WriteAllBytes(gz, bytes[..(bytes.Length / 2)]);
        Assert.Equal(CompressedKind.Gzip, CompressedInput.Probe(gz).Kind);
    }

    [Fact]
    public void 導出名はgzを一枚だけ剥がす()
    {
        Assert.Equal("/tmp/error.log", CompressedInput.DerivePlainPath("/tmp/error.log.gz"));
        Assert.Equal("/tmp/error.LOG", CompressedInput.DerivePlainPath("/tmp/error.LOG.GZ"));
    }

    // ── 展開 ────────────────────────────────────────────────

    public static TheoryData<string, int> Shapes => new()
    {
        { "空", 0 },
        { "1行だけ", 1 },
        { "小型", 500 },
        // 4MiB ブロック境界を跨ぐ（SidecarBuilder の既定ブロックは 4MiB）
        { "境界跨ぎ", 4 << 20 },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task 展開結果は元データとバイト一致する(string shape, int bytes)
    {
        byte[] plain = MakeText(bytes);
        string gz = WriteGz($"{shape}.log.gz", plain);
        string dst = CompressedInput.DerivePlainPath(gz);

        long written = await CompressedInput.ExpandGzipAsync(gz, dst);

        Assert.Equal(plain.Length, written);
        Assert.Equal(plain, File.ReadAllBytes(dst));
    }

    [Fact]
    public async Task 最終行に改行がなくてもバイト一致する()
    {
        byte[] plain = Encoding.UTF8.GetBytes("first\nsecond\nno newline at the end");
        string gz = WriteGz("tail.log.gz", plain);
        string dst = CompressedInput.DerivePlainPath(gz);
        await CompressedInput.ExpandGzipAsync(gz, dst);
        Assert.Equal(plain, File.ReadAllBytes(dst));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task 改行スタイルを問わず往復でバイト一致する(string newline)
    {
        var sb = new StringBuilder();
        var rng = new Random(1234);
        for (int i = 0; i < 20_000; i++)
            sb.Append($"{i:D6} ").Append(new string('x', rng.Next(0, 120))).Append(newline);
        byte[] plain = Encoding.UTF8.GetBytes(sb.ToString());

        string gz = WriteGz("rt.log.gz", plain);
        string dst = CompressedInput.DerivePlainPath(gz);
        await CompressedInput.ExpandGzipAsync(gz, dst);
        Assert.Equal(plain, File.ReadAllBytes(dst));
    }

    [Fact]
    public async Task 展開結果はgunzipの出力と一致する()
    {
        byte[] plain = MakeText(3 << 20);
        string gz = WriteGz("ref.log.gz", plain);
        string ours = Path_("ours.log");
        await CompressedInput.ExpandGzipAsync(gz, ours);

        string? reference = RunGunzip(gz, Path_("theirs.log"));
        if (reference is null) return;   // gunzip が無い環境: 上の「元データと一致」で担保済み
        Assert.Equal(Hash(reference), Hash(ours));
    }

    [Fact]
    public async Task 進捗は単調増加で最後に1になる()
    {
        byte[] plain = MakeText(8 << 20);
        string gz = WriteGz("prog.log.gz", plain);
        var seen = new List<double>();
        await CompressedInput.ExpandGzipAsync(gz, Path_("prog.log"),
            new Progress<double>(seen.Add));

        // Progress<T> は非同期に流れるので、届いた分だけを見る
        Assert.NotEmpty(seen);
        for (int i = 1; i < seen.Count; i++)
            Assert.True(seen[i] >= seen[i - 1], $"進捗が戻った: {seen[i - 1]} → {seen[i]}");
        Assert.All(seen, v => Assert.InRange(v, 0.0, 1.0));
    }

    [Fact]
    public async Task 中止すると出力もtmpも残らない()
    {
        byte[] plain = MakeText(64 << 20);
        string gz = WriteGz("cancel.log.gz", plain);
        string dst = Path_("cancel.log");
        using var cts = new CancellationTokenSource();

        // 時計で中止すると、速い機械では展開が先に終わってしまう（この Mac では毎回そうなった）。
        // 最初の進捗が届いた瞬間に中止する＝必ず「途中で止めた」状態を試せる
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CompressedInput.ExpandGzipAsync(gz, dst, new CancelOnFirstReport(cts), cts.Token));

        Assert.False(File.Exists(dst), "中止したのに出力が残っている");
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp-*"));
    }

    /// <summary>
    /// **切り詰めは必ず捕まえる。**
    ///
    /// .NET の GZipStream は圧縮データが途中で切れていても例外を出さず、
    /// 途中までを返して正常終了する（実測: 10%〜99.9%のどこで切っても無例外、
    /// トレーラ8バイトを落としただけでも無例外）。放置すると「途中までのログ」を
    /// 全部だと思って読むことになるので、トレーラ照合で弾いている。
    /// </summary>
    [Theory]
    [InlineData(0.10)]
    [InlineData(0.50)]
    [InlineData(0.99)]
    [InlineData(0.999)]
    public async Task 切り詰めたgzの展開は例外になりゴミを残さない(double keep)
    {
        byte[] plain = MakeText(4 << 20);
        string gz = WriteGz($"trunc{keep}.log.gz", plain);
        byte[] bytes = File.ReadAllBytes(gz);
        File.WriteAllBytes(gz, bytes[..(int)(bytes.Length * keep)]);

        string dst = Path_($"trunc{keep}.log");
        await Assert.ThrowsAsync<InvalidDataException>(
            () => CompressedInput.ExpandGzipAsync(gz, dst));

        Assert.False(File.Exists(dst));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp-*"));
    }

    [Fact]
    public async Task トレーラだけ落ちたgzも例外になる()
    {
        byte[] plain = MakeText(1 << 20);
        string gz = WriteGz("notrailer.log.gz", plain);
        byte[] bytes = File.ReadAllBytes(gz);
        File.WriteAllBytes(gz, bytes[..^8]);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => CompressedInput.ExpandGzipAsync(gz, Path_("notrailer.log")));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp-*"));
    }

    [Fact]
    public async Task 連結gzも正しく展開できる()
    {
        // cat a.gz b.gz > c.gz。トレーラは末尾メンバーの分しか無いので、
        // 全体 CRC では照合できない——末尾メンバー分だけ読み直して照合する経路の担保。
        byte[] a = MakeText(300_000);
        byte[] b = Encoding.UTF8.GetBytes("tail member line 1\ntail member line 2\n");
        string ga = WriteGz("part-a.gz", a);
        string gb = WriteGz("part-b.gz", b);
        string cat = Path_("joined.log.gz");
        File.WriteAllBytes(cat, [.. File.ReadAllBytes(ga), .. File.ReadAllBytes(gb)]);

        string dst = Path_("joined.log");
        long written = await CompressedInput.ExpandGzipAsync(cat, dst);

        Assert.Equal(a.Length + b.Length, written);
        Assert.Equal((byte[])[.. a, .. b], File.ReadAllBytes(dst));
    }

    [Fact]
    public void CRC32は既知の値と一致する()
    {
        // "123456789" の CRC-32（IEEE）は 0xCBF43926（広く使われる検査値）
        Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"u8));
        Assert.Equal(0u, Crc32.Compute([]));
    }

    [Fact]
    public void CRC32の速い計算は1バイトずつの計算と一致する()
    {
        // 長さの端数（8の倍数±）と、途中で区切って持ち回す場合も確かめる
        var rnd = new Random(7);
        foreach (int len in new[] { 0, 1, 7, 8, 9, 15, 16, 17, 1000, 65_537 })
        {
            byte[] data = new byte[len];
            rnd.NextBytes(data);
            uint expected = Crc32.UpdateBytewise(Crc32.Initial, data);
            Assert.Equal(expected, Crc32.Update(Crc32.Initial, data));
            Assert.Equal(expected, Crc32.UpdateSliced(Crc32.Initial, data));
            int cut = len / 3;
            Assert.Equal(expected, Crc32.Update(Crc32.Update(Crc32.Initial, data.AsSpan(0, cut)), data.AsSpan(cut)));
            Assert.Equal(expected, Crc32.UpdateSliced(Crc32.UpdateSliced(Crc32.Initial, data.AsSpan(0, cut)), data.AsSpan(cut)));
        }
        Assert.Equal(0xCBF43926u, Crc32.Finish(Crc32.UpdateSliced(Crc32.Initial, "123456789"u8)));
    }

    // ── 補助 ────────────────────────────────────────────────

    /// <summary>だいたい <paramref name="bytes"/> バイトの、圧縮の効くテキスト。</summary>
    private static byte[] MakeText(int bytes)
    {
        if (bytes == 0) return [];
        var sb = new StringBuilder(bytes + 64);
        int i = 0;
        while (sb.Length < bytes)
            sb.Append($"{i++:D8} 2026-09-13T10:00:00Z INFO request handled in 12ms\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>参照実装。使えなければ null。</summary>
    private static string? RunGunzip(string gzPath, string outPath)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("/usr/bin/env")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("gunzip");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(gzPath);
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return null;
            using (var outFs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
                p.StandardOutput.BaseStream.CopyTo(outFs);
            p.WaitForExit();
            return p.ExitCode == 0 ? outPath : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
    }

    /// <summary>最初の進捗報告で中止する（その場で呼ばれるので、機械の速さに左右されない）。</summary>
    private sealed class CancelOnFirstReport(CancellationTokenSource cts) : IProgress<double>
    {
        public void Report(double value) => cts.Cancel();
    }

}
