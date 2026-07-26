using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using UwView.ViewModels;

namespace UwView.Controls;

/// <summary>
/// フィルタ結果ポップアップの行リスト。メインの <see cref="TextView"/> と同じ方式で
/// 可視行だけを 1 コントロールに直接描画する。
///
/// ListBox から置き換えた理由:
/// - 1 行ごとに ListBoxItem + StackPanel + TextBlock×3 + ハイライト Inlines を生成するため、
///   ヒット数が多いとスクロールが目に見えて重い
/// - ListBox 全体で PointerPressed を Tunnel 捕捉していたので、左ドラッグでスクロールバーの
///   つまみを掴めなかった（メインはスクロールバーが外部コントロールなので普通に動く）
/// </summary>
public sealed class FilterListView : Control
{
    private const double Padding = 6;
    private const double ColGap = 10;
    private const double DragThresholdPx = 4;
    private const int WheelRows = 3;

    private static readonly IBrush BgBrush = Brushes.White;
    private static readonly IBrush TextBrush = Brushes.Black;
    private static readonly IBrush HitNumberBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x6F, 0xE8));
    private static readonly IBrush LineNumberBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
    private static readonly IBrush MatchBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x66));
    private static readonly IBrush SelBrush = new SolidColorBrush(Color.FromRgb(0xB4, 0xD5, 0xEE));
    private static readonly IBrush ContextBgBrush = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));

    // メイン（TextView）と同じ等幅フォント指定。WASM では同梱の Noto Sans JP に落ちる。
    private readonly Typeface _typeface = new(new FontFamily("Cascadia Mono,Menlo,Consolas,Courier New,monospace"));
    private double _lineHeight;
    private double _charWidth;

    private IReadOnlyList<FilterRow>? _rows;
    private int _topRow;

    private ScrollBar? _scroll;
    private bool _updatingScroll;

    // 選択（index ベース。FilterRow インスタンスに依存しないので再構築で壊れない）
    private readonly HashSet<int> _selected = [];
    private int _anchor = -1;
    private int _cursor = -1;

    // ドラッグ
    private bool _pressPending;
    private bool _dragging;
    private Point _pressPoint;
    private int _pressRow = -1;
    private DispatcherTimer? _autoScroll;
    private int _autoDelta;

    public FilterListView()
    {
        Focusable = true;
        ClipToBounds = true;
        FontSize = 12;
        SizeChanged += (_, _) => { InvalidateVisual(); NotifyScrollChanged(); };
    }

    /// <summary>行の活性化（ダブルクリック / Enter）。</summary>
    public event Action<FilterRow>? RowActivated;

    /// <summary>選択行の右クリック。ホストがコピー/保存メニューを表示する。</summary>
    public event Action<PointerPressedEventArgs>? SelectionMenuRequested;

    /// <summary>コピー要求（Cmd/Ctrl+C）。</summary>
    public event Action? CopyRequested;

    /// <summary>スクロール位置・サイズが変わった（UVP の矩形選択オーバーレイが追従に使う）。</summary>
    public event EventHandler? ScrollChanged;

    public double FontSize { get; set; }

    public IReadOnlyList<FilterRow>? Rows
    {
        get => _rows;
        set
        {
            _rows = value;
            ClampTop();
            // 行の中身が変わったので選択は保持しつつカーソルだけ範囲内へ収める
            if (_cursor >= Count) _cursor = Count - 1;
            InvalidateVisual();
            NotifyScrollChanged();
        }
    }

    public int Count => _rows?.Count ?? 0;

    /// <summary>先頭表示行（0始まり）。</summary>
    public int TopRow
    {
        get => _topRow;
        set
        {
            int v = Math.Clamp(value, 0, Math.Max(0, Count - VisibleRows));
            if (v == _topRow) return;
            _topRow = v;
            InvalidateVisual();
            NotifyScrollChanged();
        }
    }

    /// <summary>1 画面に収まる行数。</summary>
    public int VisibleRows => Math.Max(1, (int)Math.Floor(Math.Max(0, Bounds.Height) / LineHeight));

    public double RowHeight => LineHeight;

    /// <summary>本文の開始 x（矩形選択の列計算用）。描画のたびに更新される。</summary>
    public double TextStartX { get; private set; } = Padding;

    /// <summary>等幅 1 文字の幅（矩形選択の列計算用）。</summary>
    public double CharWidth => _charWidth <= 0 ? Measure().Width : _charWidth;

    /// <summary>選択行を表示順で返す。</summary>
    public List<FilterRow> SelectedRowsInOrder()
    {
        var indices = new List<int>(_selected);
        indices.Sort();
        var rows = new List<FilterRow>(indices.Count);
        var src = _rows;
        if (src is null) return rows;
        foreach (int i in indices)
            if (i >= 0 && i < src.Count) rows.Add(src[i]);
        return rows;
    }

    public bool HasSelection => _selected.Count > 0;

    /// <summary>カーソル行（単一クリックで選んだ行。ジャンプ対象）。</summary>
    public FilterRow? CursorRow
        => _rows is { } src && _cursor >= 0 && _cursor < src.Count ? src[_cursor] : null;

    public void ClearSelection()
    {
        if (_selected.Count == 0) return;
        _selected.Clear();
        InvalidateVisual();
    }

    /// <summary>指定行を可視範囲に入れる。</summary>
    public void EnsureVisible(int row)
    {
        if (row < 0) return;
        if (row < _topRow) TopRow = row;
        else if (row >= _topRow + VisibleRows) TopRow = row - VisibleRows + 1;
    }

    /// <summary>y 座標 → 行 index（範囲外は最寄りへクランプ。行が無ければ -1）。</summary>
    public int RowFromY(double y)
    {
        if (Count == 0) return -1;
        int row = _topRow + (int)Math.Floor(y / LineHeight);
        return Math.Clamp(row, 0, Count - 1);
    }

    /// <summary>行 index → 描画 y（可視範囲外の値も返す）。</summary>
    public double YOfRow(int row) => (row - _topRow) * LineHeight;

    // ── スクロールバー連携（TextView と同じ方式）──────────────

    public void AttachScrollBar(ScrollBar scrollBar)
    {
        _scroll = scrollBar;
        _scroll.Scroll += OnScroll;
        UpdateScrollBar();
    }

    private void OnScroll(object? sender, ScrollEventArgs e)
    {
        if (_updatingScroll || _scroll is null) return;
        TopRow = (int)_scroll.Value;
    }

    private void UpdateScrollBar()
    {
        if (_scroll is null) return;
        _updatingScroll = true;
        try
        {
            int rows = VisibleRows;
            _scroll.Minimum = 0;
            // Avalonia の Maximum は Value の上限そのもの。総数を入れると
            // 末尾までドラッグしたときに 1 行も描画されなくなる。
            _scroll.Maximum = Math.Max(0, Count - rows);
            _scroll.ViewportSize = rows;
            _scroll.LargeChange = Math.Max(1, rows - 1);
            _scroll.SmallChange = 1;
            _scroll.Value = Math.Clamp(_topRow, 0, _scroll.Maximum);
        }
        finally { _updatingScroll = false; }
    }

    private void NotifyScrollChanged()
    {
        UpdateScrollBar();
        ScrollChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClampTop()
    {
        int max = Math.Max(0, Count - VisibleRows);
        if (_topRow > max) _topRow = max;
        if (_topRow < 0) _topRow = 0;
    }

    // ── 入力 ─────────────────────────────────────────────────

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        TopRow = _topRow - (int)Math.Round(e.Delta.Y * WheelRows);
        e.Handled = true;
        base.OnPointerWheelChanged(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var pt = e.GetCurrentPoint(this);

        if (pt.Properties.IsRightButtonPressed)
        {
            if (HasSelection) SelectionMenuRequested?.Invoke(e);
            e.Handled = true;
            base.OnPointerPressed(e);
            return;
        }

        if (!pt.Properties.IsLeftButtonPressed) { base.OnPointerPressed(e); return; }

        int row = RowFromY(e.GetPosition(this).Y);
        if (row < 0) { base.OnPointerPressed(e); return; }

        if (e.ClickCount == 2)
        {
            if (_rows is { } src && row < src.Count) RowActivated?.Invoke(src[row]);
            e.Handled = true;
            base.OnPointerPressed(e);
            return;
        }

        bool toggle = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (toggle)
        {
            if (!_selected.Add(row)) _selected.Remove(row);
            _anchor = _cursor = row;
        }
        else if (extend && _anchor >= 0)
        {
            SelectRange(_anchor, row);
            _cursor = row;
        }
        else
        {
            _selected.Clear();
            _selected.Add(row);
            _anchor = _cursor = row;
            // ドラッグ開始待ち（閾値を超えたら範囲選択へ）
            _pressPending = true;
            _dragging = false;
            _pressPoint = e.GetPosition(this);
            _pressRow = row;
            e.Pointer.Capture(this);
        }

        InvalidateVisual();
        e.Handled = true;
        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_pressPending && !_dragging) { base.OnPointerMoved(e); return; }

        var p = e.GetPosition(this);
        if (!_dragging)
        {
            if (Math.Abs(p.X - _pressPoint.X) + Math.Abs(p.Y - _pressPoint.Y) < DragThresholdPx)
            {
                base.OnPointerMoved(e);
                return;
            }
            _dragging = true;
        }

        int row = RowFromY(p.Y);
        if (row >= 0)
        {
            SelectRange(_pressRow, row);
            _cursor = row;
        }

        // ビュー端でオートスクロール（距離で加速。TextView と同じ挙動）
        int delta = 0;
        if (p.Y < 0) delta = -(1 + (int)Math.Min(50, -p.Y / LineHeight * 3));
        else if (p.Y > Bounds.Height) delta = 1 + (int)Math.Min(50, (p.Y - Bounds.Height) / LineHeight * 3);
        _autoDelta = delta;
        if (delta != 0)
        {
            _autoScroll ??= CreateAutoScrollTimer();
            if (!_autoScroll.IsEnabled) _autoScroll.Start();
        }
        else _autoScroll?.Stop();

        InvalidateVisual();
        base.OnPointerMoved(e);
    }

    private DispatcherTimer CreateAutoScrollTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            if (!_dragging || _autoDelta == 0) { timer.Stop(); return; }
            TopRow = _topRow + _autoDelta;
            int edge = _autoDelta < 0 ? _topRow : Math.Min(Count - 1, _topRow + VisibleRows - 1);
            SelectRange(_pressRow, edge);
            _cursor = edge;
            InvalidateVisual();
        };
        return timer;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        bool wasPressed = _pressPending || _dragging;
        _pressPending = false;
        _dragging = false;
        _autoScroll?.Stop();
        e.Pointer.Capture(null);
        // 押下時の再描画が取りこぼされても選択がここで必ず反映されるようにする
        if (wasPressed) InvalidateVisual();
        base.OnPointerReleased(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int rows = VisibleRows;
        switch (e.Key)
        {
            case Key.Enter when CursorRow is { } row:
                RowActivated?.Invoke(row);
                e.Handled = true;
                return;
            case Key.C when e.KeyModifiers.HasFlag(KeyModifiers.Control)
                         || e.KeyModifiers.HasFlag(KeyModifiers.Meta):
                CopyRequested?.Invoke();
                e.Handled = true;
                return;
            case Key.A when e.KeyModifiers.HasFlag(KeyModifiers.Control)
                         || e.KeyModifiers.HasFlag(KeyModifiers.Meta):
                SelectRange(0, Count - 1);
                _anchor = 0;
                _cursor = Count - 1;
                InvalidateVisual();
                e.Handled = true;
                return;
            case Key.Up: MoveCursor(-1, e); return;
            case Key.Down: MoveCursor(1, e); return;
            case Key.PageUp: MoveCursor(-rows, e); return;
            case Key.PageDown: MoveCursor(rows, e); return;
            case Key.Home: MoveCursorTo(0, e); return;
            case Key.End: MoveCursorTo(Count - 1, e); return;
        }
        base.OnKeyDown(e);
    }

    private void MoveCursor(int delta, KeyEventArgs e)
        => MoveCursorTo((_cursor < 0 ? _topRow : _cursor) + delta, e);

    private void MoveCursorTo(int row, KeyEventArgs e)
    {
        if (Count == 0) return;
        row = Math.Clamp(row, 0, Count - 1);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _anchor >= 0)
            SelectRange(_anchor, row);
        else
        {
            _selected.Clear();
            _selected.Add(row);
            _anchor = row;
        }
        _cursor = row;
        EnsureVisible(row);
        InvalidateVisual();
        e.Handled = true;
    }

    private void SelectRange(int a, int b)
    {
        _selected.Clear();
        int lo = Math.Min(a, b), hi = Math.Max(a, b);
        for (int i = lo; i <= hi; i++) _selected.Add(i);
    }

    // ── 描画 ─────────────────────────────────────────────────

    private double LineHeight
    {
        get
        {
            if (_lineHeight <= 0) Measure();
            return _lineHeight;
        }
    }

    private FormattedText Measure()
    {
        var ft = MakeText("0000");
        _lineHeight = Math.Ceiling(ft.Height);
        _charWidth = ft.WidthIncludingTrailingWhitespace / 4.0;
        return ft;
    }

    private FormattedText MakeText(string s, IBrush? brush = null) => new(
        s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
        _typeface, FontSize, brush ?? TextBrush);

    protected override Size MeasureOverride(Size availableSize) => availableSize;

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(BgBrush, new Rect(Bounds.Size));

        var rows = _rows;
        if (rows is null || rows.Count == 0) return;

        double lh = LineHeight;
        double cw = CharWidth;
        int count = rows.Count;
        int last = Math.Min(count, _topRow + VisibleRows + 1);

        // 列幅は桁数で決める（等幅前提）。ヒット番号は総数、行番号は可視行の最大値から。
        int hitDigits = count.ToString("N0", CultureInfo.CurrentCulture).Length;
        int lineDigits = 4;
        for (int i = _topRow; i < last; i++)
            lineDigits = Math.Max(lineDigits, rows[i].LineNumberText.Length);

        double hitColW = hitDigits * cw;
        double lineColW = lineDigits * cw;
        double hitRight = Padding + hitColW;
        double lineRight = hitRight + ColGap + lineColW;
        double textX = lineRight + ColGap;
        TextStartX = textX;

        double y = 0;
        for (int i = _topRow; i < last; i++, y += lh)
        {
            var row = rows[i];

            // 行背景: 選択 > 文脈行（±N の前後行は背景で区別する。文字は常に黒）
            if (_selected.Contains(i))
                ctx.FillRectangle(SelBrush, new Rect(0, y, Bounds.Width, lh));
            else if (!row.IsHit && !row.IsSeparator)
                ctx.FillRectangle(ContextBgBrush, new Rect(0, y, Bounds.Width, lh));

            if (row.IsSeparator)
            {
                ctx.DrawText(MakeText("⋯", LineNumberBrush), new Point(textX, y));
                continue;
            }

            string hitText = row.HitNumberText;
            if (hitText.Length > 0)
            {
                var ft = MakeText(hitText, HitNumberBrush);
                ctx.DrawText(ft, new Point(hitRight - ft.WidthIncludingTrailingWhitespace, y));
            }

            string lineText = row.LineNumberText;
            if (lineText.Length > 0)
            {
                var ft = MakeText(lineText, LineNumberBrush);
                ctx.DrawText(ft, new Point(lineRight - ft.WidthIncludingTrailingWhitespace, y));
            }

            string text = row.Text;
            if (text.Length == 0) continue;

            // 検索マッチの黄ハイライト（可視行だけ再マッチ）
            if (row.HighlightRegex is { } regex)
            {
                try
                {
                    foreach (Match m in regex.Matches(text))
                    {
                        if (m.Length == 0) break;
                        double mx = m.Index == 0 ? 0 : MakeText(text[..m.Index]).WidthIncludingTrailingWhitespace;
                        double mw = MakeText(text.Substring(m.Index, m.Length)).WidthIncludingTrailingWhitespace;
                        ctx.FillRectangle(MatchBrush, new Rect(textX + mx, y, mw, lh));
                    }
                }
                catch (RegexMatchTimeoutException) { /* 部分ハイライトのまま */ }
            }

            ctx.DrawText(MakeText(text, TextBrush), new Point(textX, y));
        }
    }
}
