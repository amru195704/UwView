using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using UwView.Core;

namespace UwView.Controls;

/// <summary>
/// 2 億行を扱う自前描画・仮想化テキスト面（§4.5）。Active な <see cref="DocumentSession"/> を描画する。
/// 先頭表示位置・モードはセッションが保持するため、タブ切替はセッション差し替え＋再描画のみで状態が続く。
/// </summary>
public sealed class TextView : Control
{
    private DocumentSession? _session;
    public DocumentSession? Session
    {
        get => _session;
        set
        {
            _session = value;
            InvalidateVisual();
            NotifyChanged();
        }
    }

    public bool ShowLineNumbers { get; set; } = true;

    /// <summary>フィルタ表示（§11-⑤）: 検索ヒット行だけを表示中か。</summary>
    private bool FilterOn => _session is { FilterActive: true } s && s.SearchHits.Count > 0;

    public ViewMode Mode => _session?.Mode ?? ViewMode.Page;
    public long TopLine => _session?.TopLine ?? 0;
    public long TopByteOffset => _session?.TopByteOffset ?? 0;
    public double Percent => _session is { Document.Length: > 0 } s ? (double)s.TopByteOffset / s.Document.Length : 0;
    public long? TotalLines => _session?.Document.TotalLines;

    /// <summary>先頭表示位置・モード・セッションが変わったら発火（ステータス/スクロールバー更新用）。</summary>
    public event EventHandler? StateChanged;

    private readonly Typeface _typeface = new(new FontFamily("Cascadia Mono,Menlo,Consolas,Courier New,monospace"));
    private const double FontSize = 14;
    private const double Padding = 4;
    private double _lineHeight;
    private double _digitWidth;

    private ScrollBar? _scroll;
    private bool _updatingScroll;

    public IBrush? Background { get; set; }

    public TextView()
    {
        Focusable = true;
        ClipToBounds = true;
        Background = Brushes.White;
        SizeChanged += (_, _) => { InvalidateVisual(); NotifyChanged(); };
    }

    public void AttachScrollBar(ScrollBar scrollBar)
    {
        _scroll = scrollBar;
        _scroll.Scroll += OnScroll;
        NotifyChanged();
    }

    /// <summary>索引完了などで外部からセッション状態が変わったときの再描画。</summary>
    public void Refresh()
    {
        InvalidateVisual();
        NotifyChanged();
    }

    private int VisibleRows => Math.Max(1, (int)Math.Floor((Bounds.Height - Padding * 2) / LineHeight));

    private double LineHeight
    {
        get
        {
            if (_lineHeight <= 0)
            {
                var ft = MakeText("Ag12");
                _lineHeight = Math.Ceiling(ft.Height);
                _digitWidth = ft.WidthIncludingTrailingWhitespace / 4.0;
            }
            return _lineHeight;
        }
    }

    private LineDocument? Doc => _session?.Document;

    // ── ジャンプ ──────────────────────────────────────────────

    public void JumpToLine(long line)
    {
        if (_session?.Index is null) return;
        long total = _session.Index.TotalLines;
        _session.TopLine = Math.Clamp(line, 0, Math.Max(0, total - 1));
        Refresh();
    }

    /// <summary>バイトオフセット基準のジャンプ。行モードなら最寄り行へ変換（検索ヒット・ミニマップ用）。</summary>
    public void JumpToOffset(long byteOffset)
    {
        if (_session is null) return;
        if (FilterOn)
        {
            _session.FilterTopHitIndex = _session.HitIndexOfOffset(byteOffset);
            Refresh();
            return;
        }
        long aligned = _session.Document.AlignToLineStart(byteOffset);
        if (_session.Mode == ViewMode.Line && _session.Index is not null)
            _session.TopLine = _session.Document.OffsetToLineIndex(aligned);
        else
            _session.TopByteOffset = aligned;
        Refresh();
    }

    /// <summary>末尾へ（Tail 追従用）。</summary>
    public void GoToEnd() => ScrollToEnd();

    public void JumpToPercent(double pct)
    {
        if (_session is null) return;
        JumpToOffset((long)(_session.Document.Length * Math.Clamp(pct, 0, 1)));
    }

    /// <summary>現在の先頭表示位置のバイトオフセット（検索・ブックマークの次へ/前への基準）。</summary>
    public long CurrentOffset
    {
        get
        {
            if (_session is null) return 0;
            if (FilterOn)
            {
                int idx = Math.Clamp(_session.FilterTopHitIndex, 0, _session.SearchHits.Count - 1);
                return _session.SearchHits[idx];
            }
            if (_session.Mode == ViewMode.Line && _session.Index is not null)
                return _session.Document.LineStartOffset(_session.TopLine);
            return _session.TopByteOffset;
        }
    }

    // ── 入力 ──────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        base.OnPointerPressed(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        int lines = (int)Math.Round(e.Delta.Y) * 3;
        if (lines == 0) lines = e.Delta.Y > 0 ? 3 : -3;
        ScrollByLines(-lines);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int rows = VisibleRows;
        switch (e.Key)
        {
            case Key.Down: ScrollByLines(1); e.Handled = true; break;
            case Key.Up: ScrollByLines(-1); e.Handled = true; break;
            case Key.PageDown: ScrollByLines(rows - 1); e.Handled = true; break;
            case Key.PageUp: ScrollByLines(-(rows - 1)); e.Handled = true; break;
            case Key.Home: ScrollToHome(); e.Handled = true; break;
            case Key.End: ScrollToEnd(); e.Handled = true; break;
        }
        if (!e.Handled) base.OnKeyDown(e);
    }

    private void ScrollByLines(int delta)
    {
        if (_session is null || Doc is null || delta == 0) return;
        var doc = Doc;

        if (FilterOn)
        {
            _session.FilterTopHitIndex = (int)Math.Clamp(
                (long)_session.FilterTopHitIndex + delta, 0, _session.SearchHits.Count - 1);
            Refresh();
            return;
        }

        if (_session.Mode == ViewMode.Line)
        {
            long total = _session.Index?.TotalLines ?? 0;
            _session.TopLine = Math.Clamp(_session.TopLine + delta, 0, Math.Max(0, total - 1));
        }
        else
        {
            long off = _session.TopByteOffset;
            if (delta > 0)
                for (int i = 0; i < delta && off < doc.Length; i++)
                {
                    long next = doc.NextLineStart(off);
                    if (next >= doc.Length) break;
                    off = next;
                }
            else
                for (int i = 0; i < -delta && off > doc.BomLength; i++)
                    off = doc.PreviousLineStart(off);
            _session.TopByteOffset = off;
        }
        Refresh();
    }

    private void ScrollToHome()
    {
        if (_session is null || Doc is null) return;
        if (FilterOn) { _session.FilterTopHitIndex = 0; Refresh(); return; }
        if (_session.Mode == ViewMode.Line) _session.TopLine = 0;
        else _session.TopByteOffset = Doc.BomLength;
        Refresh();
    }

    private void ScrollToEnd()
    {
        if (_session is null || Doc is null) return;
        int rows = VisibleRows;
        if (FilterOn)
        {
            _session.FilterTopHitIndex = Math.Max(0, _session.SearchHits.Count - rows);
            Refresh();
            return;
        }
        if (_session.Mode == ViewMode.Line)
        {
            long total = _session.Index?.TotalLines ?? 0;
            _session.TopLine = Math.Max(0, total - rows);
        }
        else
        {
            long off = Doc.AlignToLineStart(Doc.Length);
            for (int i = 0; i < rows; i++) off = Doc.PreviousLineStart(off);
            _session.TopByteOffset = off;
        }
        Refresh();
    }

    // ── スクロールバー連携 ────────────────────────────────────

    private void OnScroll(object? sender, ScrollEventArgs e)
    {
        if (_updatingScroll || _session is null || Doc is null || _scroll is null) return;

        if (FilterOn)
            _session.FilterTopHitIndex = (int)Math.Clamp((long)_scroll.Value, 0, _session.SearchHits.Count - 1);
        else if (_session.Mode == ViewMode.Line)
            _session.TopLine = (long)_scroll.Value;
        else
            _session.TopByteOffset = Doc.AlignToLineStart((long)_scroll.Value);

        InvalidateVisual();
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        UpdateScrollBar();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateScrollBar()
    {
        if (_scroll is null) return;
        _updatingScroll = true;
        try
        {
            int rows = VisibleRows;
            if (_session is null || Doc is null)
            {
                _scroll.Minimum = 0; _scroll.Maximum = 0; _scroll.Value = 0; _scroll.ViewportSize = 1;
                return;
            }
            if (FilterOn)
            {
                _scroll.Minimum = 0;
                _scroll.Maximum = Math.Max(0, _session.SearchHits.Count);
                _scroll.ViewportSize = rows;
                _scroll.LargeChange = Math.Max(1, rows - 1);
                _scroll.SmallChange = 1;
                _scroll.Value = Math.Clamp(_session.FilterTopHitIndex, 0, _scroll.Maximum);
            }
            else if (_session.Mode == ViewMode.Line)
            {
                long total = _session.Index?.TotalLines ?? 0;
                _scroll.Minimum = 0;
                _scroll.Maximum = Math.Max(0, total);
                _scroll.ViewportSize = rows;
                _scroll.LargeChange = Math.Max(1, rows - 1);
                _scroll.SmallChange = 1;
                _scroll.Value = Math.Clamp(_session.TopLine, 0, _scroll.Maximum);
            }
            else
            {
                double bytesPerScreen = 80.0 * rows; // 1 行 ≒ 80 バイトと仮定した概算
                _scroll.Minimum = 0;
                _scroll.Maximum = Math.Max(0, Doc.Length);
                _scroll.ViewportSize = Math.Clamp(bytesPerScreen, 1, Math.Max(1, Doc.Length));
                _scroll.LargeChange = bytesPerScreen;
                _scroll.SmallChange = Math.Max(1, bytesPerScreen / rows);
                _scroll.Value = Math.Clamp(_session.TopByteOffset, 0, _scroll.Maximum);
            }
        }
        finally { _updatingScroll = false; }
    }

    // ── 描画 ──────────────────────────────────────────────────

    public override void Render(DrawingContext ctx)
    {
        if (Background is not null)
            ctx.FillRectangle(Background, new Rect(Bounds.Size));

        var doc = Doc;
        if (_session is null || doc is null) return;

        double lh = LineHeight;
        int rows = VisibleRows + 1;

        // 可視行を収集: (行頭オフセット, テキスト, 表示行番号[1始まり・不明は-1])
        var visible = new List<(long Offset, string Text, long LineNo)>(rows);
        bool lineMode = _session.Mode == ViewMode.Line && doc.Index is not null;
        bool showNumbers;

        if (FilterOn)
        {
            var hits = _session.SearchHits;
            int top = Math.Clamp(_session.FilterTopHitIndex, 0, hits.Count - 1);
            _session.FilterTopHitIndex = top;
            bool indexed = doc.Index is not null;
            for (int r = 0; r < rows && top + r < hits.Count; r++)
            {
                long off = hits[top + r];
                long no = indexed ? doc.OffsetToLineIndex(off) + 1 : -1;
                visible.Add((off, doc.GetLineAtOffset(off), no));
            }
            showNumbers = ShowLineNumbers && indexed;
        }
        else if (lineMode)
        {
            long total = doc.Index!.TotalLines;
            for (int r = 0; r < rows; r++)
            {
                long idx = _session.TopLine + r;
                if (idx >= total) break;
                visible.Add((doc.LineStartOffset(idx), doc.GetLine(idx), idx + 1));
            }
            showNumbers = ShowLineNumbers;
        }
        else
        {
            foreach (var (off, text) in doc.GetPageWithOffsets(_session.TopByteOffset, rows))
                visible.Add((off, text, -1));
            showNumbers = false;
        }

        // 行番号ガター
        double gutter = 0;
        var numberBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
        var textBrush = Brushes.Black;
        if (showNumbers && visible.Count > 0)
        {
            long maxNo = 0;
            foreach (var v in visible) maxNo = Math.Max(maxNo, v.LineNo);
            int digits = Math.Max(6, maxNo.ToString(CultureInfo.InvariantCulture).Length);
            gutter = digits * _digitWidth + Padding * 2;
            ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)),
                new Rect(0, 0, gutter, Bounds.Height));
        }

        // 検索ハイライト（§11-②）: 可視行だけ再マッチして背景色を塗る
        var hlRegex = _session.SearchHighlightRegex;
        var hlBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x66)); // 黄
        var bookmarkBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x6F, 0xE8)); // 青（§11-④）
        bool hasBookmarks = _session.Bookmarks.Count > 0;

        double x = gutter + Padding;
        double y = Padding;
        foreach (var (offset, text, lineNo) in visible)
        {
            // ブックマークマーカー（左端の青バー）
            if (hasBookmarks && _session.HasBookmark(offset))
                ctx.FillRectangle(bookmarkBrush, new Rect(0, y, 4, lh));

            if (showNumbers && lineNo > 0)
            {
                var num = MakeText(lineNo.ToString(CultureInfo.InvariantCulture), numberBrush);
                ctx.DrawText(num, new Point(gutter - Padding - num.Width, y));
            }

            if (hlRegex is not null && text.Length > 0)
            {
                foreach (System.Text.RegularExpressions.Match m in hlRegex.Matches(text))
                {
                    if (m.Length == 0) continue;
                    double xStart = m.Index == 0 ? 0 : MakeText(text[..m.Index]).WidthIncludingTrailingWhitespace;
                    double w = MakeText(text.Substring(m.Index, m.Length)).WidthIncludingTrailingWhitespace;
                    ctx.FillRectangle(hlBrush, new Rect(x + xStart, y, w, lh));
                }
            }

            ctx.DrawText(MakeText(text, textBrush), new Point(x, y));
            y += lh;
        }
    }

    protected override Size MeasureOverride(Size availableSize) => availableSize;

    private FormattedText MakeText(string s, IBrush? brush = null) => new(
        s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
        _typeface, FontSize, brush ?? Brushes.Black);
}
