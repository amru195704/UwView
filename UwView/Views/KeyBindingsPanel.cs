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
using UwView.Localization;
using UwView.Services;

namespace UwView.Views;

/// <summary>
/// 設定の画面の「キー」（v1.8.2 extFS E-3 §4.2。uvf の設定の画面と、Pro の設定の画面の両方に入れる）。
///
/// 行を選ぶと下の欄がキーを待つ。押したキーを付ける（1 つの操作に 2 つまで。3 つ目は 2 つ目と入れ替える）。
/// ほかの操作に付いているキーなら、その場で知らせ、どちらに残すかを選ぶ。変えたらすぐ設定に残す。
/// </summary>
public sealed class KeyBindingsPanel : UserControl
{
    private readonly bool _mac = OperatingSystem.IsMacOS();
    private KeyBindingSet _set;
    private readonly KeyBindingSet _defaults;
    private readonly List<Row> _rows;
    private readonly ListBox _list;
    private readonly Border _capture;
    private readonly TextBlock _captureText;
    private readonly StackPanel _conflictButtons;
    private readonly TextBlock _status;
    private KeyChord? _pending;   // ぶつかって、どちらに残すかを聞いているキー

    private static Localizer L => Localizer.Instance;

    /// <summary>一覧の 1 行。</summary>
    internal sealed class Row(ShortcutAction action) : INotifyPropertyChanged
    {
        public ShortcutAction Action { get; } = action;
        public string Name => L[$"KeyAction_{Action}"];
        public string Keys { get; private set; } = "";
        public string Defaults { get; private set; } = "";

        public void Update(KeyBindingSet set, KeyBindingSet defaults, bool mac)
        {
            Keys = Join(set.KeysOf(Action), mac);
            Defaults = Join(defaults.KeysOf(Action), mac);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Keys)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Defaults)));
        }

        private static string Join(IReadOnlyList<KeyChord> keys, bool mac)
            => keys.Count == 0 ? "—" : string.Join("  ", keys.Select(k => k.Display(mac)));

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public KeyBindingsPanel()
    {
        _set = UwView.Services.KeyBindings.Current.Clone();
        _defaults = KeyBindingSet.Standard(_mac);
        _rows = KeyBindingSet.Actions.Select(a => new Row(a)).ToList();
        foreach (var r in _rows) r.Update(_set, _defaults, _mac);

        var standard = Button(L["KeysPresetStandard"], L["TipKeysPresetStandard"], () => ApplyPreset(KeyBindingSet.Standard(_mac)));
        standard.Name = "KeysPresetStandard";
        var vi = Button(L["KeysPresetVi"], L["TipKeysPresetVi"], () => ApplyPreset(KeyBindingSet.ViLess(_mac)));
        vi.Name = "KeysPresetVi";
        var presets = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8),
            Children = { new TextBlock { Text = L["KeysPresetLabel"], Foreground = UwView.Services.ThemeColors.Text, VerticalAlignment = VerticalAlignment.Center }, standard, vi },
        };

        var header = RowGrid(Bold(L["KeysColAction"]), Bold(L["KeysColKeys"]), Bold(L["KeysColDefault"]), new Control());
        _list = new ListBox
        {
            Name = "KeyBindingList",
            Height = 300,
            ItemsSource = _rows,
            ItemTemplate = new FuncDataTemplate<Row>((row, _) =>
            {
                if (row is null) return new TextBlock();
                var reset = Button(L["KeysResetOne"], L["TipKeysResetOne"], () => { Change(row.Action, _defaults.KeysOf(row.Action)); });
                var clear = Button(L["KeysClearOne"], L["TipKeysClearOne"], () => { Change(row.Action, []); });
                var keys = new TextBlock { Foreground = UwView.Services.ThemeColors.Text, VerticalAlignment = VerticalAlignment.Center };
                keys.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(Row.Keys)) { Source = row });
                var defaults = new TextBlock { Foreground = UwView.Services.ThemeColors.Text, VerticalAlignment = VerticalAlignment.Center };
                defaults.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(Row.Defaults)) { Source = row });
                var name = new TextBlock { Text = row.Name, Foreground = UwView.Services.ThemeColors.Text, VerticalAlignment = VerticalAlignment.Center,
                                           TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
                ToolTip.SetTip(name, row.Name);   // 長い名前は切れるので、全文はヒントで
                return RowGrid(name,
                               keys, defaults,
                               new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { reset, clear } });
            }),
        };
        _list.SelectionChanged += (_, _) => BeginCapture();

        _captureText = new TextBlock { Foreground = UwView.Services.ThemeColors.Text, TextWrapping = TextWrapping.Wrap, Text = L["KeysCaptureIdle"] };
        _conflictButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
        _capture = new Border
        {
            Name = "KeyCapture",
            Focusable = true,
            Background = UwView.Services.ThemeColors.Get("Uv_F4F8FF", new SolidColorBrush(Color.FromRgb(0xF4, 0xF8, 0xFF))),
            BorderBrush = UwView.Services.ThemeColors.Get("Uv_1A6FE8", new SolidColorBrush(Color.FromRgb(0x1A, 0x6F, 0xE8))),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 8, 0, 8),
            Child = new StackPanel { Children = { _captureText, _conflictButtons } },
        };
        _capture.KeyDown += OnCaptureKeyDown;

        _status = new TextBlock { Foreground = UwView.Services.ThemeColors.Text, TextWrapping = TextWrapping.Wrap };
        var resetAll = Button(L["KeysResetAll"], L["TipKeysResetAll"], () => ApplyPreset(KeyBindingSet.Standard(_mac)));
        var export = Button(L["KeysExport"], L["TipKeysExport"], () => _ = ExportAsync());
        var import = Button(L["KeysImport"], L["TipKeysImport"], () => _ = ImportAsync());
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { resetAll, export, import } };

        Content = new StackPanel
        {
            Margin = new Thickness(4),
            Children =
            {
                presets, header, _list, _capture, bottom,
                new TextBlock { Text = L["KeysTypingNote"], Foreground = UwView.Services.ThemeColors.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) },
                _status,
            },
        };
    }

    private static TextBlock Bold(string text) => new() { Text = text, Foreground = UwView.Services.ThemeColors.Text, FontWeight = FontWeight.Bold };

    private static Grid RowGrid(Control name, Control keys, Control defaults, Control buttons)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,1.4*,1.4*,Auto"), MinHeight = 26 };
        Grid.SetColumn(keys, 1);
        Grid.SetColumn(defaults, 2);
        Grid.SetColumn(buttons, 3);
        g.Children.Add(name);
        g.Children.Add(keys);
        g.Children.Add(defaults);
        g.Children.Add(buttons);
        return g;
    }

    private static Button Button(string text, string tip, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 2) };
        ToolTip.SetTip(b, tip);
        b.Click += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    private Row? Selected => _list.SelectedItem as Row;

    /// <summary>行を選んだ：下の欄がキーを待つ。</summary>
    private void BeginCapture()
    {
        _pending = null;
        _conflictButtons.IsVisible = false;
        if (Selected is not { } row) { _captureText.Text = L["KeysCaptureIdle"]; return; }
        _captureText.Text = L.Format("KeysCapturePrompt", row.Name);
        _capture.Focus();
    }

    private void OnCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (Selected is not { } row) return;
        e.Handled = true;
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            _pending = null;
            _conflictButtons.IsVisible = false;
            _captureText.Text = L["KeysCaptureCanceled"];
            return;
        }
        if (KeyChord.FromKeyEvent(e.Key, e.KeyModifiers, e.KeySymbol, _mac) is not { } chord) return;
        Offer(row, chord);
    }

    /// <summary>押したキーを付ける。ほかの操作に付いていれば、どちらに残すかを聞く。</summary>
    internal void Offer(Row row, KeyChord chord)
    {
        if (chord.IsChar && char.IsAsciiDigit(chord.Char))
        {
            _captureText.Text = L["KeysDigitReserved"];   // 数字は「何行」の前置きに使う
            return;
        }
        if (_set.Owner(chord, except: row.Action) is { } other)
        {
            _pending = chord;
            _captureText.Text = L.Format("KeysConflict", chord.Display(_mac), L[$"KeyAction_{other}"], row.Name);
            _conflictButtons.Children.Clear();
            var take = Button(L.Format("KeysConflictMove", row.Name), L["TipKeysConflictMove"], () => ResolveConflict(row, other, chord));
            take.Name = "KeysConflictMove";
            var keep = Button(L.Format("KeysConflictKeep", L[$"KeyAction_{other}"]), L["TipKeysConflictKeep"], () =>
            {
                _pending = null;
                _conflictButtons.IsVisible = false;
                _captureText.Text = L.Format("KeysCapturePrompt", row.Name);
                _capture.Focus();
            });
            keep.Name = "KeysConflictKeep";
            _conflictButtons.Children.Add(take);
            _conflictButtons.Children.Add(keep);
            _conflictButtons.IsVisible = true;
            return;
        }
        Assign(row.Action, chord);
    }

    /// <summary>ぶつかったキーを、選んでいる操作の方に付け替える（前の操作からは外す）。</summary>
    internal void ResolveConflict(Row row, ShortcutAction other, KeyChord chord)
    {
        _set.SetKeys(other, _set.KeysOf(other).Where(k => k != chord));
        _pending = null;
        _conflictButtons.IsVisible = false;
        Assign(row.Action, chord);
    }

    /// <summary>付ける（2 つまで。もう 2 つなら 2 つ目と入れ替える）。</summary>
    private void Assign(ShortcutAction action, KeyChord chord)
    {
        var keys = _set.KeysOf(action).ToList();
        if (keys.Contains(chord)) { _captureText.Text = L.Format("KeysAlready", chord.Display(_mac)); return; }
        if (keys.Count >= KeyBindingSet.MaxKeysPerAction) keys[^1] = chord;
        else keys.Add(chord);
        Change(action, keys);
        _captureText.Text = L.Format("KeysAssigned", chord.Display(_mac), L[$"KeyAction_{action}"]);
    }

    private void Change(ShortcutAction action, IEnumerable<KeyChord> keys)
    {
        _set.SetKeys(action, keys.ToList());
        Save();
    }

    private void ApplyPreset(KeyBindingSet preset)
    {
        _set = preset.Clone();
        Save();
        _status.Text = L["KeysPresetApplied"];
    }

    private void Save()
    {
        UwView.Services.KeyBindings.Apply(_set.Clone());
        foreach (var r in _rows) r.Update(_set, _defaults, _mac);
    }

    /// <summary>試験用：今の割り当て。</summary>
    internal KeyBindingSet Bindings => _set;

    /// <summary>試験用：その操作の行。</summary>
    internal Row RowOf(ShortcutAction action) => _rows.First(r => r.Action == action);

    /// <summary>試験用：書き出しと同じ中身を読み込む。</summary>
    internal bool ImportText(string text)
    {
        if (KeyBindingSet.FromFileText(text, _mac) is not { } loaded) { _status.Text = L["KeysImportBad"]; return false; }
        ApplyPreset(loaded);
        _status.Text = L["KeysImported"];
        return true;
    }

    private async Task ExportAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L["KeysExport"],
            SuggestedFileName = "uwview-keys" + KeyBindingSet.FileExtension,
            DefaultExtension = KeyBindingSet.FileExtension.TrimStart('.'),
        });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(_set.ToFileText(_mac));
            _status.Text = L["KeysExported"];
        }
        catch (IOException failure) { _status.Text = failure.Message; }
    }

    private async Task ImportAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L["KeysImport"],
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("UwView keys") { Patterns = ["*" + KeyBindingSet.FileExtension] }],
        });
        if (files.Count == 0) return;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            ImportText(await reader.ReadToEndAsync());
        }
        catch (IOException failure) { _status.Text = failure.Message; }
    }
}
