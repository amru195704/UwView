using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Input;
using UwView.Views;

namespace UwView.Services;

/// <summary>キーの修飾（Primary は Win・Linux の Ctrl、Mac の ⌘。Control は Mac の control キー）。</summary>
[Flags]
public enum ChordMods { None = 0, Primary = 1, Shift = 2, Alt = 4, Control = 8 }

/// <summary>
/// 1 つのキーの組み合わせ（v1.8.2 extFS E-3）。
///
/// 文字のキー（<c>[</c> <c>j</c> <c>G</c> <c>$</c>）は、配列によらず押した文字（KeySymbol）で見る。
/// 文字には修飾を付けない（Shift は文字に入っている：<c>G</c>＝Shift＋g）。
/// それ以外（<c>Primary+F</c>・<c>F3</c>・<c>Escape</c>）は Avalonia の Key と修飾で見る。
/// 設定ファイルには <see cref="ToString"/> の形で書く（<c>Primary+Shift+F</c>・<c>Shift+F3</c>・<c>j</c>）。
/// </summary>
public readonly record struct KeyChord(ChordMods Mods, Key Key, char Char)
{
    public bool IsChar => Char != '\0';

    public static KeyChord Of(char c) => new(ChordMods.None, Key.None, c);
    public static KeyChord Of(Key key, ChordMods mods = ChordMods.None) => new(mods, key, '\0');

    /// <summary>文字を打っている欄（検索欄など）でも効くか。文字のキーと、修飾なしの名前のキー（Delete 等）は効かない。</summary>
    public bool WorksWhileTyping
        => !IsChar && ((Mods & (ChordMods.Primary | ChordMods.Alt | ChordMods.Control)) != 0
                       || Key is >= Key.F1 and <= Key.F24 or Key.Escape);

    private static readonly (ChordMods Flag, string Name)[] ModNames =
        [(ChordMods.Primary, "Primary"), (ChordMods.Control, "Control"), (ChordMods.Alt, "Alt"), (ChordMods.Shift, "Shift")];

    /// <summary>設定ファイルに書く形。</summary>
    public override string ToString()
    {
        if (IsChar) return Char.ToString();
        var mods = Mods;
        return string.Join("+", ModNames.Where(m => mods.HasFlag(m.Flag)).Select(m => m.Name).Append(Key.ToString()));
    }

    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrEmpty(text)) return false;
        if (text.Length == 1)
        {
            if (char.IsControl(text[0]) || text[0] == ' ') return false;
            chord = Of(text[0]);
            return true;
        }
        var parts = text.Split('+');
        var mods = ChordMods.None;
        foreach (string part in parts[..^1])
        {
            var m = ModNames.FirstOrDefault(n => n.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (m.Name is null) return false;
            mods |= m.Flag;
        }
        if (!Enum.TryParse(parts[^1], ignoreCase: true, out Key key) || key == Key.None || IsModifierKey(key)) return false;
        chord = Of(key, mods);
        return true;
    }

    /// <summary>画面に出す形（Mac は ⌃⌥⇧⌘、それ以外は Ctrl+Shift+F）。</summary>
    public string Display(bool mac)
    {
        if (IsChar) return Char.ToString();
        string key = KeyText(Key);
        if (mac)
            return (Mods.HasFlag(ChordMods.Control) ? "⌃" : "") + (Mods.HasFlag(ChordMods.Alt) ? "⌥" : "")
                 + (Mods.HasFlag(ChordMods.Shift) ? "⇧" : "") + (Mods.HasFlag(ChordMods.Primary) ? "⌘" : "") + key;
        var names = new List<string>();
        if (Mods.HasFlag(ChordMods.Primary) || Mods.HasFlag(ChordMods.Control)) names.Add("Ctrl");
        if (Mods.HasFlag(ChordMods.Alt)) names.Add("Alt");
        if (Mods.HasFlag(ChordMods.Shift)) names.Add("Shift");
        names.Add(key);
        return string.Join("+", names);
    }

    private static string KeyText(Key key) => key switch
    {
        Key.Escape => "Esc",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemPlus => "+",
        Key.OemMinus => "-",
        Key.Back => "Backspace",
        Key.Delete => "Delete",
        Key.Return => "Enter",
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        _ => key.ToString(),
    };

    private static bool IsModifierKey(Key key) => key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;

    /// <summary>名前で見るキー（文字のキーとして扱わない）。</summary>
    private static bool IsNamedKey(Key key) => key is >= Key.F1 and <= Key.F24
        or Key.Escape or Key.Return or Key.Tab or Key.Back or Key.Delete or Key.Insert or Key.Space
        or Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.Up or Key.Down or Key.Left or Key.Right;

    private static ChordMods ModsOf(KeyModifiers mods, bool mac)
    {
        var m = ChordMods.None;
        if (mods.HasFlag(mac ? KeyModifiers.Meta : KeyModifiers.Control)) m |= ChordMods.Primary;
        if (mac && mods.HasFlag(KeyModifiers.Control)) m |= ChordMods.Control;
        if (mods.HasFlag(KeyModifiers.Alt)) m |= ChordMods.Alt;
        if (mods.HasFlag(KeyModifiers.Shift)) m |= ChordMods.Shift;
        return m;
    }

    /// <summary>押したキーから組み合わせを作る（設定の画面で新しいキーを入れるとき）。修飾キーだけなら null。</summary>
    public static KeyChord? FromKeyEvent(Key key, KeyModifiers mods, string? symbol, bool mac)
    {
        if (IsModifierKey(key) || key == Key.None) return null;
        var m = ModsOf(mods, mac);
        if ((m & (ChordMods.Primary | ChordMods.Control)) == 0 && !IsNamedKey(key)
            && symbol is { Length: 1 } s && !char.IsControl(s[0]) && s[0] != ' ')
            return Of(s[0]);
        return Of(key, m);
    }

    /// <summary>押したキーがこの組み合わせか。</summary>
    public bool Matches(Key key, KeyModifiers mods, string? symbol, bool mac)
    {
        if (IsChar)
        {
            // AltGr（Ctrl+Alt）で打つ配列もあるので、Cmd や Ctrl だけのときを除いて文字で見る
            bool plainChar = (mods & KeyModifiers.Meta) == 0
                          && ((mods & KeyModifiers.Control) == 0 || (mods & KeyModifiers.Alt) != 0);
            return plainChar && symbol is { Length: 1 } && symbol[0] == Char;
        }
        return key == Key && ModsOf(mods, mac) == Mods;
    }
}

/// <summary>
/// 操作ごとのキー（1 つの操作に 2 つまで。v1.8.2 extFS E-3）。
/// 既定は［標準］（1.8.1 までの割り当て）。［vi・less 風］は標準に j・k などを足したもの。
/// </summary>
public sealed class KeyBindingSet
{
    public const int MaxKeysPerAction = 2;

    private readonly Dictionary<ShortcutAction, List<KeyChord>> _keys = [];

    /// <summary>一覧に出す順（enum の順）。</summary>
    public static IReadOnlyList<ShortcutAction> Actions { get; } = Enum.GetValues<ShortcutAction>();

    public IReadOnlyList<KeyChord> KeysOf(ShortcutAction action)
        => _keys.TryGetValue(action, out var keys) ? keys : [];

    /// <summary>キーを入れ直す（同じキーは 1 つに、2 つまで）。</summary>
    public void SetKeys(ShortcutAction action, IEnumerable<KeyChord> keys)
        => _keys[action] = keys.Distinct().Take(MaxKeysPerAction).ToList();

    public KeyBindingSet Clone()
    {
        var copy = new KeyBindingSet();
        foreach (var (action, keys) in _keys) copy._keys[action] = [.. keys];
        return copy;
    }

    /// <summary>そのキーが付いている操作（無ければ null）。</summary>
    public ShortcutAction? Owner(KeyChord chord, ShortcutAction? except = null)
    {
        foreach (var action in Actions)
            if (action != except && KeysOf(action).Contains(chord)) return action;
        return null;
    }

    /// <summary>押したキーから操作を決める（割り当てが無ければ null）。</summary>
    /// <param name="typing">文字を打つ欄にいるか（いれば、文字のキーと修飾なしの名前のキーは取らない）。</param>
    public ShortcutAction? Find(Key key, KeyModifiers mods, string? symbol, bool typing, bool mac)
    {
        foreach (var action in Actions)
        {
            if (action == ShortcutAction.LeaveInput && !typing) continue;   // Esc は欄から抜けるときだけ
            foreach (var chord in KeysOf(action))
                if ((!typing || chord.WorksWhileTyping) && chord.Matches(key, mods, symbol, mac)) return action;
        }
        return null;
    }

    /// <summary>［標準］（1.8.1 までの割り当て。新しい操作はキーなし）。</summary>
    public static KeyBindingSet Standard(bool mac)
    {
        var set = new KeyBindingSet();
        var P = ChordMods.Primary;
        set.SetKeys(ShortcutAction.FocusSearch, [KeyChord.Of(Key.F, P)]);
        set.SetKeys(ShortcutAction.NextHit, mac ? [KeyChord.Of(Key.F3), KeyChord.Of(Key.G, P)] : [KeyChord.Of(Key.F3)]);
        set.SetKeys(ShortcutAction.PrevHit, mac ? [KeyChord.Of(Key.F3, ChordMods.Shift), KeyChord.Of(Key.G, P | ChordMods.Shift)]
                                                : [KeyChord.Of(Key.F3, ChordMods.Shift)]);
        set.SetKeys(ShortcutAction.FocusJump, [KeyChord.Of(Key.L, P)]);
        set.SetKeys(ShortcutAction.ToggleBookmark, [KeyChord.Of(Key.B, P)]);
        set.SetKeys(ShortcutAction.PrevBookmark, [KeyChord.Of('[')]);
        set.SetKeys(ShortcutAction.NextBookmark, [KeyChord.Of(']')]);
        set.SetKeys(ShortcutAction.ToggleTail, [KeyChord.Of(Key.F, P | ChordMods.Shift)]);
        set.SetKeys(ShortcutAction.Reload, [KeyChord.Of(Key.F5)]);
        set.SetKeys(ShortcutAction.LeaveInput, [KeyChord.Of(Key.Escape)]);
        set.SetKeys(ShortcutAction.OpenCommandLine, [KeyChord.Of(Key.K, P | ChordMods.Shift)]);
        set.SetKeys(ShortcutAction.OpenSettings, [KeyChord.Of(Key.OemComma, P)]);
        return set;
    }

    /// <summary>［vi・less 風］（klogg と同じ。標準のキーはそのまま残す）。</summary>
    public static KeyBindingSet ViLess(bool mac)
    {
        var set = Standard(mac);
        void Add(ShortcutAction action, KeyChord chord) => set.SetKeys(action, [.. set.KeysOf(action), chord]);
        set.SetKeys(ShortcutAction.LineDown, [KeyChord.Of('j')]);
        set.SetKeys(ShortcutAction.LineUp, [KeyChord.Of('k')]);
        set.SetKeys(ShortcutAction.ScrollLeft, [KeyChord.Of('h')]);
        set.SetKeys(ShortcutAction.ScrollRight, [KeyChord.Of('l')]);
        set.SetKeys(ShortcutAction.LineLeftEnd, [KeyChord.Of('^')]);
        set.SetKeys(ShortcutAction.LineRightEnd, [KeyChord.Of('$')]);
        set.SetKeys(ShortcutAction.GoToLine, [KeyChord.Of('g')]);
        set.SetKeys(ShortcutAction.GoToEnd, [KeyChord.Of('G')]);
        set.SetKeys(ShortcutAction.SearchWordNext, [KeyChord.Of('*')]);
        set.SetKeys(ShortcutAction.SearchWordPrev, [KeyChord.Of('#')]);
        set.SetKeys(ShortcutAction.CycleResultList, [KeyChord.Of('v')]);
        set.SetKeys(ShortcutAction.ResultListBigger, [KeyChord.Of('+')]);
        set.SetKeys(ShortcutAction.ResultListSmaller, [KeyChord.Of('-')]);
        set.SetKeys(ShortcutAction.OpenFileSwitcher, [KeyChord.Of(Key.O, ChordMods.Primary | ChordMods.Shift)]);
        if (mac)
        {
            // Mac の標準は次・前の当たりに 2 つ付いているので、n・N は 2 つ目を置き換える
            set.SetKeys(ShortcutAction.NextHit, [KeyChord.Of(Key.F3), KeyChord.Of('n')]);
            set.SetKeys(ShortcutAction.PrevHit, [KeyChord.Of(Key.F3, ChordMods.Shift), KeyChord.Of('N')]);
        }
        else
        {
            Add(ShortcutAction.NextHit, KeyChord.Of('n'));
            Add(ShortcutAction.PrevHit, KeyChord.Of('N'));
        }
        Add(ShortcutAction.ToggleBookmark, KeyChord.Of('m'));
        Add(ShortcutAction.ToggleTail, KeyChord.Of('f'));
        return set;
    }

    /// <summary>基準と違う操作だけ（設定ファイルにはこれだけ書く）。</summary>
    public Dictionary<string, List<string>> DifferencesFrom(KeyBindingSet basis)
    {
        var diff = new Dictionary<string, List<string>>();
        foreach (var action in Actions)
            if (!KeysOf(action).SequenceEqual(basis.KeysOf(action)))
                diff[action.ToString()] = KeysOf(action).Select(k => k.ToString()).ToList();
        return diff;
    }

    /// <summary>基準に違いを当てる（知らない操作・読めないキーは飛ばす）。</summary>
    public static KeyBindingSet FromDifferences(KeyBindingSet basis, IReadOnlyDictionary<string, List<string>>? diff)
    {
        var set = basis.Clone();
        if (diff is null) return set;
        foreach (var (name, keys) in diff)
        {
            if (!Enum.TryParse(name, out ShortcutAction action) || !Enum.IsDefined(action)) continue;
            set.SetKeys(action, keys.Select(k => KeyChord.TryParse(k, out var c) ? c : (KeyChord?)null)
                                    .Where(c => c is not null).Select(c => c!.Value));
        }
        return set;
    }

    // ── 書き出し・読み込み（.uwvkeys。別の機械へ持っていく）──

    public const string FileExtension = ".uwvkeys";
    private const string FileFormat = "uwvkeys";

    /// <summary>［標準］との違いを JSON で書く（Primary で書くので、Mac と Windows の間でも持っていける）。</summary>
    public string ToFileText(bool mac)
        => JsonSerializer.Serialize(new KeyBindingsFile { Format = FileFormat, Version = 1, Bindings = DifferencesFrom(Standard(mac)) },
                                    KeyBindingsJsonContext.Default.KeyBindingsFile);

    /// <summary>.uwvkeys を読む（形が違えば null）。</summary>
    public static KeyBindingSet? FromFileText(string text, bool mac)
    {
        try
        {
            var file = JsonSerializer.Deserialize(text, KeyBindingsJsonContext.Default.KeyBindingsFile);
            return file is { Format: FileFormat } ? FromDifferences(Standard(mac), file.Bindings) : null;
        }
        catch (JsonException) { return null; }
    }
}

public sealed class KeyBindingsFile
{
    public string Format { get; set; } = "";
    public int Version { get; set; }
    public Dictionary<string, List<string>> Bindings { get; set; } = new();
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(KeyBindingsFile))]
internal sealed partial class KeyBindingsJsonContext : JsonSerializerContext;

/// <summary>今の割り当て（設定から作る。設定が替わったら作り直す）。</summary>
public static class KeyBindings
{
    private static AppSettings? _for;
    private static Dictionary<string, List<string>>? _forDiff;
    private static KeyBindingSet? _mac, _other;

    /// <summary>割り当てが変わったとき（設定の画面で変えた・読み込んだ）。</summary>
    public static event Action? Changed;

    public static KeyBindingSet For(bool mac)
    {
        var settings = AppSettingsRef.Current;
        if (!ReferenceEquals(settings, _for) || !ReferenceEquals(settings.KeyBindings, _forDiff))
        {
            _for = settings;
            _forDiff = settings.KeyBindings;
            _mac = _other = null;
        }
        return mac
            ? _mac ??= KeyBindingSet.FromDifferences(KeyBindingSet.Standard(true), settings.KeyBindings)
            : _other ??= KeyBindingSet.FromDifferences(KeyBindingSet.Standard(false), settings.KeyBindings);
    }

    public static KeyBindingSet Current => For(OperatingSystem.IsMacOS());

    /// <summary>割り当てを入れ替えて設定に残す（標準と違うものだけ書く）。</summary>
    public static void Apply(KeyBindingSet set)
    {
        var settings = AppSettingsRef.Current;
        settings.KeyBindings = set.DifferencesFrom(KeyBindingSet.Standard(OperatingSystem.IsMacOS()));
        settings.Save();
        Changed?.Invoke();
    }
}
