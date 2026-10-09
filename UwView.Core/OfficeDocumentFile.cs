using System.IO.Compression;

namespace UwView.Core;

/// <summary>Word・Excel・PDF の種類（v1.8.3 extFS E-5）。</summary>
public enum OfficeKind
{
    /// <summary>Word・Excel・PDF ではない。</summary>
    None,
    /// <summary>Word（.docx・.docm）。</summary>
    Docx,
    /// <summary>Excel（.xlsx・.xlsm）。</summary>
    Xlsx,
    /// <summary>PDF。</summary>
    Pdf,
}

/// <summary>Word・Excel・PDF のうち、読めないもの。</summary>
public enum OfficeReject
{
    None,
    /// <summary>古い Word（.doc）。</summary>
    LegacyWord,
    /// <summary>古い Excel（.xls）。</summary>
    LegacyExcel,
    /// <summary>パスワード付きの Word・Excel（中身が暗号化されて OLE の形になっている）。</summary>
    Encrypted,
}

/// <summary>判定の結果。</summary>
public sealed record OfficeProbe(OfficeKind Kind, OfficeReject Reject = OfficeReject.None)
{
    public static readonly OfficeProbe NotOffice = new(OfficeKind.None);

    /// <summary>読める Word・Excel・PDF。</summary>
    public bool IsDocument => Kind != OfficeKind.None;

    /// <summary>Word・Excel だが読めない（古い形式・パスワード付き）。</summary>
    public bool IsRejected => Reject != OfficeReject.None;

    /// <summary>どちらか（Word・Excel・PDF として扱うもの）。</summary>
    public bool IsOffice => IsDocument || IsRejected;
}

/// <summary>
/// Word（.docx）・Excel（.xlsx）・PDF かどうかの見分け（v1.8.3 extFS E-5）。
///
/// 文字を取り出して探すのは UwView Pro（uvp）の役目。無料版（uvf）は、黙ってバイナリを
/// テキストとして走査しないよう、ここで見分けて断る。.docx・.xlsx の中身は zip なので、
/// <b>zip の判定より先に</b>ここで見ること。名前ではなく中身で見る
///（PDF は先頭の <c>%PDF-</c>、Word・Excel は zip の中の <c>word/document.xml</c>・<c>xl/workbook.xml</c>）。
/// 古い形式とパスワード付きは、どちらも OLE（複合ファイル）の形なので、名前で分ける。
/// </summary>
public static class OfficeDocumentFile
{
    /// <summary>判定に読む先頭の大きさ（PDF は先頭 1024 バイトの中に <c>%PDF-</c> があればよい決まり）。</summary>
    public const int HeadBytes = 1024;

    private static ReadOnlySpan<byte> PdfMagic => "%PDF-"u8;
    private static ReadOnlySpan<byte> ZipMagic => [0x50, 0x4B, 0x03, 0x04];
    private static ReadOnlySpan<byte> OleMagic => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    /// <summary>名前が Word・Excel・PDF らしいか（たくさんのファイルから、中身を見るものを選ぶとき用）。</summary>
    public static bool NameLooksOffice(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".docx" or ".docm" or ".xlsx" or ".xlsm" or ".pdf" or ".doc" or ".xls";
    }

    /// <summary>中身で見分ける。読めないファイルは <see cref="OfficeProbe.NotOffice"/>。</summary>
    public static OfficeProbe Probe(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            Span<byte> head = stackalloc byte[HeadBytes];
            int got = file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            head = head[..got];
            if (head.IndexOf(PdfMagic) >= 0) return new(OfficeKind.Pdf);
            if (head.StartsWith(OleMagic)) return new(OfficeKind.None, OleReject(path));
            if (!head.StartsWith(ZipMagic)) return OfficeProbe.NotOffice;
            file.Position = 0;
            return new(ZipKind(file));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return OfficeProbe.NotOffice;
        }
    }

    /// <summary>zip の中の目印で Word・Excel を見分ける（どちらでもなければ普通の zip）。</summary>
    private static OfficeKind ZipKind(Stream zip)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase)) return OfficeKind.Docx;
            if (entry.FullName.Equals("xl/workbook.xml", StringComparison.OrdinalIgnoreCase)) return OfficeKind.Xlsx;
        }
        return OfficeKind.None;
    }

    private static OfficeReject OleReject(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".doc" or ".dot" => OfficeReject.LegacyWord,
        ".xls" or ".xlt" => OfficeReject.LegacyExcel,
        ".docx" or ".docm" or ".xlsx" or ".xlsm" => OfficeReject.Encrypted,
        _ => OfficeReject.None,          // msg・古い ppt など。Word・Excel ではないので普通に扱う
    };

    /// <summary>読めない理由（uvf・uvp の CLI と画面で共通）。</summary>
    public static string RejectText(OfficeReject reject, string name, bool japanese) => reject switch
    {
        OfficeReject.LegacyWord => japanese
            ? $"{name} は古い Word（.doc）です。読めません（Word で .docx に保存し直してください）"
            : $"{name} is an old Word file (.doc) and cannot be read (save it as .docx in Word)",
        OfficeReject.LegacyExcel => japanese
            ? $"{name} は古い Excel（.xls）です。読めません（Excel で .xlsx に保存し直してください）"
            : $"{name} is an old Excel file (.xls) and cannot be read (save it as .xlsx in Excel)",
        OfficeReject.Encrypted => japanese
            ? $"{name} はパスワード付きです。読めません（パスワードを外して保存し直してください）"
            : $"{name} is password-protected and cannot be read (remove the password and save it again)",
        _ => "",
    };

    /// <summary>無料版で断るときの文（Word・Excel・PDF を探すのは UwView Pro の機能）。</summary>
    public static string ProOnlyText(string name, bool japanese) => japanese
        ? $"{name}: Word・Excel・PDF を探すのは UwView Pro の機能です（uvp {name} 語）"
        : $"{name}: searching Word, Excel and PDF files is a UwView Pro feature (uvp {name} pattern)";
}
