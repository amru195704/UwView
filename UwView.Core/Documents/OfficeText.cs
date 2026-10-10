using System.IO.Compression;
using System.Text;

namespace UwView.Core.Documents;

/// <summary>
/// Word・Excel・PDF が読めないとき（古い形式・パスワード付き・文字の無い PDF・壊れている）。
/// 文は日本語と英語の両方を持ち、表示する側が選ぶ。
/// </summary>
public sealed class OfficeRejectedException(string japanese, string english, Exception? inner = null)
    : IOException(english, inner)
{
    public string Japanese { get; } = japanese;
    public string English { get; } = english;
    public string Text(bool japanese) => japanese ? Japanese : English;
}

/// <summary>
/// Word・Excel・PDF から文字を取り出す入口（v1.8.3 extFS E-5。指示書 §6）。
///
/// 取り出した文字は 1 行ずつで、改行は LF。行の形は形式ごとに決まっている:
/// <list type="bullet">
/// <item>Word：本文の段落を 1 行ずつ。表は 1 行を、セルをタブでつないで 1 行。脚注・コメントは後ろに <c>[脚注] </c>・<c>[コメント] </c> を付けて。ヘッダー・フッターは出さない。</item>
/// <item>Excel：シートごと、1 行を <c>シート名!行番号&lt;TAB&gt;A のセル&lt;TAB&gt;B のセル…</c>。空の行は出さない。セルは表示されている値（<c>raw</c> なら元の値）。</item>
/// <item>PDF：ページごとに <c>## page N</c> の 1 行、続けて読む順の行。</item>
/// </list>
/// 元の形式には戻せない（pbf と同じ扱い。<c>-extract</c> は取り出した文字を書く）。
/// uvf（取り出しながら探す）と uvp（取り出した文字で .uwvz を作る）の両方が使う（v1.8.3.2 で共通に移した）。
/// </summary>
public static class OfficeText
{
    /// <summary>
    /// 見分けて、読めないものは <see cref="OfficeRejectedException"/>。Word・Excel・PDF でなければ None。
    /// </summary>
    public static OfficeKind Check(string path)
    {
        var probe = OfficeDocumentFile.Probe(path);
        if (probe.IsRejected)
        {
            string name = Path.GetFileName(path);
            throw new OfficeRejectedException(OfficeDocumentFile.RejectText(probe.Reject, name, true),
                                              OfficeDocumentFile.RejectText(probe.Reject, name, false));
        }
        return probe.Kind;
    }

    /// <summary>取り出した文字の行（読み進みは <paramref name="progress"/> に 0〜1 で）。</summary>
    public static IEnumerable<string> Lines(string path, OfficeKind kind, bool raw, Action<double>? progress = null)
        => kind switch
        {
            OfficeKind.Docx => ZipLines(path, zip => DocxText.Lines(zip, progress)),
            OfficeKind.Xlsx => ZipLines(path, zip => XlsxText.Lines(zip, raw, progress)),
            OfficeKind.Pdf => PdfText.Lines(path, progress),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static IEnumerable<string> ZipLines(string path, Func<ZipArchive, IEnumerable<string>> lines)
    {
        FileStream file;
        ZipArchive zip;
        try
        {
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            zip = new ZipArchive(file, ZipArchiveMode.Read);
        }
        catch (InvalidDataException e) { throw Broken(path, e); }
        using (file)
        using (zip)
        {
            using var e = lines(zip).GetEnumerator();
            while (true)
            {
                try { if (!e.MoveNext()) yield break; }
                catch (Exception ex) when (ex is System.Xml.XmlException or InvalidDataException)
                {
                    throw Broken(path, ex);
                }
                yield return e.Current;
            }
        }
    }

    /// <summary>
    /// 読めるかだけを確かめる（束ねる前の下調べ用。読めない 1 本で全体を止めず、名指しで飛ばすため）。
    /// PDF は開けるか・文字のあるページがあるかを見る。Word・Excel は見分けの段階で分かっているので null。
    /// </summary>
    public static OfficeRejectedException? Precheck(string path, OfficeKind kind)
        => kind == OfficeKind.Pdf ? PdfText.Precheck(path) : null;

    internal static OfficeRejectedException Broken(string path, Exception inner)
    {
        string name = Path.GetFileName(path);
        return new OfficeRejectedException($"{name} は壊れているので読めません（{inner.Message}）",
                                           $"{name} is damaged and cannot be read ({inner.Message})", inner);
    }

    /// <summary>行を UTF-8（改行 LF）の前方専用のストリームにする（束ねた索引の 1 本としても使う）。</summary>
    public static Stream OpenStream(string path, OfficeKind kind, bool raw, Action<double>? progress = null)
        => new LineStream(Lines(path, kind, raw, progress));

    /// <summary>行の並びを UTF-8 のバイトとして読ませる。</summary>
    internal sealed class LineStream(IEnumerable<string> lines) : Stream
    {
        private readonly IEnumerator<string> _lines = lines.GetEnumerator();
        private byte[] _buffer = new byte[64 * 1024];
        private int _start, _end;
        private bool _done;

        public override int Read(Span<byte> dest)
        {
            int written = 0;
            while (written < dest.Length)
            {
                if (_start == _end && !Fill()) break;
                int n = Math.Min(dest.Length - written, _end - _start);
                _buffer.AsSpan(_start, n).CopyTo(dest[written..]);
                _start += n;
                written += n;
            }
            return written;
        }

        private bool Fill()
        {
            if (_done || !_lines.MoveNext()) { _done = true; return false; }
            string line = _lines.Current;
            int max = Encoding.UTF8.GetMaxByteCount(line.Length) + 1;
            if (_buffer.Length < max) _buffer = new byte[max];
            _end = Encoding.UTF8.GetBytes(line, _buffer);
            _buffer[_end++] = (byte)'\n';
            _start = 0;
            return true;
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _lines.Dispose();
            base.Dispose(disposing);
        }
    }
}
