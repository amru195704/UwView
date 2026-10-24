using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using UwView.Core;
using UwView.Core.Documents;
using UwView.Services;
using UwView.ViewModels;

namespace UwView.Views;

/// <summary>
/// 変換して開いたタブ（Word・Excel・PDF から取り出した文字）の、元のファイルの見張り（2026-10-14 レビュー P2）。
/// タブが見ているのは一時フォルダーの取り出した文字なので、元のファイルが変わっても外での変化の見張り（<see cref="WatchExternalChanges"/>）は気づかない。
/// 今のタブだけを 1 秒ごとに見て、元が変わっていれば帯で知らせる（設定「変換して開いたファイルの元を見張る」でオン・オフ）。
/// </summary>
public partial class MainView
{
    private DispatcherTimer? _originalTimer;
    private DocumentSession? _originalSeen;
    private bool _originalShown;

    private void StartOriginalWatch()
    {
        _originalTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _originalTimer.Tick += (_, _) => CheckOriginal();
        _originalTimer.Start();
    }

    private void CheckOriginal()
    {
        if (_vm?.ActiveTab?.Session is not { } session) return;
        if (!ReferenceEquals(session, _originalSeen)) { _originalSeen = session; _originalShown = false; }   // タブごとに 1 度だけ出す
        if (_originalShown || !AppSettingsRef.Current.WatchOriginalFile) return;
        if (ChangedOriginal(session.FilePath) is not { } original) return;
        _originalShown = true;
        ShowChangeBanner(L.Format("OriginalChanged", Path.GetFileName(original)), canReload: true, null);
    }

    /// <summary>取り出した文字のタブで、元のファイルが変わっていればその場所（取り出し直すと置き場所が変わる）。</summary>
    internal static string? ChangedOriginal(string path)
        => OfficeTextCache.OriginalOf(path) is { } original && File.Exists(original)
           && !string.Equals(OfficeTextCache.PathFor(original), Path.GetFullPath(path), StringComparison.Ordinal)
            ? original : null;

    /// <summary>元のファイルから取り出し直して、同じ場所のタブに置き換える（［読み直す］）。</summary>
    private async Task ReopenOfficeAsync(DocumentTabViewModel tab, string original)
    {
        if (_vm is null) return;
        int index = _vm.Tabs.IndexOf(tab);
        // 元のファイルを見分け直す。Word・Excel・PDF でなくなっていれば（別の形式に置き換わった）、ふつうの開き方で開き直す
        //（取り出しに種類なしを渡すと ArgumentOutOfRangeException で落ちていた。レビュー 2026-10-24 P2-4）
        var probe = OfficeDocumentFile.Probe(original);
        DocumentTabViewModel? fresh;
        if (probe.IsOffice) fresh = await OpenOfficeAsync(original, probe);
        else
        {
            await OpenPathsAsync([original]);
            fresh = _vm.ActiveTab;
        }
        if (fresh is null || ReferenceEquals(fresh, tab)) return;
        if (index >= 0 && _vm.Tabs.IndexOf(fresh) is var at and >= 0) _vm.Tabs.Move(at, Math.Min(index, _vm.Tabs.Count - 1));
        _vm.RequestClose(tab);
        _vm.ActiveTab = fresh;
        HideChangeBanner();
        SetTransientStatus(L["Reloaded"]);
    }
}
