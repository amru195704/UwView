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

public enum NavigationMode { Page, Line }

/// <summary>
/// 2 億行を扱う自前描画・仮想化テキスト面（§4.5）。
/// 画面に見えている行だけを LineDocument から取得して Render で描く。
/// ページモード（バイト位置基準）と行モード（行番号基準）の 2 状態を持つ。
/// </summary>
public sealed class TextView : Control
{
    public static readonly DirectProperty<TextView, LineDocument?> DocumentProperty =
        AvaloniaProperty.RegisterDirect<TextView, LineDocument?>(
            nameof(Document), o => o.Document, (o, v) => o.Document = v);

    private LineDocument? _document;
    public LineDocument? Document
    {
        get => _document;
        set
        {
            if (SetAndRaise(DocumentProperty, ref _document, value))
            {
                Mode = NavigationMode.Page;
                _topByteOffset = _document?.BomLength ?? 0;
                _topLine = 0;
                InvalidateVisual();
                NotifyChanged();
            }
        }
    }

    public NavigationMode Mode { get; private set; } = NavigationMode.Page;
    public bool ShowLineNumbers { get; set; } = true;

    public long TopLine => _topLine;
    public long TopByteOffset => _topByteOffset;
    public double Percent => _document is { Length: > 0 } d ? (double)_topByteOffset / d.Length : 0;
    public long? TotalLines => _document?.TotalLines;

    /// <summary>先頭表示位置・モード・索引状態が変わったら発火（ステータス/スクロールバー更新用）。</summary>
    public event EventHandler? StateChanged;

    private long _topLine;        // 行モード: 先頭表示行の index
    private long _topByteOffset;  // ページモード: 先頭表示行のバイトオフセット

    private readonly Typeface _typeface = new(new FontFamily("Cascadia Mono,Menlo,Consolas,Courier New,monospace"));
    private const double FontSize = 14;
    private const double Padding = 4;
    private double _lineHeight;
    private double _digitWidth;

    private ScrollBar? _scroll;
    private bool _updatingScroll;

    public TextView()
    {
        Focusable = true;
        ClipToBounds = true;
        Background = Brushes.White;
        SizeChanged += (_, _) => { InvalidateVisual(); NotifyChanged(); };
    }

    // Control は既定で背景を塗らないので明示的に持つ
    public IBrush? Background { get; set; }

    public void AttachScrollBar(ScrollBar scrollBar)
    {
        _scroll = scrollBar;
        _scroll.Scroll += OnScroll;
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

    // ── モード切替 ────────────────────────────────────────────

    /// <summary>索引完了 → 行モードへ昇格。現在のバイトオフセットを最寄り行番号に変換して位置継続（§3.1-3）。</summary>
    public void PromoteToLineMode()
    {
        if (_document?.Index is null) return;
        _topLine = _document.OffsetToLineIndex(_topByteOffset);
        Mode = NavigationMode.Line;
        InvalidateVisual();
        NotifyChanged();
    }

    // ── ジャンプ ──────────────────────────────────────────────

    public void JumpToLine(long line)
    {
        if (_document?.Index is null) return;
        long total = _document.Index.TotalLines;
        _topLine = Math.Clamp(line, 0, Math.Max(0, total - 1));
        InvalidateVisual();
        NotifyChanged();
    }

    public void JumpToPercent(double pct)
    {
        if (_document is null) return;
        long off = (long)(_document.Length * Math.Clamp(pct, 0, 1));
        _topByteOffset = _document.AlignToLineStart(off);
        InvalidateVisual();
        NotifyChanged();
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
        if (_document is null || delta == 0) return;

        if (Mode == NavigationMode.Line)
        {
            long total = _document.Index?.TotalLines ?? 0;
            long max = Math.Max(0, total - 1);
            _topLine = Math.Clamp(_topLine + delta, 0, max);
        }
        else
        {
            long off = _topByteOffset;
            if (delta > 0)
                for (int i = 0; i < delta && off < _document.Length; i++)
                {
                    long next = _document.NextLineStart(off);
                    if (next >= _document.Length) break;
                    off = next;
                }
            else
                for (int i = 0; i < -delta && off > _document.BomLength; i++)
                    off = _document.PreviousLineStart(off);
            _topByteOffset = off;
        }
        InvalidateVisual();
        NotifyChanged();
    }

    private void ScrollToHome()
    {
        if (_document is null) return;
        if (Mode == NavigationMode.Line) _topLine = 0;
        else _topByteOffset = _document.BomLength;
        InvalidateVisual();
        NotifyChanged();
    }

    private void ScrollToEnd()
    {
        if (_document is null) return;
        int rows = VisibleRows;
        if (Mode == NavigationMode.Line)
        {
            long total = _document.Index?.TotalLines ?? 0;
            _topLine = Math.Max(0, total - rows);
        }
        else
        {
            long off = _document.AlignToLineStart(_document.Length);
            for (int i = 0; i < rows; i++) off = _document.PreviousLineStart(off);
            _topByteOffset = off;
        }
        InvalidateVisual();
        NotifyChanged();
    }

    // ── スクロールバー連携 ────────────────────────────────────

    private void OnScroll(object? sender, ScrollEventArgs e)
    {
        if (_updatingScroll || _document is null || _scroll is null) return;

        if (Mode == NavigationMode.Line)
            _topLine = (long)_scroll.Value;
        else
            _topByteOffset = _document.AlignToLineStart((long)_scroll.Value);

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
            if (_document is null)
            {
                _scroll.Maximum = 0; _scroll.Value = 0; _scroll.ViewportSize = 1;
                return;
            }
            if (Mode == NavigationMode.Line)
            {
                long total = _document.Index?.TotalLines ?? 0;
                _scroll.Minimum = 0;
                _scroll.Maximum = Math.Max(0, total);
                _scroll.ViewportSize = rows;
                _scroll.LargeChange = Math.Max(1, rows - 1);
                _scroll.SmallChange = 1;
                _scroll.Value = Math.Clamp(_topLine, 0, _scroll.Maximum);
            }
            else
            {
                // ページモードはバイト基準の概算スクロール（less の巨大ファイル閲覧と同じ挙動）
                double bytesPerScreen = 80.0 * rows; // 1 行 ≒ 80 バイトと仮定した概算
                _scroll.Minimum = 0;
                _scroll.Maximum = Math.Max(0, _document.Length);
                _scroll.ViewportSize = Math.Clamp(bytesPerScreen, 1, Math.Max(1, _document.Length));
                _scroll.LargeChange = bytesPerScreen;
                _scroll.SmallChange = Math.Max(1, bytesPerScreen / rows);
                _scroll.Value = Math.Clamp(_topByteOffset, 0, _scroll.Maximum);
            }
        }
        finally { _updatingScroll = false; }
    }

    // ── 描画 ──────────────────────────────────────────────────

    public override void Render(DrawingContext ctx)
    {
        if (Background is not null)
            ctx.FillRectangle(Background, new Rect(Bounds.Size));

        var doc = _document;
        if (doc is null) return;

        double lh = LineHeight;
        int rows = VisibleRows + 1;

        IReadOnlyList<string> lines;
        long firstLineNumber;
        bool lineMode = Mode == NavigationMode.Line && doc.Index is not null;

        if (lineMode)
        {
            long total = doc.Index!.TotalLines;
            var list = new List<string>(rows);
            for (int r = 0; r < rows; r++)
            {
                long idx = _topLine + r;
                if (idx >= total) break;
                list.Add(doc.GetLine(idx));
            }
            lines = list;
            firstLineNumber = _topLine;
        }
        else
        {
            lines = doc.GetPage(_topByteOffset, rows);
            firstLineNumber = -1;
        }

        // 行番号ガター幅
        double gutter = 0;
        var numberBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
        var textBrush = Brushes.Black;
        if (lineMode && ShowLineNumbers)
        {
            long last = firstLineNumber + lines.Count;
            int digits = Math.Max(6, (last + 1).ToString(CultureInfo.InvariantCulture).Length);
            gutter = digits * _digitWidth + Padding * 2;
            ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)),
                new Rect(0, 0, gutter, Bounds.Height));
        }

        double x = gutter + Padding;
        double y = Padding;
        for (int r = 0; r < lines.Count; r++)
        {
            if (lineMode && ShowLineNumbers)
            {
                var num = MakeText((firstLineNumber + r + 1).ToString(CultureInfo.InvariantCulture), numberBrush);
                ctx.DrawText(num, new Point(gutter - Padding - num.Width, y));
            }
            var ft = MakeText(lines[r], textBrush);
            ctx.DrawText(ft, new Point(x, y));
            y += lh;
        }
    }

    private FormattedText MakeText(string s, IBrush? brush = null) => new(
        s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
        _typeface, FontSize, brush ?? Brushes.Black);
}
