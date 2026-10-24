namespace UwView.Core;

/// <summary>
/// UTF-32 は扱わない（オーナー指示 2026-10-17「utf-32 は全てエラー。GUI で開く必要もない」、
/// 2026-10-24「UTF-32 は対象外なので、エラーを出して終了」で無料版・Web 版にも広げた）。
/// 見分けは BOM（FF FE 00 00・00 00 FE FF）と、その後ろが UTF-32 として正しいか。BOM の無い UTF-32 は、もとから UTF-32 と判定しない。
/// </summary>
public static class Utf32Rejected
{
    /// <summary>見分けに読む頭の長さ（BOM の後ろの続きも見る）。</summary>
    public const int HeadBytes = 4096;

    /// <summary>
    /// 先頭が UTF-32 の BOM で、続きの 4 バイトずつも UTF-32 として正しいか。
    /// UTF-16LE の BOM（FF FE）の後ろの 1 字目が U+0000 だと、先頭 4 バイトが UTF-32LE の BOM と同じになる（検査課 2026-10-24 §4-2）。
    /// 続きは、0〜0x3FFFF と 0xE0000〜0x10FFFF（サロゲートを除く）だけを UTF-32 の字とみなす。UTF-16 の「字＋改行」（41 00 0A 00＝0xA0041）の
    /// ような並びは、使われていない面に入るので外れる。続きが無い（4 バイトだけ）ときは UTF-32 とみなす。
    /// </summary>
    public static bool IsUtf32Bom(ReadOnlySpan<byte> head)
    {
        if (head.Length < 4) return false;
        bool le = head[0] == 0xFF && head[1] == 0xFE && head[2] == 0 && head[3] == 0;
        bool be = head[0] == 0 && head[1] == 0 && head[2] == 0xFE && head[3] == 0xFF;
        if (!le && !be) return false;
        var body = head[4..];
        for (int i = 0; i + 4 <= body.Length; i += 4)
        {
            uint u = le ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body[i..])
                        : System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(body[i..]);
            if (!(u <= 0x3FFFF || u is >= 0xE0000 and <= 0x10FFFF) || u is >= 0xD800 and <= 0xDFFF) return false;
        }
        return true;
    }

    /// <summary>判定した文字コードが UTF-32 か。</summary>
    public static bool IsUtf32(DetectedEncoding detected) => detected.Encoding.CodePage is 12000 or 12001;

    /// <summary>平文のファイルの頭が UTF-32 か（読めなければ false。読めない理由は開くときに出る）。</summary>
    public static bool FileIsUtf32(string path)
    {
        try
        {
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> head = stackalloc byte[HeadBytes];
            int got = f.ReadAtLeast(head, HeadBytes, throwOnEndOfStream: false);
            return IsUtf32Bom(head[..got]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// 圧縮ファイル（gz・bz2・xz・lzma・zstd）の中身の頭が UTF-32 か。画面で開くとき、全部を展開する前に断るため
    ///（全部を展開し終えてから断っていて、大きいと待たされ、展開したファイルも隣に残った。検査課 2026-10-24 §4-3）。
    /// 頭だけを展開して見る。読めなければ false（読めない理由は展開するときに出る）。
    /// </summary>
    public static async ValueTask<bool> CompressedIsUtf32Async(string path, CompressedKind kind)
    {
        try
        {
            await using var src = new Cli.CompressedStreamByteSource(path, kind, threads: 1);
            var head = new byte[HeadBytes];
            int got = 0;
            while (got < head.Length)
            {
                int n = src.Read(got, head.AsSpan(got));
                if (n <= 0) break;
                got += n;
            }
            return IsUtf32Bom(head.AsSpan(0, got));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException) { return false; }
    }

    /// <summary>任意の読み口の頭が UTF-32 か（Web 版の Blob 用）。</summary>
    public static async ValueTask<bool> IsUtf32Async(IByteSource src, CancellationToken ct = default)
    {
        if (src.Length < 4) return false;
        var head = new byte[(int)Math.Min(HeadBytes, src.Length)];
        int got = await src.ReadAsync(0, head.AsMemory(), ct);
        return IsUtf32Bom(head.AsSpan(0, got));
    }

    public static string Message(string name, bool japanese)
        => japanese
            ? $"UTF-32 のファイルは扱いません（UTF-8・UTF-16・Shift_JIS・EUC-JP にしてから開いてください）: {name}"
            : $"UTF-32 files are not supported (convert to UTF-8, UTF-16, Shift_JIS or EUC-JP first): {name}";

    /// <summary>判定が UTF-32 なら止める（uvf の 1 本・複数の検索から呼ぶ）。</summary>
    public static void ThrowIf(DetectedEncoding detected, string name)
    {
        if (IsUtf32(detected)) throw new Utf32RejectedException(name);
    }

    // Web 版で断ったファイルの名前（選んだ直後に画面が取り出して知らせる）
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> Pending = new();

    public static void Defer(string name) => Pending.Enqueue(name);

    public static IReadOnlyList<string> TakePending()
    {
        var list = new List<string>();
        while (Pending.TryDequeue(out var n)) list.Add(n);
        return list;
    }
}

/// <summary>UTF-32 のファイルを断る例外。文は日本語と英語の両方を持ち、表示する側が選ぶ。</summary>
public sealed class Utf32RejectedException(string name)
    : IOException(Utf32Rejected.Message(name, japanese: false))
{
    public string FileName { get; } = name;
    public string Text(bool japanese) => Utf32Rejected.Message(FileName, japanese);
}
