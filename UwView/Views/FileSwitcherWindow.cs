using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using UwView.Localization;
using UwView.ViewModels;

namespace UwView.Views;

/// <summary>
/// 開いているファイルを選ぶ小さな窓（Ctrl＋Shift＋O。v1.8.2 extFS E-3 §4.3）。
/// 名前の一部を打つと絞り込み、Enter（ダブルクリック）でそのタブへ移る。Esc で閉じる。
/// </summary>
public sealed class FileSwitcherWindow : Window
{
    private readonly IReadOnlyList<DocumentTabViewModel> _tabs;
    private readonly Action<DocumentTabViewModel> _pick;
    private readonly TextBox _filter;
    private readonly ListBox _list;

    /// <summary>自動テスト用: いま開いている窓（無ければ null）。</summary>
    internal static FileSwitcherWindow? Current { get; private set; }

    private static Localizer L => Localizer.Instance;

    public FileSwitcherWindow(IReadOnlyList<DocumentTabViewModel> tabs, Action<DocumentTabViewModel> pick)
    {
        _tabs = tabs;
        _pick = pick;
        Title = L["FileSwitcherTitle"];
        Width = 520;
        Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brushes.White;

        _filter = new TextBox { Watermark = L["FileSwitcherWatermark"], Margin = new Thickness(0, 0, 0, 8) };
        _list = new ListBox
        {
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<DocumentTabViewModel>((t, _) => new StackPanel
            {
                Spacing = 1,
                Children =
                {
                    new TextBlock { Text = t?.DisplayName ?? "", Foreground = Brushes.Black, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = t?.FilePath ?? "", Foreground = Brushes.Black, FontSize = 11,
                                    TextTrimming = TextTrimming.CharacterEllipsis },
                },
            }),
        };
        var panel = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(_filter, Dock.Top);
        panel.Children.Add(_filter);
        panel.Children.Add(_list);
        Content = panel;

        _filter.TextChanged += (_, _) => Refill();
        _filter.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Down when _list.ItemCount > 0:
                    _list.SelectedIndex = Math.Min(_list.ItemCount - 1, _list.SelectedIndex + 1);
                    e.Handled = true;
                    break;
                case Key.Up when _list.ItemCount > 0:
                    _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    Choose();
                    e.Handled = true;
                    break;
            }
        };
        _list.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Choose(); e.Handled = true; } };
        _list.DoubleTapped += (_, _) => Choose();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Opened += (_, _) => { Current = this; _filter.Focus(); };
        Closed += (_, _) => { if (ReferenceEquals(Current, this)) Current = null; };
        Refill();
    }

    /// <summary>打った文字を名前かパスに含むタブ（大小は見ない）。</summary>
    private void Refill()
    {
        string q = _filter.Text?.Trim() ?? "";
        var shown = q.Length == 0
            ? _tabs.ToList()
            : _tabs.Where(t => t.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)
                            || (t.FilePath?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        _list.ItemsSource = shown;
        _list.SelectedIndex = shown.Count > 0 ? 0 : -1;
    }

    /// <summary>試験用：絞り込みの文字を入れる。</summary>
    internal void TypeFilter(string text) => _filter.Text = text;

    /// <summary>試験用・Enter：選んでいるタブへ移って閉じる。</summary>
    internal void Choose()
    {
        if (_list.SelectedItem is not DocumentTabViewModel tab) return;
        _pick(tab);
        Close();
    }
}
