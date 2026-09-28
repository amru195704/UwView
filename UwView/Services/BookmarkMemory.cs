using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UwView.Core;

namespace UwView.Services;

/// <summary>
/// ブックマークをファイルごとに覚えて、同じファイルを開いたら戻す（実装指示書_klogg比較の追加機能 §4）。
///
/// 妥当性は <see cref="PerFileState"/> と同じく長さ＋更新時刻。違っていても<b>追記だけ</b>なら位置はそのまま使える。
/// 追記だけかは、先頭と「記録したときの末尾の手前」の数 KB を比べて見分ける（全体は読まない＝速度優先）。
/// </summary>
public static class BookmarkMemory
{
    /// <summary>開いたときに前回のブックマークをどうしたか。</summary>
    public enum Result { None, Restored, Changed }

    private const int ProbeBytes = 4096;

    /// <summary>前回の分を戻し、以後の変化を覚えるようにする。</summary>
    public static Result Attach(AppSettings settings, DocumentSession session)
    {
        var result = Restore(settings, session);
        session.BookmarksChanged += (_, _) => Remember(settings, session);
        return result;
    }

    private static Result Restore(AppSettings settings, DocumentSession session)
    {
        if (KeyOf(session) is not { } key) return Result.None;
        var state = settings.PerFileStates.Find(s => s.PathKey == key);
        if (state is null || state.Bookmarks.Count == 0) return Result.None;
        try
        {
            var info = new FileInfo(key);
            bool same = info.Length == state.Length && info.LastWriteTimeUtc.Ticks == state.MtimeTicks;
            bool appended = !same && info.Length >= state.Length
                         && Hash(key, 0, Math.Min(ProbeBytes, state.Length)) == state.HeadHash
                         && Hash(key, Math.Max(0, state.Length - ProbeBytes), state.Length) == state.TailHash;
            if (!same && !appended)
            {
                settings.PerFileStates.Remove(state);
                settings.Save();
                return Result.Changed;
            }
            session.SetBookmarks(state.Bookmarks.Where(o => o >= 0 && o < info.Length));
            return Result.Restored;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Result.None;
        }
    }

    /// <summary>今のブックマークを覚える（無くなったら記録ごと消す）。</summary>
    public static void Remember(AppSettings settings, DocumentSession session)
    {
        if (KeyOf(session) is not { } key) return;
        settings.PerFileStates.RemoveAll(s => s.PathKey == key);
        if (session.Bookmarks.Count > 0)
        {
            try
            {
                var info = new FileInfo(key);
                long length = info.Length;
                settings.UpsertPerFileState(new PerFileState
                {
                    PathKey = key,
                    Length = length,
                    MtimeTicks = info.LastWriteTimeUtc.Ticks,
                    Bookmarks = [.. session.Bookmarks],
                    HeadHash = Hash(key, 0, Math.Min(ProbeBytes, length)),
                    TailHash = Hash(key, Math.Max(0, length - ProbeBytes), length),
                });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return;
            }
        }
        settings.Save();
    }

    private static string? KeyOf(DocumentSession session)
        => string.IsNullOrEmpty(session.FilePath) || !File.Exists(session.FilePath)
            ? null
            : Path.GetFullPath(session.FilePath);

    private static string Hash(string path, long from, long to)
    {
        var buf = new byte[Math.Max(0, to - from)];
        using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            f.Position = from;
            f.ReadExactly(buf);
        }
        return Convert.ToHexString(SHA256.HashData(buf));
    }

    /// <summary>
    /// ブックマークの一覧を書き出す（1行に「行番号<TAB>本文」）。行番号には行索引が要るので、索引ができる前は書かない。
    /// </summary>
    /// <returns>書いた件数（索引がまだなら null）。</returns>
    public static async Task<int?> ExportAsync(DocumentSession session, Stream output, CancellationToken ct = default)
    {
        var doc = session.Document;
        if (!doc.IsIndexed) return null;
        var marks = session.Bookmarks.ToArray();
        await using var writer = new StreamWriter(output, new UTF8Encoding(false), bufferSize: 1 << 16);
        foreach (long offset in marks)
        {
            ct.ThrowIfCancellationRequested();
            long line = doc.OffsetToLineIndex(offset) + 1;
            string text = await doc.GetLineAtOffsetAsync(offset, ct) ?? "";
            await writer.WriteLineAsync($"{line}\t{text}");
        }
        return marks.Length;
    }

    /// <summary>書き出すときの名前（<c>名前-bookmarks.txt</c>）。</summary>
    public static string ExportFileName(DocumentSession session)
        => Path.GetFileNameWithoutExtension(string.IsNullOrEmpty(session.FilePath) ? session.DisplayName : session.FilePath)
           + "-bookmarks.txt";
}
