namespace UwView.Core;

/// <summary>
/// 中身がテキストかどうかの見分け（オーナー指摘 2026-09-24「gz はテキストを圧縮したものか確かめているか」）。
///
/// 圧縮されたものは<b>展開してみるまで中身が分からない</b>。画像やデータベースを gzip したものを
/// テキストとして索引にすると、検索は当たらず行数も意味を持たない——時間とディスクだけを使う。
/// gz・zip のエントリで<b>同じ判定</b>を使う（片方だけ通る、という食い違いを作らない）。
///
/// 判定は「0 バイトがあるか」。UTF-8・Shift_JIS・EUC-JP に 0 バイトは出てこない。
/// UTF-16／UTF-32 は1文字おきに 0 バイトが入るので、<b>BOM があれば先にテキストと決める</b>
/// （このアプリが読めるのは BOM つきの UTF-16／UTF-32 だけ。<see cref="EncodingDetector"/> と同じ前提）。
/// </summary>
public static class TextProbe
{
    /// <summary>先頭を見て「テキストではない」と言い切れるか（分からないときは false＝テキスト扱い）。</summary>
    public static bool LooksBinary(ReadOnlySpan<byte> head)
    {
        if (head.IsEmpty) return false;
        if (HasUnicodeBom(head)) return false;
        return head.IndexOf((byte)0) >= 0;
    }

    /// <summary>
    /// UTF-16／UTF-32 で始まるか（BOM で見る）。
    ///
    /// 複数を1本に束ねるときは<b>改行が1バイト</b>でなければ継ぎ目を作れないので、
    /// これに当たるものは束ねられない（1つだけで開くぶんには読める）。
    /// </summary>
    public static bool IsWideUnicode(ReadOnlySpan<byte> head)
        => (head.Length >= 2 && head[0] == 0xFF && head[1] == 0xFE)
        || (head.Length >= 2 && head[0] == 0xFE && head[1] == 0xFF)
        || (head.Length >= 4 && head[0] == 0x00 && head[1] == 0x00 && head[2] == 0xFE && head[3] == 0xFF);

    /// <summary>UTF-8／UTF-16／UTF-32 の BOM で始まるか。</summary>
    private static bool HasUnicodeBom(ReadOnlySpan<byte> head)
        => (head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)      // UTF-8
        || (head.Length >= 2 && head[0] == 0xFF && head[1] == 0xFE)                          // UTF-16LE / UTF-32LE
        || (head.Length >= 2 && head[0] == 0xFE && head[1] == 0xFF)                          // UTF-16BE
        || (head.Length >= 4 && head[0] == 0x00 && head[1] == 0x00 && head[2] == 0xFE && head[3] == 0xFF);
}
