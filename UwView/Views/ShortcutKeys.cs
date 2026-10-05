using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace UwView.Views;

/// <summary>キー操作で呼ぶ動き（実装指示書_klogg比較の追加機能 §6 の最小セット）。</summary>
public enum ShortcutAction
{
    FocusSearch,
    NextHit,
    PrevHit,
    FocusJump,
    ToggleBookmark,
    PrevBookmark,
    NextBookmark,
    ToggleTail,
    Reload,
    LeaveInput,
    /// <summary>「コマンドライン」ダイアログを開く（Ctrl+Shift+K・Mac は ⌘⇧K。v1.8.0 Finder Scope §2）。</summary>
    OpenCommandLine,
}

/// <summary>
/// 本体の窓のキー操作（無料版・Pro 共通）。Mac は Ctrl を Cmd に読み替える。
///
/// 窓の一番外で先に受ける（本文・検索欄のどこにいても効くように）。文字を打っている欄の中では、
/// <c>[</c> <c>]</c> のような文字のキーは取らない（入力の邪魔をしない）。
/// </summary>
public static class ShortcutKeys
{
    /// <summary>キーから動きを決める（割り当てが無ければ null）。</summary>
    /// <param name="symbol">押したキーが表す文字（配列によらず <c>[</c> <c>]</c> を見分けるため）。</param>
    /// <param name="typing">文字を打つ欄（検索欄・ジャンプ欄・Pro の編集中の本文）にいるか。</param>
    public static ShortcutAction? Map(Key key, KeyModifiers mods, string? symbol, bool typing, bool mac)
    {
        var primary = mac ? KeyModifiers.Meta : KeyModifiers.Control;
        var relevant = mods & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Shift | KeyModifiers.Alt);

        if (relevant == primary)
            switch (key)
            {
                case Key.F: return ShortcutAction.FocusSearch;
                case Key.L: return ShortcutAction.FocusJump;
                case Key.B: return ShortcutAction.ToggleBookmark;
                case Key.G when mac: return ShortcutAction.NextHit;
            }
        if (relevant == (primary | KeyModifiers.Shift))
            switch (key)
            {
                case Key.F: return ShortcutAction.ToggleTail;
                case Key.G when mac: return ShortcutAction.PrevHit;
                case Key.K: return ShortcutAction.OpenCommandLine;
            }
        if (key == Key.F3 && relevant == KeyModifiers.None) return ShortcutAction.NextHit;
        if (key == Key.F3 && relevant == KeyModifiers.Shift) return ShortcutAction.PrevHit;
        if (key == Key.F5 && relevant == KeyModifiers.None) return ShortcutAction.Reload;
        if (key == Key.Escape && relevant == KeyModifiers.None) return typing ? ShortcutAction.LeaveInput : null;
        if (typing) return null;

        // AltGr（Ctrl+Alt）で打つ配列もあるので、Cmd や Ctrl だけのときを除いて文字で見る
        bool plainChar = (relevant & KeyModifiers.Meta) == 0
                      && ((relevant & KeyModifiers.Control) == 0 || (relevant & KeyModifiers.Alt) != 0);
        if (plainChar && symbol == "[") return ShortcutAction.PrevBookmark;
        if (plainChar && symbol == "]") return ShortcutAction.NextBookmark;
        return null;
    }

    /// <summary>
    /// 窓（view を載せた TopLevel）でキーを受ける。view で受けると、タブを閉じた直後のように
    /// どこにもフォーカスが無いときにキーが窓までしか届かず、効かない。
    /// </summary>
    /// <param name="run">動きを行う（行ったら true。true のときだけキーを使ったことにする）。</param>
    /// <param name="typing">文字を打つ欄にいるか。</param>
    public static void Attach(Control view, Func<ShortcutAction, bool> run, Func<bool> typing)
    {
        EventHandler<KeyEventArgs> handler = (_, e) =>
        {
            if (e.Handled) return;
            if (Map(e.Key, e.KeyModifiers, e.KeySymbol, typing(), OperatingSystem.IsMacOS()) is not { } action) return;
            if (run(action)) e.Handled = true;
        };
        TopLevel? attached = null;
        void Hook()
        {
            if (TopLevel.GetTopLevel(view) is not { } top || ReferenceEquals(top, attached)) return;
            attached?.RemoveHandler(InputElement.KeyDownEvent, handler);
            top.AddHandler(InputElement.KeyDownEvent, handler, RoutingStrategies.Tunnel);
            attached = top;
        }
        view.AttachedToVisualTree += (_, _) => Hook();
        view.DetachedFromVisualTree += (_, _) =>
        {
            attached?.RemoveHandler(InputElement.KeyDownEvent, handler);
            attached = null;
        };
        Hook();
    }

    /// <summary>フォーカスが文字を打つ欄（TextBox。AutoCompleteBox・NumericUpDown の中身も）にあるか。</summary>
    public static bool FocusInTextBox(Control root)
        => TopLevel.GetTopLevel(root)?.FocusManager?.GetFocusedElement() is TextBox;
}
