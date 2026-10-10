using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UwView.Core;
using UwView.Core.Documents;
using UwView.Services;
using UwView.ViewModels;

namespace UwView.Views;

/// <summary>
/// Word・Excel・PDF を開く（v1.8.3.2）。文字を取り出して一時フォルダーのテキストにし（<see cref="OfficeTextCache"/>）、それを開く。
/// 取り出し済みで元が変わっていなければ、取り出し直さずにすぐ開く。元の形式には戻さない。
/// </summary>
public partial class MainView
{
    /// <summary>Word・Excel・PDF を開く（読めなければ理由を知らせて null）。</summary>
    private async Task<DocumentTabViewModel?> OpenOfficeAsync(string path, OfficeProbe office)
    {
        string name = Path.GetFileName(path);
        if (office.IsRejected)
        {
            await NoticeAsync(CompressedOpenDialog.OfficeMessage(office, name));
            return null;
        }
        return await ExtractOfficeAsync(path, office.Kind) is { } text ? OpenPath(text) : null;
    }

    /// <summary>取り出した文字のファイル（取り出し済みならそれ）。読めない・止めたときは null。</summary>
    private async Task<string?> ExtractOfficeAsync(string path, OfficeKind kind, CancellationToken ct = default)
    {
        if (OfficeTextCache.Existing(path) is { } cached) return cached;
        string name = Path.GetFileName(path);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        double fraction = 0;
        BeginTaskProgress(
            Ja ? $"文字を取り出して開きます: {name}" : $"Extracting the text to open: {name}",
            () => (fraction, Ja ? "取り出した割合" : "extracted"),
            onCancel: linked.Cancel);
        try
        {
            string text = await OfficeTextCache.ExtractAsync(path, kind, f => fraction = f, linked.Token);
            EndTaskProgress(null);
            return text;
        }
        catch (OperationCanceledException)
        {
            EndTaskProgress(null);
            return null;
        }
        catch (OfficeRejectedException e)
        {
            EndTaskProgress(null);
            await NoticeAsync(e.Text(Ja));
            return null;
        }
        catch (IOException e)
        {
            EndTaskProgress(null);
            await NoticeAsync(Ja ? $"{name} の文字を取り出せませんでした。\n{e.Message}"
                                 : $"Could not extract the text of {name}.\n{e.Message}");
            return null;
        }
    }
}
