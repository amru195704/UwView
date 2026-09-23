using System.Buffers.Binary;

namespace UwView.Core;

/// <summary>
/// OSM の pbf（<c>.osm.pbf</c>）かどうかの見分け。
///
/// 無料版は pbf を<b>読まない</b>（UwView Pro の役目）。読まないからこそ、
/// 黙ってテキストとして走査して「1件も無い」と答えないよう、ここで見分けて断る。
/// 名前ではなく<b>中身</b>で見る（<c>.pbf</c> は protobuf 一般の名前でもある）。
/// </summary>
public static class OsmPbfFile
{
    /// <summary>先頭が <c>[長さ4バイト][BlobHeader("OSMHeader")]</c> なら pbf。</summary>
    public static bool Is(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            Span<byte> head = stackalloc byte[64];
            int got = file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            if (got < 16) return false;

            int headerLength = BinaryPrimitives.ReadInt32BigEndian(head);
            if (headerLength is < 9 or > 64) return false;      // BlobHeader は小さい
            return head[4..got].IndexOf("OSMHeader"u8) >= 0;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
