using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using UwView.Core;

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
}

/// <summary>Desktop 既定実装（IStorageProvider → ローカルパス → mmap）。</summary>
public sealed class DesktopDocumentOpener : IDocumentOpener
{
    public async Task<IReadOnlyList<DocumentSession>> PickFilesAsync(TopLevel topLevel)
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "テキストファイルを開く",
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
}
