using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using UwView.Core;

namespace UwView.Controls;

/// <summary>
/// 2 億行を扱う自前描画・仮想化テキスト面（§4.5）。Active な <see cref="DocumentSession"/> を描画する。
/// 先頭表示位置・モードはセッションが保持するため、タブ切替はセッション差し替え＋再描画のみで状態が続く。
/// </summary>
public class TextView : Control
{
    private DocumentSession? _session;
    public DocumentSession? Session
    {
        get => _session;
        set
        {
            _session = value;
            _emphasizedOffset = null; // 強調行は別ドキュメントへ持ち越さない
            ClearLineSelection();     // 行選択も持ち越さない
            OnSessionChanged();       // 派生クラス（Pro等）の状態リセット用フック
            InvalidateVisual();
            NotifyChanged();
        }
    }

    /// <summary>ジャンプ先として強調表示する行の行頭オフセット（フィルタ結果からのジャンプ用）。</summary>
    private long? _emphasizedOffset;

    /// <summary>セッション差し替え時に派生クラスが状態をリセットするためのフック。</summary>
    protected virtual void OnSessionChanged() { }

    // ── 派生クラス（ProTextView 等）向けの描画メトリクス公開 ──────────
    /// <summary>1行の高さ（ピクセル）。</summary>
    protected double RowHeight => LineHeight;
    /// <summary>半角1セルの幅（ピクセル）。</summary>
    protected double CellWidth { get { _ = LineHeight; return _digitWidth; } }
    /// <summary>直近の Render で使った行番号ガター幅（ピクセル）。</summary>
    protected double GutterWidth => _lastGutter;
    private double _lastGutter;
    /// <summary>可視行数。</summary>
    protected int VisibleRowCount => VisibleRows;
    /// <summary>スクロールバー・ステータスの更新通知（派生クラス用）。</summary>
    protected void RaiseStateChanged() => NotifyChanged();

    // ── 行選択（ドラッグで行範囲を選択 → 右クリック/Cmd+C で copy・save）────

    private const double DragThresholdPx = 4;
    private const long CopyMaxLines = 100_000; // 超えたらクリップボードでなく保存へ

    private long? _selAnchor, _selExtent;   // 選択の起点/現在点（行 index）
    private bool _selDragging;
    private bool _selPressPending;
    private Point _selPressPoint;
    private long _selPressLine;
    private DispatcherTimer? _selAutoScroll;
    private int _selAutoDelta;
    private bool _selCopying;
    private string? _selCopyStatus;

    /// <summary>派生クラス（Pro の矩形モード等）が行選択を無効化するためのフック。</summary>
    protected virtual bool AllowLineSelection => true;

    /// <summary>行選択があるか。</summary>
    protected bool HasLineSelection => _selAnchor is not null && _selExtent is not null;

    private long SelTop => Math.Min(_selAnchor ?? 0, _selExtent ?? 0);
    private long SelBottom => Math.Max(_selAnchor ?? 0, _selExtent ?? 0);

    /// <summary>行選択の状態表示（ステータスバー用）。選択なしなら null。</summary>
    public string? SelectionInfo =>
        _selCopyStatus is not null ? _selCopyStatus
        : HasLineSelection
            ? Localization.Localizer.Instance.Format("SelectionLines",
                (SelBottom - SelTop + 1).ToString("N0", Localization.Localizer.Instance.Culture))
            : null;

    protected void ClearLineSelection()
    {
        _selAnchor = _selExtent = null;
        _selDragging = false;
        _selPressPending = false;
        _selAutoScroll?.Stop();
    }

    /// <summary>行選択が使える状態か（行モード＋索引済み。フィルタ表示中は不可）。</summary>
    private bool LineSelectable =>
        AllowLineSelection && !FilterOn
        && _session is { Mode: ViewMode.Line } s && s.Index is not null;

    private long LineFromPoint(Point p)
    {
        long total = _session?.Index?.TotalLines ?? 1;
        return Math.Clamp(_session!.TopLine + (long)Math.Floor((p.Y - Padding) / LineHeight),
            0, Math.Max(0, total - 1));
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

    /// <summary>
    /// バイトオフセット基準のジャンプ（画面中央寄せ＋行強調）。フィルタ結果ポップアップからの
    /// ジャンプ用: 該当行が画面の中程に来るようにスクロールし、その行を強調表示する
    /// （行全体=水色、マッチ部=検索ハイライトの黄。次のジャンプ or セッション切替まで維持）。
    /// </summary>
    public void JumpToOffsetCentered(long byteOffset)
    {
        if (_session is null) return;
        long aligned = _session.Document.AlignToLineStart(byteOffset);
        int back = VisibleRows / 2;

        if (_session.Mode == ViewMode.Line && _session.Index is not null)
        {
            long line = _session.Document.OffsetToLineIndex(aligned);
            _session.TopLine = Math.Max(0, line - back);
        }
        else
        {
            // ページモード: 対象行から半画面ぶん行頭を遡る
            long top = aligned;
            for (int i = 0; i < back && top > 0; i++)
                top = _session.Document.PreviousLineStart(top);
            _session.TopByteOffset = top;
        }

        _emphasizedOffset = aligned;
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

        var pt = e.GetCurrentPoint(this);
        if (pt.Properties.IsLeftButtonPressed && LineSelectable)
        {
            _selPressPoint = e.GetPosition(this);
            _selPressLine = LineFromPoint(_selPressPoint);
            _selPressPending = true;
            _selDragging = false;
            e.Pointer.Capture(this);
        }
        else if (pt.Properties.IsRightButtonPressed && HasLineSelection)
        {
            ShowSelectionMenu();
            e.Handled = true;
        }
        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_selPressPending || _selDragging)
        {
            var p = e.GetPosition(this);

            if (!_selDragging)
            {
                if (Math.Abs(p.X - _selPressPoint.X) + Math.Abs(p.Y - _selPressPoint.Y) < DragThresholdPx)
                {
                    base.OnPointerMoved(e);
                    return;
                }
                _selDragging = true;
                _selAnchor = _selExtent = _selPressLine;
            }

            _selExtent = LineFromPoint(p);

            // ビュー端でオートスクロール（距離で加速）
            int delta = 0;
            if (p.Y < 0) delta = -(1 + (int)Math.Min(50, -p.Y / LineHeight * 3));
            else if (p.Y > Bounds.Height) delta = 1 + (int)Math.Min(50, (p.Y - Bounds.Height) / LineHeight * 3);
            _selAutoDelta = delta;
            if (delta != 0)
            {
                _selAutoScroll ??= CreateSelAutoScrollTimer();
                if (!_selAutoScroll.IsEnabled) _selAutoScroll.Start();
            }
            else
            {
                _selAutoScroll?.Stop();
            }

            InvalidateVisual();
            NotifyChanged();
        }
        base.OnPointerMoved(e);
    }

    private DispatcherTimer CreateSelAutoScrollTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            if (!_selDragging || _session is null || _selAutoDelta == 0) { timer.Stop(); return; }
            long total = _session.Index?.TotalLines ?? 0;
            _session.TopLine = Math.Clamp(_session.TopLine + _selAutoDelta, 0, Math.Max(0, total - 1));
            _selExtent = _selAutoDelta < 0
                ? _session.TopLine
                : Math.Min(total - 1, _session.TopLine + VisibleRows - 1);
            Refresh();
        };
        return timer;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_selDragging)
        {
            _selDragging = false;   // リリースで確定
            _selPressPending = false;
            _selAutoScroll?.Stop();
            InvalidateVisual();
            NotifyChanged();
        }
        else if (_selPressPending)
        {
            _selPressPending = false;   // 単クリック＝既存選択の解除
            if (_selAnchor is not null)
            {
                ClearLineSelection();
                InvalidateVisual();
                NotifyChanged();
            }
        }
        e.Pointer.Capture(null);
        base.OnPointerReleased(e);
    }

    private void ShowSelectionMenu()
    {
        var l = Localization.Localizer.Instance;
        var flyout = new MenuFlyout();
        var copy = new MenuItem { Header = l["MenuCopy"] };
        copy.Click += (_, _) => _ = CopyOrSaveSelectionAsync(toFile: false);
        var save = new MenuItem { Header = l["MenuSaveAs"] };
        save.Click += (_, _) => _ = CopyOrSaveSelectionAsync(toFile: true);
        flyout.Items.Add(copy);
        flyout.Items.Add(save);
        flyout.ShowAt(this, showAtPointer: true);
    }

    /// <summary>選択行を書き出す（コピー or ファイル保存。上限超のコピーは保存へ振替）。</summary>
    private async Task CopyOrSaveSelectionAsync(bool toFile)
    {
        if (!HasLineSelection || Doc is not { IsIndexed: true } doc) return;
        if (_selCopying) return;

        long top = SelTop, bottom = SelBottom;
        long rows = bottom - top + 1;
        if (!toFile && rows > CopyMaxLines) toFile = true;

        _selCopying = true;
        try
        {
            if (!toFile)
            {
                var sb = new System.Text.StringBuilder();
                for (long i = top; i <= bottom; i++)
                {
                    sb.AppendLine(doc.GetLine(i));
                    if ((i & 1023) == 1023) await Task.Yield();
                }
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard is not null)
                    await clipboard.SetTextAsync(sb.ToString());
            }
            else
            {
                var l = Localization.Localizer.Instance;
                var top2 = TopLevel.GetTopLevel(this);
                if (top2 is null) return;
                var file = await top2.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = l["SaveSelectionTitle"],
                    SuggestedFileName = "selection.txt",
                    DefaultExtension = "txt",
                });
                if (file is null) return;
                await using var stream = await file.OpenWriteAsync();
                await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), 1 << 16);
                for (long i = top; i <= bottom; i++)
                {
                    await writer.WriteLineAsync(doc.GetLine(i));
                    if ((i & 1023) == 1023)
                    {
                        _selCopyStatus = ((double)(i - top + 1) / rows).ToString("P0", CultureInfo.CurrentCulture);
                        NotifyChanged();
                        await Task.Yield();
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException) { /* 中断は黙って終了 */ }
        finally
        {
            _selCopying = false;
            _selCopyStatus = null;
            NotifyChanged();
        }
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
            case Key.Escape when HasLineSelection:
                ClearLineSelection();
                Refresh();
                e.Handled = true;
                break;
            case Key.C when HasLineSelection
                         && (e.KeyModifiers.HasFlag(KeyModifiers.Control)
                          || e.KeyModifiers.HasFlag(KeyModifiers.Meta)):
                _ = CopyOrSaveSelectionAsync(toFile: false);
                e.Handled = true;
                break;
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
        _lastGutter = gutter; // ピクセル→セル座標変換（矩形選択）用に保存

        // 検索ハイライト（§11-②）: 可視行だけ再マッチして背景色を塗る
        var hlRegex = _session.SearchHighlightRegex;
        var hlBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x66)); // 黄
        var bookmarkBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x6F, 0xE8)); // 青（§11-④）
        var emphasisBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xE8, 0xFA)); // 水色（ジャンプ先の行）
        var selBrush = new SolidColorBrush(Color.FromRgb(0xB4, 0xD5, 0xEE)); // 行選択（やや濃い水色）
        bool hasBookmarks = _session.Bookmarks.Count > 0;
        bool hasSel = HasLineSelection && lineMode && !FilterOn;
        long selTop = SelTop, selBottom = SelBottom;

        double x = gutter + Padding;
        double y = Padding;
        foreach (var (offset, text, lineNo) in visible)
        {
            // 行選択の反転表示（行番号 lineNo は 1 始まり）
            if (hasSel && lineNo > 0 && lineNo - 1 >= selTop && lineNo - 1 <= selBottom)
                ctx.FillRectangle(selBrush, new Rect(gutter, y, Math.Max(0, Bounds.Width - gutter), lh));

            // ジャンプ先の強調行: 行全体を水色に（マッチ部の黄はこの上に塗られる）
            if (_emphasizedOffset == offset)
                ctx.FillRectangle(emphasisBrush, new Rect(gutter, y, Math.Max(0, Bounds.Width - gutter), lh));

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
