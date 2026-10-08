using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using UwView.Core;
using UwView.Services;
using UwView.ViewModels;

namespace UwView.Views;

/// <summary>キー操作・ブックマークを残す・読み直し（実装指示書_klogg比較の追加機能 §4・§6）。</summary>
public partial class MainView
{
    private void AttachShortcuts()
    {
        ShortcutKeys.Attach(this, RunShortcut, () => ShortcutKeys.FocusInTextBox(this));
        JumpBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            OnJumpClick(this, new Avalonia.Interactivity.RoutedEventArgs());
            e.Handled = true;
        };
        App.RequestExportBookmarks = ExportBookmarks;
    }

    internal bool RunShortcut(ShortcutAction action)
    {
        switch (action)
        {
            case ShortcutAction.FocusSearch:
                FocusAndSelectAll(SearchBox.IsVisible ? SearchBox : SearchBoxPlain);
                return true;
            case ShortcutAction.FocusJump:
                FocusAndSelectAll(JumpBox);
                return true;
            case ShortcutAction.NextHit: GoToHit(next: true); return true;
            case ShortcutAction.PrevHit: GoToHit(next: false); return true;
            case ShortcutAction.ToggleBookmark: ToggleBookmarkHere(); return true;
            case ShortcutAction.NextBookmark: GoToBookmark(next: true); return true;
            case ShortcutAction.PrevBookmark: GoToBookmark(next: false); return true;
            case ShortcutAction.ToggleTail:
                if (!TailToggle.IsVisible || !TailToggle.IsEnabled) return false;
                TailToggle.IsChecked = TailToggle.IsChecked != true;
                return true;
            case ShortcutAction.Reload: ReloadActiveTab(); return true;
            case ShortcutAction.OpenCommandLine: OpenCommandLine(); return true;
            case ShortcutAction.LeaveInput:
                if (SearchBox.IsDropDownOpen) return false;   // 候補の一覧を閉じるのが先
                TextView.Focus();
                return true;
            default: return false;
        }
    }

    private void FocusAndSelectAll(Control box)
    {
        box.Focus();
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox text) text.SelectAll();
    }

    private void ToggleBookmarkHere()
    {
        if (_vm?.ActiveTab is not { } t) return;
        t.Session.ToggleBookmark(TextView.CurrentOffset);
        TextView.Refresh();
        Minimap.InvalidateVisual();
    }

    /// <summary>前回のブックマークを戻し、以後の変化を覚える（開いたタブごと）。</summary>
    private void AttachBookmarkMemory(DocumentSession session)
    {
        if (BookmarkMemory.Attach(UwView.App.Settings, session) == BookmarkMemory.Result.Changed)
            SetTransientStatus(L.Format("BookmarksNotRestored", Path.GetFileName(session.FilePath)));
    }

    /// <summary>
    /// 今のタブのファイルを開き直す（F5）。同じ位置・同じタブの並びに戻す。ブックマークは覚えている分が戻る。
    /// 複数ファイルの結果のタブは組が崩れるので読み直さない。
    /// </summary>
    /// <param name="follow">末尾追従のまま読み直す（外で切り詰められた等。末尾へ移り、追従を続ける。v1.8.2 extFS E-0）。</param>
    internal DocumentTabViewModel? ReloadActiveTab(bool follow = false)
    {
        if (_vm?.ActiveTab is not { } tab) return null;
        string path = tab.FilePath;
        if (string.IsNullOrEmpty(path) || _multiGroup?.Contains(tab) == true)
        {
            SetTransientStatus(L["ReloadNotAvailable"]);
            return null;
        }
        if (!File.Exists(path))
        {
            SetTransientStatus(L.Format("FileMissing", Path.GetFileName(path)));
            return null;
        }
        long top = TopOffsetOf(tab.Session);
        int index = _vm.Tabs.IndexOf(tab);
        if (OpenPath(path) is not { } fresh) return null;
        _vm.Tabs.Move(_vm.Tabs.IndexOf(fresh), index);
        if (!follow) RestorePosition(fresh, new Services.OpenDoc { Path = path, LastTopOffset = top });
        _vm.RequestClose(tab);
        _vm.ActiveTab = fresh;
        if (follow)
        {
            fresh.Session.StartTail();
            _suppressToggleApply = true;
            TailToggle.IsChecked = true;
            _suppressToggleApply = false;
            TextView.GoToEnd();
        }
        SetTransientStatus(L["Reloaded"]);
        return fresh;
    }

    private async void ExportBookmarks()
    {
        if (_vm?.ActiveTab?.Session is not { } session) return;
        if (session.Bookmarks.Count == 0) { SetTransientStatus(L["BookmarksNone"]); return; }
        if (!session.Document.IsIndexed) { SetTransientStatus(L["BookmarksWaitIndex"]); return; }
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L["ExportBookmarksMenu"],
            SuggestedFileName = BookmarkMemory.ExportFileName(session),
            DefaultExtension = "txt",
        });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            if (await BookmarkMemory.ExportAsync(session, stream) is { } count)
                SetTransientStatus(L.Format("BookmarksExported", N(count)));
        }
        catch (IOException failure)
        {
            SetTransientStatus(failure.Message);
        }
    }
}
