using System.IO.Enumeration;

namespace UwView.Core.Cli;

/// <summary>
/// 指定文字列 (A) からファイル一覧を作る（Wide Field v1.7.0 段階3・指示書 §2.2・§2.2b）。
///
/// 決めごと:
/// <list type="bullet">
///   <item><b>引用符で囲むのは必須。</b>シェルに展開させない（展開されると (A) が届かず、
///         uvp が <c>.uwvz</c> の名前を決められない）</item>
///   <item><b>区切りは空白とカンマの両方。</b>名前に空白を含むパス（<c>Program Files</c>）はカンマ区切りで書く</item>
///   <item><b>並びは「書いた順」→「名前順」</b>（ロケールに依存しないバイト順）。重複は先に出た側を採る</item>
///   <item>ワイルドカードは分けて考えない。<c>*/*.log</c> も <c>**</c> も、この仕組みが解釈できる形はすべて受ける</item>
///   <item>パス区切りは <c>/</c> と <c>\</c> の両方を受ける（Windows で <c>\</c> を書く人がいる）</item>
/// </list>
/// </summary>
public static class FileSet
{
    /// <summary>件数がこれを超えたら、止めずに<b>知らせる</b>（ログ調査では数千件の指定も普通に起こる）。</summary>
    public const int ManyFiles = 1000;

    /// <summary>件数の上限を変える環境変数（知らせる目安を動かすだけで、止めることはしない）。</summary>
    public const string ManyFilesEnvironmentVariable = "UVF_MANY_FILES";

    /// <param name="Files">見つかったファイル（順序は §2.2 の規則どおり）。</param>
    /// <param name="Missing">1件も当たらなかった断片（そのまま利用者に見せる）。</param>
    public readonly record struct Result(IReadOnlyList<string> Files, IReadOnlyList<string> Missing);

    /// <summary>
    /// (A) を断片に割る。
    ///
    /// <b>カンマがあれば、カンマだけで割る。</b>こうしないと <c>Program Files/app.log</c> のように
    /// 名前に空白を含むパスを書けない（空白でも割ると4つに割れて全部外れる。指示書 §2.2b）。
    /// カンマが無いときは空白で割る。
    /// </summary>
    public static string[] Split(string specification)
        => specification.Contains(',')
            ? specification.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : specification.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>(A) が複数のファイル・ワイルドカードを含むか（単一ファイルの経路を変えないための判定）。</summary>
    public static bool IsMultiple(string specification)
        => Split(specification).Length > 1 || HasWildcard(specification);

    /// <summary>ワイルドカードを含むか。</summary>
    public static bool HasWildcard(string text) => text.AsSpan().IndexOfAny('*', '?', '[') >= 0;

    /// <summary>
    /// ワイルドカードの展開から外す拡張子（自分たちが作った派生ファイル）。
    ///
    /// <c>uvp '*' 語</c> は同じフォルダに <c>.uwvz</c> を作る。次に同じ指定で走らせると
    /// <b>自分が作った索引まで対象に含めてしまう</b>（中身は圧縮バイトなので、本文として束ねると壊れる）。
    /// 名指しで書いたときは外さない（<c>uvp '%a.log.uwvz' 語</c> は (B) を指す正しい使い方）。
    /// </summary>
    private static readonly string[] DerivedExtensions = [".uwvz", ".uwvidx"];

    private static bool IsDerived(string path)
        => DerivedExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// (A) を展開する。<paramref name="baseDirectory"/> は相対パスの起点（省略すればカレント）。
    /// </summary>
    public static Result Expand(string specification, string? baseDirectory = null)
    {
        string root = baseDirectory ?? Directory.GetCurrentDirectory();
        var files = new List<string>();
        var seen = new HashSet<string>(OperatingSystem.IsLinux() ? StringComparer.Ordinal
                                                                 : StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (string fragment in Split(specification))
        {
            var matched = ExpandOne(fragment, root);
            if (matched.Count == 0) { missing.Add(fragment); continue; }
            char separator = SeparatorOf(fragment);
            foreach (string path in matched)
                if (seen.Add(Path.GetFullPath(path)))     // 重複は先に出た側を採る
                    files.Add(AsWritten(path, separator));
        }
        return new Result(files, missing);
    }

    /// <summary>
    /// 書かれたとおりの区切り文字（<c>osm/x</c> と書いたら <c>/</c> のまま返す）。
    ///
    /// Windows では OS の区切りが <c>\</c> なので、そのまま出すと
    /// <c>osm/japan-dv-ac</c> と指定したのに <c>osm\japan-dv-ac:12\t…</c> と出て、
    /// ほかの道具（シェルの展開結果や以前の出力）と突き合わせたときに食い違う
    /// （オーナー報告 2026-09-22: Windows の比較テストで「不一致」）。
    /// <b>利用者が書いた形で返す</b>ことにして、突き合わせが成り立つようにする。
    /// </summary>
    private static char SeparatorOf(string fragment)
        // Unix では \ は区切りではなく<b>普通の文字</b>なので、書いたとおりに返すと開けないパスになる。
        // 書き分けを許すのは Windows だけ（そこでは / も \ もどちらも開ける）
        => OperatingSystem.IsWindows() && fragment.Contains('\\') && !fragment.Contains('/') ? '\\' : '/';

    private static string AsWritten(string path, char separator)
        => separator == '/' ? path.Replace('\\', '/') : path.Replace('/', '\\');

    /// <summary>断片1つぶん。ワイルドカードが無ければそのファイル、あれば名前順に展開する。</summary>
    private static List<string> ExpandOne(string fragment, string root)
    {
        string spec = fragment.Replace('\\', Path.DirectorySeparatorChar)
                              .Replace('/', Path.DirectorySeparatorChar);
        if (!HasWildcard(spec))
        {
            string path = Path.IsPathRooted(spec) ? spec : Path.Combine(root, spec);
            return File.Exists(path) ? [spec] : [];
        }

        // ワイルドカードは「どこまでが普通のフォルダか」で分けて、残りを照合に使う
        string directoryPart = Path.GetDirectoryName(spec) ?? "";
        string pattern = Path.GetFileName(spec);
        while (HasWildcard(directoryPart) is false && directoryPart.Length > 0
               && !Directory.Exists(Path.IsPathRooted(directoryPart) ? directoryPart : Path.Combine(root, directoryPart)))
            return [];   // 手前のフォルダが無い＝1件も当たらない

        string searchRoot = directoryPart.Length == 0 ? root
                          : Path.IsPathRooted(directoryPart) ? directoryPart
                          : Path.Combine(root, directoryPart);
        var options = new EnumerationOptions
        {
            MatchCasing = OperatingSystem.IsLinux() ? MatchCasing.CaseSensitive : MatchCasing.CaseInsensitive,
            RecurseSubdirectories = HasWildcard(directoryPart) || spec.Contains("**"),
            IgnoreInaccessible = true,
        };

        try
        {
            // 手前にワイルドカードがある（logs/*/app.log・**/x.log）ときは、下まで見てから形で絞る
            var found = options.RecurseSubdirectories
                ? Directory.EnumerateFiles(WildcardFreeRoot(searchRoot, root), "*", options)
                           .Where(path => Matches(path, spec, root))
                : Directory.EnumerateFiles(searchRoot, pattern, options);

            // 自分たちが作った派生ファイル（.uwvz など）は、ワイルドカードの対象にしない
            var list = found.Where(path => !IsDerived(path)).Select(path => Relative(path, root)).ToList();
            list.Sort(StringComparer.Ordinal);   // 名前順（ロケールに依存しないバイト順）
            return list;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>ワイルドカードが始まる手前までのフォルダ。</summary>
    private static string WildcardFreeRoot(string searchRoot, string root)
    {
        string current = searchRoot;
        while (HasWildcard(current))
            current = Path.GetDirectoryName(current) ?? "";
        return current.Length == 0 ? root : current;
    }

    /// <summary>このパスが (A) の断片の形に当てはまるか（フォルダ部分のワイルドカードも見る）。</summary>
    private static bool Matches(string path, string spec, string root)
    {
        string relative = Relative(path, root);
        string normalized = spec.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var comparison = OperatingSystem.IsLinux() ? MatchCasing.CaseSensitive : MatchCasing.CaseInsensitive;
        bool ignoreCase = comparison == MatchCasing.CaseInsensitive;

        // `**` は「間のフォルダは何段でもよい」。照合しやすい形に直してから当てる
        if (normalized.Contains("**"))
        {
            string tail = normalized[(normalized.LastIndexOf("**", StringComparison.Ordinal) + 2)..]
                          .TrimStart(Path.DirectorySeparatorChar);
            return FileSystemName.MatchesSimpleExpression(tail, Path.GetFileName(relative), ignoreCase);
        }
        return FileSystemName.MatchesSimpleExpression(normalized, relative, ignoreCase);
    }

    private static string Relative(string path, string root)
    {
        string full = Path.GetFullPath(path);
        string prefix = Path.GetFullPath(root);
        return full.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? full[(prefix.Length + 1)..] : full;
    }
}
