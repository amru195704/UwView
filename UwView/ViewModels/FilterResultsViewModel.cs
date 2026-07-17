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

    /// <summary>ブロック区切り行か（`⋯` を表示）。</summary>
    public bool IsSeparator { get; init; }
    /// <summary>ヒット行か（false＝文脈行。淡色表示）。</summary>
    public bool IsHit { get; init; }
    /// <summary>行番号（0始まり。行モード時のみ有効、それ以外 -1）。</summary>
    public long LineIndex { get; init; } = -1;
    /// <summary>検索結果の通し番号（1〜N。ヒット行のみ、それ以外 -1）。</summary>
    public long HitOrdinal { get; init; } = -1;
    /// <summary>行頭バイトオフセット（ジャンプ用）。</summary>
    public long Offset { get; init; } = -1;
    /// <summary>ヒット行のハイライト用（文脈行・separator は null）。</summary>
    public Regex? HighlightRegex { get; init; }

    /// <summary>文脈行・区切り行は淡く表示する（指示書 §2）。</summary>
    public double RowOpacity => IsHit ? 1.0 : 0.55;

    public FilterRow(LineDocument? doc) => _doc = doc;

    /// <summary>表示用行番号（1始まり）。ページモード（行番号不明）は空。</summary>
    public string LineNumberText =>
        _lineNumberText ??= IsSeparator || _doc is null ? ""
            : LineIndex >= 0 ? (LineIndex + 1).ToString("N0", Localizer.Instance.Culture)
            : "";

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
                _text = LineIndex >= 0 ? _doc.GetLine(LineIndex)
                      : Offset >= 0 ? _doc.GetLineAtOffset(Offset)
                      : "";
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
        if (_session is not null)
            _session.SearchUpdated -= OnSearchUpdated;
        _session = session;
        if (_session is not null)
            _session.SearchUpdated += OnSearchUpdated;
        DocumentName = _session?.DisplayName ?? "";
        Rebuild();
    }

    private void OnSearchUpdated(object? sender, EventArgs e) => Rebuild();

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
            Rows = new HitOnlyRowList(doc, s.SearchHits, regex);
        }
        else
        {
            // 前後±N: 行番号へ写像 → ブロック結合 → 遅延展開
            var hits = s.SearchHits;
            var hitLines = new long[hits.Count];
            for (int i = 0; i < hits.Count; i++)
                hitLines[i] = doc.OffsetToLineIndex(hits[i]);
            var blocks = FilterBlocks.Build(hitLines, n, doc.TotalLines ?? 0);
            Rows = new BlockRowList(doc, blocks, regex);
        }

        HitInfo = Localizer.Instance.Format("SearchHits",
            s.SearchHits.Count.ToString("N0", Localizer.Instance.Culture))
            + (s.SearchTruncated ? Localizer.Instance["SearchTruncated"] : "");
    }

    public void Jump(FilterRow? row)
    {
        if (row is null || row.IsSeparator) return;
        long off = row.ResolveJumpOffset();
        if (off >= 0) _onJump(off);
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
        if (_session is not null)
            _session.SearchUpdated -= OnSearchUpdated;
        _session = null;
    }

    // ── 仮想化用の遅延リスト ─────────────────────────────────

    /// <summary>ヒット行のみ（N=0 / 未索引）: hits[i] をそのまま行にする。</summary>
    private sealed class HitOnlyRowList(LineDocument doc, IReadOnlyList<long> hits, Regex? regex)
        : IReadOnlyList<FilterRow>
    {
        public int Count => hits.Count;

        public FilterRow this[int index] => new(doc)
        {
            IsHit = true,
            Offset = hits[index],
            LineIndex = doc.IsIndexed ? doc.OffsetToLineIndex(hits[index]) : -1,
            HitOrdinal = index + 1,
            HighlightRegex = regex,
        };

        public IEnumerator<FilterRow> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return this[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>±N ブロック展開（ブロック間に区切り行を挟む）。prefix sum で i→(block,行) を解決。</summary>
    private sealed class BlockRowList : IReadOnlyList<FilterRow>
    {
        private readonly LineDocument _doc;
        private readonly List<FilterBlock> _blocks;
        private readonly Regex? _regex;
        private readonly long[] _rowStart; // 各ブロックの先頭表示行 index（separator 込み）
        private readonly long[] _hitStart; // 各ブロック先頭ヒットの通し番号（0始まり）
        private readonly int _count;

        public BlockRowList(LineDocument doc, List<FilterBlock> blocks, Regex? regex)
        {
            _doc = doc;
            _blocks = blocks;
            _regex = regex;
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

        public int Count => _count;

        public FilterRow this[int index]
        {
            get
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
                    return new FilterRow(_doc) { IsSeparator = true };

                long line = block.StartLine + rel;
                int hitIdx = IndexOfSorted(block.HitLines, line);
                return new FilterRow(_doc)
                {
                    IsHit = hitIdx >= 0,
                    LineIndex = line,
                    HitOrdinal = hitIdx >= 0 ? _hitStart[lo] + hitIdx + 1 : -1,
                    HighlightRegex = hitIdx >= 0 ? _regex : null,
                };
            }
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

        public IEnumerator<FilterRow> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return this[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
