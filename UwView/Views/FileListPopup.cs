using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using UwView.Controls;
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

    /// <summary>行を開く（番号の位置・タブで開くか）。開く先が「アプリ」のときは呼ばない（一覧が自分で開く）。</summary>
    public required Action<int, bool> Open { get; init; }

    /// <summary>その行がどこに出ているか（右端の「メイン」「タブ」の印。無ければ null）。</summary>
    public Func<int, FileOpenAction?>? Placement { get; init; }

    /// <summary>この組のタブの数（メインを含む。「タブ n / 8」）。</summary>
    public Func<int>? TabCount { get; init; }

    /// <summary>「当たりのあるファイルだけ」の切り替えを出すか（uvf の複数ファイルの検索結果）。</summary>
    public bool OffersHitsOnly { get; init; }
}

/// <summary>
/// 番号 → 実ファイルの対応表（ファイル一覧）。uvf の複数ファイルの結果と、uvp の束ねた索引の両方から開く。
///
/// 行番号欄には番号しか出せない（名前は長すぎる）ので、対応はここで見せる。
/// ダブルクリックか Enter で開く。右上の「開く先」で、メイン・追加のタブ・アプリを切り替える。
/// アプリは、元のファイルを拡張子に合った外部のアプリで開く（.uwvz ではない。2026-10-10 オーナー依頼）。
/// 行を右クリックすると、共通の操作の窓（フルパス・パスのコピー・フォルダー・ターミナル・アプリ）を出す。
/// </summary>
public sealed class FileListPopup : Window
{
    private readonly FileListSource _source;
    private readonly List<Row> _rows;
    private readonly ListBox _list;
    private readonly RadioButton _openMain, _openTab, _openApp;
    private readonly CheckBox? _hitsOnly;
    private readonly TextBlock _tabs;
    private readonly TextBlock _notice;

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

        // 開く先（メイン・タブ・アプリ）。前の「タブで開く」の設定はタブとして引き継ぐ
        var mode = AppSettingsRef.Current.FileListOpenMode;
        string group = "FileListOpen" + Guid.NewGuid().ToString("N");
        RadioButton Choice(string name, string label, string tip, FileListOpenMode value)
        {
            var radio = new RadioButton
            {
                Name = name, GroupName = group, Content = L[label],
                Foreground = UwView.Services.ThemeColors.Text,
                IsChecked = mode == value, VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(radio, L[tip]);
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked != true) return;
                AppSettingsRef.Current.FileListOpenMode = value;
                AppSettingsRef.Current.Save();
            };
            return radio;
        }
        _openMain = Choice("FileListOpenMain", "FileListOpenMain", "TipFileListOpenMain", FileListOpenMode.Main);
        _openTab = Choice("FileListOpenTab", "FileListOpenTab", "TipFileListOpenTab", FileListOpenMode.Tab);
        _openApp = Choice("FileListOpenApp", "FileListOpenApp", "TipFileListOpenApp", FileListOpenMode.App);
        var openAt = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                new TextBlock { Text = L["FileListOpenAt"], Foreground = UwView.Services.ThemeColors.Text, VerticalAlignment = VerticalAlignment.Center },
                _openMain, _openTab, _openApp,
            },
        };

        if (source.OffersHitsOnly)
        {
            _hitsOnly = new CheckBox
            {
                Name = "FileListHitsOnly",
                Content = L.Format("FileListHitsOnly",
                                   _rows.Count(r => r.Item.Hits > 0).ToString("N0", L.Culture),
                                   _rows.Count.ToString("N0", L.Culture)),
                Foreground = UwView.Services.ThemeColors.Text,
                IsChecked = AppSettingsRef.Current.FileListHitsOnly,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            ToolTip.SetTip(_hitsOnly, L["TipFileListHitsOnly"]);
            _hitsOnly.IsCheckedChanged += (_, _) =>
            {
                AppSettingsRef.Current.FileListHitsOnly = _hitsOnly.IsChecked == true;
                AppSettingsRef.Current.Save();
                Refresh();
            };
        }

        _list = new ListBox
        {
            Name = "FileListRows",
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

        _tabs = new TextBlock { Foreground = UwView.Services.ThemeColors.Text, VerticalAlignment = VerticalAlignment.Center };
        _notice = new TextBlock
        {
            Name = "FileListNotice", Foreground = UwView.Services.ThemeColors.Text,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

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
        DockPanel.SetDock(_tabs, Dock.Left);
        footer.Children.Add(_tabs);
        footer.Children.Add(_notice);

        var top = new DockPanel();
        DockPanel.SetDock(openAt, Dock.Right);
        top.Children.Add(openAt);
        if (_hitsOnly is not null) top.Children.Add(_hitsOnly);

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

    /// <summary>「メイン」「タブ」の印と「タブ n / 8」、出す行を今の状態に直す。</summary>
    public void Refresh()
    {
        foreach (var row in _rows)
            row.Mark = _source.Placement?.Invoke(row.Index) switch
            {
                FileOpenAction.ShowMain => L["FileListMain"],
                FileOpenAction.ShowTab => L["FileListTab"],
                _ => "",
            };
        // 当たりのあるファイルだけ（番号はそのまま）。いま開いているものは当たりが無くても残す
        var shown = _hitsOnly?.IsChecked == true
            ? _rows.Where(r => r.Item.Hits > 0 || r.Mark.Length > 0).ToList()
            : _rows;
        // 行が変わらないなら差し替えない（開いたあとの更新でスクロール位置が先頭へ戻らないように）
        if (_list.ItemsSource is not IEnumerable<Row> current || !current.SequenceEqual(shown))
            _list.ItemsSource = shown;
        _tabs.Text = _source.TabCount is { } count
            ? L.Format("FileListTabCount", count(), FileTabGroup<object>.MaxTabs)
            : "";
    }

    /// <summary>一覧の中身（自動テスト用）。</summary>
    internal IReadOnlyList<FileListItem> Items => _source.Items;

    /// <summary>いま出している行の番号（自動テスト用）。</summary>
    internal IReadOnlyList<string> ShownLabels
        => ((IEnumerable<Row>)_list.ItemsSource!).Select(r => r.Item.Label).ToList();

    /// <summary>「当たりのあるファイルだけ」（自動テスト用に外から切り替えられる。切り替えが無ければ null）。</summary>
    internal bool? HitsOnly
    {
        get => _hitsOnly?.IsChecked;
        set { if (_hitsOnly is not null) _hitsOnly.IsChecked = value; }
    }

    /// <summary>その行の右端の印（自動テスト用）。</summary>
    internal string MarkAt(int index) => _rows[index].Mark;

    /// <summary>「タブ n / 8」の文字（自動テスト用）。</summary>
    internal string TabCountText => _tabs.Text ?? "";

    /// <summary>開く先（自動テスト用に外から切り替えられる）。</summary>
    internal FileListOpenMode Mode
    {
        get => _openApp.IsChecked == true ? FileListOpenMode.App
             : _openTab.IsChecked == true ? FileListOpenMode.Tab : FileListOpenMode.Main;
        set => (value switch { FileListOpenMode.App => _openApp, FileListOpenMode.Tab => _openTab, _ => _openMain }).IsChecked = true;
    }

    /// <summary>開く先がタブか（1.8.3.0 までの「タブで開く」。オフはメイン。自動テスト用）。</summary>
    internal bool OpenInTab
    {
        get => Mode == FileListOpenMode.Tab;
        set => Mode = value ? FileListOpenMode.Tab : FileListOpenMode.Main;
    }

    /// <summary>下の行の知らせ（アプリで開いた・開けなかった。自動テスト用）。</summary>
    internal string NoticeText => _notice.Text ?? "";

    /// <summary>番号の位置 i の行を開く（ダブルクリック・Enter と同じ。自動テスト用）。</summary>
    internal void OpenAt(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        _notice.Text = "";
        if (Mode == FileListOpenMode.App)
        {
            _ = OpenInAppAsync(index);
            return;
        }
        _source.Open(index, Mode == FileListOpenMode.Tab);
        Refresh();
    }

    /// <summary>
    /// 元のファイルを、拡張子に合った外部のアプリで開く（OS に任せる。.pdf なら PDF のアプリなど）。
    /// zip の中のファイル（名前が <c>zip!エントリ</c>）と、見つからない元のファイルは開かずに知らせる。
    /// </summary>
    internal async Task OpenInAppAsync(int index)
    {
        var item = _source.Items[index];
        string name = Path.GetFileName(item.Name);
        // zip の中のファイル（名前が zip!エントリ。元のファイルとしては zip しか無い）
        int bang = item.Name.IndexOf('!');
        if (bang > 0 && item.FullPath is { } zipPath
            && Path.GetFileName(zipPath).Equals(Path.GetFileName(item.Name[..bang]), StringComparison.OrdinalIgnoreCase))
        {
            _notice.Text = L.Format("FileListAppZipEntry", item.Name);
            return;
        }
        if (item.FullPath is not { } path || !File.Exists(path))
        {
            _notice.Text = L.Format("FileListAppMissing", item.Name);
            return;
        }
        bool opened = await FileActions.LaunchAsync(TopLevel.GetTopLevel(this), path);
        _notice.Text = L.Format(opened ? "FileListAppOpened" : "FileListAppFailed", name);
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
        // 右クリックで共通の操作の窓（左クリック・ダブルクリックは今までどおり開く）。中身は開くときに作る
        if (item.FullPath is not null)
        {
            grid.Background = Brushes.Transparent;   // 文字の隙間でも右クリックを受ける
            var flyout = new Flyout();
            flyout.Opening += (_, _) =>
            {
                var panel = ActionsFor(item);
                panel.FitTo(TopLevel.GetTopLevel(grid));
                flyout.Content = panel;
            };
            grid.ContextFlyout = flyout;
        }
        return grid;
    }

    /// <summary>行の共通の操作の窓の中身（zip の中のファイルは zip!エントリ を出し、アプリでは開かない）。</summary>
    internal static FileActionsPanel ActionsFor(FileListItem item)
    {
        string file = item.FullPath!;
        int bang = item.Name.IndexOf('!');
        bool zipEntry = bang > 0
                        && Path.GetFileName(file).Equals(Path.GetFileName(item.Name[..bang]), StringComparison.OrdinalIgnoreCase);
        return new FileActionsPanel(zipEntry ? file + item.Name[bang..] : file, file, zipEntry);
    }

    private static TextBlock AddCell(Grid grid, int column, string text, HorizontalAlignment align)
    {
        var cell = new TextBlock
        {
            Text = text,
            Foreground = UwView.Services.ThemeColors.Text,
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
