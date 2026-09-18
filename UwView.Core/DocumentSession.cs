using System.Text;
using System.Text.RegularExpressions;

namespace UwView.Core;

public enum ViewMode { Page, Line }

/// <summary>
/// 1 つの開いたファイル = 1 つのセッション（§4.7）。
/// I/O ソース・文字コード・索引・ドキュメント・表示状態を 1 つに束ね、
/// タブUI はこのセッションを差し替えて表示する（状態保持で即時切替・索引再構築なし）。
/// </summary>
public sealed class DocumentSession : IAsyncDisposable
{
    public string FilePath { get; }
    public string DisplayName { get; }
    public IByteSource Source { get; }
    public DetectedEncoding DetectedEncoding { get; }
    public NewlineStyle Newline { get; }
    public LineDocument Document { get; }

    // 表示状態（タブ切替で保持）
    public ViewMode Mode { get; set; } = ViewMode.Page;
    public long TopByteOffset { get; set; }
    public long TopLine { get; set; }

    // 索引状態
    public SparseLineIndex? Index => Document.Index;
    public bool IsIndexed => Document.IsIndexed;
    public double IndexProgress { get; private set; }
    public bool IsIndexing { get; private set; }

    /// <summary>索引進捗が動いたとき（UI スレッドで発火）。</summary>
    public event EventHandler? IndexProgressChanged;
    /// <summary>索引完了＝行モード昇格したとき。</summary>
    public event EventHandler? IndexCompleted;

    private CancellationTokenSource? _cts;
    private bool _disposed;
    private IByteSource? _scanSource;

    /// <summary>
    /// <b>通しで読む用</b>の読み取り元（索引作成・全文検索）。表示は <see cref="Source"/>（mmap）のまま。
    ///
    /// mmap は好きな位置をすぐ覗けるが、頭から順に読むと帯域の6割しか引けない
    /// （実測 575MB/s 対 pread 966MB/s。258GB なら 459秒 対 273秒）。
    /// そこで通し読みだけ pread に替える（オーナー指示 2026-09-18）。
    /// 実ファイルでなければ（ブラウザ等）これまでどおり <see cref="Source"/> を使う。
    /// </summary>
    internal IByteSource ScanSource
    {
        get
        {
            if (_scanSource is not null) return _scanSource;
            if (Source is not MmapByteSource || !File.Exists(FilePath)) return Source;
            try { return _scanSource = new SequentialFileByteSource(FilePath); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Source; }
        }
    }

    private DocumentSession(string path, IByteSource source, DetectedEncoding enc,
        NewlineStyle newline, LineDocument doc)
    {
        FilePath = path;
        DisplayName = Path.GetFileName(path);
        Source = source;
        DetectedEncoding = enc;
        Newline = newline;
        Document = doc;
        TopByteOffset = enc.BomLength;
    }

    /// <summary>ファイルを開いて文字コード判定まで行う（索引は待たない）。Desktop 用（mmap）。</summary>
    public static DocumentSession Open(string path)
    {
        var src = new MmapByteSource(path);
        var detected = EncodingDetector.Detect(src);
        var newline = EncodingDetector.DetectNewline(src);
        var doc = new LineDocument(src, detected, newline);
        return new DocumentSession(path, src, detected, newline, doc);
    }

    /// <summary>任意の IByteSource からセッションを作る（Browser=Blob 等、async I/O 実装用）。</summary>
    public static async Task<DocumentSession> CreateAsync(string displayName, IByteSource src, CancellationToken ct = default)
    {
        var detected = await EncodingDetector.DetectAsync(src, ct: ct);
        var newline = await EncodingDetector.DetectNewlineAsync(src, ct: ct);
        var doc = new LineDocument(src, detected, newline);
        return new DocumentSession(displayName, src, detected, newline, doc);
    }

    /// <summary>背景で索引を構築し、完了で行モードへ昇格（§3.1-2/3）。</summary>
    public async Task BuildIndexAsync()
    {
        if (IsIndexed || IsIndexing) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsIndexing = true;
        IndexProgress = 0;
        RaiseProgress();

        var progress = new Progress<double>(p => { IndexProgress = p; RaiseProgress(); });
        try
        {
            await Document.BuildIndexAsync(progress, ct, ScanSource);
            if (!ct.IsCancellationRequested)
            {
                TopLine = Document.OffsetToLineIndex(TopByteOffset); // 位置継続
                Mode = ViewMode.Line;
                IndexCompleted?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException) { /* ページモードのまま継続 */ }
        finally { IsIndexing = false; RaiseProgress(); }
    }

    public void CancelIndex() => _cts?.Cancel();

    /// <summary>
    /// CLI が検索のついでに作った索引をそのまま使う（<c>uvf ファイル 語 -open</c>）。
    /// 画面はファイルを読み直さずに行モードへ上がれる（オーナー指示 2026-09-18）。
    /// すでに索引があるときは何もしない。
    /// </summary>
    public bool AdoptIndex(SparseLineIndex index)
    {
        if (IsIndexed) return false;
        CancelIndex();
        Document.AdoptIndex(index);
        TopLine = Document.OffsetToLineIndex(TopByteOffset);
        Mode = ViewMode.Line;
        IsIndexing = false;
        IndexProgress = 1;
        RaiseProgress();
        IndexCompleted?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>手動の文字コード切替（索引再構築なし §4.6）。</summary>
    public void SetEncoding(Encoding encoding) => Document.Encoding = encoding;

    private void RaiseProgress() => IndexProgressChanged?.Invoke(this, EventArgs.Empty);

    // ── 検索（§11-①。結果はバイトオフセットでセッションが保持 §4.7）──────

    private readonly List<long> _searchHits = [];
    private CancellationTokenSource? _searchCts;

    /// <summary>ヒット行の行頭バイトオフセット（昇順）。</summary>
    public IReadOnlyList<long> SearchHits => _searchHits;

    /// <summary>CLI から渡された行番号（無ければ null＝索引から解決する）。</summary>
    public long[]? SearchHitLines { get; private set; }
    public SearchOptions? ActiveSearch { get; private set; }
    /// <summary>可視行ハイライト用（TextView が使う）。検索中でなければ null。</summary>
    public Regex? SearchHighlightRegex { get; private set; }
    public bool IsSearching { get; private set; }
    public double SearchProgress { get; private set; }
    public bool SearchTruncated { get; private set; }

    /// <summary>ヒット追加・進捗更新のたびに UI スレッドで発火。</summary>
    public event EventHandler? SearchUpdated;
    public event EventHandler? SearchCompleted;

    /// <summary>
    /// <b>すでに分かっている検索結果をそのまま受け取る</b>（<c>uvf ファイル 語 -open</c> の受け渡し）。
    /// CLI がファイルを通しで読んで見つけた行頭位置を渡してくるので、画面は同じ検索をやり直さない
    /// （50GB なら丸ごと1回読み直す時間が浮く。オーナー指示 2026-09-18）。
    /// 条件の表示・強調表示は普通の検索と同じに整える。
    /// </summary>
    /// <param name="hitLines">ヒット行の行番号（0 始まり・件数が合うときだけ使う）。
    /// これがあると結果一覧は索引の完成を待たずに行番号を出せる。</param>
    public void AdoptSearchResults(SearchOptions options, IReadOnlyList<long> hits, bool truncated,
                                   IReadOnlyList<long>? hitLines = null)
    {
        CancelSearch();
        _searchHits.Clear();
        _searchHits.AddRange(hits);
        SearchHitLines = hitLines is not null && hitLines.Count == hits.Count ? [.. hitLines] : null;
        ActiveSearch = options;
        SearchTruncated = truncated;
        SearchProgress = 1;
        IsSearching = false;
        try { SearchHighlightRegex = SearchService.BuildRegex(options); }
        catch (ArgumentException) { SearchHighlightRegex = null; }
        SearchUpdated?.Invoke(this, EventArgs.Empty);
        SearchCompleted?.Invoke(this, EventArgs.Empty);
    }

    public async Task StartSearchAsync(SearchOptions options)
    {
        CancelSearch();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        _searchHits.Clear();
        SearchHitLines = null;
        ActiveSearch = options;
        SearchTruncated = false;
        SearchProgress = 0;
        try { SearchHighlightRegex = SearchService.BuildRegex(options); }
        catch (ArgumentException) { SearchHighlightRegex = null; ActiveSearch = null; return; } // 不正な正規表現
        IsSearching = true;
        SearchUpdated?.Invoke(this, EventArgs.Empty);

        // バッチは背景スレッドから同期的に届く。UI コンテキストがあれば Post、なければ即時反映
        //（Progress<T> は post 完了を待たないため、await 後の一貫性が保てない）
        var syncCtx = SynchronizationContext.Current;
        void ApplyBatch(IReadOnlyList<long> b)
        {
            _searchHits.AddRange(b);
            SearchUpdated?.Invoke(this, EventArgs.Empty);
        }
        Action<IReadOnlyList<long>> batches = syncCtx is null
            ? ApplyBatch
            : b => syncCtx.Post(_ => ApplyBatch(b), null);

        var progress = new Progress<double>(p =>
        {
            SearchProgress = p;
            SearchUpdated?.Invoke(this, EventArgs.Empty);
        });

        try
        {
            // 索引がまだなら、検索のついでに索引も作る（1回読みで両方。オーナー指示 2026-09-18 B-1）。
            // 別々に読むと 258GB で 459秒 × 2 になる。走行中の索引作成は止めて、こちらへ相乗りさせる
            if (!IsIndexed && Cli.RawGrep.CanCombineWithIndex(options))
            {
                CancelIndex();
                SearchTruncated = await SearchAndIndexAsync(options, batches, progress, ct);
            }
            else
            {
                var outcome = await SearchService.SearchAsync(
                    ScanSource, Document.BomLength, Document.Encoding, options, batches, progress, ct);
                SearchTruncated = outcome.Truncated;
            }
        }
        catch (OperationCanceledException) { /* 中断: それまでのヒットは有効 */ }
        finally
        {
            IsSearching = false;
            SearchUpdated?.Invoke(this, EventArgs.Empty);
            SearchCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 検索しながら索引も作る（1回読み）。終わったら索引を採用して行モードへ上げる。
    /// 判定の規則は <see cref="SearchService"/> と同じ（<see cref="Cli.RawGrep"/> が合わせてある）。
    /// </summary>
    private async Task<bool> SearchAndIndexAsync(SearchOptions options, Action<IReadOnlyList<long>> batches,
                                                 IProgress<double> progress, CancellationToken ct)
    {
        var marks = new Cli.RawGrep.IndexMarks(Document.BlockLines);
        var batch = new List<long>(256);
        long fileLength = Math.Max(1, ScanSource.Length - Document.BomLength);
        long reported = 0;

        var outcome = await Cli.RawGrep.RunAsync(
            ScanSource, Document.BomLength, Document.Encoding, options, invert: false,
            (_, lineStart, _) =>
            {
                batch.Add(lineStart);
                if (batch.Count < 256) return;
                batches([.. batch]);
                batch.Clear();
                if (lineStart - reported < (16 << 20)) return;
                reported = lineStart;
                progress.Report((double)(lineStart - Document.BomLength) / fileLength);
            }, ct, marks);

        if (batch.Count > 0) batches([.. batch]);
        progress.Report(1.0);

        // 索引はファイルを最後まで読み切ったときだけ採る（上限で打ち切ったら不完全）
        if (!outcome.Truncated && !ct.IsCancellationRequested)
            AdoptIndex(SparseLineIndex.FromCheckpoints(Document.BomLength, Newline, Document.BlockLines,
                                                       marks.Marks, marks.NewlineCount, marks.LastByte,
                                                       ScanSource.Length));
        return outcome.Truncated;
    }

    public void CancelSearch() => _searchCts?.Cancel();

    public void ClearSearch()
    {
        CancelSearch();
        _searchHits.Clear();
        ActiveSearch = null;
        SearchHighlightRegex = null;
        SearchTruncated = false;
        SearchProgress = 0;
        SearchUpdated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>fromOffset より後の最初のヒット（なければ null）。二分探索。</summary>
    public long? NextHit(long fromOffset)
    {
        int i = LowerBound(fromOffset + 1);
        return i < _searchHits.Count ? _searchHits[i] : null;
    }

    /// <summary>fromOffset より前の最後のヒット（なければ null）。</summary>
    public long? PrevHit(long fromOffset)
    {
        int i = LowerBound(fromOffset) - 1;
        return i >= 0 ? _searchHits[i] : null;
    }

    /// <summary>オフセット以上の最初のヒット index（フィルタ表示のジャンプ用）。</summary>
    public int HitIndexOfOffset(long offset)
        => Math.Min(LowerBound(offset), Math.Max(0, _searchHits.Count - 1));

    private int LowerBound(long value) => LowerBound(_searchHits, value);

    private static int LowerBound(List<long> list, long value)
    {
        int lo = 0, hi = list.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (list[mid] < value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    // ── フィルタ表示状態（§11-⑤。タブ切替でも保持）────────────

    /// <summary>検索ヒット行だけを表示するモード。</summary>
    public bool FilterActive { get; set; }
    /// <summary>フィルタ表示の先頭ヒット index（SearchHits 内の位置）。</summary>
    public int FilterTopHitIndex { get; set; }

    // ── ブックマーク（§11-④。バイトオフセット基準）────────────

    private readonly List<long> _bookmarks = [];
    /// <summary>ブックマーク行の行頭バイトオフセット（昇順）。</summary>
    public IReadOnlyList<long> Bookmarks => _bookmarks;
    public event EventHandler? BookmarksChanged;

    /// <summary>指定行頭オフセットのブックマークをトグル。追加したら true。</summary>
    public bool ToggleBookmark(long lineStartOffset)
    {
        int i = LowerBound(_bookmarks, lineStartOffset);
        bool added;
        if (i < _bookmarks.Count && _bookmarks[i] == lineStartOffset)
        {
            _bookmarks.RemoveAt(i);
            added = false;
        }
        else
        {
            _bookmarks.Insert(i, lineStartOffset);
            added = true;
        }
        BookmarksChanged?.Invoke(this, EventArgs.Empty);
        return added;
    }

    public bool HasBookmark(long lineStartOffset)
    {
        int i = LowerBound(_bookmarks, lineStartOffset);
        return i < _bookmarks.Count && _bookmarks[i] == lineStartOffset;
    }

    public long? NextBookmark(long fromOffset)
    {
        int i = LowerBound(_bookmarks, fromOffset + 1);
        return i < _bookmarks.Count ? _bookmarks[i] : null;
    }

    public long? PrevBookmark(long fromOffset)
    {
        int i = LowerBound(_bookmarks, fromOffset) - 1;
        return i >= 0 ? _bookmarks[i] : null;
    }

    // ── リアルタイム Tail（§11-③。Desktop=mmap のみ）──────────

    /// <summary>Tail 可能か（Blob は不可＝スナップショットのため）。</summary>
    public bool SupportsTail => Source is MmapByteSource;
    public bool IsTailing { get; private set; }
    /// <summary>追記を検知して索引拡張が済んだときに発火（StartTail 呼び出し元スレッドへマーシャル）。</summary>
    public event EventHandler? TailGrew;

    private CancellationTokenSource? _tailCts;

    /// <summary>1 回ぶんのポーリング: 伸びていれば再マップ＋増分索引＋キャッシュ破棄して true。</summary>
    public async Task<bool> PollTailAsync(CancellationToken ct = default)
    {
        if (Source is not MmapByteSource mmap || !mmap.TryExpand())
            return false;
        if (Index is not null)
            await Index.ExtendAsync(Source, ct);
        Document.OnSourceExtended();
        return true;
    }

    public void StartTail(int intervalMs = 1000)
    {
        if (IsTailing || !SupportsTail) return;
        IsTailing = true;
        _tailCts = new CancellationTokenSource();
        var ct = _tailCts.Token;
        var syncCtx = SynchronizationContext.Current;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(intervalMs, ct);
                    if (IsIndexing) continue; // 初期索引の構築中は拡張しない
                    if (await PollTailAsync(ct))
                    {
                        if (syncCtx is null) TailGrew?.Invoke(this, EventArgs.Empty);
                        else syncCtx.Post(_ => TailGrew?.Invoke(this, EventArgs.Empty), null);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }, ct);
    }

    public void StopTail()
    {
        _tailCts?.Cancel();
        IsTailing = false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _searchCts?.Cancel();
        _tailCts?.Cancel();
        if (_scanSource is { } scan) { _scanSource = null; await scan.DisposeAsync(); }   // 通し読み用の pread
        await Document.DisposeAsync(); // Source も解放（mmap アンマップ＝ファイルロック解除）
    }
}
