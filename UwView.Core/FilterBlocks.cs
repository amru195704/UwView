namespace UwView.Core;

/// <summary>
/// フィルタ結果ポップアップの文脈ブロック（機能修正指示書_検索フィルタPopup.md §4）。
/// [StartLine, EndLine] は表示する行範囲（両端含む）、HitLines はその中のヒット行（昇順）。
/// </summary>
public sealed record FilterBlock(long StartLine, long EndLine, IReadOnlyList<long> HitLines)
{
    public long LineCount => EndLine - StartLine + 1;
}

/// <summary>
/// ヒット行番号列から表示・保存共用の文脈ブロック列を作る。
/// 各ヒット L に [L−N, L+N]（ファイル端でクランプ）を割り当て、
/// 範囲が重なる・隣接するブロックは1つに結合して行の二重表示を防ぐ。
/// </summary>
public static class FilterBlocks
{
    /// <param name="hitLines">ヒット行番号（昇順。重複可＝同一行の複数ヒットは1つに畳む）。</param>
    /// <param name="contextN">前後に表示する行数（0＝ヒット行のみ）。</param>
    /// <param name="totalLines">総行数（クランプ用）。</param>
    public static List<FilterBlock> Build(IReadOnlyList<long> hitLines, int contextN, long totalLines)
    {
        if (contextN < 0) throw new ArgumentOutOfRangeException(nameof(contextN));
        var blocks = new List<FilterBlock>();
        if (hitLines.Count == 0 || totalLines <= 0) return blocks;

        long start = -1, end = -1;
        var hits = new List<long>();

        foreach (long hit in hitLines)
        {
            long line = Math.Clamp(hit, 0, totalLines - 1);
            if (hits.Count > 0 && hits[^1] == line) continue; // 同一行の重複ヒット

            long s = Math.Max(0, line - contextN);
            long e = Math.Min(totalLines - 1, line + contextN);

            if (start < 0)
            {
                start = s; end = e;
            }
            else if (s <= end + 1)
            {
                end = Math.Max(end, e); // 重なり・隣接 → 結合
            }
            else
            {
                blocks.Add(new FilterBlock(start, end, hits.ToArray()));
                hits.Clear();
                start = s; end = e;
            }
            hits.Add(line);
        }
        blocks.Add(new FilterBlock(start, end, hits.ToArray()));
        return blocks;
    }
}
