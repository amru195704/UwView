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
///   <item>ワイルドカードは <c>*</c>（1段の中の任意の文字の並び）・<c>?</c>（任意の1文字）・<c>**</c>（フォルダーを何段でも。0段も含む）。
///         <c>*</c> はフォルダーの区切りをまたがない（シェルと同じ。<c>*/*.log</c> はちょうど1段下）。
///         <c>[ ]</c> <c>{ }</c> は使えない（文字どおりの名前）。照合は段ごとに進め、必要な段だけ読む（指示書 §9）</item>
///   <item>隠しファイル・隠しフォルダーは対象外。シンボリックリンクのフォルダーはたどらない（ループを避ける）</item>
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

    /// <summary>ワイルドカードを含むか（<c>*</c> と <c>?</c>。<c>[ ]</c> は文字どおりの名前として扱う）。</summary>
    public static bool HasWildcard(string text) => text.AsSpan().IndexOfAny('*', '?') >= 0;

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
                // 重複は先に出た側を採る。突き合わせは<b>展開の起点</b>から見た絶対パスで行う
                //（カレントから見ると、別の場所を起点に展開したときに同じものを二重に数える）
                if (seen.Add(Path.GetFullPath(path, root)))
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

        // 起点（絶対パスならその根、相対なら root）と、段ごとの形に分ける。
        // ワイルドカードの無い先頭の段は、普通のフォルダーとしてそのままたどる（列挙しない）
        string start = Path.IsPathRooted(spec) ? Path.GetPathRoot(spec)! : root;
        string rest = Path.IsPathRooted(spec) ? spec[Path.GetPathRoot(spec)!.Length..] : spec;
        var segments = rest.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        int first = 0;
        while (first < segments.Length - 1 && !HasWildcard(segments[first]))
        {
            start = Path.Combine(start, segments[first]);
            first++;
        }
        if (!Directory.Exists(start)) return [];   // 手前のフォルダーが無い＝1件も当たらない

        var found = new HashSet<string>(StringComparer.Ordinal);
        try { Walk(start, segments, first, found); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }

        // 自分たちが作った派生ファイル（.uwvz など）は、ワイルドカードの対象にしない
        var list = found.Where(path => !IsDerived(path)).Select(path => Relative(path, root)).ToList();
        list.Sort(StringComparer.Ordinal);   // 名前順（ロケールに依存しないバイト順）
        return list;
    }

    private static bool IgnoreCase => !OperatingSystem.IsLinux();

    /// <summary>
    /// 1段ずつ照合する（<c>*</c> <c>?</c> は1段の中だけ・<c>**</c> は0段以上）。
    /// 隠しファイル・隠しフォルダーは見ない（.NET の既定。Unix は <c>.</c> で始まる名前）。
    /// シンボリックリンクのフォルダーは下りない（ループを避ける）。リンクのファイルは対象にする。
    /// </summary>
    private static void Walk(string directory, string[] segments, int index, HashSet<string> found)
    {
        string segment = segments[index];
        bool last = index == segments.Length - 1;

        if (segment == "**")
        {
            if (last)
            {
                // dir/** … dir 以下のすべてのファイル
                foreach (string file in Files(directory, "*")) found.Add(file);
                foreach (string sub in Directories(directory, "*")) Walk(sub, segments, index, found);
                return;
            }
            Walk(directory, segments, index + 1, found);                     // 0段
            foreach (string sub in Directories(directory, "*")) Walk(sub, segments, index, found);   // 1段以上
            return;
        }

        if (last)
        {
            foreach (string file in Files(directory, segment)) found.Add(file);
            return;
        }
        foreach (string sub in Directories(directory, segment)) Walk(sub, segments, index + 1, found);
    }

    private static readonly EnumerationOptions Listing = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.PlatformDefault,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    private static IEnumerable<string> Files(string directory, string pattern)
        => Directory.EnumerateFiles(directory, "*", Listing)
                    .Where(path => FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(path), IgnoreCase));

    private static IEnumerable<string> Directories(string directory, string pattern)
        => new DirectoryInfo(directory).EnumerateDirectories("*", Listing)
                                       .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0)   // リンクは下りない
                                       .Where(d => FileSystemName.MatchesSimpleExpression(pattern, d.Name, IgnoreCase))
                                       .Select(d => d.FullName);

    private static string Relative(string path, string root)
    {
        string full = Path.GetFullPath(path);
        string prefix = Path.GetFullPath(root);
        return full.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? full[(prefix.Length + 1)..] : full;
    }
}
