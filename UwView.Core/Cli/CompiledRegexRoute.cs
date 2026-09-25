using System.Runtime.CompilerServices;

namespace UwView.Core.Cli;

/// <summary>
/// 正規表現を全行に当てる大きな検索を、JIT で動く本体（画面のアプリ）に任せるかの判断（mac の NativeAOT の CLI 用）。
///
/// NativeAOT は実行時にコードを作れないので、<c>RegexOptions.Compiled</c> が効かず、正規表現が解釈実行になる。
/// 必須の文字列で候補行を先に絞れない正規表現（<c>-E -v '^ +&lt;'</c> など）は、全行に正規表現を当てるので
/// 約 2 倍遅くなった（3G: 2.75 → 5.58 秒。管理部の全体テスト 2026-09-25 で発覚）。
/// 絞れる正規表現は当てる行が少なく、NativeAOT のままのほうが起動が速い分だけ得。
/// NonBacktracking は速くならなかった（パターンによってはさらに遅い）。
/// </summary>
public static class CompiledRegexRoute
{
    /// <summary>
    /// 本体に任せる入力の大きさ（展開後の目安）。NativeAOT で余計にかかる時間は約 1.1 ミリ秒/MB（3G 実測）で、
    /// 本体の起動（約 0.15 秒）と釣り合うのが 130MB 前後。余裕をみてこの大きさから。
    /// </summary>
    public const long MinTextBytes = 256L << 20;

    /// <summary>この実行ファイルは正規表現をコンパイルできない（NativeAOT）。</summary>
    public static bool Interpreted => !RuntimeFeature.IsDynamicCodeCompiled;

    /// <summary>必須の文字列で候補行を絞れない正規表現か（全行に正規表現を当てることになる）。</summary>
    public static bool ScansEveryLine(string pattern)
        => RegexLiterals.Extract(pattern, ignoreCase: false) is null;

    /// <summary>uvf の引数から判断する。</summary>
    public static bool ForUvf(IReadOnlyList<string> argv)
    {
        if (!Interpreted) return false;
        var (inv, _, _) = UvfCli.Parse(argv);
        return inv is { Regex: true, Pattern: { } pattern, File: { Length: > 0 } file }
               && ScansEveryLine(pattern)
               && TextBytes(file) >= MinTextBytes;
    }

    /// <summary>
    /// 入力（1ファイル・複数ファイルの指定）の展開後のおおよその大きさ。<paramref name="sizeOf"/> が null を返したものは
    /// <see cref="Estimate"/> で見積もる。目安を超えたところで数えるのをやめる。
    /// </summary>
    public static long TextBytes(string specification, Func<string, long?>? sizeOf = null)
    {
        IReadOnlyList<string> files = FileSet.IsMultiple(specification)
            ? FileSet.Expand(specification).Files
            : [specification];
        long total = 0;
        foreach (string file in files)
        {
            total += sizeOf?.Invoke(file) ?? Estimate(file);
            if (total >= MinTextBytes) break;
        }
        return total;
    }

    /// <summary>1ファイルの展開後のおおよその大きさ。圧縮は大きさの 8 倍とみる（テキストはおおむね 1/8〜1/10 に縮む）。</summary>
    public static long Estimate(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return 0;
            return CompressedInput.Probe(path).Kind == CompressedKind.None ? info.Length : info.Length * 8;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }
}
