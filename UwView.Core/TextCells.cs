namespace UwView.Core;

/// <summary>
/// 行内の表示セル計算と語切り出し（V1.1.1 クイック着色用）。
/// 半角=1セル・全角(CJK)=2セル・タブ=8セルストップ。TextView から利用しテスト可能にする。
/// </summary>
public static class TextCells
{
    /// <summary>全角相当（2セル幅）か（CJK 中心の簡易判定）。</summary>
    public static bool IsWide(char c) =>
        (c >= 0x1100 && c <= 0x115F) || (c >= 0x2E80 && c <= 0xA4CF && c != 0x303F) ||
        (c >= 0xAC00 && c <= 0xD7A3) || (c >= 0xF900 && c <= 0xFAFF) ||
        (c >= 0xFE30 && c <= 0xFE4F) || (c >= 0xFF00 && c <= 0xFF60) || (c >= 0xFFE0 && c <= 0xFFE6);

    /// <summary>文字インデックス → 行頭からの表示セル数。</summary>
    public static int CellOffset(string text, int charIndex)
    {
        int cell = 0;
        int n = charIndex < text.Length ? charIndex : text.Length;
        for (int i = 0; i < n; i++)
            cell += text[i] == '\t' ? 8 - cell % 8 : IsWide(text[i]) ? 2 : 1;
        return cell;
    }

    /// <summary>表示セル位置 → 文字インデックス（範囲外は行末）。</summary>
    public static int CharIndexAtCell(string text, int targetCell)
    {
        if (targetCell < 0) return 0;
        int cell = 0;
        for (int i = 0; i < text.Length; i++)
        {
            int w = text[i] == '\t' ? 8 - cell % 8 : IsWide(text[i]) ? 2 : 1;
            if (targetCell < cell + w) return i;
            cell += w;
        }
        return text.Length;
    }

    /// <summary>
    /// 指定文字インデックス位置の「語」（連続する非空白トークン）の範囲 [Start, End)。広い単位。
    /// 空白・範囲外なら null（着色対象なし）。
    /// </summary>
    public static (int Start, int End)? WordAt(string text, int charIndex)
    {
        if (charIndex < 0 || charIndex >= text.Length || char.IsWhiteSpace(text[charIndex])) return null;
        int s = charIndex; while (s > 0 && !char.IsWhiteSpace(text[s - 1])) s--;
        int e = charIndex; while (e < text.Length && !char.IsWhiteSpace(text[e])) e++;
        return (s, e);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// 指定位置の「最短の語」の範囲 [Start, End)。クリック文字と同じ字種の連続で切る。
    /// 英数字/アンダースコアの塊、または区切り記号の塊（"req-8f2a" なら "req" だけ等）。
    /// 空白・範囲外なら null。
    /// </summary>
    public static (int Start, int End)? WordAtNarrow(string text, int charIndex)
    {
        if (charIndex < 0 || charIndex >= text.Length || char.IsWhiteSpace(text[charIndex])) return null;
        bool word = IsWordChar(text[charIndex]);
        bool Same(char c) => !char.IsWhiteSpace(c) && IsWordChar(c) == word;
        int s = charIndex; while (s > 0 && Same(text[s - 1])) s--;
        int e = charIndex; while (e < text.Length && Same(text[e])) e++;
        return (s, e);
    }
}
