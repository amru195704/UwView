using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace UwView.Core.Documents;

/// <summary>
/// Word（.docx）の文字を取り出す（v1.8.3 extFS E-5。指示書 §6.1・§9-3）。
///
/// <list type="bullet">
/// <item>本文の段落を 1 行ずつ（空の段落は空の行）。段落の中の改行（<c>w:br</c>）は行を分ける。タブは <c>\t</c>。</item>
/// <item>表は 1 行（<c>w:tr</c>）を、セルをタブでつないで 1 行。セルの中の段落は空白でつなぐ。</item>
/// <item>テキストボックスの段落は、それを置いた段落のあとに。</item>
/// <item>本文のあとに、脚注を <c>[脚注] </c>、コメントを <c>[コメント] </c> を付けて。ヘッダー・フッターは出さない（ページごとに同じ文が並ぶため）。</item>
/// <item>変更履歴で消した文字（<c>w:delText</c>）・フィールドの式（<c>w:instrText</c>）は出さない。</item>
/// </list>
/// 要素は名前（ローカル名）で見る（Strict 形式の名前空間でも同じに読めるように）。
/// </summary>
internal static class DocxText
{
    private const string FootnotePrefix = "[脚注] ";
    private const string CommentPrefix = "[コメント] ";
    private static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    public static IEnumerable<string> Lines(ZipArchive zip, Action<double>? progress)
    {
        var body = Load(zip, "word/document.xml")?.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "body")
                   ?? throw new InvalidDataException("word/document.xml has no body");
        progress?.Invoke(0.2);
        foreach (var line in Block(body)) yield return line;
        progress?.Invoke(0.8);

        if (Load(zip, "word/footnotes.xml")?.Root is { } footnotes)
            foreach (var note in footnotes.Elements().Where(e => e.Name.LocalName == "footnote" && !IsSeparator(e)))
                foreach (var line in Block(note))
                    if (line.Length > 0) yield return FootnotePrefix + line;
        if (Load(zip, "word/comments.xml")?.Root is { } comments)
            foreach (var comment in comments.Elements().Where(e => e.Name.LocalName == "comment"))
                foreach (var line in Block(comment))
                    if (line.Length > 0) yield return CommentPrefix + line;
        progress?.Invoke(1);
    }

    private static XDocument? Load(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? zip.Entries.FirstOrDefault(e => e.FullName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return null;
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    /// <summary>脚注の区切り線（本文ではない）。</summary>
    private static bool IsSeparator(XElement note)
        => note.Attributes().FirstOrDefault(a => a.Name.LocalName == "type")?.Value
            is "separator" or "continuationSeparator" or "continuationNotice";

    /// <summary>段落・表の並び（本文・セル・脚注・テキストボックスの中身）。</summary>
    private static IEnumerable<string> Block(XElement container)
    {
        foreach (var child in container.Elements())
        {
            if (child.Name.Namespace == Mc)
            {
                // 新しい形（Choice）と古い形（Fallback）に同じ中身が入っている。Choice だけ読む
                if (child.Name.LocalName == "AlternateContent" && child.Elements().FirstOrDefault(e => e.Name.LocalName == "Choice") is { } choice)
                    foreach (var line in Block(choice)) yield return line;
                continue;
            }
            switch (child.Name.LocalName)
            {
                case "p":
                    foreach (var line in Paragraph(child)) yield return line;
                    break;
                case "tbl":
                    foreach (var row in child.Elements().Where(e => e.Name.LocalName == "tr"))
                        yield return string.Join('\t', row.Elements().Where(e => e.Name.LocalName == "tc").Select(CellText));
                    break;
                case "sdt" or "sdtContent" or "customXml" or "smartTag" or "ins":
                    foreach (var line in Block(child)) yield return line;
                    break;
            }
        }
    }

    /// <summary>1 つの段落（<c>w:br</c> で行が分かれる）と、その中のテキストボックス。</summary>
    private static IEnumerable<string> Paragraph(XElement paragraph)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        var boxes = new List<XElement>();
        Collect(paragraph, line, lines, boxes);
        lines.Add(line.ToString());
        foreach (var box in boxes)
            lines.AddRange(Block(box));
        return lines;
    }

    private static void Collect(XElement element, StringBuilder line, List<string> lines, List<XElement> boxes)
    {
        foreach (var child in element.Elements())
        {
            if (child.Name.Namespace == Mc)
            {
                if (child.Name.LocalName == "AlternateContent" && child.Elements().FirstOrDefault(e => e.Name.LocalName == "Choice") is { } choice)
                    Collect(choice, line, lines, boxes);
                continue;
            }
            switch (child.Name.LocalName)
            {
                case "t":
                    line.Append(child.Value);
                    break;
                case "tab":
                    line.Append('\t');
                    break;
                case "br" or "cr":
                    if (child.Attributes().FirstOrDefault(a => a.Name.LocalName == "type")?.Value is "page" or "column") break;
                    lines.Add(line.ToString());
                    line.Clear();
                    break;
                case "noBreakHyphen":
                    line.Append('-');
                    break;
                case "txbxContent":
                    boxes.Add(child);
                    break;
                // 文字ではないもの（段落・文字の書式の中の w:tab はタブ位置の定義）・消した文字・フィールドの式
                case "pPr" or "rPr" or "delText" or "instrText" or "fldData" or "del" or "moveFrom":
                    break;
                default:
                    Collect(child, line, lines, boxes);
                    break;
            }
        }
    }

    /// <summary>セルの文字（中の段落・入れ子の表は空白でつなぐ）。</summary>
    private static string CellText(XElement cell)
        => string.Join(' ', Block(cell).Select(l => l.Replace('\t', ' ')).Where(l => l.Length > 0));
}
