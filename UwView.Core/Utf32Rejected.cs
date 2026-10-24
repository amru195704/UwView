namespace UwView.Core;

/// <summary>
/// UTF-32 は扱わない（オーナー指示 2026-10-17「utf-32 は全てエラー。GUI で開く必要もない」、
/// 2026-10-24「UTF-32 は対象外なので、エラーを出して終了」で無料版・Web 版にも広げた）。
/// 見分けは BOM（FF FE 00 00・00 00 FE FF）。BOM の無い UTF-32 は、もとから UTF-32 と判定しない。
/// </summary>
public static class Utf32Rejected
{
    /// <summary>先頭が UTF-32 の BOM か。</summary>
    public static bool IsUtf32Bom(ReadOnlySpan<byte> head)
        => head.Length >= 4
           && ((head[0] == 0xFF && head[1] == 0xFE && head[2] == 0 && head[3] == 0)
               || (head[0] == 0 && head[1] == 0 && head[2] == 0xFE && head[3] == 0xFF));

    /// <summary>判定した文字コードが UTF-32 か。</summary>
    public static bool IsUtf32(DetectedEncoding detected) => detected.Encoding.CodePage is 12000 or 12001;

    /// <summary>平文のファイルの頭が UTF-32 か（読めなければ false。読めない理由は開くときに出る）。</summary>
    public static bool FileIsUtf32(string path)
    {
        try
        {
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> head = stackalloc byte[4];
            int got = f.ReadAtLeast(head, 4, throwOnEndOfStream: false);
            return IsUtf32Bom(head[..got]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>任意の読み口の頭が UTF-32 か（Web 版の Blob 用）。</summary>
    public static async ValueTask<bool> IsUtf32Async(IByteSource src, CancellationToken ct = default)
    {
        if (src.Length < 4) return false;
        var head = new byte[4];
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
