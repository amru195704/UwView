using UwView.Core.Documents;

namespace UwView.Core.Tests;

/// <summary>Word・Excel・PDF の見分けと文字の取り出し（v1.8.3 extFS E-5。指示書 §6.1・§9-3・§9-4）。</summary>
public class OfficeTextTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "uvp-office-" + Guid.NewGuid().ToString("N"));

    public OfficeTextTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string P(string name) => Path.Combine(_dir, name);

    private static string[] Lines(string path, OfficeKind kind, bool raw = false)
        => [.. OfficeText.Lines(path, kind, raw)];

    [Fact]
    public void 中身でWordとExcelとPDFを見分ける()
    {
        OfficeSamples.WriteDocx(P("a.docx"));
        OfficeSamples.WriteXlsx(P("b.xlsx"));
        OfficeSamples.WriteDocx(P("名前が違う.zip"));
        OfficeSamples.WritePlainZip(P("c.zip"));
        File.WriteAllText(P("d.txt"), "plain");

        Assert.Equal(OfficeKind.Docx, OfficeDocumentFile.Probe(P("a.docx")).Kind);
        Assert.Equal(OfficeKind.Xlsx, OfficeDocumentFile.Probe(P("b.xlsx")).Kind);
        Assert.Equal(OfficeKind.Docx, OfficeDocumentFile.Probe(P("名前が違う.zip")).Kind);   // 名前ではなく中身
        Assert.Equal(OfficeKind.Pdf, OfficeDocumentFile.Probe(OfficeSamples.Fixture("office-ja.pdf")).Kind);
        Assert.False(OfficeDocumentFile.Probe(P("c.zip")).IsOffice);
        Assert.False(OfficeDocumentFile.Probe(P("d.txt")).IsOffice);
    }

    [Fact]
    public void 古い形式とパスワード付きは理由を付けて断る()
    {
        OfficeSamples.WriteOle(P("old.doc"));
        OfficeSamples.WriteOle(P("old.xls"));
        OfficeSamples.WriteOle(P("locked.xlsx"));
        OfficeSamples.WriteOle(P("mail.msg"));

        Assert.Equal(OfficeReject.LegacyWord, OfficeDocumentFile.Probe(P("old.doc")).Reject);
        Assert.Equal(OfficeReject.LegacyExcel, OfficeDocumentFile.Probe(P("old.xls")).Reject);
        Assert.Equal(OfficeReject.Encrypted, OfficeDocumentFile.Probe(P("locked.xlsx")).Reject);
        Assert.False(OfficeDocumentFile.Probe(P("mail.msg")).IsOffice);   // Word・Excel ではない OLE は普通に扱う

        var e = Assert.Throws<OfficeRejectedException>(() => OfficeText.Check(P("old.doc")));
        Assert.Contains("古い Word", e.Japanese);
        Assert.Contains("old Word", e.English);
    }

    [Fact]
    public void Wordは段落と表の行とテキストボックスと脚注とコメントを出す()
    {
        OfficeSamples.WriteDocx(P("a.docx"));
        Assert.Equal(OfficeSamples.DocxLines, Lines(P("a.docx"), OfficeKind.Docx));
    }

    [Fact]
    public void Excelは表示されている値をシート名と行番号つきで出す()
    {
        OfficeSamples.WriteXlsx(P("b.xlsx"));
        Assert.Equal(OfficeSamples.XlsxLines, Lines(P("b.xlsx"), OfficeKind.Xlsx));
    }

    [Fact]
    public void Excelのrawは元の値を出す()
    {
        OfficeSamples.WriteXlsx(P("b.xlsx"));
        Assert.Equal(OfficeSamples.XlsxRawLines, Lines(P("b.xlsx"), OfficeKind.Xlsx, raw: true));
    }

    [Theory]
    [InlineData(45678, "yyyy/m/d", "2025/1/21")]
    [InlineData(45678.5, "yyyy/m/d h:mm", "2025/1/21 12:00")]
    [InlineData(0.75, "h:mm AM/PM", "6:00 PM")]
    [InlineData(1.5, "[h]:mm:ss", "36:00:00")]
    [InlineData(45678, "yyyy\"年\"m\"月\"d\"日\"(aaa)", "2025年1月21日(火)")]
    [InlineData(45678, "mmm d, yyyy", "Jan 21, 2025")]
    [InlineData(1234.5, "#,##0.00", "1,234.50")]
    [InlineData(-1234.5, "#,##0;[Red]\\-#,##0", "-1,235")]
    [InlineData(-5, "#,##0;(#,##0)", "(5)")]
    [InlineData(0, "#,##0;-#,##0;\"-\"", "-")]
    [InlineData(0.0123, "0.00%", "1.23%")]
    [InlineData(12345, "0.00E+00", "1.23E+04")]
    [InlineData(1234567, "#,##0,\"千円\"", "1,235千円")]
    [InlineData(42, "[$-411]0_);(0)", "42 ")]
    [InlineData(0.1, "General", "0.1")]
    [InlineData(123456789012, "General", "1.23457E+11")]
    public void Excelの表示形式(double value, string code, string expected)
        => Assert.Equal(expected, ExcelNumberFormat.Format(value, code, date1904: false));

    [Fact]
    public void Excelの1904年基準の日付()
        => Assert.Equal("2025/1/21", ExcelNumberFormat.Format(45678 - 1462, "yyyy/m/d", date1904: true));

    [Fact]
    public void PDFはページの見出しと読む順の行を出し部首の字を直す()
    {
        var lines = Lines(OfficeSamples.Fixture("office-ja.pdf"), OfficeKind.Pdf);
        Assert.Equal("## page 1", lines[0]);
        Assert.Contains("本契約は、甲と乙の間で締結される。timeout は 30 秒とする。", lines);   // ⼄（康煕部首）ではなく乙
        Assert.Contains(lines, l => l.Contains("左の段の文章です"));
        Assert.True(Array.FindIndex(lines, l => l.Contains("左の段")) < Array.FindIndex(lines, l => l.Contains("右の段")));
        Assert.Contains("## page 2", lines);
        Assert.Equal("契約の終わり。English text on page two.", lines[^1]);
        Assert.DoesNotContain(lines, l => l.Length == 0);
    }

    [Fact]
    public void パスワード付きと文字の無いPDFは断る()
    {
        var locked = Assert.Throws<OfficeRejectedException>(() => Lines(OfficeSamples.Fixture("office-locked.pdf"), OfficeKind.Pdf));
        Assert.Contains("パスワード付き", locked.Japanese);
        var image = Assert.Throws<OfficeRejectedException>(() => Lines(OfficeSamples.Fixture("office-image-only.pdf"), OfficeKind.Pdf));
        Assert.Contains("文字がありません", image.Japanese);
    }

    [Fact]
    public void 壊れたWordとPDFは断る()
    {
        File.WriteAllBytes(P("broken.pdf"), [.. "%PDF-1.7\n"u8, .. new byte[100]]);
        Assert.Throws<OfficeRejectedException>(() => Lines(P("broken.pdf"), OfficeKind.Pdf));

        OfficeSamples.WriteDocx(P("a.docx"));
        var bytes = File.ReadAllBytes(P("a.docx"));
        File.WriteAllBytes(P("cut.docx"), bytes[..(bytes.Length / 2)]);
        Assert.Throws<OfficeRejectedException>(() => Lines(P("cut.docx"), OfficeKind.Docx));
    }

    /// <summary>切れた・壊れた .docx・.xlsx は「普通のファイル」ではなく、壊れた Word・Excel として断る（2026-10-10 レビュー指摘）。</summary>
    [Fact]
    public void 切れたWordとExcelは壊れていると見分ける()
    {
        OfficeSamples.WriteDocx(P("a.docx"));
        var bytes = File.ReadAllBytes(P("a.docx"));
        File.WriteAllBytes(P("cut.docx"), bytes[..(bytes.Length / 2)]);
        File.WriteAllBytes(P("cut.zip"), bytes[..(bytes.Length / 2)]);

        Assert.Equal(OfficeReject.Corrupt, OfficeDocumentFile.Probe(P("cut.docx")).Reject);
        Assert.False(OfficeDocumentFile.Probe(P("cut.zip")).IsOffice);       // 名前が zip なら zip の扱いに任せる
        var e = Assert.Throws<OfficeRejectedException>(() => OfficeText.Check(P("cut.docx")));
        Assert.Contains("壊れている", e.Japanese);
    }

    /// <summary>zip として開けなかったファイルは閉じる（1,000 回で 1,000 本開いたままになった。2026-10-10 レビュー指摘）。</summary>
    [Fact]
    public void 壊れたWordを何度読んでも開いたファイルが増えない()
    {
        if (OperatingSystem.IsWindows()) return;            // 数えるのは /dev/fd（mac・Linux）
        OfficeSamples.WriteDocx(P("a.docx"));
        var bytes = File.ReadAllBytes(P("a.docx"));
        File.WriteAllBytes(P("cut.docx"), bytes[..(bytes.Length / 2)]);
        int before = Directory.GetFiles("/dev/fd").Length;
        for (int i = 0; i < 200; i++)
            Assert.Throws<OfficeRejectedException>(() => OfficeText.Lines(P("cut.docx"), OfficeKind.Docx, raw: false).ToList());
        int after = Directory.GetFiles("/dev/fd").Length;   // GC には任せない（閉じ忘れは後片付けで見えなくなる）
        Assert.True(after - before < 20, $"開いたままのファイルが増えた（{before} → {after}）");
    }

    [Fact]
    public void 部首の字だけ直して全角英数字はそのまま()
    {
        Assert.Equal("文章 ＡＢＣ１２３", CjkRadicals.Normalize("⽂章 ＡＢＣ１２３"));
        string plain = "普通の文";
        Assert.Same(plain, CjkRadicals.Normalize(plain));
    }
}
