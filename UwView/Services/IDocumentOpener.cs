using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using UwView.Core;
using UwView.Localization;

namespace UwView.Services;

/// <summary>
/// 「ファイルを開く」の head 差し替えポイント。
/// Desktop はローカルパス + mmap、Browser は JS の File(Blob) + BlobByteSource を注入する。
/// </summary>
public interface IDocumentOpener
{
    /// <summary>ピッカーを表示して選択ファイルをセッションとして開く（複数可）。</summary>
    Task<IReadOnlyList<DocumentSession>> PickFilesAsync(TopLevel topLevel);

    /// <summary>ローカルパスから開く（ドラッグ&ドロップ用）。対応しない head は null。</summary>
    DocumentSession? OpenLocalPath(string path);

    /// <summary>
    /// パスを扱える head か（Desktop/Pro は true・Browser は false）。
    /// 圧縮ファイル（.gz/.zip）は「展開して開く／.uwvz に変換して開く」を尋ねてから開くので、
    /// セッションを作る前にパスを見る必要がある。
    /// </summary>
    bool SupportsPathPicking => false;

    /// <summary>ピッカーでパスだけを選ばせる（開くのは呼び出し側）。</summary>
    Task<IReadOnlyList<string>> PickPathsAsync(TopLevel topLevel)
        => Task.FromResult<IReadOnlyList<string>>([]);
}

/// <summary>Desktop 既定実装（IStorageProvider → ローカルパス → mmap）。</summary>
public sealed class DesktopDocumentOpener : IDocumentOpener
{
    public async Task<IReadOnlyList<DocumentSession>> PickFilesAsync(TopLevel topLevel)
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.Instance["OpenDialogTitle"],
            AllowMultiple = true
        });

        var sessions = new List<DocumentSession>();
        foreach (var path in files.Select(f => f.TryGetLocalPath()).OfType<string>())
        {
            try { sessions.Add(DocumentSession.Open(path)); }
            catch { /* 開けないファイルはスキップ */ }
        }
        return sessions;
    }

    public DocumentSession? OpenLocalPath(string path)
    {
        try { return DocumentSession.Open(path); }
        catch { return null; }
    }

    public bool SupportsPathPicking => true;

    public async Task<IReadOnlyList<string>> PickPathsAsync(TopLevel topLevel)
    {
        // FileTypeFilter は指定しない。macOS ではパターン指定が拡張子なしファイルを
        // 選択不可にする癖があるため、無指定＝全ファイル選択可とする（.gz/.zip もそのまま選べる）
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.Instance["OpenDialogTitle"],
            AllowMultiple = true,
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }
}
