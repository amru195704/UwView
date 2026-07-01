namespace UwView.Core;

/// <summary>
/// 実バイトのランダム読みを OS/環境非依存の最小 API で提供する I/O 抽象。
/// Desktop は mmap、Browser は Blob.slice で実装を差し替える。
/// 上位層（索引・ドキュメント・UI）はこの実装を一切知らない。
/// </summary>
public interface IByteSource : IAsyncDisposable
{
    long Length { get; }

    /// <summary>
    /// offset から buffer.Length バイト読み、実際に読めたバイト数を返す（末尾で不足可）。
    /// offset が Length 以上、または buffer が空なら 0 を返す。
    /// </summary>
    int Read(long offset, Span<byte> buffer);
}
