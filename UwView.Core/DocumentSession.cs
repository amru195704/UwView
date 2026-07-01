using System.Text;

namespace UwView.Core;

public enum ViewMode { Page, Line }

/// <summary>
/// 1 つの開いたファイル = 1 つのセッション（§4.7）。
/// I/O ソース・文字コード・索引・ドキュメント・表示状態を 1 つに束ね、
/// タブUI はこのセッションを差し替えて表示する（状態保持で即時切替・索引再構築なし）。
/// </summary>
public sealed class DocumentSession : IAsyncDisposable
{
    public string FilePath { get; }
    public string DisplayName { get; }
    public IByteSource Source { get; }
    public DetectedEncoding DetectedEncoding { get; }
    public NewlineStyle Newline { get; }
    public LineDocument Document { get; }

    // 表示状態（タブ切替で保持）
    public ViewMode Mode { get; set; } = ViewMode.Page;
    public long TopByteOffset { get; set; }
    public long TopLine { get; set; }

    // 索引状態
    public SparseLineIndex? Index => Document.Index;
    public bool IsIndexed => Document.IsIndexed;
    public double IndexProgress { get; private set; }
    public bool IsIndexing { get; private set; }

    /// <summary>索引進捗が動いたとき（UI スレッドで発火）。</summary>
    public event EventHandler? IndexProgressChanged;
    /// <summary>索引完了＝行モード昇格したとき。</summary>
    public event EventHandler? IndexCompleted;

    private CancellationTokenSource? _cts;
    private bool _disposed;

    private DocumentSession(string path, IByteSource source, DetectedEncoding enc,
        NewlineStyle newline, LineDocument doc)
    {
        FilePath = path;
        DisplayName = Path.GetFileName(path);
        Source = source;
        DetectedEncoding = enc;
        Newline = newline;
        Document = doc;
        TopByteOffset = enc.BomLength;
    }

    /// <summary>ファイルを開いて文字コード判定まで行う（索引は待たない）。</summary>
    public static DocumentSession Open(string path)
    {
        var src = new MmapByteSource(path);
        var detected = EncodingDetector.Detect(src);
        var newline = EncodingDetector.DetectNewline(src);
        var doc = new LineDocument(src, detected, newline);
        return new DocumentSession(path, src, detected, newline, doc);
    }

    /// <summary>背景で索引を構築し、完了で行モードへ昇格（§3.1-2/3）。</summary>
    public async Task BuildIndexAsync()
    {
        if (IsIndexed || IsIndexing) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsIndexing = true;
        IndexProgress = 0;
        RaiseProgress();

        var progress = new Progress<double>(p => { IndexProgress = p; RaiseProgress(); });
        try
        {
            await Document.BuildIndexAsync(progress, ct);
            if (!ct.IsCancellationRequested)
            {
                TopLine = Document.OffsetToLineIndex(TopByteOffset); // 位置継続
                Mode = ViewMode.Line;
                IndexCompleted?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException) { /* ページモードのまま継続 */ }
        finally { IsIndexing = false; RaiseProgress(); }
    }

    public void CancelIndex() => _cts?.Cancel();

    /// <summary>手動の文字コード切替（索引再構築なし §4.6）。</summary>
    public void SetEncoding(Encoding encoding) => Document.Encoding = encoding;

    private void RaiseProgress() => IndexProgressChanged?.Invoke(this, EventArgs.Empty);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        await Document.DisposeAsync(); // Source も解放（mmap アンマップ＝ファイルロック解除）
    }
}
