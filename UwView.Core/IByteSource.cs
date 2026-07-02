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
    /// Browser(Blob) 実装ではキャッシュ済み範囲のみ返す（未取得部分は 0 で打ち切り）。
    /// </summary>
    int Read(long offset, Span<byte> buffer);

    /// <summary>
    /// 非同期読み（WASM 用の async 経路）。索引構築・文字コード判定など
    /// 非同期にできる処理はこちらを使う。既定実装は同期 Read の薄いラッパ（Desktop）。
    /// </summary>
    ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken ct = default)
        => new(Read(offset, buffer.Span));
}

/// <summary>
/// 非同期 I/O 実装（Blob 等）が「裏で取得したデータがキャッシュに載った」ことを
/// UI へ知らせるための任意インターフェース。描画側は受信したら再描画すればよい。
/// </summary>
public interface INotifyDataArrived
{
    event EventHandler? DataArrived;
}
