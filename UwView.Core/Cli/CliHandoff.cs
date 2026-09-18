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
public sealed record CliHandoff(
    string Pattern, bool IgnoreCase, bool Regex, bool Invert, long SourceLength, bool Truncated, long[] Hits)
{
    /// <summary>画面へ渡すときの引数名。</summary>
    public const string Argument = "--uvf-hits";

    private const uint Magic = 0x48465655;   // "UVFH"
    private const int Version = 1;

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
                return new CliHandoff(pattern, icase, regex, invert, length, truncated, hits);
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
}
