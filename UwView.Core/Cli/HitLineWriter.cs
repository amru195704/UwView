using System.Text;

namespace UwView.Core.Cli;

/// <summary>
/// 当たった行をテキストで書き出す（<c>ファイル名:行番号&lt;TAB&gt;本文</c>・<c>行番号&lt;TAB&gt;本文</c>）。
/// 1 行ごとに文字列を作らず、使い回す領域へ本文を文字に直してそのまま書く。
/// ほとんどの行が当たる式（3G の <c>\d+\.\d{6,}</c> で 2,830 万行）では、1 行に文字列を 3 回作っていたのが
/// 確保の合計 28GB になり、rg の 1.7 倍かかっていた（2026-10-07）。出力の中身は従来と同じ。
/// </summary>
public sealed class HitLineWriter
{
    // 当たりが無いファイルでは確保しない（カーネル 8.6 万本の多くは 0 件）
    private char[] _chars = [];

    /// <summary>1 行を書く。<paramref name="name"/> が null ならファイル名を付けない。</summary>
    public void Write(TextWriter writer, string? name, bool lineNumbers, long line, ReadOnlySpan<byte> text, Encoding encoding)
    {
        if (name is not null)
        {
            writer.Write(name);
            writer.Write(':');
        }
        if (lineNumbers)
        {
            // 数字は自分で並べる（汎用の書式化を初めて使うときの準備が、当たりの少ない検索で目立った）
            Span<char> number = stackalloc char[20];
            ulong value = (ulong)(line + 1);
            int at = number.Length;
            do { number[--at] = (char)('0' + (int)(value % 10)); value /= 10; } while (value != 0);
            writer.Write(number[at..]);
            writer.Write('\t');
        }
        int max = encoding.GetMaxCharCount(text.Length);
        if (max > _chars.Length) _chars = new char[Math.Max(Math.Max(max, 256), _chars.Length * 2)];
        int count = encoding.GetChars(text, _chars);
        writer.Write(_chars, 0, count);
        writer.WriteLine();
    }
}
