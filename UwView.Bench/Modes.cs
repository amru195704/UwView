using System.Buffers;
using System.Diagnostics;
using System.Runtime.Intrinsics;
using UwView.Core;

/// <summary>
/// 読み出し帯域と改行数えの計測（宣伝部の指示書 2026-09-20 の調査1・調査2）。
/// 記事の読者が同じコマンドで追試できるよう、実装は製品と同じやり方に合わせてある:
///   - pread … RandomAccess.Read（SequentialFileByteSource と同じ）
///   - mmap  … MmapByteSource（表示が使う読み方）
///   - 改行数え … byte=旧実装の1バイトループ／indexof=現実装（Span.IndexOf で跳ぶ・チェックポイントつき）
/// </summary>
internal static class Modes
{
    public static int Run(string[] args)
    {
        string mode = args[0];
        string path = args[1];
        if (!File.Exists(path)) { Console.Error.WriteLine($"ファイルがありません: {path}"); return 1; }

        int block = Bytes(Option(args, "--block") ?? "1m");
        string how = Option(args, "--mode") ?? "indexof";
        bool inMemory = args.Contains("--memory");

        return mode switch
        {
            "--read-mmap" => ReadMmap(path, block),
            "--read-pread" => ReadPread(path, block),
            "--count-newlines" => CountNewlines(path, how, inMemory, block),
            "--read-gzip" => ReadGzip(path, how, block),
            _ => Usage(),
        };
    }

    private static int Usage()
    {
        Console.Error.WriteLine("--read-mmap <file> [--block 1m|4m|16m]");
        Console.Error.WriteLine("--read-pread <file> [--block 1m|4m|16m]");
        Console.Error.WriteLine("--count-newlines <file> [--mode byte|indexof|vector] [--memory]");
        Console.Error.WriteLine("--read-gzip <file> --mode net|zlib");
        return 1;
    }

    private static string? Option(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int Bytes(string text)
    {
        text = text.Trim().ToLowerInvariant();
        int unit = text.EndsWith('m') ? 1 << 20 : text.EndsWith('k') ? 1 << 10 : 1;
        return int.Parse(unit == 1 ? text : text[..^1]) * unit;
    }

    private static void Report(string label, long bytes, TimeSpan elapsed)
    {
        double gb = bytes / 1024.0 / 1024 / 1024;
        Console.WriteLine($"{label}: {bytes:N0} bytes  {elapsed.TotalSeconds:F2} s  "
                          + $"{gb / elapsed.TotalSeconds * 1024:N0} MB/s  ({gb / elapsed.TotalSeconds:F2} GB/s)");
    }

    /// <summary>mmap（MmapByteSource）で先頭から block ずつコピーして読む。</summary>
    private static int ReadMmap(string path, int block)
    {
        var src = new MmapByteSource(path);
        byte[] buf = ArrayPool<byte>.Shared.Rent(block);
        var sw = Stopwatch.StartNew();
        long pos = 0, sum = 0;
        while (pos < src.Length)
        {
            int got = src.Read(pos, buf.AsSpan(0, (int)Math.Min(block, src.Length - pos)));
            if (got <= 0) break;
            sum += buf[0] + buf[got - 1];     // 読んだ中身を触る（最適化で消されないように）
            pos += got;
        }
        sw.Stop();
        ArrayPool<byte>.Shared.Return(buf);
        src.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Report($"mmap  block={block >> 20}MB", pos, sw.Elapsed);
        return sum == long.MinValue ? 1 : 0;
    }

    /// <summary>pread（RandomAccess.Read＝SequentialFileByteSource と同じ）で先頭から block ずつ読む。</summary>
    private static int ReadPread(string path, int block)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
                                           FileShare.ReadWrite | FileShare.Delete, FileOptions.SequentialScan);
        long length = RandomAccess.GetLength(handle);
        byte[] buf = ArrayPool<byte>.Shared.Rent(block);
        var sw = Stopwatch.StartNew();
        long pos = 0, sum = 0;
        while (pos < length)
        {
            int got = RandomAccess.Read(handle, buf.AsSpan(0, (int)Math.Min(block, length - pos)), pos);
            if (got <= 0) break;
            sum += buf[0] + buf[got - 1];
            pos += got;
        }
        sw.Stop();
        ArrayPool<byte>.Shared.Return(buf);
        Report($"pread block={block >> 20}MB", pos, sw.Elapsed);
        return sum == long.MinValue ? 1 : 0;
    }

    /// <summary>改行を数える。--memory は先に全部メモリへ載せてから5回（CPU の上限を見る）。</summary>
    private static int CountNewlines(string path, string how, bool inMemory, int block)
    {
        Func<ReadOnlySpan<byte>, long, long> count = how switch
        {
            "byte" => CountByte,
            "vector" => CountVector,
            _ => CountIndexOf,
        };

        if (inMemory)
        {
            // 2GB を超えるファイルは先頭 1.5GB だけ使う（CPU の上限を見るのが目的なので量は足りる）
            const int Cap = 1536 << 20;
            long length = Math.Min(new FileInfo(path).Length, Cap);
            byte[] all = new byte[length];
            using (var h = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
                                           FileShare.ReadWrite | FileShare.Delete, FileOptions.SequentialScan))
            {
                long read = 0;
                while (read < length)
                {
                    int got = RandomAccess.Read(h, all.AsSpan((int)read, (int)Math.Min(1 << 20, length - read)), read);
                    if (got <= 0) break;
                    read += got;
                }
            }
            for (int round = 0; round < 5; round++)
            {
                var sw = Stopwatch.StartNew();
                long lines = count(all, 0);
                sw.Stop();
                Report($"{how,-8} memory  行 {lines:N0}", all.LongLength, sw.Elapsed);
            }
            return 0;
        }

        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
                                           FileShare.ReadWrite | FileShare.Delete, FileOptions.SequentialScan);
        long total = RandomAccess.GetLength(handle);
        byte[] buf = ArrayPool<byte>.Shared.Rent(block);
        var watch = Stopwatch.StartNew();
        long pos = 0, all2 = 0;
        while (pos < total)
        {
            int got = RandomAccess.Read(handle, buf.AsSpan(0, (int)Math.Min(block, total - pos)), pos);
            if (got <= 0) break;
            all2 += count(buf.AsSpan(0, got), pos);
            pos += got;
        }
        watch.Stop();
        ArrayPool<byte>.Shared.Return(buf);
        Report($"{how,-8} disk block={block >> 20}MB  行 {all2:N0}", pos, watch.Elapsed);
        return 0;
    }

    /// <summary>gz を展開しきる時間（--mode net=.NET 付属／zlib=OS 標準。Linux で速い方を選ぶための計測）。</summary>
    private static int ReadGzip(string path, string how, int block)
    {
        Environment.SetEnvironmentVariable("UWVIEW_SYSTEM_ZLIB", how == "zlib" ? "1" : "0");
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                                        FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
        using var gz = GzipDecoder.Open(file, out bool verifies);
        byte[] buf = ArrayPool<byte>.Shared.Rent(block);
        var sw = Stopwatch.StartNew();
        long total = 0;
        int got;
        while ((got = gz.Read(buf, 0, block)) > 0) total += got;
        sw.Stop();
        ArrayPool<byte>.Shared.Return(buf);
        Report($"gzip  {how,-4} (末尾照合 {(verifies ? "zlib" : "呼び出し側")})  展開後", total, sw.Elapsed);
        Console.WriteLine($"      圧縮側 {new FileInfo(path).Length:N0} bytes  "
                          + $"{new FileInfo(path).Length / 1024.0 / 1024 / sw.Elapsed.TotalSeconds:N0} MB/s");
        return 0;
    }

    /// <summary>旧実装: 1バイトずつ比べる。</summary>
    private static long CountByte(ReadOnlySpan<byte> span, long basePos)
    {
        long lines = 0;
        for (int i = 0; i < span.Length; i++)
            if (span[i] == (byte)'\n') lines++;
        return lines;
    }

    /// <summary>現実装: Span.IndexOf で次の改行へ跳ぶ（256行ごとのチェックポイントも同じように作る）。</summary>
    private static long CountIndexOf(ReadOnlySpan<byte> span, long basePos)
    {
        const int BlockLines = 256;
        long lines = 0;
        int at = 0;
        for (int i = 0; i < span.Length; )
        {
            int rel = span[i..].IndexOf((byte)'\n');
            if (rel < 0) break;
            i += rel + 1;
            lines++;
            if (lines % BlockLines == 0) at = i;   // チェックポイントの記録に相当する作業
        }
        return lines + (at == int.MinValue ? 1 : 0);
    }

    /// <summary>参考: 128/256 ビットのベクタで 0x0A を数える（跳ばずに全部見る）。</summary>
    private static long CountVector(ReadOnlySpan<byte> span, long basePos)
    {
        long lines = 0;
        int i = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            var needle = Vector256.Create((byte)'\n');
            for (; i + Vector256<byte>.Count <= span.Length; i += Vector256<byte>.Count)
                lines += long.PopCount(Vector256.Equals(Vector256.LoadUnsafe(in span[i]), needle)
                                       .ExtractMostSignificantBits());
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            var needle = Vector128.Create((byte)'\n');
            for (; i + Vector128<byte>.Count <= span.Length; i += Vector128<byte>.Count)
                lines += long.PopCount(Vector128.Equals(Vector128.LoadUnsafe(in span[i]), needle)
                                       .ExtractMostSignificantBits());
        }
        for (; i < span.Length; i++) if (span[i] == (byte)'\n') lines++;
        return lines;
    }
}
