using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia.Controls;
using UwView.Core;
using UwView.Services;

namespace UwView.Browser;

/// <summary>Browser head の IDocumentOpener（JS のファイル選択 → BlobByteSource）。</summary>
[SupportedOSPlatform("browser")]
public sealed class BrowserDocumentOpener : IDocumentOpener
{
    public async Task<IReadOnlyList<DocumentSession>> PickFilesAsync(TopLevel topLevel)
    {
        // 形式: "id\tsize\tname\n..."（blobRead.js 参照。トリミング安全のため JSON は使わない）
        string lines = await BlobInterop.PickFilesAsync();

        var sessions = new List<DocumentSession>();
        foreach (var line in lines.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', 3);
            if (parts.Length != 3) continue;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) continue;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double size)) continue;

            var src = new BlobByteSource(id, (long)size);
            sessions.Add(await DocumentSession.CreateAsync(parts[2], src));
        }
        return sessions;
    }

    // ブラウザではローカルパス直開き不可（既知制限 §9）
    public DocumentSession? OpenLocalPath(string path) => null;
}
