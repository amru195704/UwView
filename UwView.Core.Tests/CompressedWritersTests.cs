using System.Text;

namespace UwView.Core.Tests;

/// <summary>
/// 7 形式の書き出し（<see cref="CompressedWriters"/>・指示書 WideField v1.7 §11）。
/// 書いたものを読み込み側（<see cref="CompressedFormats.Open(Stream, CompressedKind, string, int)"/>）で読み、元とバイト一致すること。
/// 外部コマンド（gzip・xz など）で戻せることは UwTest/uv_compress_test.sh で確かめる。
/// </summary>
public class CompressedWritersTests
{
    private static byte[] Sample()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 60_000; i++) sb.Append($"{i:D6} ERROR 東京 dev{i % 4} payload={i * 7919 % 100003}\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static TheoryData<CompressedKind> Kinds => new()
    {
        CompressedKind.Gzip, CompressedKind.Bzip2, CompressedKind.Xz, CompressedKind.Lzma,
        CompressedKind.Zstd, CompressedKind.Lz4, CompressedKind.Brotli,
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void 書いたものを読むと元とバイト一致する(CompressedKind kind)
    {
        if (kind == CompressedKind.Bzip2 && OperatingSystem.IsWindows()) return;   // Windows には libbz2 を同梱しない
        byte[] original = Sample();
        using var packed = new MemoryStream();
        using (var writer = CompressedWriters.Create(packed, kind, leaveOpen: true, threads: 4))
        {
            // 書く大きさをばらばらにする（小刻みに書いても締めくくりまで正しいこと）
            for (int at = 0, step = 1; at < original.Length; at += step, step = step * 7 % 65_521 + 1)
                writer.Write(original, at, Math.Min(step, original.Length - at));
        }
        Assert.True(packed.Length < original.Length / 2, $"{kind} が縮んでいない（{packed.Length}）");

        packed.Position = 0;
        using var reader = CompressedFormats.Open(packed, kind, "sample");
        using var back = new MemoryStream();
        reader.CopyTo(back);
        Assert.Equal(original, back.ToArray());
    }

    [Fact]
    public void 中身から形式が分かる形で書く()
    {
        // 拡張子の無い一時ファイルでも、読み込み側が中身で見分けられる（lzma と br はマジックが無いので除く）
        foreach (var kind in new[] { CompressedKind.Gzip, CompressedKind.Xz, CompressedKind.Zstd, CompressedKind.Lz4 })
        {
            string path = Path.Combine(Path.GetTempPath(), "uv-writer-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var file = File.Create(path))
                using (var writer = CompressedWriters.Create(file, kind, leaveOpen: false))
                    writer.Write(Encoding.UTF8.GetBytes("a1 ERROR\n"));
                Assert.Equal(kind, CompressedFormats.Sniff(path));
            }
            finally { File.Delete(path); }
        }
    }
}
