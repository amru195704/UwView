using System.Text;

namespace UwView.Core.Cli;

/// <summary>受け渡しの1ファイル（番号は並びの位置＋1。CLI の <c>--files</c> と同じ）。</summary>
/// <param name="Path">絶対パス。</param>
/// <param name="Display">表示用（利用者が書いた形のまま。相対なら相対）。</param>
/// <param name="Length">CLI が見たときの大きさ。</param>
/// <param name="MtimeTicks">CLI が見たときの更新日時（UTC の ticks）。開くときに変わっていないかを見る。</param>
/// <param name="Kind">圧縮の種類。</param>
/// <param name="CanOpen">画面で開けるか（zip・pbf・読めない圧縮は false。番号だけ持って、探さない）。</param>
public sealed record MultiHandoffFile(
    string Path, string Display, long Length, long MtimeTicks, CompressedKind Kind, bool CanOpen);

/// <summary>受け渡しの1ヒット。</summary>
/// <param name="File">ファイルの並びの位置（0 始まり）。</param>
/// <param name="Line">行番号（0 始まり。圧縮ファイルは展開後の行）。</param>
/// <param name="Offset">行頭のバイト位置（展開後）。これで索引を待たずにその行へ移れる。</param>
/// <param name="Text">本文（長い行は先頭だけ）。</param>
public sealed record MultiHandoffHit(int File, long Line, long Offset, string Text);

/// <summary>
/// <c>uvf -open '*.log' 語</c> のとき、<b>複数ファイルの結果</b>を画面へ渡す小さなファイル
/// （実装指示書_uvf複数ファイルGUI表示とタブ §4.2）。1本のときの <see cref="CliHandoff"/> を広げたもの。
///
/// 画面はファイルを探し直さない。結果の窓は本文をここから出し、ダブルクリックで初めてそのファイルを開く。
/// </summary>
/// <param name="Spec">(A) の原文（窓の題名に出す）。</param>
/// <param name="Pattern">検索語（検索なしの <c>uvf -open '*.log'</c> は null）。</param>
/// <param name="Truncated">件数の上限で止めたか。</param>
public sealed record MultiHandoff(
    string Spec, string? Pattern, bool IgnoreCase, bool Regex, bool Invert, bool Truncated,
    IReadOnlyList<MultiHandoffFile> Files, IReadOnlyList<MultiHandoffHit> Hits)
{
    /// <summary>画面へ渡すときの引数名。</summary>
    public const string Argument = "--uvf-multi";

    /// <summary>1行の本文として持つ長さの上限（文字数）。一覧で読むには足り、受け渡しを膨らませない。</summary>
    public const int MaxTextLength = 1000;

    private const uint Magic = 0x4D465655;   // "UVFM"
    private const int Version = 1;

    /// <summary>検索したか（検索語なしで開いたときは false＝結果の窓を出さない）。</summary>
    public bool HasSearch => Pattern is not null;

    /// <summary>ファイルごとの当たり件数（ファイル一覧の「当たり」の列）。</summary>
    public long[] HitsPerFile()
    {
        var counts = new long[Files.Count];
        foreach (var hit in Hits)
            if (hit.File >= 0 && hit.File < counts.Length) counts[hit.File]++;
        return counts;
    }

    /// <summary>一時ファイルへ書いて、その場所を返す（書けなければ null）。</summary>
    public string? WriteTemp()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"uvf-multi-{Guid.NewGuid():N}.uvfm");
        try
        {
            using var w = new BinaryWriter(File.Create(path), new UTF8Encoding(false));
            w.Write(Magic);
            w.Write(Version);
            w.Write(Spec);
            w.Write(Pattern is not null);
            w.Write(Pattern ?? "");
            w.Write(IgnoreCase);
            w.Write(Regex);
            w.Write(Invert);
            w.Write(Truncated);
            w.Write(Files.Count);
            foreach (var f in Files)
            {
                w.Write(f.Path);
                w.Write(f.Display);
                w.Write(f.Length);
                w.Write(f.MtimeTicks);
                w.Write((int)f.Kind);
                w.Write(f.CanOpen);
            }
            w.Write(Hits.Count);
            foreach (var h in Hits)
            {
                w.Write(h.File);
                w.Write(h.Line);
                w.Write(h.Offset);
                w.Write(h.Text);
            }
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(path); } catch (IOException) { }
            return null;
        }
    }

    /// <summary>読んで<b>消す</b>（読めなければ null）。一度きりの受け渡し。</summary>
    public static MultiHandoff? TakeFrom(string path)
    {
        try
        {
            using var r = new BinaryReader(File.OpenRead(path), new UTF8Encoding(false));
            if (r.ReadUInt32() != Magic || r.ReadInt32() != Version) return null;
            string spec = r.ReadString();
            bool hasPattern = r.ReadBoolean();
            string pattern = r.ReadString();
            bool icase = r.ReadBoolean(), regex = r.ReadBoolean(), invert = r.ReadBoolean();
            bool truncated = r.ReadBoolean();
            int fileCount = r.ReadInt32();
            if (fileCount < 0) return null;
            var files = new MultiHandoffFile[fileCount];
            for (int i = 0; i < fileCount; i++)
                files[i] = new MultiHandoffFile(r.ReadString(), r.ReadString(), r.ReadInt64(), r.ReadInt64(),
                                                (CompressedKind)r.ReadInt32(), r.ReadBoolean());
            int hitCount = r.ReadInt32();
            if (hitCount < 0) return null;
            var hits = new MultiHandoffHit[hitCount];
            for (int i = 0; i < hitCount; i++)
                hits[i] = new MultiHandoffHit(r.ReadInt32(), r.ReadInt64(), r.ReadInt64(), r.ReadString());
            return new MultiHandoff(spec, hasPattern ? pattern : null, icase, regex, invert, truncated, files, hits);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or EndOfStreamException
                                       or ArgumentException)
        {
            return null;
        }
        finally
        {
            try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>画面の検索条件に直す（検索なしなら null）。</summary>
    public SearchOptions? ToOptions() => Pattern is null ? null : new(Pattern, Regex, IgnoreCase);

    /// <summary>ファイルが受け渡しのときから変わったか（大きさか更新日時が違う）。</summary>
    public static bool Changed(MultiHandoffFile file)
    {
        try
        {
            var info = new FileInfo(file.Path);
            return info.Length != file.Length || info.LastWriteTimeUtc.Ticks != file.MtimeTicks;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}
