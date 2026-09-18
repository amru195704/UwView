using System.Reflection;
using Avalonia.Headless.XUnit;
using UwView.Core;
using UwView.ViewModels;
using UwView.Views;

namespace UwView.UiTests;

/// <summary>
/// ソースレビュー・再レビュー（2026-09-19）の再発防止テスト（画面側）。
/// </summary>
public class SourceReview0919UiTests
{
    /// <summary>
    /// 指摘D: 選択行の保存・コピーも、本文の取得を待ってから書く。
    ///
    /// 保存はファイル選択画面を通るので、両方が実際に使う整形処理を直接呼んで確かめる。
    /// </summary>
    [AvaloniaFact]
    public async Task 選択行の整形は未取得の本文を待つ()
    {
        await using var session = await DocumentSession.CreateAsync("browser", new AsyncOnlySource());
        session.AdoptSearchResults(new SearchOptions("alpha"), [0], false, [0]);
        using var vm = new FilterResultsViewModel(_ => { }, maxContext: 0);
        vm.SetSession(session);
        vm.IncludeLineNumbersOnSave = false;

        var view = new FilterResultsView(vm);
        var row = vm.Rows.First(r => r.IsHit);

        var format = typeof(FilterResultsView).GetMethod("FormatRowAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new InvalidOperationException("FormatRowAsync が見つかりません（選択行の保存・コピーが使う整形処理）");
        object? result = format.Invoke(view, [row, CancellationToken.None]);
        string? actual = await (Task<string?>)result!;

        Assert.Equal("alpha", actual);
    }

    /// <summary>同期 Read が常に未取得を返す読み取り元（ブラウザー版の契約を模す）。</summary>
    private sealed class AsyncOnlySource : IByteSource
    {
        private readonly byte[] _data = "alpha\n"u8.ToArray();
        public long Length => _data.Length;
        public int Read(long offset, Span<byte> buffer) => 0;
        public ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken ct = default)
        {
            int n = (int)Math.Min(buffer.Length, Math.Max(0, _data.Length - offset));
            _data.AsSpan((int)offset, n).CopyTo(buffer.Span);
            return new(n);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
