namespace UwView.Core.Cli;

/// <summary>
/// 除外の設定（<c>--no-ignore</c>・<c>--ignore-file</c>。指示書 WideField v1.7 §12）。uvf・uvp 共通。
/// </summary>
/// <param name="Enabled">false なら除外をしない（<c>--no-ignore</c>。足したファイルも止める。ripgrep と同じ）。</param>
/// <param name="ExtraFiles">足す除外ファイル（<c>--ignore-file</c>。一番弱い。後に書いたものほど強い）。</param>
public sealed record IgnoreOptions(bool Enabled = true, IReadOnlyList<string>? ExtraFiles = null)
{
    public static readonly IgnoreOptions Default = new();
    public static readonly IgnoreOptions None = new(Enabled: false);

    /// <summary>
    /// 引数から <c>--no-ignore</c>・<c>--ignore-file &lt;path&gt;</c> を取り除いて読む（位置は自由）。
    /// <c>--ignore-file</c> の後ろにファイルが無ければ、その文言を返す。どちらも無ければ null（既定）。
    /// </summary>
    public static (IgnoreOptions? Options, List<string> Remaining, string? ErrorJa, string? ErrorEn) Take(IReadOnlyList<string> argv)
    {
        var rest = new List<string>(argv.Count);
        var extra = new List<string>();
        bool enabled = true;
        for (int i = 0; i < argv.Count; i++)
        {
            if (argv[i] == "--no-ignore") { enabled = false; continue; }
            if (argv[i] == "--ignore-file")
            {
                if (i + 1 >= argv.Count)
                    return (Default, rest, "--ignore-file には除外のファイルを付けてください",
                            "--ignore-file needs a file");
                extra.Add(argv[++i]);
                continue;
            }
            rest.Add(argv[i]);
        }
        return (enabled && extra.Count == 0 ? null : new IgnoreOptions(enabled, extra), rest, null, null);
    }
}

/// <summary>1つの規則が当たった結果（git と同じく、ファイルの中では後の行が勝つ）。</summary>
public enum IgnoreMatch { None, Ignore, Include }

/// <summary>
/// gitignore の書式の除外ファイル1つ（<c>.gitignore</c>・<c>.ignore</c>・<c>.git/info/exclude</c>・全体設定・<c>--ignore-file</c>）。
///
/// 書式は git と同じ（指示書 §12.3）: 空行と <c>#</c> は無視・<c>!</c> で取り消し・末尾 <c>/</c> はフォルダーだけ・
/// 途中か先頭に <c>/</c> があればこのファイルのフォルダーから見たパス・無ければどの深さの名前にも当たる・
/// <c>* ? [a-z] [!a-z]</c>（<c>*</c> <c>?</c> は <c>/</c> をまたがない）・<c>**</c> は何段でも・<c>\</c> で次の1文字をそのまま。
/// 大文字・小文字は区別する（git・ripgrep と同じ）。
/// </summary>
public sealed class IgnoreFile
{
    private sealed record Rule(string[] Segments, bool Negate, bool DirectoryOnly);

    private readonly Rule[] _rules;

    /// <summary>このファイルのフォルダー（パスはここから見て照合する。区切りは <c>/</c>・末尾に <c>/</c> を付けて持つ）。</summary>
    public string Root { get; }

    public bool IsEmpty => _rules.Length == 0;

    private IgnoreFile(string root, Rule[] rules)
    {
        Root = Slash(root).TrimEnd('/') + "/";
        _rules = rules;
    }

    /// <summary>ファイルを読む（無い・読めないなら null）。</summary>
    public static IgnoreFile? Load(string path, string? root = null)
    {
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        var file = Parse(text, root ?? Path.GetDirectoryName(Path.GetFullPath(path))!);
        return file.IsEmpty ? null : file;
    }

    /// <summary>中身から作る（CRLF・先頭の BOM も受ける）。</summary>
    public static IgnoreFile Parse(string text, string root)
    {
        var rules = new List<Rule>();
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            // 行末の空白は無視（\ で逃がした空白は残す）
            int end = line.Length;
            while (end > 0 && line[end - 1] == ' ' && !(end >= 2 && line[end - 2] == '\\')) end--;
            line = line[..end];
            if (line.Length == 0 || line[0] == '#') continue;

            bool negate = false;
            if (line[0] == '!') { negate = true; line = line[1..]; }
            bool directoryOnly = false;
            while (line.EndsWith('/') && !line.EndsWith("\\/")) { directoryOnly = true; line = line[..^1]; }
            if (line.Length == 0) continue;

            bool anchored = line.Contains('/');
            line = line.TrimStart('/');
            if (line.Length == 0) continue;
            string[] segments = line.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (!anchored) segments = ["**", .. segments];   // 名前だけ＝どの深さにも当たる
            rules.Add(new Rule(segments, negate, directoryOnly));
        }
        return new IgnoreFile(root, [.. rules]);
    }

    /// <summary>
    /// このファイルの規則を当てる（<paramref name="fullPath"/> は絶対パス）。後の行ほど強い。
    /// このファイルのフォルダーの中ならそこから見たパスで、外ならパス全体で照らす（ripgrep と同じ。
    /// <c>--ignore-file</c> で渡した別の場所のファイルの <c>*.md</c> も、どこのファイルにも当たる）。
    /// </summary>
    public IgnoreMatch Match(string fullPath, bool isDirectory)
    {
        string path = Slash(fullPath);
        string relative = path.StartsWith(Root, StringComparison.Ordinal) ? path[Root.Length..] : path;
        string[] parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return IgnoreMatch.None;
        for (int i = _rules.Length - 1; i >= 0; i--)
        {
            var rule = _rules[i];
            if (rule.DirectoryOnly && !isDirectory) continue;
            if (Segments(rule.Segments, 0, parts, 0))
                return rule.Negate ? IgnoreMatch.Include : IgnoreMatch.Ignore;
        }
        return IgnoreMatch.None;
    }

    private static string Slash(string path) => OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;

    /// <summary>段ごとの照合（<c>**</c> は0段以上。末尾の <c>**</c> は「中のもの」なので1段以上）。</summary>
    private static bool Segments(string[] pattern, int p, string[] path, int s)
    {
        while (true)
        {
            if (p == pattern.Length) return s == path.Length;
            if (pattern[p] == "**")
            {
                if (p == pattern.Length - 1) return s < path.Length;
                for (int k = s; k <= path.Length; k++)
                    if (Segments(pattern, p + 1, path, k)) return true;
                return false;
            }
            if (s == path.Length || !Wildcard(pattern[p], path[s])) return false;
            p++;
            s++;
        }
    }

    /// <summary>1段の中の照合（<c>*</c>・<c>?</c>・<c>[…]</c>・<c>\</c>）。</summary>
    internal static bool Wildcard(string pattern, string name)
    {
        int p = 0, n = 0, starP = -1, starN = 0;
        while (n < name.Length)
        {
            if (p < pattern.Length)
            {
                char c = pattern[p];
                if (c == '*')
                {
                    while (p < pattern.Length && pattern[p] == '*') p++;   // ** は1段の中では * と同じ
                    starP = p;
                    starN = n;
                    continue;
                }
                if (c == '?') { p++; n++; continue; }
                if (c == '[' && Class(pattern, p, name[n]) is { } next)
                {
                    if (next.Matched) { p = next.End; n++; continue; }
                }
                else
                {
                    char literal = c;
                    int width = 1;
                    if (c == '\\' && p + 1 < pattern.Length) { literal = pattern[p + 1]; width = 2; }
                    if (literal == name[n]) { p += width; n++; continue; }
                }
            }
            if (starP < 0) return false;
            p = starP;
            n = ++starN;
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    /// <summary><c>[…]</c> を1文字に当てる。閉じていなければ null（<c>[</c> を文字として扱う）。</summary>
    private static (bool Matched, int End)? Class(string pattern, int start, char c)
    {
        int i = start + 1;
        bool negate = i < pattern.Length && pattern[i] is '!' or '^';
        if (negate) i++;
        bool matched = false, first = true;
        while (i < pattern.Length && (pattern[i] != ']' || first))
        {
            first = false;
            char low = pattern[i];
            if (low == '\\' && i + 1 < pattern.Length) low = pattern[++i];
            char high = low;
            if (i + 2 < pattern.Length && pattern[i + 1] == '-' && pattern[i + 2] != ']')
            {
                high = pattern[i + 2];
                if (high == '\\' && i + 3 < pattern.Length) { high = pattern[i + 3]; i++; }
                i += 2;
            }
            if (c >= low && c <= high) matched = true;
            i++;
        }
        if (i >= pattern.Length) return null;
        return (matched != negate, i + 1);
    }
}

/// <summary>
/// フォルダーごとの除外ファイルを束ね、ripgrep と同じ強さの順で判定する（指示書 §12.1・§12.2）。
///
/// 強さ（先に当たったものが勝つ）: <c>.ignore</c>（深いフォルダーから）→ <c>.gitignore</c>（深いフォルダーから・
/// git のリポジトリの根まで）→ <c>.git/info/exclude</c> → git の全体設定 → <c>--ignore-file</c>（後に書いたものから）。
/// <c>.gitignore</c>・<c>exclude</c>・全体設定は、どこかの親に <c>.git</c> があるときだけ効く。
/// 除外ファイルはフォルダーごとに1回だけ読む。除外ファイルが1つも無ければ判定は素通り（今と同じ速さ）。
/// </summary>
public sealed class IgnoreTree
{
    private sealed class Node
    {
        public Node? Parent;
        public IgnoreFile? Ignore, GitIgnore, Exclude;
        public bool HasGit;
        public bool AnyGit;          // 自分か親のどこかに .git がある
        public bool AnyRules;        // 自分か親のどこかに除外ファイルがある
    }

    private readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal);
    private readonly IgnoreFile? _global;
    private readonly IgnoreFile[] _extra;
    private readonly bool _enabled;

    public IgnoreTree(IgnoreOptions options, string? workingDirectory = null)
    {
        _enabled = options.Enabled;
        if (!_enabled) { _extra = []; return; }
        string cwd = Path.GetFullPath(workingDirectory ?? Directory.GetCurrentDirectory());
        _global = GlobalExcludesFile() is { } global ? IgnoreFile.Load(global, cwd) : null;
        _extra = [.. (options.ExtraFiles ?? []).Select(f => IgnoreFile.Load(Path.GetFullPath(f, cwd))).OfType<IgnoreFile>()];
    }

    /// <summary>
    /// 除外するか。<paramref name="fullPath"/> は絶対パス、<paramref name="parentDirectory"/> はそれが入っているフォルダー（絶対パス）。
    /// </summary>
    public bool IsIgnored(string fullPath, bool isDirectory, string parentDirectory)
    {
        if (!_enabled) return false;
        var node = NodeFor(parentDirectory);
        if (!node.AnyRules && _global is null && _extra.Length == 0) return false;

        IgnoreMatch ignore = IgnoreMatch.None, git = IgnoreMatch.None, exclude = IgnoreMatch.None;
        bool sawGit = false;
        for (var n = node; n is not null; n = n.Parent)
        {
            if (ignore == IgnoreMatch.None && n.Ignore is { } i) ignore = i.Match(fullPath, isDirectory);
            if (node.AnyGit && !sawGit)
            {
                if (git == IgnoreMatch.None && n.GitIgnore is { } g) git = g.Match(fullPath, isDirectory);
                if (exclude == IgnoreMatch.None && n.Exclude is { } x) exclude = x.Match(fullPath, isDirectory);
            }
            sawGit |= n.HasGit;
        }
        var result = ignore != IgnoreMatch.None ? ignore
                   : git != IgnoreMatch.None ? git
                   : exclude != IgnoreMatch.None ? exclude
                   : node.AnyGit && _global is not null ? _global.Match(fullPath, isDirectory)
                   : IgnoreMatch.None;
        for (int k = _extra.Length - 1; result == IgnoreMatch.None && k >= 0; k--)
            result = _extra[k].Match(fullPath, isDirectory);
        return result == IgnoreMatch.Ignore;
    }

    private Node NodeFor(string directory)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (_nodes.TryGetValue(full, out var cached)) return cached;

        string? parentPath = Path.GetDirectoryName(full);
        var parent = parentPath is null || parentPath == full ? null : NodeFor(parentPath);
        string git = Path.Combine(full, ".git");
        var node = new Node
        {
            Parent = parent,
            Ignore = LoadIfExists(Path.Combine(full, ".ignore"), full),
            GitIgnore = LoadIfExists(Path.Combine(full, ".gitignore"), full),
            HasGit = Directory.Exists(git) || File.Exists(git),
        };
        if (Directory.Exists(git)) node.Exclude = LoadIfExists(Path.Combine(git, "info", "exclude"), full);
        node.AnyGit = node.HasGit || parent is { AnyGit: true };
        node.AnyRules = node.Ignore is not null || node.GitIgnore is not null || node.Exclude is not null
                        || parent is { AnyRules: true };
        _nodes[full] = node;
        return node;
    }

    private static IgnoreFile? LoadIfExists(string path, string root)
        => File.Exists(path) ? IgnoreFile.Load(path, root) : null;

    /// <summary>
    /// git の全体の除外ファイル: <c>~/.gitconfig</c> の <c>[core]</c> の <c>excludesFile</c>、無ければ
    /// <c>$XDG_CONFIG_HOME/git/ignore</c>（<c>~/.config/git/ignore</c>）。<c>include</c> などは追わない（指示書 §12.5）。
    /// </summary>
    public static string? GlobalExcludesFile()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        try
        {
            string config = Path.Combine(home, ".gitconfig");
            if (File.Exists(config))
            {
                bool core = false;
                foreach (string raw in File.ReadLines(config))
                {
                    string line = raw.Trim();
                    if (line.StartsWith('[')) { core = line.StartsWith("[core", StringComparison.OrdinalIgnoreCase); continue; }
                    if (!core) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0 || !line[..eq].Trim().Equals("excludesfile", StringComparison.OrdinalIgnoreCase)) continue;
                    string value = line[(eq + 1)..].Trim().Trim('"');
                    if (value.StartsWith("~/")) value = Path.Combine(home, value[2..]);
                    return value.Length > 0 ? value : null;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        string xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(home, ".config");
        string fallback = Path.Combine(xdg, "git", "ignore");
        return File.Exists(fallback) ? fallback : null;
    }
}
