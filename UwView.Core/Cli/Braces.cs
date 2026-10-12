namespace UwView.Core.Cli;

/// <summary>
/// ファイルの指定の波かっこ <c>{a,b}</c>（「a か b」。指示書「ファイル指定の波かっこ」2026-10-11・オーナー裁定で波かっこだけ）。
/// <list type="bullet">
///   <item>展開してから、今までどおりワイルドカード（<c>*</c>・<c>?</c>・<c>**</c>）を当てる</item>
///   <item>空の選択肢（<c>{,.gz}</c>）・1 つの指定に複数・入れ子（<c>{a,{b,c}}</c>）を許す。並びは書いた順（bash と同じ）</item>
///   <item>カンマを含まない波かっこ（<c>{}</c>・<c>{a}</c>）と閉じていない波かっこは文字どおり（bash と同じ。今までの動作）</item>
///   <item>1 つの指定から <see cref="MaxAlternatives"/> 通りまで。超えたら <see cref="BraceLimitException"/></item>
///   <item><c>\{</c> のようなエスケープは作らない（<c>\</c> がパスの区切りと紛らわしい。文字どおりにしたいときは、
///         指定と同じ名前のファイルが実在すればそれを優先する。<see cref="FileSet"/>）</item>
/// </list>
/// </summary>
public static class Braces
{
    public const int MaxAlternatives = 1000;

    /// <summary>波かっこを含み、すべて閉じているか（<c>{</c> と <c>}</c> の対応が取れている）。</summary>
    public static bool Balanced(string text)
    {
        int depth = 0;
        bool any = false;
        foreach (char c in text)
        {
            if (c == '{') { depth++; any = true; }
            else if (c == '}')
            {
                if (depth == 0) return false;
                depth--;
            }
        }
        return any && depth == 0;
    }

    /// <summary>波かっこの外（いちばん上の段）に <paramref name="ch"/> があるか。</summary>
    public static bool HasTopLevel(string text, char ch)
    {
        int depth = 0;
        foreach (char c in text)
        {
            if (c == '{') depth++;
            else if (c == '}') depth--;
            else if (c == ch && depth == 0) return true;
        }
        return false;
    }

    /// <summary>波かっこの外の区切りで割る（前後の空白は取り、空の断片は捨てる）。</summary>
    public static string[] SplitTopLevel(string text, Func<char, bool> separator)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '{') depth++;
            else if (c == '}') depth--;
            else if (depth == 0 && separator(c))
            {
                Add(text[start..i]);
                start = i + 1;
            }
        }
        Add(text[start..]);
        return [.. parts];

        void Add(string part)
        {
            part = part.Trim();
            if (part.Length > 0) parts.Add(part);
        }
    }

    /// <summary>選択肢のある（中にカンマを持つ）波かっこを含むか。</summary>
    public static bool HasAlternatives(string text) => Balanced(text) && FindGroup(text, 0) is not null;

    /// <summary>
    /// 波かっこを展開する（書いた順。選択肢が無ければ <paramref name="text"/> そのもの 1 つ）。
    /// </summary>
    /// <exception cref="BraceLimitException"><see cref="MaxAlternatives"/> 通りを超えた。</exception>
    public static IReadOnlyList<string> Expand(string text)
    {
        if (!Balanced(text)) return [text];
        long count = Count(text);
        if (count > MaxAlternatives) throw new BraceLimitException(text, count);
        var result = new List<string>((int)count);
        ExpandInto(text, "", result);
        return result;
    }

    // 最初の「選択肢のある」波かっこ（開き・閉じの位置）。カンマの無い波かっこ（{}・{a}）は文字どおりなので飛ばす
    private static (int Open, int Close)? FindGroup(string text, int from)
    {
        for (int i = from; i < text.Length; i++)
        {
            if (text[i] != '{') continue;
            int depth = 0;
            bool comma = false;
            for (int j = i; j < text.Length; j++)
            {
                char c = text[j];
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        if (comma) return (i, j);
                        break;   // 文字どおりの波かっこ。その中に選択肢のある波かっこがあれば、次の { から見る
                    }
                }
                else if (c == ',' && depth == 1) comma = true;
            }
        }
        return null;
    }

    private static List<string> Items(string body)
    {
        var items = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (c == '{') depth++;
            else if (c == '}') depth--;
            else if (c == ',' && depth == 0)
            {
                items.Add(body[start..i]);
                start = i + 1;
            }
        }
        items.Add(body[start..]);
        return items;
    }

    // 展開せずに数を数える（上限を超える組み合わせを作り始める前に止めるため）
    private static long Count(string text)
    {
        if (FindGroup(text, 0) is not var (open, close)) return 1;
        long inner = 0;
        foreach (string item in Items(text[(open + 1)..close]))
        {
            inner += Count(item);
            if (inner > MaxAlternatives) return inner;
        }
        long rest = Count(text[(close + 1)..]);
        return Math.Min(inner * rest, long.MaxValue / 2);
    }

    private static void ExpandInto(string text, string prefix, List<string> result)
    {
        if (FindGroup(text, 0) is not var (open, close))
        {
            result.Add(prefix + text);
            return;
        }
        string head = prefix + text[..open];
        string tail = text[(close + 1)..];
        foreach (string item in Items(text[(open + 1)..close]))
            foreach (string expanded in Expand(item))
                ExpandInto(tail, head + expanded, result);
    }
}

/// <summary>波かっこの組み合わせが多すぎる（<see cref="Braces.MaxAlternatives"/> 通りを超えた）。</summary>
public sealed class BraceLimitException(string fragment, long count)
    : ArgumentException($"too many brace combinations ({count:N0}; up to {Braces.MaxAlternatives:N0}): {fragment}")
{
    public string Fragment { get; } = fragment;
    public long Count { get; } = count;

    /// <summary>知らせの文面（先頭の道具名は付けない。呼び手の Err が付ける）。</summary>
    public string MessageFor(bool ja)
        => ja ? $"波かっこの組み合わせが多すぎます（{Count:N0} 通り。{Braces.MaxAlternatives:N0} 通りまで）: {Fragment}"
              : $"too many brace combinations ({Count:N0}; up to {Braces.MaxAlternatives:N0}): {Fragment}";
}
