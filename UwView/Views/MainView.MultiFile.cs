using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using UwView.Core;
using UwView.Core.Cli;
using UwView.ViewModels;

namespace UwView.Views;

// ── 複数ファイルの結果（uvf -open '*.log' 語。実装指示書_uvf複数ファイルGUI表示とタブ §4・§5）──
//
// CLI が探した結果（受け渡し）を受け、番号 1 のファイルをメインで開き、結果の窓を「n:行番号」で出す。
// 結果をダブルクリックすると、そのファイルをメインに出してその行へ移る（uvp のように束ねていないので、
// 開き直してから移る）。ファイル一覧からは追加タブでも開ける（メイン＋7 個まで）。
public partial class MainView
{
    private MultiHandoff? _multi;
    private long[] _multiHitsPerFile = [];
    private FileTabGroup<DocumentTabViewModel>? _multiGroup;
    private FileListPopup? _fileListWindow;
    private FilterResultsWindow? _multiResultsWindow;
    private FilterResultsViewModel? _multiResultsVm;

    /// <summary>待っている間に別の行を開いたら、後のほうを優先する（前の待ちは取り消す）。</summary>
    private CancellationTokenSource? _multiOpenCts;

    /// <summary>複数ファイルの結果から取り込んだ検索（画面で検索し直したら外す）。</summary>
    private readonly HashSet<DocumentSession> _multiAdopted = [];

    /// <summary>自動テスト用。</summary>
    internal MultiHandoff? MultiResult => _multi;
    internal FileTabGroup<DocumentTabViewModel>? MultiGroup => _multiGroup;
    internal FilterResultsViewModel? MultiResultsViewModel => _multiResultsVm;

    /// <summary>複数ファイルの結果を受けて開く（起動時。§4.1 の 4）。</summary>
    internal async Task StartMultiAsync(string handoffPath)
    {
        if (MultiHandoff.TakeFrom(handoffPath) is not { } set || _vm is null) return;
        _multi = set;
        _multiHitsPerFile = set.HitsPerFile();
        FileListButton.IsVisible = true;

        // 開いた直後は、ヒットした最初のファイルをメインに出す。番号 1 がヒット 0 件だと、
        // 本文にも当たりが無く、「ヒットしたファイルだけ」の一覧にも 0 件の行が残った（9.29修正）。
        // ヒットが無い・検索しないときは、開ける最初のもの（番号 1。zip などで開けなければその次。D4）
        var openable = Enumerable.Range(0, set.Files.Count).Where(i => set.Files[i].CanOpen).ToList();
        int first = openable.FirstOrDefault(i => _multiHitsPerFile[i] > 0, openable.FirstOrDefault(-1));
        if (first >= 0 && await OpenMultiTabAsync(first, CancellationToken.None) is { } main)
        {
            _multiGroup = new FileTabGroup<DocumentTabViewModel>(main) { MainFile = first };
            MakeMain(main, first);
            _vm.ActiveTab = main;
        }

        if (set.HasSearch) OpenMultiResults();
        else OpenFileList();
    }

    /// <summary>
    /// 前の複数ファイルの結果を片付ける（コマンドラインダイアログから続けて実行したとき）。
    /// 結果に結びついたタブ・結果の窓・ファイル一覧を閉じる。
    /// </summary>
    private void ForgetMultiResult()
    {
        if (_multi is null || _vm is null) return;
        _fileListWindow?.Close();
        _multiResultsWindow?.Close();
        _multiResultsVm = null;
        if (_multiGroup is { } group)
            foreach (var tab in _vm.Tabs.Where(group.Contains).ToList())
            {
                tab.CanClose = true;
                _vm.RequestClose(tab);
            }
        _multiGroup = null;
        _multiAdopted.Clear();
        _multi = null;
        _multiHitsPerFile = [];
        FileListButton.IsVisible = false;
    }

    // ── 結果の窓（結果セットに結びつける。メインを差し替えても窓はそのまま §4.3）──

    private void OpenMultiResults()
    {
        if (_multi is not { HasSearch: true } set) return;
        if (_multiResultsWindow is not null) { _multiResultsWindow.Activate(); return; }
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        if (_multiResultsVm is null)
        {
            _multiResultsVm = new FilterResultsViewModel(_ => { }, maxContext: 0)
            {
                OpenHit = hit => _ = OpenMultiHitAsync(hit),
                OpenFileList = OpenFileList,
            };
            _multiResultsVm.SetResultSet(set);
        }
        _multiResultsWindow = new FilterResultsWindow(_multiResultsVm);
        _multiResultsWindow.Closed += (_, _) => _multiResultsWindow = null;
        _multiResultsWindow.Show(owner);
    }

    /// <summary>
    /// 「結果一覧」ボタン: いまのタブで画面から検索していれば、その結果。
    /// そうでなければ（複数ファイルの結果を開いているなら）複数ファイルの結果。
    /// </summary>
    private void OnFilterResultsClick()
    {
        var session = _vm?.ActiveTab?.Session;
        if (_multi is { HasSearch: true }
            && (session?.ActiveSearch is null || _multiAdopted.Contains(session)))
        {
            OpenMultiResults();
            return;
        }
        OpenFilterResults();
    }

    /// <summary>画面で検索し直した（メインの1ファイルだけを探す §4.6）。取り込んだ結果ではなくなる。</summary>
    private void ForgetMultiSearch(DocumentSession session) => _multiAdopted.Remove(session);

    // ── 結果をダブルクリック（§4.4。検索結果からは常にメインで開く §9 Q2）──

    private async Task OpenMultiHitAsync(MultiHandoffHit hit)
    {
        if (_multi is not { } set || _vm is null || hit.File < 0 || hit.File >= set.Files.Count) return;
        _multiOpenCts?.Cancel();
        var cts = new CancellationTokenSource();
        _multiOpenCts = cts;

        SetTransientStatus(L.Format("MultiOpening", $"{hit.File + 1}:{(hit.Line + 1).ToString("N0", L.Culture)}"));
        var tab = await ShowMultiFileAsync(hit.File, inTab: false, cts.Token);
        if (tab is null || cts.IsCancellationRequested) return;

        long offset = Math.Clamp(hit.Offset, 0, Math.Max(0, tab.Session.Document.Length - 1));
        TextView.JumpToOffsetCentered(offset);     // 中央寄せ＋その行を強調（今の検索移動と同じ）
        SetCurrentHitOrdinal(tab.Session, offset);
        UpdateSearchInfo();
        TextView.Focus();
        if (ReferenceEquals(_multiOpenCts, cts)) _multiOpenCts = null;
    }

    // ── ファイル一覧（§5）──

    internal void OpenFileList()
    {
        if (_multi is not { } set) return;
        if (_fileListWindow is not null) { _fileListWindow.Activate(); return; }
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var items = set.Files.Select((f, i) => new FileListItem(
            (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), f.Display, f.Path,
            set.HasSearch && f.CanOpen ? _multiHitsPerFile[i] : null, f.Length)).ToList();
        _fileListWindow = FileListPopup.Show(owner, new FileListSource
        {
            Items = items,
            Open = (index, inTab) => _ = OpenFromFileListAsync(index, inTab),
            Placement = index => _multiGroup?.Placement(index),
            TabCount = () => _multiGroup?.Count ?? 0,
            OffersHitsOnly = set.HasSearch,
        });
        _fileListWindow.Closed += (_, _) => _fileListWindow = null;
    }

    private async Task OpenFromFileListAsync(int index, bool inTab)
    {
        _multiOpenCts?.Cancel();
        var cts = new CancellationTokenSource();
        _multiOpenCts = cts;
        // 新しく開いたものは先頭が出る。すでに出ていたものは切り替えるだけ
        if (await ShowMultiFileAsync(index, inTab, cts.Token) is not null && !cts.IsCancellationRequested)
            TextView.Focus();
        _fileListWindow?.Refresh();
    }

    /// <summary>
    /// 番号 index のファイルを出す。すでにメイン・追加タブにあれば切り替えるだけ（同じファイルを2つ開かない）。
    /// </summary>
    private async Task<DocumentTabViewModel?> ShowMultiFileAsync(int index, bool inTab, CancellationToken ct)
    {
        if (_multi is not { } set || _vm is null) return null;
        if (_multiGroup is null)
        {
            // 起動時にメインを開けなかった（番号 1 が消えていた等）。最初に開いたものをメインにする
            if (await OpenMultiTabAsync(index, ct) is not { } first) return null;
            _multiGroup = new FileTabGroup<DocumentTabViewModel>(first) { MainFile = index };
            MakeMain(first, index);
            _vm.ActiveTab = first;
            _fileListWindow?.Refresh();
            return first;
        }

        var group = _multiGroup;
        DocumentTabViewModel? tab;
        switch (group.Decide(index, inTab))
        {
            case FileOpenAction.ShowMain:
                tab = group.Main;
                break;
            case FileOpenAction.ShowTab:
                tab = group.TabOf(index);
                break;
            case FileOpenAction.RefuseFull:
                SetTransientStatus(L.Format("TabsFull", FileTabGroup<object>.MaxTabs, FileTabGroup<object>.MaxTabs - 1));
                return null;
            case FileOpenAction.AddTab:
                tab = await OpenMultiTabAsync(index, ct);
                if (tab is null) return null;
                if (!group.TryAdd(index, tab))
                {
                    // 待っている間にほかの経路で上限に達した・同じファイルが開いた
                    _vm.RequestClose(tab);
                    return group.TabOf(index);
                }
                tab.TitlePrefix = $"{index + 1}:";
                break;
            default:
                tab = await ReplaceMainAsync(index, ct);
                break;
        }
        if (tab is not null && !ct.IsCancellationRequested) _vm.ActiveTab = tab;
        _fileListWindow?.Refresh();
        return tab;
    }

    /// <summary>メインの中身をそのファイルに差し替える（前のファイルは閉じてメモリを返す §4.4）。</summary>
    private async Task<DocumentTabViewModel?> ReplaceMainAsync(int index, CancellationToken ct)
    {
        if (_vm is null || _multiGroup is not { } group) return null;
        var tab = await OpenMultiTabAsync(index, ct);
        if (tab is null) return null;
        if (ct.IsCancellationRequested) { _vm.RequestClose(tab); return null; }

        var old = group.Main;
        int at = _vm.Tabs.IndexOf(old);
        int now = _vm.Tabs.IndexOf(tab);
        if (at >= 0 && now >= 0 && now != at) _vm.Tabs.Move(now, at);
        group.Main = tab;
        group.MainFile = index;
        MakeMain(tab, index);
        _vm.ActiveTab = tab;

        old.CanClose = true;
        _multiAdopted.Remove(old.Session);
        _vm.RequestClose(old);
        return tab;
    }

    /// <summary>メインにしたタブ: 閉じるボタンを出さず、番号を名前に付ける（§5.5）。</summary>
    private static void MakeMain(DocumentTabViewModel tab, int index)
    {
        tab.CanClose = false;
        tab.IsResultMain = true;
        tab.TitlePrefix = $"{index + 1}:";
    }

    /// <summary>
    /// 番号 index のファイルをタブで開く（まだ組には入れない）。圧縮ファイルは確認を出さずに既定の方法で開く
    /// （隣に展開して開く。展開済みで元より新しければそれを開く §4.4）。開いたら、その中の当たりを取り込む。
    /// </summary>
    private async Task<DocumentTabViewModel?> OpenMultiTabAsync(int index, CancellationToken ct)
    {
        if (_multi is not { } set || _vm is null) return null;
        var file = set.Files[index];
        if (!file.CanOpen)
        {
            SetTransientStatus(L.Format("MultiCannotOpen", file.Display));
            return null;
        }
        if (!File.Exists(file.Path))
        {
            SetTransientStatus(L.Format("FileMissing", file.Display));
            return null;
        }

        string path = file.Kind == CompressedKind.None ? file.Path : await ExpandForMultiAsync(file, ct) ?? "";
        if (path.Length == 0 || ct.IsCancellationRequested) return null;
        if (UwView.App.DocumentOpener.OpenLocalPath(path) is not { } session) return null;
        var tab = AddSessions([session]);
        if (tab is null) return null;

        // 検索の後で変わっていたら知らせる（止めない §4.5）。行がずれている恐れがあるので当たりは取り込まない
        if (MultiHandoff.Changed(file))
            SetTransientStatus(L.Format("MultiFileChanged", file.Display));
        else
            AdoptMultiHits(tab.Session, index);
        return tab;
    }

    /// <summary>
    /// そのファイルの当たりを、開いたセッションの検索結果として取り込む
    /// （本体で強調され、「次へ」「前へ」もそのファイルの中で動く。探し直さない）。
    /// </summary>
    private void AdoptMultiHits(DocumentSession session, int index)
    {
        if (_multi is not { } set || set.ToOptions() is not { } options) return;
        var mine = set.Hits.Where(h => h.File == index).ToList();
        if (mine.Count == 0) return;
        session.AdoptSearchResults(options, mine.Select(h => h.Offset).ToArray(), set.Truncated,
                                   mine.Select(h => h.Line).ToArray());
        _multiAdopted.Add(session);
    }

    /// <summary>圧縮ファイルを隣に展開する（確認は出さない）。展開済みで元より新しければそれを使う。</summary>
    private async Task<string?> ExpandForMultiAsync(MultiHandoffFile file, CancellationToken ct)
    {
        string dst = file.Kind == CompressedKind.Gzip
            ? CompressedInput.DerivePlainPath(file.Path)
            : CompressedFormats.PlainPath(file.Path, file.Kind);
        if (File.Exists(dst) && File.GetLastWriteTimeUtc(dst) >= File.GetLastWriteTimeUtc(file.Path)) return dst;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        double fraction = 0;
        string name = Path.GetFileName(file.Path);
        BeginTaskProgress(
            Ja ? $"展開: {name}" : $"Expanding: {name}",
            () => (fraction, Ja ? "圧縮側の読み進み" : "read from the compressed file"),
            onCancel: linked.Cancel);
        try
        {
            await CompressedInput.ExpandAsync(file.Path, file.Kind, dst,
                new Progress<double>(f => fraction = f), linked.Token);
            EndTaskProgress(null);
            return dst;
        }
        catch (OperationCanceledException)
        {
            EndTaskProgress(null);
            return null;
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            EndTaskProgress(null);
            await NoticeAsync(Ja ? $"{name} を展開できませんでした。\n{e.Message}"
                                 : $"Could not expand {name}.\n{e.Message}");
            return null;
        }
    }

    /// <summary>閉じたタブを組から外す（数え直して、一覧の印を直す）。</summary>
    private void ForgetMultiTab(DocumentTabViewModel tab)
    {
        _multiAdopted.Remove(tab.Session);
        if (_multiGroup?.Remove(tab) == true) _fileListWindow?.Refresh();
    }
}
