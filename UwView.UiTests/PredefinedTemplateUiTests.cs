using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using UwView.Core;
using UwView.Services;

namespace UwView.UiTests;

/// <summary>
/// 定義済み★の AND・NOT・OR のひな形（v1.8.2 extFS E-1）。
/// 既定で入っていて、選ぶと検索欄に入り正規表現がオンになり、最初の語が選ばれる（探さない）。消せる。
/// </summary>
public class PredefinedTemplateUiTests
{
    private static IReadOnlyList<PredefinedFilter> Templates => PredefinedTemplates.For(japanese: true);

    [AvaloniaFact]
    public async Task ひな形が既定で入っていて選ぶと検索欄に入り最初の語が選ばれる()
    {
        var (w, v, vm) = UiHarness.OpenMainWindow();
        try
        {
            await UiHarness.Pump();
            Assert.Equal(Templates.Select(t => t.Name), vm.PredefinedFilters.Take(3).Select(f => f.Name));

            var combo = UiHarness.Find<ComboBox>(v, "PredefinedCombo");
            combo.SelectedItem = vm.PredefinedFilters[0];                 // AND
            await UiHarness.Pump();

            Assert.Equal("^(?=.*語1)(?=.*語2)", vm.SearchText);
            Assert.True(vm.SearchIsRegex);
            var box = UiHarness.Find<AutoCompleteBox>(v, "SearchBox").GetVisualDescendants().OfType<TextBox>().First();
            Assert.Equal("語1", box.SelectedText);
            Assert.Empty(vm.Tabs);                                         // 探しには行かない（開いていなくても入る）
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public async Task 行のバツで消すと消えて開き直しても戻らない()
    {
        var (w, v, vm) = UiHarness.OpenMainWindow();
        try
        {
            await UiHarness.Pump();
            var combo = UiHarness.Find<ComboBox>(v, "PredefinedCombo");
            combo.IsDropDownOpen = true;
            await UiHarness.Pump();
            var first = combo.GetLogicalDescendants().OfType<ComboBoxItem>().First();
            UiHarness.Click(first.GetVisualDescendants().OfType<Button>().Single());
            await UiHarness.Pump();

            Assert.DoesNotContain(vm.PredefinedFilters, f => f.Name == Templates[0].Name);
            Assert.DoesNotContain(UwView.App.Settings.PredefinedFilters, f => f.Name == Templates[0].Name);
            Assert.Null(combo.SelectedItem);                               // 消しただけで、選んだことにはならない
            Assert.True(UwView.App.Settings.PredefinedTemplatesAdded);
            Assert.False(PredefinedTemplates.AddOnce(UwView.App.Settings, japanese: true));   // 次に起動しても戻さない
        }
        finally { w.Close(); }
    }

    /// <summary>ひな形の語を置き換えた式で探すと、説明書の表の例のとおりになる。</summary>
    [Theory]
    [InlineData(0, new[] { "ERROR", "timeout" }, new[] { 1, 2 })]             // AND：両方を含む行だけ
    [InlineData(1, new[] { "dev2", "ERROR" }, new[] { 0, 1 })]               // NOT：ERROR を含み dev2 を含まない
    [InlineData(2, new[] { "ERROR", "WARN" }, new[] { 0, 1, 2, 3 })]         // OR：どちらか
    public async Task ひな形の語を置き換えて探すと意味どおりに当たる(int template, string[] words, int[] expectedLines)
    {
        string[] lines = ["ERROR disk", "ERROR timeout", "ERROR timeout dev2", "WARN slow", "INFO ok"];
        string pattern = Templates[template].Pattern;
        var placeholders = Templates[template].Placeholders!;
        for (int i = 0; i < placeholders.Count; i++) pattern = pattern.Replace(placeholders[i], words[i]);

        var data = Encoding.UTF8.GetBytes(string.Concat(lines.Select(l => l + "\n")));
        await using var src = new MemorySource(data);
        var hits = new List<long>();
        await SearchService.SearchAsync(src, 0, Encoding.UTF8, new SearchOptions(pattern, UseRegex: true), b => hits.AddRange(b));

        long[] starts = [.. lines.Select((_, i) => (long)lines.Take(i).Sum(l => l.Length + 1))];
        Assert.Equal(expectedLines.Select(i => starts[i]), hits.Order());
    }

    private sealed class MemorySource(byte[] data) : IByteSource
    {
        public long Length => data.Length;
        public int Read(long offset, Span<byte> buffer)
        {
            if (offset >= data.Length) return 0;
            int n = (int)Math.Min(buffer.Length, data.Length - offset);
            data.AsSpan((int)offset, n).CopyTo(buffer);
            return n;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
