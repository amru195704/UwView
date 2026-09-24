using System.Text.Encodings.Web;
using System.Text.Json;

namespace UwView.Core.Cli;

/// <summary>
/// <c>--json</c> の出力（Wide Field v1.7.0 段階2・指示書 §2.4）。
///
/// <b>1行に1つのオブジェクト</b>を書く（JSON Lines）。配列で包まない——包むと、
/// 検索しながら流せず、最後まで貯めないと1文字も出せなくなる。
/// キー名は短く固定する: <c>file</c> / <c>n</c> / <c>line</c>、集計は <c>value</c> / <c>count</c>。
/// </summary>
public static class JsonLines
{
    /// <summary>
    /// 文字列を JSON の文字列にする（前後の引用符つき）。
    /// 出力は端末やファイルへそのまま流すので、日本語をエスケープしない（テ… にしない）。
    ///
    /// <see cref="JsonSerializer"/> は型をリフレクションで調べるので、NativeAOT（mac の uvf）では動かない。
    /// <see cref="JsonEncodedText"/> は文字列の逃がしだけをするので、どのビルドでも同じ結果になる。
    /// </summary>
    private static string Quote(string value)
        => "\"" + JsonEncodedText.Encode(value, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).Value + "\"";

    /// <summary>ヒット1行（<paramref name="file"/> は単一ファイルのときは null で省ける）。</summary>
    public static string Hit(string? file, long lineNumber, string text)
    {
        string body = $"\"n\":{lineNumber},\"line\":{Quote(text)}";
        return file is null ? $"{{{body}}}" : $"{{\"file\":{Quote(file)},{body}}}";
    }

    /// <summary>行番号を出さない（<c>--no-line-number</c>）ときのヒット1行。</summary>
    public static string HitWithoutNumber(string? file, string text)
        => file is null
            ? $"{{\"line\":{Quote(text)}}}"
            : $"{{\"file\":{Quote(file)},\"line\":{Quote(text)}}}";

    /// <summary>集計（<c>-uniq</c>）の1行。</summary>
    public static string Tally(string value, long count)
        => $"{{\"value\":{Quote(value)},\"count\":{count}}}";

    /// <summary>順序検索（<c>-seq</c>）の1件（語ごとの行番号の並び）。</summary>
    public static string Sequence(IEnumerable<long> lineNumbers)
        => $"{{\"lines\":[{string.Join(',', lineNumbers)}]}}";
}
