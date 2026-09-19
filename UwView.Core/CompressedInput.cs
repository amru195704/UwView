using System.Buffers.Binary;
using System.IO.Compression;

namespace UwView.Core;

/// <summary>圧縮入力の種別。</summary>
public enum CompressedKind
{
    /// <summary>圧縮ファイルではない（そのまま開く）。</summary>
    None,
    Gzip,
    Zip,
}

/// <summary>受け付けられない理由。表示文言は呼び出し側（UI）が言語に応じて作る。</summary>
public enum CompressedReject
{
    None,
    /// <summary>拡張子は .gz だが先頭2バイトが 1F 8B ではない。</summary>
    NotGzip,
    /// <summary>拡張子は .zip だが先頭が PK\x03\x04 ではない。</summary>
    NotZip,
    /// <summary>中身が tar アーカイブ（.tar.gz / .tgz）。</summary>
    TarArchive,
    /// <summary>gz の中がさらに gz（多重圧縮）。</summary>
    NestedGzip,
    /// <summary>
    /// gz として壊れている（最小サイズ未満・先頭が展開できない）。
    /// **後ろが切れているだけの gz はここでは分からない**（先頭しか見ないため）。
    /// それは展開時のトレーラ照合で捕まえる（<see cref="CompressedInput.VerifyGzipOutput"/>）。
    /// </summary>
    Corrupt,
    /// <summary>読めなかった（権限・I/O）。</summary>
    Unreadable,
}

/// <summary>
/// 圧縮入力の判定結果。<see cref="Kind"/> が <see cref="CompressedKind.None"/> で
/// <see cref="Reject"/> も None なら「普通のファイル」。
/// Kind が None で Reject が付いていれば「圧縮を意図しているが受け付けられない」。
/// </summary>
public sealed record CompressedProbe(CompressedKind Kind, CompressedReject Reject = CompressedReject.None)
{
    public static readonly CompressedProbe Plain = new(CompressedKind.None);
    public bool IsCompressed => Kind != CompressedKind.None;
    public bool IsRejected => Reject != CompressedReject.None;
}

/// <summary>
/// 圧縮ファイル（gz / zip）の受け付け判定と、gz の展開。
///
/// 判定は「拡張子」だけでは足りない（中身が tar や二重 gz のことがある）ので、
/// 先頭を実際に少しだけ展開して確かめる。**黙って失敗させない**——受け付けられない場合は
/// 理由（<see cref="CompressedReject"/>）を返し、UI がその理由を出す。
///
/// 書き込み規律: 出力は必ず `<出力名>.tmp-&lt;GUID&gt;` に書き切ってから <c>File.Move</c> する
/// （2026-09-02 のデータ消失対策と同じ流儀）。中止・例外時は tmp を消す。
///
/// 進捗は**圧縮側の読み進み割合**で出す。展開後サイズは事前に分からない——
/// gzip ヘッダー末尾の ISIZE は 4GB 超で mod 2^32 になるため**使ってはいけない**。
/// </summary>
public static class CompressedInput
{
    public const string GzipExtension = ".gz";
    public const string ZipExtension = ".zip";

    /// <summary>展開後サイズの見積り係数（空き容量の事前チェック用）。</summary>
    public const int PlainSizeGuessFactor = 4;

    /// <summary>読み書きのブロック。</summary>
    private const int BufferSize = 1 << 20;

    /// <summary>tar のヘッダーは 512B で、257 バイト目から "ustar" が入る。</summary>
    private const int TarMagicOffset = 257;

    /// <summary>gzip の最小サイズ（ヘッダー10B＋トレーラ8B）。</summary>
    private const int MinGzipLength = 18;

    private static bool HasExtension(string path, string ext)
        => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase);

    /// <summary>`.gz` を1枚剥がした平文の導出名（`error.log.gz` → `error.log`）。</summary>
    public static string DerivePlainPath(string gzPath)
        => HasExtension(gzPath, GzipExtension) ? gzPath[..^GzipExtension.Length] : gzPath + ".txt";

    /// <summary>
    /// 開こうとしているファイルが圧縮入力かどうかを、拡張子と中身の両方から判定する。
    /// 拡張子が圧縮でなければ中身は見ない（普通のファイルを毎回突くのは無駄）。
    /// </summary>
    public static CompressedProbe Probe(string path)
    {
        try
        {
            // .tgz は ".gz" で終わらないので、先に見る
            if (HasExtension(path, ".tgz"))
                return new CompressedProbe(CompressedKind.None, CompressedReject.TarArchive);
            if (HasExtension(path, GzipExtension)) return ProbeGzip(path);
            if (HasExtension(path, ZipExtension)) return ProbeZip(path);
            return CompressedProbe.Plain;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new CompressedProbe(CompressedKind.None, CompressedReject.Unreadable);
        }
    }

    private static CompressedProbe ProbeGzip(string path)
    {
        // .tar.gz / .tgz は名前で分かる分は先に弾く（中身を見ても弾くが、理由が同じなら早い方でよい）
        if (HasExtension(path, ".tar" + GzipExtension) || HasExtension(path, ".tgz"))
            return new CompressedProbe(CompressedKind.None, CompressedReject.TarArchive);

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Span<byte> magic = stackalloc byte[2];
        if (fs.Read(magic) != 2 || magic[0] != 0x1F || magic[1] != 0x8B)
            return new CompressedProbe(CompressedKind.None, CompressedReject.NotGzip);
        // gzip は最小でもヘッダー10B＋トレーラ8B。これを下回るなら中身を見るまでもない
        if (fs.Length < MinGzipLength)
            return new CompressedProbe(CompressedKind.None, CompressedReject.Corrupt);

        // 先頭だけ展開して中身を確かめる。tar は 257B 目の "ustar" で分かるので 512B 読めば足りる
        fs.Position = 0;
        var head = new byte[512];
        int got;
        try
        {
            using var gz = new GZipStream(fs, CompressionMode.Decompress, leaveOpen: true);
            got = gz.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        }
        catch (InvalidDataException)
        {
            return new CompressedProbe(CompressedKind.None, CompressedReject.Corrupt);
        }

        if (got >= 2 && head[0] == 0x1F && head[1] == 0x8B)
            return new CompressedProbe(CompressedKind.None, CompressedReject.NestedGzip);
        if (got >= TarMagicOffset + 5
            && head.AsSpan(TarMagicOffset, 5).SequenceEqual("ustar"u8))
            return new CompressedProbe(CompressedKind.None, CompressedReject.TarArchive);

        return new CompressedProbe(CompressedKind.Gzip);
    }

    private static CompressedProbe ProbeZip(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Span<byte> magic = stackalloc byte[4];
        if (fs.Read(magic) != 4 || BinaryPrimitives.ReadUInt32LittleEndian(magic) != 0x04034B50)
            return new CompressedProbe(CompressedKind.None, CompressedReject.NotZip);
        return new CompressedProbe(CompressedKind.Zip);
    }

    // ── gzip トレーラの照合（切り詰めを黙って通さないため）──────────

    /// <summary>gzip メンバー末尾の8バイト（CRC32 と展開後サイズ mod 2^32）。</summary>
    public readonly record struct GzipTrailer(uint Crc32Value, uint ISizeMod32);

    /// <summary>
    /// ファイル末尾8バイトを gzip トレーラとして読む。8バイト未満なら null。
    /// これは**最後のメンバー**のトレーラ（連結 gz の場合も末尾のもの）。
    /// </summary>
    public static GzipTrailer? ReadTrailer(string gzPath)
    {
        try
        {
            using var fs = new FileStream(gzPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < 8) return null;
            fs.Position = fs.Length - 8;
            Span<byte> tail = stackalloc byte[8];
            if (fs.ReadAtLeast(tail, 8, throwOnEndOfStream: false) != 8) return null;
            return new GzipTrailer(
                BinaryPrimitives.ReadUInt32LittleEndian(tail),
                BinaryPrimitives.ReadUInt32LittleEndian(tail[4..]));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 展開結果が gzip トレーラと合っているかを確かめる。合わなければ <see cref="InvalidDataException"/>。
    ///
    /// **これが無いと切り詰めを検出できない。** .NET の <see cref="GZipStream"/> は、
    /// 圧縮データが途中で切れていても**例外を出さずに読み終わったふりをする**（実測: 10%〜99.9%の
    /// どの位置で切っても無例外。トレーラ8バイトを落としただけでも無例外。CRC を壊したときだけ例外）。
    /// そのまま通すと「途中までのログ」を全部だと思って読んでしまうので、ここで必ず照合する。
    /// </summary>
    /// <param name="written">展開して書き出した総バイト数。</param>
    /// <param name="wholeCrc">展開結果**全体**の CRC32（1パスで計算したもの）。</param>
    /// <param name="suffixCrc">
    /// 展開結果の**末尾 n バイト**の CRC32 を返す関数（連結 gz の照合用）。
    /// 渡せない場合は null——そのときは単一メンバーとしてだけ照合する。
    /// </param>
    public static void VerifyGzipOutput(
        GzipTrailer trailer, long written, uint wholeCrc, Func<long, uint>? suffixCrc = null)
    {
        // 単一メンバー: 展開後サイズ（mod 2^32）と全体 CRC が両方合う
        bool sizeMatchesWhole = (uint)(written & 0xFFFFFFFF) == trailer.ISizeMod32;
        if (sizeMatchesWhole && wholeCrc == trailer.Crc32Value) return;

        // 連結 gz（cat a.gz b.gz）: 末尾メンバーだけがトレーラの対象になる。
        // ISIZE が末尾メンバーの長さなので、その分だけ後ろから読み直して照合する。
        if (suffixCrc is not null && !sizeMatchesWhole
            && trailer.ISizeMod32 > 0 && trailer.ISizeMod32 <= written
            && suffixCrc(trailer.ISizeMod32) == trailer.Crc32Value)
            return;

        throw new InvalidDataException(
            "gzip truncated or corrupted: trailer does not match the decompressed data "
            + $"(written={written}, isize={trailer.ISizeMod32})");
    }

    /// <summary>ファイルの末尾 <paramref name="length"/> バイトの CRC32。</summary>
    private static uint SuffixCrcOfFile(string path, long length)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Position = fs.Length - length;
        var buffer = new byte[BufferSize];
        uint state = Crc32.Initial;
        long left = length;
        while (left > 0)
        {
            int n = fs.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
            if (n <= 0) break;
            state = Crc32.Update(state, buffer.AsSpan(0, n));
            left -= n;
        }
        return Crc32.Finish(state);
    }

    /// <summary>
    /// 展開に必要な空き容量の目安（圧縮サイズ×<see cref="PlainSizeGuessFactor"/>）。
    /// 展開後サイズは事前に分からないので、あくまで目安として警告に使う（続行は禁止しない）。
    /// </summary>
    public static long GuessPlainSize(string compressedPath)
    {
        try { return LinkedFile.Info(compressedPath).Length * PlainSizeGuessFactor; }
        catch (IOException) { return 0; }
    }

    /// <summary>出力先ボリュームの空き容量。分からなければ null。</summary>
    public static long? FreeSpaceFor(string path)
    {
        try
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(dir)) return null;
            return new DriveInfo(dir).AvailableFreeSpace;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// gz を <paramref name="dstPath"/> へストリーム展開する（展開後の全量をメモリに持たない）。
    /// 進捗は圧縮側の読み進み割合（0..1・単調増加）。
    /// 戻り値は書き出した平文のバイト数。
    /// </summary>
    /// <exception cref="InvalidDataException">gz として壊れている。</exception>
    /// <exception cref="OperationCanceledException">中止された（tmp は掃除済み）。</exception>
    public static async Task<long> ExpandGzipAsync(
        string gzPath, string dstPath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        string tmp = dstPath + ".tmp-" + Guid.NewGuid().ToString("N");
        long written = 0;
        uint crc = Crc32.Initial;
        try
        {
            await using (var src = new FileStream(gzPath, FileMode.Open, FileAccess.Read,
                             FileShare.ReadWrite | FileShare.Delete, BufferSize, FileOptions.SequentialScan))
            {
                long total = src.Length;
                await using var gz = GzipDecoder.Open(src, out _);
                await using var dst = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, BufferSize, FileOptions.SequentialScan);

                var buffer = new byte[BufferSize];
                double last = -1;
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    int n = await gz.ReadAsync(buffer, ct);
                    if (n == 0) break;
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                    crc = Crc32.Update(crc, buffer.AsSpan(0, n));
                    written += n;

                    // 圧縮側の読み進みで出す。1% 刻みに間引く（報告が多すぎると UI が遅くなる）
                    if (total > 0 && progress is not null)
                    {
                        double f = Math.Clamp((double)src.Position / total, 0, 1);
                        if (f - last >= 0.01) { last = f; progress.Report(f); }
                    }
                }
            }

            // 切り詰めを黙って通さない（GZipStream は切れていても例外を出さない）
            if (ReadTrailer(gzPath) is { } trailer)
                VerifyGzipOutput(trailer, written, Crc32.Finish(crc),
                                 length => SuffixCrcOfFile(tmp, length));

            File.Move(tmp, dstPath, overwrite: true);
            progress?.Report(1.0);
            return written;
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
