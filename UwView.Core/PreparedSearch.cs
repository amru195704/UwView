using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;

namespace UwView.Core;

/// <summary>
/// 検索の方式（外部レビュー 2026-10-04「整理の進め方」3）。どの検索（画面・uvf・uvp・置換）も、この 4 つのどれかで探す。
/// </summary>
public enum SearchMethod
{
    /// <summary>素の文字列を、バイト列のまま探す（デコードも正規表現も要らない）。</summary>
    Bytes,
    /// <summary>素の文字列（<c>-i</c>）を、ASCII の大小を畳んでバイトのまま探す。</summary>
    AsciiFold,
    /// <summary>手がかり（必ず含むバイト列）のある行だけをデコードして、正規表現を当てる。</summary>
    CandidateLines,
    /// <summary>全行をデコードして、正規表現を当てる。</summary>
    EveryLine,
}

/// <summary>
/// 検索 1 回ぶんの準備のうち、<b>文字コードに依らない</b>部分（外部レビュー 2026-10-04「整理の進め方」2）。
/// 式の読み替え・正規表現（コンパイル済み）・必須リテラル・短い手がかり・大小無視の手がかりを 1 回だけ作る。
///
/// 検索の要求ごとに 1 つ作り、その検索が読むファイル・範囲・ワーカーすべてで使い回す
///（多数ファイルの検索はファイルごとに式を読み直し、正規表現をコンパイルし直していた。同 指摘6）。
/// 作ったあとは変わらないので、複数のスレッドから同時に使ってよい。
/// ファイルの文字コードが分かったら <see cref="For"/> で方式を決める。
/// </summary>
public sealed class PreparedSearch
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SearchOptions, PreparedSearch> Shared = new();

    private readonly Lazy<Regex> _regex;
    private readonly System.Collections.Concurrent.ConcurrentBag<Regex> _spare = new();
    private readonly bool _prefilter, _shortClues;     // 作ったときの切り替え（Of が使い回してよいかを見る）
    private SearchPlan? _lastPlan;

    public SearchOptions Options { get; }

    /// <summary>大小を区別する正規表現の手がかり（UTF-8 のバイト列。必須リテラルか、短い手がかり）。無ければ null。</summary>
    internal byte[][]? ExactClues { get; }

    /// <summary><see cref="ExactClues"/> が必須リテラル（2 文字以上）か。false なら短い手がかり（計測の表示用）。</summary>
    internal bool ExactCluesAreLiterals { get; }

    /// <summary>大小無視の手がかり（当たる行に必ず入っている ASCII の連続部分。k/K を含まない）。無ければ空。</summary>
    internal string IcaseRun { get; } = "";

    private PreparedSearch(SearchOptions options)
    {
        Options = options;
        _regex = new Lazy<Regex>(() => BuildScanRegex(options), LazyThreadSafetyMode.ExecutionAndPublication);
        _prefilter = SearchService.UseLiteralPrefilter;
        _shortClues = SearchService.UseShortClues;
        if (!_prefilter) return;     // 手がかりを全部切る（比べる用・万一の切り戻し）

        if (options.UseRegex)
        {
            var literals = RegexLiterals.Extract(options.Pattern, ignoreCase: false);
            if (!options.IgnoreCase)
            {
                if (literals is not null)
                {
                    ExactClues = literals.Select(Encoding.UTF8.GetBytes).ToArray();
                    ExactCluesAreLiterals = true;
                }
                // 2 文字以上の必須リテラルが無い式は、1 文字・文字クラスのバイトの並びで絞る（RegexClues）
                else if (_shortClues && RegexClues.Extract(options.Pattern) is { } clues)
                    ExactClues = clues.ToArray();
            }
            // 大小無視は、必須リテラルが 1 つに決まるときだけ（候補が複数だと、どれが入るか決められない）
            else if (literals is [string only]) IcaseRun = AsciiCaseFold.LongestFoldableRun(only);
        }
        // 素の文字列の -i でバイトのまま畳めない語（k を含む・非 ASCII）は、語の一部を手がかりにする
        else if (options.IgnoreCase) IcaseRun = AsciiCaseFold.LongestFoldableRun(options.Pattern);
    }

    /// <summary>準備を作る（式は読むが、正規表現のコンパイルは要るときまで遅らせる）。</summary>
    public static PreparedSearch Create(SearchOptions options) => new(options);

    /// <summary>
    /// この検索条件の<b>オブジェクト</b>に付いた準備（無ければ作って付ける）。並列検索のように、1 つの要求を
    /// いくつもの作業役・範囲に分けて同じ <see cref="SearchOptions"/> を渡す呼び手が、それぞれ正規表現をコンパイルし直さないように。
    /// 条件のオブジェクトが要らなくなれば、準備も一緒に消える（いつまでも残る共有のキャッシュにはしない）。
    /// </summary>
    public static PreparedSearch Of(SearchOptions options)
    {
        if (Shared.TryGetValue(options, out var prepared)
            && prepared._prefilter == SearchService.UseLiteralPrefilter && prepared._shortClues == SearchService.UseShortClues)
            return prepared;
        prepared = new PreparedSearch(options);
        Shared.AddOrUpdate(options, prepared);
        return prepared;
    }

    /// <summary>
    /// 全行・候補行に当てる正規表現。意味は <see cref="SearchService.BuildRegex"/> と同じで、コンパイルする
    /// （3GB の実測で指定なしより最大 1.6 倍速く、NonBacktracking は 3 割遅かった）。ブラウザ（WASM）はコンパイルしない。
    /// 書き間違いの式は、最初に使ったところで <see cref="ArgumentException"/>。
    /// </summary>
    public Regex Regex => _regex.Value;

    /// <summary>
    /// 作業役が自分だけで使う正規表現を借りる（使い終わったら <see cref="ReturnRegex"/>）。返されたものを使い回し、
    /// 足りなければコンパイルして足す（同時に走る作業役の数まで）。
    /// 1 つの Regex を何本もの作業役で同時に使うと、.NET は呼ぶたびに照合の係（runner）を作り直す。
    /// uvp の 10 並列で 9,200 万行の <c>-E -v '^ +&lt;'</c> が 1.37 → 11.5 秒になった（テスト一式 2026-10-05）。
    /// </summary>
    public Regex RentRegex()
    {
        if (_spare.TryTake(out var regex)) return regex;
        _ = Regex;                                   // 書き間違いの式はここで知らせる（1 回だけ読む）
        return BuildScanRegex(Options);
    }

    /// <summary>借りた正規表現を返す。</summary>
    public void ReturnRegex(Regex? regex)
    {
        if (regex is not null) _spare.Add(regex);
    }

    /// <summary>この文字コードのファイルを探す方式（同じ文字コードが続く間は同じものを返す）。</summary>
    public SearchPlan For(Encoding encoding)
    {
        if (Volatile.Read(ref _lastPlan) is { } last && last.CodePage == encoding.CodePage) return last;
        var plan = new SearchPlan(this, encoding);
        Volatile.Write(ref _lastPlan, plan);
        return plan;
    }

    private static Regex BuildScanRegex(SearchOptions options)
    {
        var regex = SearchService.BuildRegex(options);
        return OperatingSystem.IsBrowser()
            ? regex
            : new Regex(regex.ToString(), regex.Options | RegexOptions.Compiled, regex.MatchTimeout);
    }
}

/// <summary>
/// 文字コードが分かったあとの、検索の方式と道具（<see cref="PreparedSearch.For"/>）。
/// 方式の選び方はここ 1 か所だけにある（画面・uvf・uvp・置換と、本体に任せる判断が同じものを見る）。
/// </summary>
public sealed class SearchPlan
{
    private readonly byte[][]? _needles;            // CandidateLines（大小を区別する）
    private readonly SearchValues<byte>? _anchor;   // AsciiFold・CandidateLines（大小無視）の目印
    private readonly int _anchorAt;

    internal SearchPlan(PreparedSearch prepared, Encoding encoding)
    {
        Prepared = prepared;
        CodePage = encoding.CodePage;
        var o = prepared.Options;
        // バイト列のまま探せるのは、ASCII がそのまま現れて 1 対 1 に対応する文字コードだけ
        //（Shift-JIS 等は 2 バイト文字の後半に当たる。ソースレビュー 2026-09-19 の指摘5）
        bool ascii = AsciiCaseFold.IsAsciiCompatible(encoding);

        if (o is { UseRegex: false, IgnoreCase: false } && o.Pattern.Length > 0 && ascii
            && encoding.GetBytes(o.Pattern) is { Length: > 0 } bytes)
        {
            Method = SearchMethod.Bytes;
            Literal = bytes;
        }
        // -i の素の文字列も、条件が合えばバイトのまま大小無視で探す。k/K を含む語は U+212A（ケルビン記号）とも
        // 一致するので、バイトだけでは決められない（下の手がかり＋正規表現へ）
        else if (o is { UseRegex: false, IgnoreCase: true } && AsciiCaseFold.IsFoldable(o.Pattern) && ascii)
        {
            Method = SearchMethod.AsciiFold;
            Literal = AsciiCaseFold.ToLowerBytes(o.Pattern);
            _anchor = AsciiCaseFold.Anchor(Literal, out _anchorAt);
        }
        // UTF-8 のときだけ（他の文字コードではデコード結果とバイト列が 1 対 1 にならないことがある）
        else if (!o.IgnoreCase && prepared.ExactClues is { } needles && LiteralFinder.Supports(encoding))
        {
            Method = SearchMethod.CandidateLines;
            _needles = needles;
            ClueKind = prepared.ExactCluesAreLiterals ? "prefilter" : "short-clue";
            Clue = needles[0];
        }
        else if (o.IgnoreCase && prepared.IcaseRun.Length > 0 && ascii)
        {
            Method = SearchMethod.CandidateLines;
            Literal = AsciiCaseFold.ToLowerBytes(prepared.IcaseRun);
            _anchor = AsciiCaseFold.Anchor(Literal, out _anchorAt);
            ClueKind = "icase-clue";
            Clue = Literal;
        }
        else Method = SearchMethod.EveryLine;

        if (Method is SearchMethod.Bytes or SearchMethod.AsciiFold) { ClueKind = "literal"; Clue = Literal; }
    }

    public PreparedSearch Prepared { get; }
    public SearchOptions Options => Prepared.Options;
    public SearchMethod Method { get; }
    internal int CodePage { get; }

    /// <summary>素の文字列の方式か（行をデコードしない）。</summary>
    public bool IsLiteral => Method is SearchMethod.Bytes or SearchMethod.AsciiFold;

    /// <summary>
    /// <see cref="SearchMethod.Bytes"/>: 探すバイト列。<see cref="SearchMethod.AsciiFold"/>: 小文字にしたバイト列。
    /// 大小無視の <see cref="SearchMethod.CandidateLines"/>: 手がかり（小文字）。ほかは空。
    /// </summary>
    public byte[] Literal { get; } = [];

    /// <summary>
    /// 正規表現（<see cref="SearchMethod.CandidateLines"/>・<see cref="SearchMethod.EveryLine"/> で使う）。
    /// 何本もの作業役で同時に当てるときは、<see cref="PreparedSearch.RentRegex"/> でそれぞれ借りること。
    /// </summary>
    public Regex Regex => Prepared.Regex;

    /// <summary>計測の表示用（<c>UV_TRACE=1</c>）：手がかりの種類とバイト列。</summary>
    public string ClueKind { get; } = "none";
    public byte[] Clue { get; } = [];

    /// <summary>素の文字列を探す（大小を区別する／ASCII の大小を畳む）。見つからなければ -1。</summary>
    public int FindLiteral(ReadOnlySpan<byte> hay) => Method == SearchMethod.Bytes
        ? hay.IndexOf(Literal)
        : AsciiCaseFold.IndexOf(hay, Literal, _anchor!, _anchorAt);

    /// <summary>候補行を探す係（<see cref="SearchMethod.CandidateLines"/> のときだけ。探した位置を覚えるので、走査ごとに作る）。</summary>
    public ClueFinder? NewClueFinder() => Method != SearchMethod.CandidateLines ? null
        : _needles is not null ? new ClueFinder(new LiteralFinder(_needles))
        : new ClueFinder(Literal, _anchor!, _anchorAt);

    /// <summary>
    /// この行が当たりうるか（当たる行は必ず true。false の行には正規表現を当てなくてよい）。
    /// 1 行ずつ見る道（<c>-v</c> など）で、デコードを省くのに使う。
    /// </summary>
    public bool MayMatch(ReadOnlySpan<byte> line)
    {
        switch (Method)
        {
            case SearchMethod.Bytes or SearchMethod.AsciiFold:
                return FindLiteral(line) >= 0;
            case SearchMethod.CandidateLines when _needles is not null:
                foreach (var needle in _needles)
                    if (line.IndexOf(needle) >= 0) return true;
                return false;
            case SearchMethod.CandidateLines:
                return AsciiCaseFold.IndexOf(line, Literal, _anchor!, _anchorAt) >= 0;
            default:
                return true;
        }
    }
}

/// <summary>
/// 候補行の手がかりを、領域の中で順に探す係（<see cref="SearchPlan.NewClueFinder"/>）。
/// 大小を区別する手がかりは <see cref="LiteralFinder"/>（候補ごとに次の位置を覚える）、大小無視は畳んで探す。
/// 探した位置を覚えるので、ワーカー・走査ごとに作り、同時に使わない。
/// </summary>
public sealed class ClueFinder
{
    private readonly LiteralFinder? _exact;
    private readonly byte[] _folded = [];
    private readonly SearchValues<byte>? _anchor;
    private readonly int _anchorAt;

    internal ClueFinder(LiteralFinder exact) => _exact = exact;

    internal ClueFinder(byte[] folded, SearchValues<byte> anchor, int anchorAt)
    {
        _folded = folded;
        _anchor = anchor;
        _anchorAt = anchorAt;
    }

    /// <summary>新しい領域を探し始める前に呼ぶ。</summary>
    public void Reset() => _exact?.Reset();

    /// <summary>from 以降で、手がかりがいちばん手前に現れる位置（無ければ -1）。</summary>
    public int IndexOf(ReadOnlySpan<byte> region, int from)
    {
        if (_exact is not null) return _exact.IndexOf(region, from);
        int rel = AsciiCaseFold.IndexOf(region[from..], _folded, _anchor!, _anchorAt);
        return rel < 0 ? -1 : from + rel;
    }
}
