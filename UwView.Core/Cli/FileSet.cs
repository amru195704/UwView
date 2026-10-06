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
///   <item><b>ワイルドカードで広げたものは <c>.ignore</c>・<c>.gitignore</c> に従って外す</b>（ripgrep と同じ規則。
///         除外したフォルダーには降りない。名前を書いたファイルは外さない。指示書 §12・<see cref="IgnoreTree"/>）</item>
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
    /// <param name="IgnoredFiles">除外したファイルの数（<c>--files</c> で知らせる）。</param>
    /// <param name="IgnoredFolders">除外したフォルダーの数（中は見ていないので、中のファイルは数えていない）。</param>
    /// <param name="MissingByIgnore"><paramref name="Missing"/> のうち、除外がなければ当たっていた断片。</param>
    /// <param name="ExtraRules">
    /// <c>--ignore-file</c> で足した規則が効いているか。効いているなら、知らせに <c>--no-ignore-files</c> も添える
    /// （<c>--no-ignore</c> は足した規則を止めないため。外部レビュー 2026-09-27）。
    /// </param>
    /// <param name="LinkedFiles">
    /// 外したシンボリックリンクのファイルの数。<c>--no-ignore</c> でも含めない（ripgrep も同じ。たどるのは <c>-L</c>）。
    /// </param>
    public readonly record struct Result(IReadOnlyList<string> Files, IReadOnlyList<string> Missing,
                                         int IgnoredFiles = 0, int IgnoredFolders = 0,
                                         IReadOnlyList<string>? MissingByIgnore = null, bool ExtraRules = false,
                                         int LinkedFiles = 0)
    {
        public int Ignored => IgnoredFiles + IgnoredFolders;

        private string HowToIncludeJa => ExtraRules
            ? "--no-ignore で含めます。--ignore-file で足した規則は --no-ignore-files で止めます"
            : "--no-ignore で含めます";

        private string HowToIncludeEn => ExtraRules
            ? "--no-ignore includes them; rules added with --ignore-file are stopped by --no-ignore-files"
            : "--no-ignore includes them";

        /// <summary><c>--files</c> の最後に出す知らせ（何も外していなければ null）。</summary>
        public string? IgnoredNotice(bool ja, string tool)
        {
            var parts = new List<string>();
            if (Ignored > 0)
                parts.Add(ja ? $"除外 {IgnoredFiles:N0} 本"
                               + (IgnoredFolders > 0 ? $"・フォルダー {IgnoredFolders:N0} 個（中は見ていません）" : "")
                               + $"（.gitignore ほか。{HowToIncludeJa}）"
                             : $"{IgnoredFiles:N0} files excluded"
                               + (IgnoredFolders > 0 ? $" and {IgnoredFolders:N0} folders not entered" : "")
                               + $" (.gitignore and others; {HowToIncludeEn})");
            if (LinkedFiles > 0)
                parts.Add(ja ? $"シンボリックリンク {LinkedFiles:N0} 本は対象にしません（ripgrep と同じ。名前で書けば探します）"
                             : $"{LinkedFiles:N0} symbolic links skipped (as ripgrep does; name a link to search it)");
            return parts.Count == 0 ? null : $"{tool}: " + string.Join(ja ? "／" : "; ", parts);
        }

        /// <summary>1件も当たらなかった断片の知らせ（除外のせいなら、そう添える）。</summary>
        public string MissingNotice(string fragment, bool ja, string tool)
            => MissingByIgnore?.Contains(fragment) == true
                ? ja ? $"{tool}: 1件も当たりません: {fragment}（.gitignore などで除外。{HowToIncludeJa}）"
                     : $"{tool}: nothing matched: {fragment} (excluded by .gitignore or the like; {HowToIncludeEn})"
                : ja ? $"{tool}: 1件も当たりません: {fragment}" : $"{tool}: nothing matched: {fragment}";
    }

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

    /// <summary>
    /// (A) が複数のファイル・ワイルドカードを含むか（単一ファイルの経路を変えないための判定）。
    /// <b>全体がそのまま実在するファイルの名前なら1本</b>とみる。名前に空白を含むファイル
    /// （<c>'my log.txt'</c>）を1本だけ書くと、空白で割れて見つからなかった（外部レビュー 2026-09-27）。
    /// カンマを含む指定（<c>'C:/Program Files/app/*.log,'</c> のように末尾に付けたものも）は複数の書き方とみる。
    /// </summary>
    public static bool IsMultiple(string specification)
    {
        if (!specification.Contains(',') && File.Exists(specification)) return false;
        return Split(specification).Length > 1 || HasWildcard(specification) || specification.Contains(',');
    }

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
    /// <param name="ignore">除外の設定（省略すれば <see cref="IgnoreOptions.Default"/>＝.ignore・.gitignore に従う）。</param>
    public static Result Expand(string specification, string? baseDirectory = null, IgnoreOptions? ignore = null)
    {
        string root = baseDirectory ?? Directory.GetCurrentDirectory();
        var files = new List<string>();
        var seen = new HashSet<string>(OperatingSystem.IsLinux() ? StringComparer.Ordinal
                                                                 : StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        var missingByIgnore = new List<string>();
        var walk = new WalkState(new IgnoreTree(ignore ?? IgnoreOptions.Default, root));

        // 全体がそのまま実在するファイルの名前なら、その1本（名前に空白を含むファイル。IsMultiple と同じ判定）
        if (!specification.Contains(',') && File.Exists(Path.IsPathRooted(specification) ? specification : Path.Combine(root, specification)))
            return new Result([specification], []);

        foreach (string fragment in Split(specification))
        {
            int ignoredBefore = walk.IgnoredFiles.Count + walk.IgnoredFolders.Count;
            var matched = ExpandOne(fragment, root, walk);
            if (matched.Count == 0)
            {
                missing.Add(fragment);
                if (walk.IgnoredFiles.Count + walk.IgnoredFolders.Count > ignoredBefore) missingByIgnore.Add(fragment);
                continue;
            }
            char separator = SeparatorOf(fragment);
            foreach (var (full, path) in matched)
                // 重複は先に出た側を採る。突き合わせは<b>展開の起点</b>から見た絶対パスで行う
                //（カレントから見ると、別の場所を起点に展開したときに同じものを二重に数える）
                if (seen.Add(full))
                    files.Add(AsWritten(path, separator));
        }
        var options = ignore ?? IgnoreOptions.Default;
        bool extraRules = options.ExtraEnabled && options.ExtraFiles is { Count: > 0 };
        return new Result(files, missing, walk.IgnoredFiles.Count, walk.IgnoredFolders.Count, missingByIgnore, extraRules,
                          walk.LinkedFiles.Count);
    }

    /// <summary>1回の展開で共有するもの（除外ファイルはフォルダーごとに1回だけ読む）。</summary>
    private sealed class WalkState(IgnoreTree ignore)
    {
        public IgnoreTree Ignore { get; } = ignore;
        public HashSet<string> IgnoredFiles { get; } = new(StringComparer.Ordinal);
        public HashSet<string> IgnoredFolders { get; } = new(StringComparer.Ordinal);
        public HashSet<string> LinkedFiles { get; } = new(StringComparer.Ordinal);
        /// <summary>読んだ（読みかけの）フォルダー。下のフォルダーは見つけた時点で別スレッドで先に読み始める。</summary>
        public System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Listing>> Listings { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// 書かれたとおりの区切り文字（<c>osm/x</c> と書いたら <c>/</c> のまま返す）。
    ///
    /// Windows では OS の区切りが <c>\</c> なので、そのまま出すと
    /// <c>osm/japan-dv-ac</c> と指定したのに <c>osm\japan-dv-ac:12\t…</c> と出て、
    /// ほかの道具（シェルの展開結果や以前の出力）と突き合わせたときに食い違う
    /// （9.22修正: Windows の比較テストで「不一致」）。
    /// <b>利用者が書いた形で返す</b>ことにして、突き合わせが成り立つようにする。
    /// </summary>
    private static char SeparatorOf(string fragment)
        // Unix では \ は区切りではなく<b>普通の文字</b>なので、書いたとおりに返すと開けないパスになる。
        // 書き分けを許すのは Windows だけ（そこでは / も \ もどちらも開ける）
        => OperatingSystem.IsWindows() && fragment.Contains('\\') && !fragment.Contains('/') ? '\\' : '/';

    private static string AsWritten(string path, char separator)
        => separator == '/' ? path.Replace('\\', '/') : path.Replace('/', '\\');

    /// <summary>断片1つぶん（絶対パスと、返す形のパス）。ワイルドカードが無ければそのファイル、あれば名前順に展開する。</summary>
    private static List<(string Full, string Path)> ExpandOne(string fragment, string root, WalkState walk)
    {
        string spec = fragment.Replace('\\', Path.DirectorySeparatorChar)
                              .Replace('/', Path.DirectorySeparatorChar);
        if (!HasWildcard(spec))
        {
            string path = Path.IsPathRooted(spec) ? spec : Path.Combine(root, spec);
            return File.Exists(path) ? [(Path.GetFullPath(spec, root), spec)] : [];
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
        // 先頭のワイルドカードの無い段（名前を書いたフォルダー）は除外しない（ripgrep に渡したフォルダーと同じ）
        string full = Path.GetFullPath(start);
        // ** で下を全部たどり、除外の決まりも無いなら、下のフォルダーの一覧を見つけた先から並べて読んでおく
        Prefetch(full, walk, cascade: segments[first] == "**" && !walk.Ignore.MayIgnoreBelow(full));
        try { Walk(full, segments, first, found, walk); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }

        // 自分たちが作った派生ファイル（.uwvz など）は、ワイルドカードの対象にしない。
        // 拾ったパスは列挙で作った絶対パスなので、正規化し直さない（8.6 万本で1本ずつ GetFullPath すると重い）
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var list = found.Where(path => !IsDerived(path))
                        .Select(path => (path, path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : path))
                        .ToList();
        list.Sort((a, b) => string.CompareOrdinal(a.Item2, b.Item2));   // 名前順（ロケールに依存しないバイト順）
        return list;
    }

    private static bool IgnoreCase => !OperatingSystem.IsLinux();

    /// <summary>
    /// 1段ずつ照合する（<c>*</c> <c>?</c> は1段の中だけ・<c>**</c> は0段以上）。
    ///
    /// 隠しファイル・隠しフォルダーは見ない（.NET の既定。Unix は <c>.</c> で始まる名前）。
    /// シンボリックリンクは、フォルダーには下りず（ループを避ける）、ファイルも対象にしない
    /// （ripgrep と同じ。同じ中身を2回数えない。名前で書いたリンクは探す。2026-10-02）。
    /// </summary>
    /// 除外（.ignore・.gitignore）に当たるファイルは拾わず、当たるフォルダーには降りない（指示書 §12）。
    /// </summary>
    private static void Walk(string directory, string[] segments, int index, HashSet<string> found, WalkState walk)
    {
        string segment = segments[index];
        bool last = index == segments.Length - 1;

        if (segment == "**")
        {
            if (last)
            {
                // dir/** … dir 以下のすべてのファイル
                foreach (string file in Files(directory, "*", walk)) found.Add(file);
                foreach (string sub in Directories(directory, "*", walk)) Walk(sub, segments, index, found, walk);
                return;
            }
            Walk(directory, segments, index + 1, found, walk);                     // 0段
            foreach (string sub in Directories(directory, "*", walk)) Walk(sub, segments, index, found, walk);   // 1段以上
            return;
        }

        if (last)
        {
            foreach (string file in Files(directory, segment, walk)) found.Add(file);
            return;
        }
        foreach (string sub in Directories(directory, segment, walk)) Walk(sub, segments, index + 1, found, walk);
    }

    /// <summary>1つのフォルダーの中身（名前と絶対パス）。隠し・リンクは除いてある。</summary>
    private sealed class Listing
    {
        public readonly List<(string Name, string Path)> Files = [];
        public readonly List<(string Name, string Path)> Directories = [];
        public readonly List<(string Name, string Path)> LinkedFiles = [];
        /// <summary>.gitignore・.ignore・.git がある（ここから下は除外の決まりがあるかもしれない）。</summary>
        public bool HasIgnoreFiles;
        /// <summary>下のフォルダーの一覧を、見つけた先から読んでおいてよい（<see cref="ListingOf"/>）。</summary>
        public bool CascadeBelow;
    }

    /// <summary>
    /// リンクを外して読む。mac・Linux ではリンクかどうかをフォルダーの一覧（d_type）だけで見分けられるので、
    /// ファイルごとに属性を読まない（属性を読むと1本ずつ lstat が走る。8.6 万本で約 0.4 秒。2026-10-02）。
    /// </summary>
    private static readonly EnumerationOptions WithoutLinks = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = OperatingSystem.IsWindows()
            ? FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System
            : FileAttributes.ReparsePoint,
    };

    /// <summary>リンクも含めて数える（件数が違えば、そのフォルダーにリンクがある）。</summary>
    private static readonly EnumerationOptions WithLinks = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = OperatingSystem.IsWindows() ? FileAttributes.Hidden | FileAttributes.System : 0,
    };

    /// <summary>
    /// 「.」で始まる名前は隠し（ripgrep と同じ・どの OS でも）。Windows はこれに隠し属性を付けないので、
    /// 属性だけで外すと .git などが対象に入る（Windows の全体テスト 2026-09-28）。
    /// </summary>
    private static bool IsDotName(ReadOnlySpan<char> name) => name.Length > 0 && name[0] == '.';

    /// <summary>
    /// フォルダーを1回だけ読む（<c>**</c> では同じフォルダーを段ごとに何度も見るので覚えておく）。
    /// 除外の判定は順に行うが、一覧を読むのは先読みで並列にする（8.6 万本で約 0.4 秒かかっていた。2026-10-02）。
    /// </summary>
    private static Listing Read(string directory, WalkState walk) => ListingOf(directory, walk, cascade: false).Value;

    /// <param name="cascade">
    /// 読んだら、その下のフォルダーも続けて先読みする（除外の決まりのあるフォルダーで止める）。
    /// 下りる番が来てから頼むと、深い枝で読み終わりを待つことになる（カーネル 6 万本の一覧で 0.34 → 0.24 秒。2026-10-06）。
    /// 読むのは一覧だけで、除外の判定は今までどおりたどる側でする（結果は変えない）。
    /// </param>
    private static Lazy<Listing> ListingOf(string directory, WalkState walk, bool cascade)
        => walk.Listings.GetOrAdd(directory, d => new Lazy<Listing>(() =>
        {
            var listing = ReadNow(d);
            listing.CascadeBelow = cascade && !listing.HasIgnoreFiles;
            if (listing.CascadeBelow)
                foreach (var (_, sub) in listing.Directories) Prefetch(sub, walk, cascade: true);
            return listing;
        }));

    /// <summary>あとで下りるフォルダーを、別スレッドで先に読んでおく。</summary>
    private static void Prefetch(string directory, WalkState walk, bool cascade)
    {
        var lazy = ListingOf(directory, walk, cascade);
        if (!lazy.IsValueCreated) ThreadPool.UnsafeQueueUserWorkItem(l => _ = l.Value, lazy, preferLocal: false);
    }

    /// <summary>除外の決まりを置くファイル・フォルダーの名前（あれば、その下は先読みを深くしない）。</summary>
    private static bool IsIgnoreFileName(ReadOnlySpan<char> name)
        => name is ".gitignore" or ".ignore" or ".git";

    private static Listing ReadNow(string directory)
    {
        var listing = new Listing();
        int count = 0;
        try
        {
            var entries = new FileSystemEnumerable<(string Name, string Path, bool IsDirectory)>(directory,
                (ref FileSystemEntry e) => (e.FileName.ToString(), e.ToFullPath(), e.IsDirectory), WithoutLinks)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) =>
                {
                    if (!IsDotName(e.FileName)) return true;
                    if (IsIgnoreFileName(e.FileName)) listing.HasIgnoreFiles = true;
                    return false;
                },
            };
            foreach (var (name, path, isDirectory) in entries)
            {
                count++;
                (isDirectory ? listing.Directories : listing.Files).Add((name, path));
            }
            var all = new FileSystemEnumerable<byte>(directory, (ref FileSystemEntry _) => 0, WithLinks)
            { ShouldIncludePredicate = (ref FileSystemEntry e) => !IsDotName(e.FileName) };
            if (all.Count() != count) AddLinkedFiles(directory, listing);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return listing;
    }

    /// <summary>リンクがあるフォルダーだけ、属性を読んでリンクのファイルを拾う（お知らせの件数に使う）。</summary>
    private static void AddLinkedFiles(string directory, Listing listing)
    {
        foreach (var info in new DirectoryInfo(directory).EnumerateFiles("*", WithLinks))
            if (!IsDotName(info.Name) && (info.Attributes & FileAttributes.ReparsePoint) != 0)
                listing.LinkedFiles.Add((info.Name, info.FullName));
    }

    private static IEnumerable<string> Files(string directory, string pattern, WalkState walk)
    {
        var listing = Read(directory, walk);
        foreach (var (name, path) in listing.LinkedFiles)
            if (FileSystemName.MatchesSimpleExpression(pattern, name, IgnoreCase)) walk.LinkedFiles.Add(path);
        foreach (var (name, path) in listing.Files)
        {
            if (!FileSystemName.MatchesSimpleExpression(pattern, name, IgnoreCase)) continue;
            if (walk.Ignore.IsIgnored(path, isDirectory: false, directory)) { walk.IgnoredFiles.Add(path); continue; }
            yield return path;
        }
    }

    private static List<string> Directories(string directory, string pattern, WalkState walk)
    {
        var result = new List<string>();
        foreach (var (name, path) in Read(directory, walk).Directories)   // リンクのフォルダーは入っていない（下りない）
        {
            if (!FileSystemName.MatchesSimpleExpression(pattern, name, IgnoreCase)) continue;
            if (walk.Ignore.IsIgnored(path, isDirectory: true, directory)) { walk.IgnoredFolders.Add(path); continue; }
            result.Add(path);
            Prefetch(path, walk, cascade: false);
        }
        return result;
    }
}
