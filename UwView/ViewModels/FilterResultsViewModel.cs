using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using UwView.Core;
using UwView.Localization;

namespace UwView.ViewModels;

/// <summary>フィルタ結果ポップアップの1行（仮想化リストの要素。テキストは遅延解決）。</summary>
public sealed class FilterRow
{
    private readonly LineDocument? _doc;
    private string? _text;
    private string? _lineNumberText;

    /// <summary>一覧内での行 index（0始まり）。ListBox の IndexOf を走査なしで答えるために持つ。</summary>
    public int RowIndex { get; init; } = -1;
    /// <summary>ブロック区切り行か（`⋯` を表示）。</summary>
    public bool IsSeparator { get; init; }
    /// <summary>ヒット行か（false＝文脈行。淡色表示）。</summary>
    public bool IsHit { get; init; }
    /// <summary>行番号（0始まり。行モード時のみ有効、それ以外 -1）。</summary>
    public long LineIndex { get; init; } = -1;
    /// <summary>LineIndex 未指定でも Offset から行番号を解決するか（行モードのヒット行）。</summary>
    public bool ResolveLineFromOffset { get; init; }
    /// <summary>検索結果の通し番号（1〜N。ヒット行のみ、それ以外 -1）。</summary>
    public long HitOrdinal { get; init; } = -1;
    /// <summary>行頭バイトオフセット（ジャンプ用）。</summary>
    public long Offset { get; init; } = -1;
    /// <summary>ヒット行のハイライト用（文脈行・separator は null）。</summary>
    public Regex? HighlightRegex { get; init; }

    public FilterRow(LineDocument? doc) => _doc = doc;

    private long _resolvedLine = -2; // -2 = 未解決

    /// <summary>
    /// 実効行番号。LineIndex 未指定のヒット行は Offset から解決する。
    /// WASM でデータ未到着のときは確定させず -1 を返す（到着後に再解決される）。
    /// </summary>
    private long EffectiveLineIndex
    {
        get
        {
            if (LineIndex >= 0) return LineIndex;
            if (_resolvedLine != -2) return _resolvedLine;
            if (!ResolveLineFromOffset || _doc is null || Offset < 0) return -1;
            try
            {
                long v = _doc.OffsetToLineIndex(Offset);
                if (_doc.LastReadIncomplete) return -1;
                return _resolvedLine = v;
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
            {
                return -1;
            }
        }
    }

    /// <summary>表示用行番号（1始まり）。ページモード（行番号不明）は空。</summary>
    public string LineNumberText
    {
        get
        {
            if (_lineNumberText is not null) return _lineNumberText;
            if (IsSeparator || _doc is null) return _lineNumberText = "";
            long line = EffectiveLineIndex;
            if (line < 0) return ""; // 未解決は焼き付けない
            return _lineNumberText = (line + 1).ToString("N0", Localizer.Instance.Culture);
        }
    }

    /// <summary>検索結果の通し番号表示（1〜N。文脈行・区切り行は空）。</summary>
    public string HitNumberText =>
        HitOrdinal > 0 ? HitOrdinal.ToString("N0", Localizer.Instance.Culture) : "";

    public string Text
    {
        get
        {
            if (_text is not null) return _text;
            if (IsSeparator || _doc is null) return _text = "⋯";
            try
            {
                // 行頭オフセットが判っているなら索引を介さず直接読む。
                // 行番号経由は checkpoint からの改行走査が必要で、WASM でデータ未到着だと
                // 行番号自体がズレて誤った行を読んでしまう。
                string t = Offset >= 0 ? _doc.GetLineAtOffset(Offset)
                         : LineIndex >= 0 ? _doc.GetLine(LineIndex)
                         : "";
                // WASM: データ未到着なら焼き付けず、到着後の再表示で正しい本文にする
                if (_doc.LastReadIncomplete) return t;
                _text = t;
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
                _text = "";
            }
            return _text;
        }
    }

    /// <summary>ジャンプ先オフセット（行番号しか持たない文脈行は行頭へ解決）。</summary>
    public long ResolveJumpOffset()
    {
        if (Offset >= 0) return Offset;
        if (_doc is not null && LineIndex >= 0)
        {
            try { return _doc.LineStartOffset(LineIndex); }
            catch (Exception e) when (e is IOException or ObjectDisposedException) { }
        }
        return -1;
    }
}

/// <summary>
/// フィルタ結果ポップアップの VM（機能修正指示書_検索フィルタPopup.md）。
/// - データ源は Session の既存ヒット（再検索しない）
/// - 行リストは仮想化前提の遅延リスト（100万件でも展開しない）
/// - 前後±N（AllowContext=true のとき有効。Pro 限定機能）は FilterBlocks で
///   表示・保存共通のブロックに展開する
/// </summary>
public sealed partial class FilterResultsViewModel : ObservableObject, IDisposable
{
    private readonly Action<long> _onJump;
    private DocumentSession? _session;

    [ObservableProperty] private IReadOnlyList<FilterRow> _rows = Array.Empty<FilterRow>();
    [ObservableProperty] private string _hitInfo = "";
    [ObservableProperty] private int _contextN;
    [ObservableProperty] private bool _includeLineNumbersOnSave = true;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private double _saveProgress;
    [ObservableProperty] private string _documentName = "";

    /// <summary>現在表示中の検索位置（1始まりの通し番号。0=未確定）。件数表示 "C/Total" の C。</summary>
    private long _currentOrdinal;

    /// <summary>
    /// 閉じた時点の先頭表示行。閉じて開き直したときに同じ位置へ戻すためにここに置く
    /// （VM はポップアップを閉じても破棄せず使い回す）。
    /// </summary>
    public int SavedTopRow { get; set; }

    /// <summary>前後±N を UI で使えるか（MaxContext > 0）。</summary>
    public bool AllowContext => MaxContext > 0;

    /// <summary>前後±N の上限（UVF=1 / UVP=1000）。0 でヒット行のみ。</summary>
    public int MaxContext { get; }

    public DocumentSession? Session => _session;

    public FilterResultsViewModel(Action<long> onJump, int maxContext)
    {
        _onJump = onJump;
        MaxContext = maxContext;
    }

    /// <summary>対象セッションを差し替える（タブ切替・公開版は1ウィンドウ連動）。</summary>
    public void SetSession(DocumentSession? session)
    {
        if (ReferenceEquals(_session, session)) { Rebuild(); return; }
        InvalidateLineMapping();
        if (_session is not null)
            _session.SearchUpdated -= OnSearchUpdated;
        _session = session;
        if (_session is not null)
            _session.SearchUpdated += OnSearchUpdated;
        DocumentName = _session?.DisplayName ?? "";
        Rebuild();
    }

    /// <summary>検索中の再構築を間引くための最終更新時刻（WASM でのフリーズ防止）。</summary>
    private DateTime _lastRebuild = DateTime.MinValue;

    private void OnSearchUpdated(object? sender, EventArgs e)
    {
        // ヒット列が変わったので行番号の写像は作り直し（検索完了時に Rebuild から再開される）
        InvalidateLineMapping();

        // 検索中はヒットのバッチごとに呼ばれる。数十万件になると再構築の連打で
        //（特に WASM で）固まるため、進行中は 120ms 間隔に間引く。完了時は必ず反映。
        if (_session is { IsSearching: true })
        {
            var now = DateTime.UtcNow;
            if ((now - _lastRebuild).TotalMilliseconds < 120) return;
            _lastRebuild = now;
        }
        Rebuild();
    }

    partial void OnContextNChanged(int value)
    {
        if (value < 0 || value > MaxContext) { ContextN = Math.Clamp(value, 0, MaxContext); return; }
        Rebuild();
    }

    /// <summary>行リストを現在のヒット・±N から作り直す（遅延リストなので軽い）。</summary>
    public void Rebuild()
    {
        var s = _session;
        if (s is null || s.SearchHits.Count == 0)
        {
            Rows = Array.Empty<FilterRow>();
            HitInfo = s?.ActiveSearch is null ? "" : Localizer.Instance.Format("SearchHits", 0);
            return;
        }

        var doc = s.Document;
        var regex = s.SearchHighlightRegex;
        int n = Math.Clamp(ContextN, 0, MaxContext);

        if (n <= 0 || !doc.IsIndexed)
        {
            // ヒット行のみ: ヒット（行頭オフセット列）をそのまま1行=1ヒットで並べる
            CancelLineMapping();
            Rows = new HitOnlyRowList(doc, s.SearchHits, regex);
        }
        else if (_hitLines is { } cached && cached.Length == s.SearchHits.Count)
        {
            // 行番号への写像が済んでいる: ブロック結合 → 遅延展開
            Rows = new BlockRowList(doc, FilterBlocks.Build(cached, n, doc.TotalLines ?? 0), regex,
                s.SearchHits);
        }
        else
        {
            // 写像がまだ: 先にヒット行のみを出しておき、裏で非同期に写像する。
            // 同期 Read で写像すると WASM の未取得チャンクで数え落とし、
            // DataArrived → Rebuild → また未取得… の無限ループになる（タブが固まる）。
            Rows = new HitOnlyRowList(doc, s.SearchHits, regex);
            StartLineMapping(s, doc);
        }

        UpdateHitInfo();
    }

    // ── ヒット → 行番号の非同期写像（前後±N 用）──────────────

    /// <summary>写像済みのヒット行番号。ヒット列が変わったら破棄する。</summary>
    private long[]? _hitLines;
    private CancellationTokenSource? _mapCts;

    private void CancelLineMapping()
    {
        _mapCts?.Cancel();
        _mapCts = null;
    }

    /// <summary>ヒット列が変わったら写像結果を捨てる（検索やり直し・タブ切替）。</summary>
    private void InvalidateLineMapping()
    {
        CancelLineMapping();
        _hitLines = null;
    }

    /// <summary>
    /// 写像済みの行番号列を外から渡す（Pro 拡張用）。
    /// 多段階検索はタブ切替のたびにヒット列を差し替えるため、そのたびに
    /// 10万件を写像し直すと巨大ファイルで固まる。段側にキャッシュした結果を
    /// ここから注入すれば即ブロック表示になる。件数が合わないものは無視。
    /// </summary>
    public void SupplyHitLines(long[] hitLines)
    {
        if (_session is not { } s || s.SearchHits.Count != hitLines.Length) return;
        CancelLineMapping();
        _hitLines = hitLines;
        Rebuild();
    }

    private void StartLineMapping(DocumentSession session, LineDocument doc)
    {
        if (_mapCts is not null) return;    // 実行中は二重起動しない
        if (session.IsSearching) return;    // ヒットが増え続けている間は待つ

        var hits = session.SearchHits;
        int count = hits.Count;
        if (count == 0) return;

        var cts = new CancellationTokenSource();
        _mapCts = cts;
        _ = MapAsync(session, doc, count, cts);
    }

    private async Task MapAsync(DocumentSession session, LineDocument doc, int count, CancellationTokenSource cts)
    {
        try
        {
            var lines = new long[count];
            var hits = session.SearchHits;
            if (OperatingSystem.IsBrowser())
            {
                // WASM: 同期 Read は未取得チャンクを数え落とすので必ず async 経路
                for (int i = 0; i < count; i++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    lines[i] = await doc.OffsetToLineIndexAsync(hits[i], cts.Token);
                }
            }
            else
            {
                // デスクトップ: OffsetToLineIndexAsync は同期完了するため、この
                // ループを UI スレッドで回すと巨大ファイル×10万ヒットで固まる。
                // 背景スレッドへ逃がす（読み取りは検索と同じくスレッド安全）。
                await Task.Run(() =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        lines[i] = doc.OffsetToLineIndex(hits[i]);
                    }
                }, cts.Token);
            }
            if (cts.IsCancellationRequested || !ReferenceEquals(_mapCts, cts)) return;
            if (!ReferenceEquals(_session, session) || session.SearchHits.Count != count) return;

            _hitLines = lines;
            _mapCts = null;
            Rebuild();      // 写像が揃ったので ±N のブロック表示へ差し替える
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException
                                   or InvalidOperationException)
        {
            // キャンセル・ファイル閉じ・索引破棄は無視（表示はヒット行のみのまま）
        }
        finally
        {
            if (ReferenceEquals(_mapCts, cts)) _mapCts = null;
            cts.Dispose();
        }
    }

    /// <summary>件数表示を更新する（現在位置が判っていれば "C/Total"、未確定なら "Total"）。</summary>
    private void UpdateHitInfo()
    {
        var s = _session;
        if (s is null || s.SearchHits.Count == 0)
        {
            HitInfo = s?.ActiveSearch is null ? "" : Localizer.Instance.Format("SearchHits", 0);
            return;
        }

        var culture = Localizer.Instance.Culture;
        string total = s.SearchHits.Count.ToString("N0", culture);
        HitInfo = (_currentOrdinal > 0
                ? Localizer.Instance.Format("SearchHitsCurrent", _currentOrdinal.ToString("N0", culture), total)
                : Localizer.Instance.Format("SearchHits", total))
            + (s.SearchTruncated ? Localizer.Instance["SearchTruncated"] : "");
    }

    /// <summary>
    /// 現在表示中の検索位置を設定して件数表示を "C/Total" に更新する。
    /// 一覧クリックだけでなく、メイン画面の「次へ/前へ」からも呼ばれる。
    /// </summary>
    public void SetCurrentOffset(long offset)
    {
        var s = _session;
        if (s is null || s.SearchHits.Count == 0) return;
        long ordinal = s.HitIndexOfOffset(offset) + 1;   // 0始まり index → 1始まり通し番号
        if (ordinal == _currentOrdinal) return;
        _currentOrdinal = ordinal;
        UpdateHitInfo();
    }

    public void Jump(FilterRow? row)
    {
        if (row is null || row.IsSeparator) return;
        long off = row.ResolveJumpOffset();
        if (off >= 0)
        {
            // 一覧の通し番号があればそれを、無ければオフセットから逆引きして C を更新
            if (row.HitOrdinal > 0) { _currentOrdinal = row.HitOrdinal; UpdateHitInfo(); }
            else SetCurrentOffset(off);
            _onJump(off);
        }
    }

    // ── 保存（指示書 §3-②。逐次書き出し・進捗・キャンセル）─────────

    private CancellationTokenSource? _saveCts;

    public void CancelSave() => _saveCts?.Cancel();

    /// <summary>
    /// 現在の表示内容（Rows）をそのままテキストで書き出す。
    /// LineDocument はスレッド安全でないため UI スレッド上でチャンクごとに
    /// await Task.Yield() しながら進める（メモリに全展開しない）。
    /// </summary>
    public async Task SaveAsync(Stream output, Encoding encoding)
    {
        var rows = Rows;
        _saveCts = new CancellationTokenSource();
        var ct = _saveCts.Token;
        IsSaving = true;
        SaveProgress = 0;
        try
        {
            await using var writer = new StreamWriter(output, encoding, bufferSize: 1 << 16, leaveOpen: false);
            for (int i = 0; i < rows.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var row = rows[i];
                if (row.IsSeparator)
                {
                    await writer.WriteLineAsync("⋯");
                }
                else
                {
                    string prefix = IncludeLineNumbersOnSave && row.LineNumberText.Length > 0
                        ? row.LineNumberText + "\t" : "";
                    await writer.WriteLineAsync(prefix + row.Text);
                }

                if ((i & 1023) == 0)
                {
                    SaveProgress = (double)i / rows.Count;
                    await Task.Yield(); // UI を固めない
                }
            }
            SaveProgress = 1.0;
        }
        finally
        {
            IsSaving = false;
        }
    }

    public void Dispose()
    {
        _saveCts?.Cancel();
        CancelLineMapping();
        if (_session is not null)
            _session.SearchUpdated -= OnSearchUpdated;
        _session = null;
    }

    // ── 仮想化用の遅延リスト ─────────────────────────────────

    /// <summary>
    /// 遅延生成する行リストの土台。
    /// - <see cref="IList"/> を実装するのが重要: Avalonia の ItemsSourceView は IList を
    ///   そのままラップするが、IReadOnlyList だけだと全件を List&lt;object&gt; へコピーしてしまう
    ///   （ヒット数が多いとポップアップを開くだけで固まる）。
    /// - 同じ index には同じ FilterRow を返す: スクロールで再訪するたびに本文読み直しと
    ///   ハイライト Inlines の再構築が走るのを防ぐ（選択状態の追跡も壊れなくなる）。
    /// </summary>
    private abstract class LazyRowList : IReadOnlyList<FilterRow>, IList
    {
        private const int MaxCached = 4096;
        private readonly Dictionary<int, FilterRow> _materialized = [];

        public abstract int Count { get; }

        /// <summary>index 番目の行を実際に組み立てる（キャッシュ未ヒット時のみ呼ばれる。
        /// 実装側で <see cref="FilterRow.RowIndex"/> に index を入れること）。</summary>
        protected abstract FilterRow Create(int index);

        public FilterRow this[int index]
        {
            get
            {
                if (_materialized.TryGetValue(index, out var row)) return row;
                if (_materialized.Count >= MaxCached) _materialized.Clear();
                return _materialized[index] = Create(index);
            }
        }

        public IEnumerator<FilterRow> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return this[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        // IList（読み取り専用。ItemsSourceView が IList として扱えるようにするためだけの実装）
        object? IList.this[int index] { get => this[index]; set => throw new NotSupportedException(); }
        bool IList.IsFixedSize => true;
        bool IList.IsReadOnly => true;
        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => this;
        int IList.Add(object? value) => throw new NotSupportedException();
        void IList.Clear() => throw new NotSupportedException();
        bool IList.Contains(object? value) => ((IList)this).IndexOf(value) >= 0;
        void IList.Insert(int index, object? value) => throw new NotSupportedException();
        void IList.Remove(object? value) => throw new NotSupportedException();
        void IList.RemoveAt(int index) => throw new NotSupportedException();

        // 行が自分の index を持っているので走査不要（キャッシュを破棄しても壊れない）
        int IList.IndexOf(object? value)
            => value is FilterRow row && row.RowIndex >= 0 && row.RowIndex < Count ? row.RowIndex : -1;

        void ICollection.CopyTo(Array array, int index)
        {
            for (int i = 0; i < Count; i++) array.SetValue(this[i], index + i);
        }
    }

    /// <summary>ヒット行のみ（N=0 / 未索引）: hits[i] をそのまま行にする。</summary>
    private sealed class HitOnlyRowList(LineDocument doc, IReadOnlyList<long> hits, Regex? regex)
        : LazyRowList
    {
        public override int Count => hits.Count;

        // 行番号は FilterRow 側で遅延解決する（未索引なら空欄、WASM の未到着時は到着後に再解決）
        protected override FilterRow Create(int index) => new(doc)
        {
            RowIndex = index,
            IsHit = true,
            Offset = hits[index],
            ResolveLineFromOffset = doc.IsIndexed,
            HitOrdinal = index + 1,
            HighlightRegex = regex,
        };
    }

    /// <summary>±N ブロック展開（ブロック間に区切り行を挟む）。prefix sum で i→(block,行) を解決。</summary>
    private sealed class BlockRowList : LazyRowList
    {
        private readonly LineDocument _doc;
        private readonly List<FilterBlock> _blocks;
        private readonly Regex? _regex;
        private readonly long[] _rowStart; // 各ブロックの先頭表示行 index（separator 込み）
        private readonly long[] _hitStart; // 各ブロック先頭ヒットの通し番号（0始まり）
        private readonly int _count;

        private readonly IReadOnlyList<long> _hitOffsets; // ヒット通し番号(0始まり) → 行頭オフセット

        public BlockRowList(LineDocument doc, List<FilterBlock> blocks, Regex? regex,
            IReadOnlyList<long> hitOffsets)
        {
            _doc = doc;
            _blocks = blocks;
            _regex = regex;
            _hitOffsets = hitOffsets;
            _rowStart = new long[blocks.Count];
            _hitStart = new long[blocks.Count];
            long pos = 0, hitNo = 0;
            for (int b = 0; b < blocks.Count; b++)
            {
                _rowStart[b] = pos;
                _hitStart[b] = hitNo;
                pos += blocks[b].LineCount + 1; // +1 = ブロック後の区切り行
                hitNo += blocks[b].HitLines.Count;
            }
            _count = (int)Math.Min(int.MaxValue, Math.Max(0, pos - 1)); // 末尾の区切りは無し
        }

        public override int Count => _count;

        protected override FilterRow Create(int index)
        {
            // index が属するブロックを二分探索
            int lo = 0, hi = _blocks.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (_rowStart[mid] <= index) lo = mid;
                else hi = mid - 1;
            }
            var block = _blocks[lo];
            long rel = index - _rowStart[lo];
            if (rel >= block.LineCount)
                return new FilterRow(_doc) { RowIndex = index, IsSeparator = true };

            long line = block.StartLine + rel;
            int hitIdx = IndexOfSorted(block.HitLines, line);
            long hitNo = hitIdx >= 0 ? _hitStart[lo] + hitIdx : -1;
            // ヒット行は行頭オフセットが判っているので、索引を介さず直読みできる
            // （WASM でデータ未到着のとき行番号経由だと誤った行を読んでしまう）
            long offset = hitNo >= 0 && hitNo < _hitOffsets.Count ? _hitOffsets[(int)hitNo] : -1;
            return new FilterRow(_doc)
            {
                RowIndex = index,
                IsHit = hitIdx >= 0,
                LineIndex = line,
                Offset = offset,
                HitOrdinal = hitNo >= 0 ? hitNo + 1 : -1,
                HighlightRegex = hitIdx >= 0 ? _regex : null,
            };
        }

        private static int IndexOfSorted(IReadOnlyList<long> sorted, long value)
        {
            int lo = 0, hi = sorted.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (sorted[mid] == value) return mid;
                if (sorted[mid] < value) lo = mid + 1;
                else hi = mid - 1;
            }
            return -1;
        }
    }
}
