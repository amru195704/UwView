using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using UwView.Localization;
using UwView.Services;

namespace UwView.Controls;

/// <summary>
/// ステータスバーのファイルの名前（2026-10-10 オーナー依頼）。長いパスで右の情報が押し出されないよう、
/// ふだんは<b>ファイル名だけ</b>を出し、押すとフルパスを小さな窓に出す。窓のパスは選んでコピーでき、［コピー］もある。
/// パスでないもの（「（ファイル未選択）」など）はそのまま出し、押しても何もしない。
/// </summary>
public sealed class PathLabel : Border
{
    public static readonly StyledProperty<string?> PathProperty =
        AvaloniaProperty.Register<PathLabel, string?>(nameof(Path));

    private readonly TextBlock _name = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _full = new()
    {
        Name = "PathLabelFull", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxWidth = 640, MinWidth = 320,
    };
    private readonly Button _copy = new() { Name = "PathLabelCopy", HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Flyout _flyout;

    private static Localizer L => Localizer.Instance;

    /// <summary>出すファイルのパス（フルパス。パスでなければそのまま出す）。</summary>
    public string? Path
    {
        get => GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    /// <summary>自動テスト用: 押したあとの窓に出ているパス。</summary>
    internal string FullText => _full.Text ?? "";

    /// <summary>自動テスト用: いま出しているファイル名。</summary>
    internal string ShownText => _name.Text ?? "";

    public PathLabel()
    {
        Name = "StatusPath";
        Background = Brushes.Transparent;      // 文字の隙間も押せるように
        VerticalAlignment = VerticalAlignment.Center;
        Child = _name;
        _name.Foreground = ThemeColors.Text;

        _copy.Content = L["CmdCopy"];
        _copy.Click += async (_, _) => await CopyAsync();
        _flyout = new Flyout
        {
            Placement = PlacementMode.TopEdgeAlignedLeft,
            Content = new StackPanel { Spacing = 6, Children = { _full, _copy } },
        };
        FlyoutBase.SetAttachedFlyout(this, _flyout);
        PointerPressed += (_, e) =>
        {
            if (!IsPath(Path)) return;
            ShowFull();
            e.Handled = true;
        };
    }

    /// <summary>フルパスをクリップボードに写す（窓の［コピー］と同じ。自動テスト用にも）。</summary>
    internal async System.Threading.Tasks.Task CopyAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard || Path is not { Length: > 0 } path) return;
        await clipboard.SetTextAsync(path);
        _copy.Content = L["PathCopied"];
    }

    /// <summary>フルパスの窓を出す（押したときと同じ。自動テスト用にも）。</summary>
    internal void ShowFull()
    {
        _full.Text = Path;
        _copy.Content = L["CmdCopy"];
        FlyoutBase.ShowAttachedFlyout(this);
    }

    private void OnThemeChanged() => _name.Foreground = ThemeColors.Text;

    /// <summary>言語を切り替えたら、ヒントと［コピー］の言葉を作り直す。</summary>
    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        _copy.Content = L["CmdCopy"];
        UpdateShown();
    }

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
        if (_flyout.IsOpen) _full.Text = Path;
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
