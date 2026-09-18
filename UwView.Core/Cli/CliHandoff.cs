using System.Text;

namespace UwView.Core.Cli;

/// <summary>
/// <c>uvf ファイル 語 -open</c> のとき、<b>CLI が見つけた結果をそのまま画面へ渡す</b>ための小さなファイル
/// （オーナー指示 2026-09-18。UwView Pro が先に同じ仕組みを入れている）。
///
/// これまでは CLI が起動を頼むだけで、画面側が同じ検索をもう一度やり直していた。
/// 50GB なら丸ごと1回読み直すことになり、待ち時間がそのまま倍になる。
/// CLI はどのみち全部読んでいるので、見つけた行の位置を書き出して渡せば、画面は読み直さずに済む。
///
/// 中身はヒット行の<b>行頭バイト位置</b>だけ（行番号も本文も、画面が索引から作れる）。
/// 渡した先で<b>ファイルが変わっていないか</b>を長さで確かめ、違っていれば捨てて普通に検索し直す。
/// </summary>
/// <param name="Pattern">検索語（画面の検索欄に入れて、強調表示にも使う）。</param>
/// <param name="SourceLength">CLI が見たときのファイルの長さ（変わっていたら使わない）。</param>
/// <param name="Truncated">上限で打ち切ったか（画面にもその旨を出す）。</param>
/// <param name="Hits">ヒット行の行頭バイト位置（昇順）。</param>
/// <param name="Lines">同じ並びの行番号（0 始まり）。これがあると<b>画面は索引の完成を待たずに</b>
/// 結果一覧を出せる（本文は行頭位置から直接読める。オーナー指摘 2026-09-18「索引待ちが余計」）。</param>
/// <param name="BlockLines">索引の目印の間隔（0＝索引を渡していない）。</param>
/// <param name="IndexMarks">その間隔ごとの行頭位置。画面はこれで索引を組み立て、<b>ファイルを読み直さない</b>。</param>
public sealed record CliHandoff(
    string Pattern, bool IgnoreCase, bool Regex, bool Invert, long SourceLength, bool Truncated,
    long[] Hits, long[] Lines,
    int BlockLines = 0, long[]? IndexMarks = null, long NewlineCount = 0, byte LastByte = 0)
{
    /// <summary>画面へ渡すときの引数名。</summary>
    public const string Argument = "--uvf-hits";

    private const uint Magic = 0x48465655;   // "UVFH"
    private const int Version = 2;   // v2: 行番号を追加（2026-09-18）

    /// <summary>一時ファイルへ書いて、その場所を返す（書けなければ null＝画面が検索し直す）。</summary>
    public string? WriteTemp()
    {
        string path = Path.Combine(Path.GetTempPath(), $"uvf-open-{Guid.NewGuid():N}.uvfh");
        try
        {
            using var w = new BinaryWriter(File.Create(path), new UTF8Encoding(false));
            w.Write(Magic);
            w.Write(Version);
            w.Write(Pattern);
            w.Write(IgnoreCase);
            w.Write(Regex);
            w.Write(Invert);
            w.Write(SourceLength);
            w.Write(Truncated);
            w.Write(Hits.Length);
            foreach (long offset in Hits) w.Write(offset);
            foreach (long line in Lines) w.Write(line);

            w.Write(BlockLines);
            w.Write(IndexMarks?.Length ?? 0);
            foreach (long at in IndexMarks ?? []) w.Write(at);
            w.Write(NewlineCount);
            w.Write(LastByte);
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(path); } catch (IOException) { }
            return null;
        }
    }

    /// <summary>読んで<b>消す</b>（受け取れなければ null＝画面が普通に検索する）。一度きりの受け渡し。</summary>
    public static CliHandoff? TakeFrom(string path)
    {
        try
        {
            using (var r = new BinaryReader(File.OpenRead(path), new UTF8Encoding(false)))
            {
                if (r.ReadUInt32() != Magic || r.ReadInt32() != Version) return null;
                string pattern = r.ReadString();
                bool icase = r.ReadBoolean(), regex = r.ReadBoolean(), invert = r.ReadBoolean();
                long length = r.ReadInt64();
                bool truncated = r.ReadBoolean();
                int count = r.ReadInt32();
                if (count < 0) return null;
                var hits = new long[count];
                for (int i = 0; i < count; i++) hits[i] = r.ReadInt64();
                var lines = new long[count];
                for (int i = 0; i < count; i++) lines[i] = r.ReadInt64();

                int blockLines = r.ReadInt32();
                int markCount = r.ReadInt32();
                if (markCount < 0) return null;
                var marks = new long[markCount];
                for (int i = 0; i < markCount; i++) marks[i] = r.ReadInt64();
                long newlines = r.ReadInt64();
                byte lastByte = r.ReadByte();
                return new CliHandoff(pattern, icase, regex, invert, length, truncated, hits, lines,
                                      blockLines, marks, newlines, lastByte);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or EndOfStreamException
                                       or ObjectDisposedException or ArgumentException)
        {
            return null;
        }
        finally
        {
            try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>渡された結果が、いま開いたファイルのものか（途中で書き換わっていたら使わない）。</summary>
    public bool Matches(long sourceLength) => SourceLength == sourceLength;

    /// <summary>画面の検索条件に直す。</summary>
    public SearchOptions ToOptions() => new(Pattern, Regex, IgnoreCase);

    /// <summary>渡された索引を組み立てる（渡っていなければ null＝画面が自分で作る）。</summary>
    public SparseLineIndex? BuildIndex(int bomLength, NewlineStyle newline)
        => BlockLines > 0 && IndexMarks is not null
            ? SparseLineIndex.FromCheckpoints(bomLength, newline, BlockLines, IndexMarks,
                                              NewlineCount, LastByte, SourceLength)
            : null;
}
