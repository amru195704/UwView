using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using UwView.Localization;
using UwView.Services;

namespace UwView.Controls;

/// <summary>
/// ステータスバーのファイルの名前（2026-10-10 オーナー依頼）。長いパスで右の情報が押し出されないよう、
/// ふだんは<b>ファイル名だけ</b>を出す。押すと（左でも右でも）共通の操作の窓（<see cref="FileActionsPanel"/>）を出す：
/// フルパスと［パスをコピー］［フォルダーを開く］［ターミナルで開く］［アプリで開く］。
/// パスでないもの（「（ファイル未選択）」など）はそのまま出し、押しても何もしない。
/// </summary>
public sealed class PathLabel : Border
{
    public static readonly StyledProperty<string?> PathProperty =
        AvaloniaProperty.Register<PathLabel, string?>(nameof(Path));

    private readonly TextBlock _name = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Flyout _flyout = new() { Placement = PlacementMode.TopEdgeAlignedLeft };

    private static Localizer L => Localizer.Instance;

    /// <summary>出すファイルのパス（フルパス。パスでなければそのまま出す）。</summary>
    public string? Path
    {
        get => GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    /// <summary>自動テスト用: いま出しているファイル名。</summary>
    internal string ShownText => _name.Text ?? "";

    /// <summary>自動テスト用: 押したあとの操作の窓の中身（まだ押していなければ null）。</summary>
    internal FileActionsPanel? Actions => _flyout.Content as FileActionsPanel;

    public PathLabel()
    {
        Name = "StatusPath";
        Background = Brushes.Transparent;      // 文字の隙間も押せるように
        VerticalAlignment = VerticalAlignment.Center;
        Child = _name;
        _name.Foreground = ThemeColors.Text;
        FlyoutBase.SetAttachedFlyout(this, _flyout);
        PointerPressed += (_, e) =>
        {
            if (!IsPath(Path)) return;
            ShowActions();
            e.Handled = true;
        };
    }

    /// <summary>操作の窓を出す（押したときと同じ。自動テスト用にも）。</summary>
    internal void ShowActions()
    {
        if (Path is not { } path || !IsPath(path)) return;
        _flyout.Content = new FileActionsPanel(path, path);
        FlyoutBase.ShowAttachedFlyout(this);
    }

    private void OnThemeChanged() => _name.Foreground = ThemeColors.Text;

    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateShown();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ThemeColors.Changed += OnThemeChanged;
        L.PropertyChanged += OnLanguageChanged;
        OnThemeChanged();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ThemeColors.Changed -= OnThemeChanged;
        L.PropertyChanged -= OnLanguageChanged;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != PathProperty) return;
        UpdateShown();
        if (_flyout.IsOpen) _flyout.Hide();       // 別のファイルに替わったら、前のファイルの窓は閉じる
    }

    /// <summary>ファイル名・押せるか・ヒントを今のパスと言語に合わせる。</summary>
    private void UpdateShown()
    {
        string? path = Path;
        bool isPath = IsPath(path);
        _name.Text = isPath ? System.IO.Path.GetFileName(path!.TrimEnd('/', '\\')) : path ?? "";
        Cursor = isPath ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        ToolTip.SetTip(this, isPath ? L["TipPathLabel"] : null);
    }

    private static bool IsPath(string? text) => !string.IsNullOrEmpty(text) && System.IO.Path.IsPathRooted(text);
}
