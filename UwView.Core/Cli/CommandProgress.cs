using System.Diagnostics;

namespace UwView.Core.Cli;

/// <summary>
/// 「コマンドライン」ダイアログ（v1.8.0 Finder Scope §3.5）の進み具合。何本目／全体・読んだ量・残りの目安。
/// コマンドとして走るときは誰も見ていないので <see cref="Current"/> は null（数えるだけの手間も掛けない）。
/// 数えるのはファイルを読み終えたとき（1 本の中の細かい進みは見ない）。
/// </summary>
public sealed class CommandProgress
{
    private static readonly AsyncLocal<CommandProgress?> CurrentSlot = new();

    /// <summary>いま走っているコマンドの進み具合（ダイアログが入れる。無ければ null）。</summary>
    public static CommandProgress? Current
    {
        get => CurrentSlot.Value;
        set => CurrentSlot.Value = value;
    }

    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private int _filesTotal;
    private int _filesDone;
    private long _bytesTotal;
    private long _bytesDone;

    /// <summary>これから読む本数と大きさ（何度呼んでも足していく）。</summary>
    public void Plan(int files, long bytes)
    {
        Interlocked.Add(ref _filesTotal, files);
        Interlocked.Add(ref _bytesTotal, bytes);
    }

    /// <summary>1 本読み終えた。</summary>
    public void FileDone(long bytes)
    {
        Interlocked.Increment(ref _filesDone);
        Interlocked.Add(ref _bytesDone, bytes);
    }

    /// <summary>いまの値。</summary>
    public Snapshot Read() => new(Volatile.Read(ref _filesDone), Volatile.Read(ref _filesTotal),
                                  Interlocked.Read(ref _bytesDone), Interlocked.Read(ref _bytesTotal), _watch.Elapsed);

    public readonly record struct Snapshot(int FilesDone, int FilesTotal, long BytesDone, long BytesTotal, TimeSpan Elapsed)
    {
        /// <summary>残りの目安（読んだ量の速さから。まだ何も読み終えていなければ null）。</summary>
        public TimeSpan? Remaining => BytesDone > 0 && BytesTotal > BytesDone
            ? TimeSpan.FromSeconds(Elapsed.TotalSeconds * (BytesTotal - BytesDone) / BytesDone)
            : null;
    }

    /// <summary>ファイルの大きさ（無ければ 0）。</summary>
    public static long SizeOf(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return 0; }
    }
}
