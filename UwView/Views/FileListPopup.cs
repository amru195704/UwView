using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using UwView.Core;
using UwView.Localization;
using UwView.Services;

namespace UwView.Views;

/// <summary>ファイル一覧の1行。</summary>
/// <param name="Label">番号（uvp の zip の中は <c>n-m</c>）。</param>
/// <param name="Name">表示用の名前（相対パスのまま）。</param>
/// <param name="FullPath">絶対パス（ツールチップに出す。分からなければ null）。</param>
/// <param name="Hits">当たりの件数（検索していなければ null）。</param>
/// <param name="Size">大きさ（分からなければ null）。</param>
public sealed record FileListItem(string Label, string Name, string? FullPath, long? Hits, long? Size);

/// <summary>
/// ファイル一覧の中身と、行を開いたときの動き。uvf は受け渡しの <c>files[]</c>、uvp は束ねた索引から作る
/// （実装指示書_uvf複数ファイルGUI表示とタブ §5.1）。
/// </summary>
public sealed class FileListSource
{
    public required IReadOnlyList<FileListItem> Items { get; init; }

    /// <summary>行を開く（番号の位置・「タブで開く」がオンか）。</summary>
    public required Action<int, bool> Open { get; init; }

    /// <summary>その行がどこに出ているか（右端の「メイン」「タブ」の印。無ければ null）。</summary>
    public Func<int, FileOpenAction?>? Placement { get; init; }

    /// <summary>この組のタブの数（メインを含む。「タブ n / 8」）。</summary>
    public Func<int>? TabCount { get; init; }
}

/// <summary>
/// 番号 → 実ファイルの対応表（ファイル一覧）。uvf の複数ファイルの結果と、uvp の束ねた索引の両方から開く。
///
/// 行番号欄には番号しか出せない（名前は長すぎる）ので、対応はここで見せる。
/// ダブルクリックか Enter で開く。右上の「タブで開く」で、追加タブかメインかを切り替える。
/// </summary>
public sealed class FileListPopup : Window
{
    private readonly FileListSource _source;
    private readonly List<Row> _rows;
    private readonly ListBox _list;
    private readonly CheckBox _inTab;
    private readonly TextBlock _tabs;

    /// <summary>自動テスト用: いま開いている一覧（無ければ null）。</summary>
    internal static FileListPopup? Current { get; private set; }

    private static Localizer L => Localizer.Instance;

    /// <summary>一覧の1行（印だけは開いた・閉じたで変わる）。</summary>
    private sealed class Row(int index, FileListItem item) : INotifyPropertyChanged
    {
        public int Index { get; } = index;
        public FileListItem Item { get; } = item;
        private string _mark = "";

        public string Mark
        {
            get => _mark;
            set
            {
                if (_mark == value) return;
                _mark = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mark)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private FileListPopup(FileListSource source)
    {
        _source = source;
        _rows = source.Items.Select((item, i) => new Row(i, item)).ToList();

        Title = L["FileListTitle"];
        Width = 680;
        Height = 460;
        MinWidth = 420;
        MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;

        _inTab = new CheckBox
        {
            Name = "FileListInTab",
            Content = L["FileListOpenInTab"],
            Foreground = Brushes.Black,
            IsChecked = AppSettingsRef.Current.FileListOpenInTab,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        ToolTip.SetTip(_inTab, L["TipFileListOpenInTab"]);
        _inTab.IsCheckedChanged += (_, _) =>
        {
            AppSettingsRef.Current.FileListOpenInTab = _inTab.IsChecked == true;
            AppSettingsRef.Current.Save();
        };

        _list = new ListBox
        {
            Name = "FileListRows",
            ItemsSource = _rows,
            SelectionMode = SelectionMode.Single,
            // 行の入れ物を使い回すとき、中身の無い（null の）行で呼ばれることがある。
            // そのまま作ると落ちた（710 ファイルの一覧をスクロールして NullReferenceException・2026-09-28）
            ItemTemplate = new FuncDataTemplate<Row>((row, _) => row is null ? new Panel() : RowView(row),
                                                     supportsRecycling: false),
        };
        _list.DoubleTapped += (_, _) => OpenSelected();
        _list.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            OpenSelected();
            e.Handled = true;
        };

        _tabs = new TextBlock { Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center };

        var close = new Button
        {
            Name = "MergedFilesCloseButton",
            Content = L["Close"],
            Padding = new Thickness(16, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        close.Click += (_, _) => Close();

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*,70,80,56") };
        AddCell(header, 0, L["FileListNumber"], HorizontalAlignment.Right);
        AddCell(header, 1, L["FileListFile"], HorizontalAlignment.Left);
        AddCell(header, 2, L["FileListHits"], HorizontalAlignment.Right);
        AddCell(header, 3, L["FileListSize"], HorizontalAlignment.Right);

        var footer = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(close);
        footer.Children.Add(_tabs);

        var top = new DockPanel();
        top.Children.Add(_inTab);

        var body = new DockPanel { Margin = new Thickness(12), LastChildFill = true };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        body.Children.Add(top);
        body.Children.Add(header);
        body.Children.Add(footer);
        body.Children.Add(_list);
        header.Margin = new Thickness(10, 8, 22, 4);
        footer.Margin = new Thickness(0, 8, 0, 0);
        Content = body;

        Closed += (_, _) => { if (ReferenceEquals(Current, this)) Current = null; };
        Refresh();
    }

    /// <summary>一覧を開く（開いたまま、タブを開いた・閉じたときは <see cref="Refresh"/> で印を直す）。</summary>
    public static FileListPopup Show(Window owner, FileListSource source)
    {
        var window = new FileListPopup(source);
        Current = window;
        window.Show(owner);
        return window;
    }

    /// <summary>「メイン」「タブ」の印と「タブ n / 8」を今の状態に直す。</summary>
    public void Refresh()
    {
        foreach (var row in _rows)
            row.Mark = _source.Placement?.Invoke(row.Index) switch
            {
                FileOpenAction.ShowMain => L["FileListMain"],
                FileOpenAction.ShowTab => L["FileListTab"],
                _ => "",
            };
        _tabs.Text = _source.TabCount is { } count
            ? L.Format("FileListTabCount", count(), FileTabGroup<object>.MaxTabs)
            : "";
    }

    /// <summary>一覧の中身（自動テスト用）。</summary>
    internal IReadOnlyList<FileListItem> Items => _source.Items;

    /// <summary>その行の右端の印（自動テスト用）。</summary>
    internal string MarkAt(int index) => _rows[index].Mark;

    /// <summary>「タブ n / 8」の文字（自動テスト用）。</summary>
    internal string TabCountText => _tabs.Text ?? "";

    /// <summary>「タブで開く」がオンか（自動テスト用に外から切り替えられる）。</summary>
    internal bool OpenInTab
    {
        get => _inTab.IsChecked == true;
        set => _inTab.IsChecked = value;
    }

    /// <summary>番号の位置 i の行を開く（ダブルクリック・Enter と同じ。自動テスト用）。</summary>
    internal void OpenAt(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        _source.Open(index, OpenInTab);
        Refresh();
    }

    private void OpenSelected()
    {
        if (_list.SelectedItem is Row row) OpenAt(row.Index);
    }

    private static Control RowView(Row row)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*,70,80,56") };
        var item = row.Item;
        AddCell(grid, 0, item.Label, HorizontalAlignment.Right);
        var name = AddCell(grid, 1, item.Name, HorizontalAlignment.Left);
        name.TextTrimming = TextTrimming.PathSegmentEllipsis;
        if (item.FullPath is { } full) ToolTip.SetTip(grid, full);
        AddCell(grid, 2, item.Hits?.ToString("N0", L.Culture) ?? "", HorizontalAlignment.Right);
        AddCell(grid, 3, item.Size is { } size ? SizeText(size) : "", HorizontalAlignment.Right);
        var mark = AddCell(grid, 4, row.Mark, HorizontalAlignment.Right);
        mark.FontWeight = FontWeight.SemiBold;
        row.PropertyChanged += (_, _) => mark.Text = row.Mark;
        return grid;
    }

    private static TextBlock AddCell(Grid grid, int column, string text, HorizontalAlignment align)
    {
        var cell = new TextBlock
        {
            Text = text,
            Foreground = Brushes.Black,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(column == 0 ? 0 : 8, 0, 0, 0),
        };
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
        return cell;
    }

    /// <summary>大きさ（1.2 GB・340 MB・12 KB）。</summary>
    internal static string SizeText(long bytes)
    {
        var c = System.Globalization.CultureInfo.InvariantCulture;
        return bytes switch
        {
            >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.0", c) + " GB",
            >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0.0", c) + " MB",
            >= 1L << 10 => (bytes / (double)(1L << 10)).ToString("0", c) + " KB",
            _ => bytes.ToString(c) + " B",
        };
    }
}
