using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UwView.Core;
using UwView.Core.Documents;

namespace UwView.Services;

/// <summary>
/// 画面（uvf）で開く Word・Excel・PDF の、取り出した文字の置き場（v1.8.3.2）。
///
/// uvf は索引（.uwvz）を作らないので、取り出した文字を一時フォルダーのテキストにして、それを開く。
/// 元のファイルの隣には何も作らない。置き場は <c>&lt;一時フォルダー&gt;/UwView-office/&lt;番号&gt;/report.docx.txt</c>。
/// 番号は元のファイルのフルパス・大きさ・更新時刻から決まる（元が変われば別の番号＝取り出し直す）。
/// 同じフォルダーに元のパスを書いておき（<c>.source</c>）、ステータスバーの名前やファイルの操作は元のファイルを指す。
/// 行番号は uvf のコマンドと同じ（同じ取り出し方）。
/// </summary>
public static class OfficeTextCache
{
    private const string SourceFile = ".source";

    /// <summary>置き場の一番上。</summary>
    public static string Root => Path.Combine(Path.GetTempPath(), "UwView-office");

    /// <summary>元のファイルの、取り出した文字の置き場所（まだ無いこともある）。</summary>
    public static string PathFor(string original)
    {
        string full = Path.GetFullPath(original);
        var info = new FileInfo(full);
        string key = $"{full}|{(info.Exists ? info.Length : 0)}|{(info.Exists ? info.LastWriteTimeUtc.Ticks : 0)}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();
        return Path.Combine(Root, hash, Path.GetFileName(full) + ".txt");
    }

    /// <summary>取り出し済みで元が変わっていなければ、その置き場所。無ければ null。</summary>
    public static string? Existing(string original)
    {
        string path = PathFor(original);
        return File.Exists(path) ? path : null;
    }

    /// <summary>取り出した文字のファイルなら、その元のファイル（それ以外は null）。</summary>
    public static string? OriginalOf(string path)
    {
        try
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (dir is null || !string.Equals(Path.GetDirectoryName(dir), Path.GetFullPath(Root), StringComparison.Ordinal)) return null;
            string source = Path.Combine(dir, SourceFile);
            return File.Exists(source) ? File.ReadAllText(source, Encoding.UTF8).Trim() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    /// <summary>
    /// 文字を取り出して置き場に書く（書き終えてから名前を付けるので、途中で止めても半端なものは残らない）。
    /// 読めないものは <see cref="OfficeRejectedException"/>。
    /// </summary>
    public static async Task<string> ExtractAsync(string original, OfficeKind kind, Action<double>? progress, CancellationToken ct)
    {
        string path = PathFor(original);
        string dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        string temp = path + ".part";
        try
        {
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            await using (var text = OfficeText.OpenStream(original, kind, raw: false, progress))
                await Task.Run(() => text.CopyToAsync(output, 1 << 20, ct), ct);
            await File.WriteAllTextAsync(Path.Combine(dir, SourceFile), Path.GetFullPath(original), Encoding.UTF8, ct);
            File.Move(temp, path, overwrite: true);
            return path;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
        }
    }
}
