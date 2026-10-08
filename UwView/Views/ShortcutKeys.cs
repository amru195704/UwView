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
    /// <summary>設定の画面を開く（Ctrl＋,・Mac は ⌘,。v1.8.2 extFS E-3）。</summary>
    OpenSettings,

    // ── v1.8.2 extFS E-3 §4.3 で足した操作（［vi・less 風］でキーが付く。［標準］ではキーなし）──
    /// <summary>1 行下（j。数字を前に打てば N 行）。</summary>
    LineDown,
    /// <summary>1 行上（k）。</summary>
    LineUp,
    /// <summary>左へ横に動かす（h）。</summary>
    ScrollLeft,
    /// <summary>右へ横に動かす（l）。</summary>
    ScrollRight,
    /// <summary>行の左端へ（^）。</summary>
    LineLeftEnd,
    /// <summary>行の右端へ（$）。</summary>
    LineRightEnd,
    /// <summary>数字＋g でその行へ、g だけなら先頭へ。</summary>
    GoToLine,
    /// <summary>末尾へ（G）。</summary>
    GoToEnd,
    /// <summary>選んだ語の次を探す（*）。</summary>
    SearchWordNext,
    /// <summary>選んだ語の前を探す（#）。</summary>
    SearchWordPrev,
    /// <summary>結果の一覧の切り替え（v）。</summary>
    CycleResultList,
    /// <summary>結果の一覧の窓を大きく（+）。</summary>
    ResultListBigger,
    /// <summary>結果の一覧の窓を小さく（-）。</summary>
    ResultListSmaller,
    /// <summary>開いているファイルを選ぶ小さな窓（Ctrl＋Shift＋O）。</summary>
    OpenFileSwitcher,

    // ── v1.8.2 extFS E-4 §5：文字の拡大・縮小（本文と結果の一覧の両方）──
    /// <summary>文字を大きく（Ctrl＋＋）。</summary>
    ZoomIn,
    /// <summary>文字を小さく（Ctrl＋－）。</summary>
    ZoomOut,
    /// <summary>文字を既定の大きさに戻す（Ctrl＋0）。</summary>
    ZoomReset,
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
    /// <remarks>割り当ては設定で変えられる（<see cref="UwView.Services.KeyBindings"/>。v1.8.2 extFS E-3）。</remarks>
    public static ShortcutAction? Map(Key key, KeyModifiers mods, string? symbol, bool typing, bool mac)
        => UwView.Services.KeyBindings.For(mac).Find(key, mods, symbol, typing, mac);

    // ── 数字の前置き（5j で 5 行下・120g で 120 行目へ）──
    private static int _count;

    /// <summary>打った数字（無ければ null）を受け取って消す。</summary>
    public static int? TakeCount()
    {
        int n = _count;
        _count = 0;
        return n > 0 ? n : null;
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
            bool inText = typing();
            // 文字を打つ欄の外で、修飾なしの数字は「何行」の前置き（j・k・g が使う）
            if (!inText && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Alt)) == 0
                && e.KeySymbol is { Length: 1 } digit && char.IsAsciiDigit(digit[0]))
            {
                _count = (int)Math.Min(int.MaxValue / 10, (long)_count * 10 + (digit[0] - '0'));
                e.Handled = true;
                return;
            }
            var action = Map(e.Key, e.KeyModifiers, e.KeySymbol, inText, OperatingSystem.IsMacOS());
            if (action is null)
            {
                if (e.KeySymbol is { Length: > 0 }) _count = 0;   // ほかのキーを打ったら前置きは捨てる
                return;
            }
            if (run(action.Value)) e.Handled = true;
            _count = 0;
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
