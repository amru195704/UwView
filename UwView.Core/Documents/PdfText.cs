using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace UwView.Core.Documents;

/// <summary>
/// PDF の文字を取り出す（v1.8.3 extFS E-5。指示書 §6.1。PdfPig・Apache 2.0）。
///
/// ページごとに <c>## page N</c> の 1 行、続けてそのページの文字を読む順に行で
///（2 段組は左の段を読み切ってから右の段）。空の行は出さない。
/// 康煕部首で書かれた漢字は普通の漢字に直す（<see cref="CjkRadicals"/>）。
/// パスワード付き・文字の無い（画像だけの）PDF・壊れた PDF は断る（OCR はしない）。
/// </summary>
internal static class PdfText
{
    public static IEnumerable<string> Lines(string path, Action<double>? progress)
    {
        string name = Path.GetFileName(path);
        PdfDocument document;
        try { document = PdfDocument.Open(path); }
        catch (PdfDocumentEncryptedException e)
        {
            throw new OfficeRejectedException($"{name} はパスワード付きです。読めません（パスワードを外して保存し直してください）",
                                              $"{name} is password-protected and cannot be read (remove the password and save it again)", e);
        }
        catch (Exception e) when (IsDamage(e))
        {
            throw OfficeText.Broken(path, e);
        }

        using (document)
        {
            int pages = document.NumberOfPages;
            long letters = 0;
            for (int number = 1; number <= pages; number++)
            {
                string text;
                try { text = ContentOrderTextExtractor.GetText(document.GetPage(number)); }
                catch (Exception e) when (IsDamage(e))
                {
                    throw OfficeText.Broken(path, e);
                }
                yield return $"## page {number}";
                foreach (var raw in text.Split('\n'))
                {
                    string line = CjkRadicals.Normalize(raw.TrimEnd('\r', ' ', '\t'));
                    if (line.Length == 0) continue;
                    letters += line.Length;
                    yield return line;
                }
                progress?.Invoke((double)number / pages);
            }
            if (letters == 0)
                throw new OfficeRejectedException($"{name} には文字がありません（画像だけの PDF は探せません。OCR はしません）",
                                                  $"{name} has no text (an image-only PDF cannot be searched; OCR is not done)");
        }
    }

    /// <summary>
    /// 読めるかだけを確かめる（束ねる前の下調べ用。読めない 1 本で全体を止めず、名指しで飛ばすため）。
    /// 開けるか（パスワード付きでないか）と、文字のあるページが 1 つでもあるかを見る。ふつうは 1 ページ目で終わる。
    /// </summary>
    public static OfficeRejectedException? Precheck(string path)
    {
        try
        {
            using var document = PdfDocument.Open(path);
            for (int number = 1; number <= document.NumberOfPages; number++)
                if (document.GetPage(number).Letters.Any(l => !string.IsNullOrWhiteSpace(l.Value))) return null;
            string name = Path.GetFileName(path);
            return new OfficeRejectedException($"{name} には文字がありません（画像だけの PDF は探せません。OCR はしません）",
                                               $"{name} has no text (an image-only PDF cannot be searched; OCR is not done)");
        }
        catch (PdfDocumentEncryptedException e)
        {
            string name = Path.GetFileName(path);
            return new OfficeRejectedException($"{name} はパスワード付きです。読めません（パスワードを外して保存し直してください）",
                                               $"{name} is password-protected and cannot be read (remove the password and save it again)", e);
        }
        catch (Exception e) when (IsDamage(e)) { return OfficeText.Broken(path, e); }
    }

    /// <summary>PdfPig が壊れた PDF で出す例外はいろいろある（書式・フォント・展開）。取り消しとメモリ不足のほかは「壊れている」として断る。</summary>
    private static bool IsDamage(Exception e) => e is not (OfficeRejectedException or OperationCanceledException or OutOfMemoryException);
}
