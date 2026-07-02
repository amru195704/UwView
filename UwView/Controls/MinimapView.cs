using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using UwView.Core;

namespace UwView.Controls;

/// <summary>
/// ミニマップ（§11-⑥）。ファイル全体を縦に圧縮し、検索ヒット位置を色点で表示する。
/// すべてバイトオフセット基準（0..Length → 0..Height）。クリック/ドラッグでジャンプ。
/// </summary>
public sealed class MinimapView : Control
{
    private DocumentSession? _session;
    public DocumentSession? Session
    {
        get => _session;
        set { _session = value; InvalidateVisual(); }
    }

    /// <summary>現在の先頭表示位置（TextView.CurrentOffset）。ビューポート印に使う。</summary>
    private long _viewOffset;
    public long ViewOffset
    {
        get => _viewOffset;
        set { _viewOffset = value; InvalidateVisual(); }
    }

    /// <summary>クリック位置の割合（0..1）でジャンプ要求。</summary>
    public event EventHandler<double>? JumpRequested;

    private static readonly IBrush BgBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
    private static readonly IBrush HitBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0x71, 0x1A)); // 橙（検索）
    private static readonly IBrush BookmarkBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x6F, 0xE8)); // 青（§11-④）
    private static readonly IBrush ViewBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x40, 0x40, 0x40));

    public MinimapView()
    {
        ClipToBounds = true;
        Width = 14;
    }

    public override void Render(DrawingContext ctx)
    {
        double h = Bounds.Height, w = Bounds.Width;
        ctx.FillRectangle(BgBrush, new Rect(0, 0, w, h));

        var session = _session;
        if (session is null || session.Document.Length <= 0 || h <= 2) return;
        double length = session.Document.Length;

        // ヒットをピクセル行に集約してから描く（100万ヒットでも描画は高々 Height 本）
        var hits = session.SearchHits;
        if (hits.Count > 0)
        {
            int rows = Math.Max(1, (int)h);
            var mark = new bool[rows + 1];
            for (int i = 0; i < hits.Count; i++)
            {
                int y = (int)(hits[i] / length * rows);
                mark[Math.Clamp(y, 0, rows)] = true;
            }
            for (int y = 0; y <= rows; y++)
                if (mark[y])
                    ctx.FillRectangle(HitBrush, new Rect(1, y, w - 2, 1.5));
        }

        // ブックマーク（左半分・青）
        var bookmarks = session.Bookmarks;
        for (int i = 0; i < bookmarks.Count; i++)
        {
            double by = bookmarks[i] / length * h;
            ctx.FillRectangle(BookmarkBrush, new Rect(0, Math.Clamp(by, 0, h - 2), w / 2, 2));
        }

        // 現在位置マーカー
        double vy = _viewOffset / length * h;
        ctx.FillRectangle(ViewBrush, new Rect(0, Math.Clamp(vy - 2, 0, h - 4), w, 4));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        RequestJump(e.GetPosition(this).Y);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            RequestJump(e.GetPosition(this).Y);
            e.Handled = true;
        }
    }

    private void RequestJump(double y)
    {
        if (Bounds.Height <= 0) return;
        JumpRequested?.Invoke(this, Math.Clamp(y / Bounds.Height, 0, 1));
    }
}
