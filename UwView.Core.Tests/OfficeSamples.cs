using System.IO.Compression;
using System.Text;

namespace UwView.Core.Tests;

/// <summary>
/// テスト用の Word・Excel・PDF（v1.8.3 extFS E-5）。
/// Word・Excel は中身が zip と XML なので、ここで組み立てる（何が入っているかがテストの横で読めるように）。
/// uvf（UwView.Core.Tests）と uvp（UwView.Pro.Core.Tests がこのファイルと TestData をリンク）の両方のテストが使う。
/// PDF は TestData の実物（mac の Chrome で日本語・2 段組の HTML から作ったもの。元は office-ja.source.html）。
/// </summary>
internal static class OfficeSamples
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);

    /// <summary>Word。段落・空の段落・タブと改行・消した文字・表・テキストボックス・脚注・コメント。</summary>
    public static void WriteDocx(string path) => WriteZip(path, new()
    {
        ["[Content_Types].xml"] = "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>",
        ["word/document.xml"] = $"""
            <w:document xmlns:w="{W}" xmlns:mc="{Mc}"><w:body>
            <w:p><w:pPr><w:tabs><w:tab w:val="left" w:pos="720"/></w:tabs></w:pPr><w:r><w:t>契約書</w:t></w:r></w:p>
            <w:p/>
            <w:p><w:r><w:t>ERROR</w:t></w:r><w:r><w:tab/><w:t xml:space="preserve">timeout 30</w:t></w:r><w:r><w:br/><w:t>次の行</w:t></w:r></w:p>
            <w:p><w:del><w:r><w:delText>消した</w:delText></w:r></w:del><w:r><w:instrText>PAGE</w:instrText></w:r><w:r><w:t>残す</w:t></w:r></w:p>
            <w:tbl><w:tblPr/>
            <w:tr><w:tc><w:p><w:r><w:t>品名</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>数量</w:t></w:r></w:p></w:tc></w:tr>
            <w:tr><w:tc><w:p><w:r><w:t>りんご</w:t></w:r></w:p><w:p><w:r><w:t>青森</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>3</w:t></w:r></w:p></w:tc></w:tr>
            </w:tbl>
            <w:p><w:r><mc:AlternateContent><mc:Choice Requires="wps"><w:drawing><w:txbxContent><w:p><w:r><w:t>箱の中</w:t></w:r></w:p></w:txbxContent></w:drawing></mc:Choice>
            <mc:Fallback><w:pict><w:txbxContent><w:p><w:r><w:t>箱の中</w:t></w:r></w:p></w:txbxContent></w:pict></mc:Fallback></mc:AlternateContent><w:t>本文</w:t></w:r></w:p>
            <w:sectPr/></w:body></w:document>
            """,
        ["word/footnotes.xml"] = $"""
            <w:footnotes xmlns:w="{W}">
            <w:footnote w:type="separator" w:id="-1"><w:p><w:r><w:separator/></w:r></w:p></w:footnote>
            <w:footnote w:id="1"><w:p><w:r><w:t>注の本文</w:t></w:r></w:p></w:footnote>
            </w:footnotes>
            """,
        ["word/comments.xml"] = $"""
            <w:comments xmlns:w="{W}"><w:comment w:id="0" w:author="a"><w:p><w:r><w:t>確認してください</w:t></w:r></w:p></w:comment></w:comments>
            """,
        ["word/header1.xml"] = $"<w:hdr xmlns:w=\"{W}\"><w:p><w:r><w:t>ヘッダーは出さない</w:t></w:r></w:p></w:hdr>",
    });

    /// <summary>Word から取り出す行（正解）。</summary>
    public static readonly string[] DocxLines =
    [
        "契約書", "", "ERROR\ttimeout 30", "次の行", "残す", "品名\t数量", "りんご 青森\t3", "本文", "箱の中",
        "[脚注] 注の本文", "[コメント] 確認してください",
    ];

    /// <summary>Excel。2 シート・共有文字列（ふりがな・書式付きの文字）・日付・桁区切り・独自の形式・数式・％・真偽・エラー・空の行。</summary>
    public static void WriteXlsx(string path) => WriteZip(path, new()
    {
        ["[Content_Types].xml"] = "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>",
        ["xl/workbook.xml"] = $"""
            <workbook xmlns="{S}" xmlns:r="{R}"><workbookPr/><sheets>
            <sheet name="売上" sheetId="1" r:id="rId1"/><sheet name="Sheet2" sheetId="2" r:id="rId2"/></sheets></workbook>
            """,
        ["xl/_rels/workbook.xml.rels"] = """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            <Relationship Id="rId1" Type="worksheet" Target="worksheets/sheet1.xml"/>
            <Relationship Id="rId2" Type="worksheet" Target="/xl/worksheets/sheet2.xml"/>
            <Relationship Id="rId3" Type="sharedStrings" Target="sharedStrings.xml"/></Relationships>
            """,
        ["xl/sharedStrings.xml"] = $"""
            <sst xmlns="{S}" count="4" uniqueCount="4">
            <si><t>品名</t><rPh sb="0" eb="2"><t>ヒンメイ</t></rPh><phoneticPr fontId="1"/></si>
            <si><r><t>日</t></r><r><rPr><b/></rPr><t>付</t></r></si>
            <si><t>金額</t></si><si><t>りんご</t></si></sst>
            """,
        ["xl/styles.xml"] = $"""
            <styleSheet xmlns="{S}"><numFmts count="2"><numFmt numFmtId="164" formatCode="#,##0&quot;円&quot;"/>
            <numFmt numFmtId="165" formatCode="yyyy&quot;年&quot;m&quot;月&quot;d&quot;日&quot;"/></numFmts>
            <cellXfs count="6"><xf numFmtId="0"/><xf numFmtId="14"/><xf numFmtId="3"/><xf numFmtId="164"/><xf numFmtId="10"/><xf numFmtId="165"/></cellXfs></styleSheet>
            """,
        ["xl/worksheets/sheet1.xml"] = $"""
            <worksheet xmlns="{S}"><sheetData>
            <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c><c r="C1" t="s"><v>2</v></c></row>
            <row r="2"><c r="A2" t="s"><v>3</v></c><c r="B2" s="1"><v>45678</v></c><c r="C2" s="3"><v>1234567</v></c>
              <c r="D2" s="2"><f>C2*0.1</f><v>123456.7</v></c></row>
            <row r="3"/>
            <row r="4"><c r="A4" t="inlineStr"><is><t>メモ
            改行</t></is></c><c r="C4" s="4"><v>0.256</v></c><c r="E4" t="b"><v>1</v></c><c r="F4" t="e"><v>#DIV/0!</v></c></row>
            </sheetData><mergeCells count="1"><mergeCell ref="A1:A2"/></mergeCells></worksheet>
            """,
        ["xl/worksheets/sheet2.xml"] = $"""
            <worksheet xmlns="{S}"><sheetData>
            <row r="1"><c r="A1" s="5"><v>45678</v></c></row>
            <row r="2"><c r="A2"><v>0.30000000000000004</v></c><c r="B2" t="str"><f>"a"&amp;"b"</f><v>ab</v></c></row>
            </sheetData></worksheet>
            """,
    });

    /// <summary>Excel から取り出す行（表示されている値・正解）。</summary>
    public static readonly string[] XlsxLines =
    [
        "売上!1\t品名\t日付\t金額",
        "売上!2\tりんご\t2025/1/21\t1,234,567円\t123,457",
        "売上!4\tメモ 改行\t\t25.60%\t\tTRUE\t#DIV/0!",
        "Sheet2!1\t2025年1月21日",
        "Sheet2!2\t0.3\tab",
    ];

    /// <summary>Excel から取り出す行（元の値・<c>--raw</c>）。</summary>
    public static readonly string[] XlsxRawLines =
    [
        "売上!1\t品名\t日付\t金額",
        "売上!2\tりんご\t45678\t1234567\t123456.7",
        "売上!4\tメモ 改行\t\t0.256\t\tTRUE\t#DIV/0!",
        "Sheet2!1\t45678",
        "Sheet2!2\t0.30000000000000004\tab",
    ];

    /// <summary>ふつうの zip（Word・Excel ではない）。</summary>
    public static void WritePlainZip(string path) => WriteZip(path, new() { ["a.txt"] = "hello" });

    /// <summary>古い形式・パスワード付きと同じ形（OLE の複合ファイル。先頭の目印だけ）。</summary>
    public static void WriteOle(string path)
        => File.WriteAllBytes(path, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, .. new byte[504]]);

    private static void WriteZip(string path, Dictionary<string, string> parts)
    {
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var (name, text) in parts)
        {
            using var stream = zip.CreateEntry(name).Open();
            stream.Write(Encoding.UTF8.GetBytes(text));
        }
    }
}
