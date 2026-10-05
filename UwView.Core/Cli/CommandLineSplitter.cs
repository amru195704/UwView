using System.Text;

namespace UwView.Core.Cli;

/// <summary>
/// 「コマンドライン」ダイアログ（v1.8.0 Finder Scope §7.2）の入力欄を、コマンドの引数の列（argv）にする係と、その逆。
///
/// 分け方は POSIX のシェルと同じ：空白で区切る、<c>'…'</c> の中はそのまま、<c>"…"</c> の中は <c>\"</c> <c>\\</c> <c>\$</c> <c>\`</c> だけを逃がす、
/// 引用の外の <c>\</c> は次の 1 文字をそのまま。<b>展開（<c>*</c> <c>?</c> <c>$</c> <c>~</c>）はしない</b>——ファイルの指定はコマンドの中で広げるので、
/// シェルに任せたときと同じ argv になる。OS で規則を変えない（GUI の中ではシェルを通らない）。
///
/// 逆向き（<see cref="Join"/>）は、そのままでは別の意味になる引数だけ <c>'…'</c> で囲む。
/// 「分ける → 組み立てる → 分ける」で同じ argv に戻る（完了条件 #4）。
/// </summary>
public static class CommandLineSplitter
{
    /// <summary>分けた結果。<see cref="Error"/> が null でなければ書き方の誤り（日英）。</summary>
    public sealed record Result(IReadOnlyList<string> Args, (string Ja, string En)? Error);

    /// <summary>入力欄の文字を引数の列に分ける。</summary>
    public static Result Split(string? text)
    {
        var args = new List<string>();
        if (string.IsNullOrEmpty(text)) return new Result(args, null);
        var cur = new StringBuilder();
        bool inArg = false;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c is ' ' or '\t' or '\n' or '\r')   // シェルの区切り（全角空白 U+3000 では区切らない）
            {
                if (inArg) { args.Add(cur.ToString()); cur.Clear(); inArg = false; }
                i++;
                continue;
            }
            inArg = true;
            switch (c)
            {
                case '\'':
                {
                    int end = text.IndexOf('\'', i + 1);
                    if (end < 0) return Unclosed('\'');
                    cur.Append(text, i + 1, end - i - 1);
                    i = end + 1;
                    break;
                }
                case '"':
                {
                    i++;
                    while (true)
                    {
                        if (i >= text.Length) return Unclosed('"');
                        char d = text[i];
                        if (d == '"') { i++; break; }
                        if (d == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\' or '$' or '`')
                        {
                            cur.Append(text[i + 1]);
                            i += 2;
                            continue;
                        }
                        if (d == '\\' && i + 1 < text.Length && text[i + 1] == '\n') { i += 2; continue; }   // 行の続き
                        cur.Append(d);
                        i++;
                    }
                    break;
                }
                case '\\':
                    if (i + 1 >= text.Length) { cur.Append('\\'); i++; break; }   // 末尾の \ はそのまま
                    if (text[i + 1] == '\n') { i += 2; break; }                    // 行の続き
                    cur.Append(text[i + 1]);
                    i += 2;
                    break;
                default:
                    cur.Append(c);
                    i++;
                    break;
            }
        }
        if (inArg) args.Add(cur.ToString());
        return new Result(args, null);
    }

    private static Result Unclosed(char quote) => new([], (
        $"引用符 {quote} が閉じていません",
        $"The quote {quote} is not closed"));

    /// <summary>引数の列を、シェルに貼れる 1 行にする（必要な引数だけ <c>'…'</c> で囲む）。</summary>
    public static string Join(IEnumerable<string> args) => string.Join(' ', args.Select(Quote));

    /// <summary>1 つの引数を、シェルでそのまま 1 つの引数に戻る形にする。</summary>
    public static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.All(IsPlain)) return arg;
        // ' は '\'' に（引用を閉じて、逃がした ' を挟み、また開く）
        return "'" + arg.Replace("'", "'\\''") + "'";
    }

    /// <summary>
    /// 引用しなくてもシェルが変えない文字。ASCII の英数字と、意味を持たない記号、ASCII でない文字（日本語など）。
    /// <c>=</c> <c>,</c> <c>:</c> <c>@</c> <c>%</c> <c>+</c> <c>/</c> <c>.</c> <c>-</c> <c>_</c> は、引数の途中でも先頭でもシェルが変えない。
    /// </summary>
    private static bool IsPlain(char c)
        => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'
                or '-' or '_' or '.' or '/' or ',' or ':' or '=' or '@' or '%' or '+'
           || c > 0x7F && !char.IsWhiteSpace(c) && !char.IsControl(c);   // 全角空白は区切りにならないが、見分けにくいので囲む
}
