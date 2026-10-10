using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace UwView.Core.Documents;

/// <summary>
/// Excel（.xlsx）の文字を取り出す（v1.8.3 extFS E-5。指示書 §6.1・§9-4）。
///
/// シートの順に、1 行を <c>シート名!行番号&lt;TAB&gt;A&lt;TAB&gt;B…</c> の 1 行にする（A 列から、値のある最後の列まで）。
/// 空の行は出さない。セルは<b>表示されている値</b>（数式は計算済みの値。日付・数値は書式どおり）。
/// <c>raw</c> のときは元の値（数式の結果の数値そのまま）。結合したセルは左上だけに値がある（Excel と同じ）。
/// シートは大きいことがあるので、1 行ずつ流しながら読む（全体をメモリに載せない）。
/// </summary>
internal static class XlsxText
{
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private sealed record Sheet(string Name, string Part);

    public static IEnumerable<string> Lines(ZipArchive zip, bool raw, Action<double>? progress)
    {
        var (sheets, date1904) = Workbook(zip);
        var strings = SharedStrings(zip);
        var formats = Styles(zip);
        long total = Math.Max(1, sheets.Sum(s => Entry(zip, s.Part)?.Length ?? 0));
        long before = 0;
        foreach (var sheet in sheets)
        {
            var entry = Entry(zip, sheet.Part);
            if (entry is null) continue;          // グラフだけのシートなど
            foreach (var line in SheetLines(entry, sheet.Name, strings, formats, date1904, raw))
                yield return line;
            before += entry.Length;
            progress?.Invoke((double)before / total);
        }
    }

    private static ZipArchiveEntry? Entry(ZipArchive zip, string name)
        => zip.GetEntry(name) ?? zip.Entries.FirstOrDefault(e => e.FullName.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static XmlReader Reader(ZipArchiveEntry entry)
        => XmlReader.Create(entry.Open(), new XmlReaderSettings { IgnoreWhitespace = false, DtdProcessing = DtdProcessing.Prohibit, CloseInput = true });

    /// <summary>シートの名前と部品（並びはブックの順）、1904 年基準か。</summary>
    private static (List<Sheet> Sheets, bool Date1904) Workbook(ZipArchive zip)
    {
        var targets = new Dictionary<string, string>();
        if (Entry(zip, "xl/_rels/workbook.xml.rels") is { } rels)
        {
            using var r = Reader(rels);
            while (r.Read())
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "Relationship" && r.GetAttribute("Id") is { } id && r.GetAttribute("Target") is { } target)
                    targets[id] = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
        }
        var sheets = new List<Sheet>();
        bool date1904 = false;
        using (var r = Reader(Entry(zip, "xl/workbook.xml")!))
        {
            while (r.Read())
            {
                if (r.NodeType != XmlNodeType.Element) continue;
                if (r.LocalName == "workbookPr") date1904 = r.GetAttribute("date1904") is "1" or "true";
                else if (r.LocalName == "sheet" && r.GetAttribute("name") is { } name
                         && (r.GetAttribute("id", RelNs) ?? r.GetAttribute("r:id")) is { } rid && targets.TryGetValue(rid, out var part))
                    sheets.Add(new Sheet(name, part));
            }
        }
        return (sheets, date1904);
    }

    /// <summary>共有文字列（ふりがな <c>rPh</c> は除く）。</summary>
    private static List<string> SharedStrings(ZipArchive zip)
    {
        var list = new List<string>();
        if (Entry(zip, "xl/sharedStrings.xml") is not { } entry) return list;
        using var r = Reader(entry);
        while (r.Read())
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "si")
                list.Add(RichText(r));
        return list;
    }

    /// <summary>今の要素（si・is）の中の t を集める。ふりがな（rPh）は飛ばす。読み終えると要素の終わり（空の要素ならその要素）にいる。</summary>
    private static string RichText(XmlReader r)
    {
        if (r.IsEmptyElement) return "";
        var sb = new StringBuilder();
        int depth = r.Depth;
        r.Read();
        while (!r.EOF && !(r.NodeType == XmlNodeType.EndElement && r.Depth == depth))
        {
            if (r.NodeType == XmlNodeType.Element)
            {
                // ReadElementContentAsString・Skip は次の節まで進むので、ここでは Read しない
                if (r.LocalName is "rPh" or "phoneticPr") { r.Skip(); continue; }
                if (r.LocalName == "t") { sb.Append(r.ReadElementContentAsString()); continue; }
            }
            r.Read();
        }
        return sb.ToString();
    }

    /// <summary>セルの書式番号 → 表示形式。</summary>
    private static List<string> Styles(ZipArchive zip)
    {
        var custom = new Dictionary<int, string>();
        var cellFormats = new List<string>();
        if (Entry(zip, "xl/styles.xml") is not { } entry) return cellFormats;
        using var r = Reader(entry);
        bool inCellXfs = false;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "cellXfs") inCellXfs = false;
            if (r.NodeType != XmlNodeType.Element) continue;
            switch (r.LocalName)
            {
                case "numFmt" when int.TryParse(r.GetAttribute("numFmtId"), out int id):
                    custom[id] = r.GetAttribute("formatCode") ?? "General";
                    break;
                case "cellXfs":
                    inCellXfs = !r.IsEmptyElement;
                    break;
                case "xf" when inCellXfs:
                    int.TryParse(r.GetAttribute("numFmtId"), out int fmt);
                    cellFormats.Add(custom.TryGetValue(fmt, out var code) ? code : ExcelNumberFormat.BuiltInCode(fmt));
                    break;
            }
        }
        return cellFormats;
    }

    private static IEnumerable<string> SheetLines(ZipArchiveEntry entry, string sheetName, List<string> strings, List<string> formats, bool date1904, bool raw)
    {
        using var r = Reader(entry);
        var cells = new List<string>();
        int rowNumber = 0;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.LocalName != "row") continue;
            rowNumber = int.TryParse(r.GetAttribute("r"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : rowNumber + 1;
            cells.Clear();
            if (!r.IsEmptyElement)
            {
                int depth = r.Depth, column = 0;
                while (r.Read() && !(r.NodeType == XmlNodeType.EndElement && r.Depth == depth))
                {
                    if (r.NodeType != XmlNodeType.Element || r.LocalName != "c") continue;
                    column = ColumnOf(r.GetAttribute("r")) ?? column + 1;
                    string value = CellValue(r, strings, formats, date1904, raw);
                    if (value.Length == 0) continue;
                    while (cells.Count < column) cells.Add("");
                    cells[column - 1] = Flatten(value);
                }
            }
            if (cells.Count == 0) continue;
            yield return $"{sheetName}!{rowNumber}\t{string.Join('\t', cells)}";
        }
    }

    /// <summary>セルの中の改行・タブは空白に（1 行 1 行、セルはタブ区切りのため）。</summary>
    private static string Flatten(string value)
        => value.IndexOfAny(['\n', '\r', '\t']) < 0 ? value : value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');

    /// <summary>セル番地（B12）の列（B → 2）。</summary>
    internal static int? ColumnOf(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return null;
        int column = 0, i = 0;
        for (; i < reference.Length && char.IsAsciiLetter(reference[i]); i++)
            column = column * 26 + (char.ToUpperInvariant(reference[i]) - 'A' + 1);
        return i == 0 ? null : column;
    }

    /// <summary>今の c 要素の値（読み終えると c の終わりにいる）。</summary>
    private static string CellValue(XmlReader r, List<string> strings, List<string> formats, bool date1904, bool raw)
    {
        string type = r.GetAttribute("t") ?? "n";
        int style = int.TryParse(r.GetAttribute("s"), out int s) ? s : 0;
        if (r.IsEmptyElement) return "";
        string? v = null, inline = null;
        int depth = r.Depth;
        r.Read();
        while (!r.EOF && !(r.NodeType == XmlNodeType.EndElement && r.Depth == depth))
        {
            if (r.NodeType == XmlNodeType.Element)
            {
                // ReadElementContentAsString・Skip は次の節まで進むので、ここでは Read しない
                if (r.LocalName == "v") { v = r.ReadElementContentAsString(); continue; }
                if (r.LocalName == "is") inline = RichText(r);   // is の終わりにいる。下の Read で次へ
                else { r.Skip(); continue; }
            }
            r.Read();
        }
        switch (type)
        {
            case "s":
                return int.TryParse(v, out int index) && index >= 0 && index < strings.Count ? strings[index] : "";
            case "inlineStr":
                return inline ?? v ?? "";
            case "str" or "e" or "d":
                return v ?? "";
            case "b":
                return v == "1" ? "TRUE" : v == "0" ? "FALSE" : v ?? "";
            default:
                if (v is null) return "";
                if (raw || !double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) return v;
                string code = style >= 0 && style < formats.Count ? formats[style] : "General";
                return ExcelNumberFormat.Format(number, code, date1904);
        }
    }
}
