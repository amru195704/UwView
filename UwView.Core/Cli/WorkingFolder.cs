namespace UwView.Core.Cli;

/// <summary>
/// 「基準フォルダーに <c>cd</c> してコマンドを打った」のと同じにするため、作業フォルダーを一時的に切り替えて走らせる
///（v1.8.0 Finder Scope §7.1）。コマンドの部品は、相対パスをプロセスの作業フォルダーから解く
///（<see cref="FileSet"/>・除外の規則・uvp の索引の名前。uvp は <c>PWD</c> も見る）。
///
/// 作業フォルダーはプロセスに 1 つなので、同時に走らせるのは 1 つだけにする（ほかは順に待つ）。
/// </summary>
public static class WorkingFolder
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary><paramref name="folder"/> を作業フォルダーにして <paramref name="body"/> を走らせ、終わったら元に戻す。</summary>
    public static async Task<T> RunAsync<T>(string folder, Func<Task<T>> body, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        string previous = Directory.GetCurrentDirectory();
        string? previousPwd = Environment.GetEnvironmentVariable("PWD");
        try
        {
            Directory.SetCurrentDirectory(folder);
            Environment.SetEnvironmentVariable("PWD", folder);
            return await body().ConfigureAwait(false);
        }
        finally
        {
            try { Directory.SetCurrentDirectory(previous); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            Environment.SetEnvironmentVariable("PWD", previousPwd);
            Gate.Release();
        }
    }

    /// <summary>同期の処理を同じ決まりで走らせる（ファイルの展開など）。</summary>
    public static Task<T> Run<T>(string folder, Func<T> body, CancellationToken ct = default)
        => RunAsync(folder, () => Task.FromResult(body()), ct);
}
