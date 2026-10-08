using System;
using System.Linq;
using Avalonia.Controls;
using UwView.Services;

namespace UwView.Views;

/// <summary>キーの割り当てで足した操作（v1.8.2 extFS E-3 §4.3。［vi・less 風］でキーが付く）。</summary>
public partial class MainView
{
    private bool RunMoreShortcut(ShortcutAction action)
    {
        int? count = ShortcutKeys.TakeCount();
        int n = count ?? 1;
        switch (action)
        {
            case ShortcutAction.OpenSettings: PreferencesWindow.Open(TopLevel.GetTopLevel(this) as Window); return true;
            case ShortcutAction.OpenFileSwitcher: OpenFileSwitcher(); return true;
            case ShortcutAction.CycleResultList:
                if (FilterResultsOpen) _filterResultsVm?.CycleListMode();
                else OpenFilterResults();
                return true;
            case ShortcutAction.ResultListBigger: ResizeFilterResults(1.15); return true;
            case ShortcutAction.ResultListSmaller: ResizeFilterResults(1 / 1.15); return true;
        }
        if (_vm?.ActiveTab is null) return false;
        switch (action)
        {
            case ShortcutAction.LineDown: TextView.ScrollLines(n); return true;
            case ShortcutAction.LineUp: TextView.ScrollLines(-n); return true;
            case ShortcutAction.ScrollLeft: TextView.ScrollColumns(-n); return true;
            case ShortcutAction.ScrollRight: TextView.ScrollColumns(n); return true;
            case ShortcutAction.LineLeftEnd: TextView.ScrollToLineStart(); return true;
            case ShortcutAction.LineRightEnd: TextView.ScrollToLineEnd(); return true;
            case ShortcutAction.GoToLine:
                if (count is { } line) TextView.JumpToLine(line - 1);
                else TextView.GoToStart();
                return true;
            case ShortcutAction.GoToEnd: TextView.GoToEnd(); return true;
            case ShortcutAction.SearchWordNext: SearchSelectedWord(next: true); return true;
            case ShortcutAction.SearchWordPrev: SearchSelectedWord(next: false); return true;
            default: return false;
        }
    }

    /// <summary>ダブルクリックで選んだ語を探す（* と #）。同じ語で探し済みなら、次・前の当たりへ移るだけ。</summary>
    private void SearchSelectedWord(bool next)
    {
        if (_vm?.ActiveTab?.Session is not { } session) return;
        if (TextView.SelectedWord is not { Length: > 0 } word) { SetTransientStatus(L["SelectWordFirst"]); return; }
        if (session.ActiveSearch is { UseRegex: false } s && s.Pattern == word && session.SearchHits.Count > 0)
        {
            GoToHit(next);
            return;
        }
        _vm.SearchText = word;
        _vm.SearchIsRegex = false;
        _vm.SearchIgnoreCase = false;
        StartSearch();
    }

    /// <summary>結果の一覧の窓の高さを変える（+ と -。開いていなければ開く）。</summary>
    private void ResizeFilterResults(double factor)
    {
        if (_filterResultsWindow is not { } win) { OpenFilterResults(); return; }
        win.Height = Math.Clamp(win.Height * factor, 160, 4000);
    }

    /// <summary>開いているファイルを選ぶ小さな窓（Ctrl＋Shift＋O）。</summary>
    private void OpenFileSwitcher()
    {
        if (_vm is null || _vm.Tabs.Count == 0) return;
        var vm = _vm;
        var win = new FileSwitcherWindow(vm.Tabs.ToList(), tab => vm.ActiveTab = tab);
        if (TopLevel.GetTopLevel(this) is Window owner) win.Show(owner);
        else win.Show();
    }
}
