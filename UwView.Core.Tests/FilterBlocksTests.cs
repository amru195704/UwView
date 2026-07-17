using UwView.Core;
using Xunit;

namespace UwView.Core.Tests;

/// <summary>FilterBlocks.Build（文脈ブロック生成・重複マージ）のテスト（指示書 §5-2/3）。</summary>
public class FilterBlocksTests
{
    private static long[][] Ranges(List<FilterBlock> blocks)
        => blocks.Select(b => new[] { b.StartLine, b.EndLine }).ToArray();

    [Fact]
    public void N0_HitsOnly_OneBlockPerIsolatedHit()
    {
        var blocks = FilterBlocks.Build([10, 20, 30], contextN: 0, totalLines: 100);
        Assert.Equal([[10, 10], [20, 20], [30, 30]], Ranges(blocks));
        Assert.All(blocks, b => Assert.Single(b.HitLines));
    }

    [Fact]
    public void N0_AdjacentHits_MergeIntoOneBlock()
    {
        var blocks = FilterBlocks.Build([10, 11, 12, 20], contextN: 0, totalLines: 100);
        Assert.Equal([[10, 12], [20, 20]], Ranges(blocks));
        Assert.Equal([10L, 11, 12], blocks[0].HitLines);
    }

    [Fact]
    public void Context_OverlappingRanges_Merge()
    {
        // 指示書の例: L1=100, L2=103, N=2 → 98–105 を1ブロック
        var blocks = FilterBlocks.Build([100, 103], contextN: 2, totalLines: 1000);
        Assert.Equal([[98, 105]], Ranges(blocks));
        Assert.Equal([100L, 103], blocks[0].HitLines);
    }

    [Fact]
    public void Context_AdjacentRanges_Merge()
    {
        // [8,12] と [13,17] は隣接（gap無し）→ 結合
        var blocks = FilterBlocks.Build([10, 15], contextN: 2, totalLines: 1000);
        Assert.Equal([[8, 17]], Ranges(blocks));
    }

    [Fact]
    public void Context_SeparatedRanges_StaySplit()
    {
        // [8,12] と [18,22] は gap があるので別ブロック
        var blocks = FilterBlocks.Build([10, 20], contextN: 2, totalLines: 1000);
        Assert.Equal([[8, 12], [18, 22]], Ranges(blocks));
    }

    [Fact]
    public void Context_ClampsAtFileEdges()
    {
        var blocks = FilterBlocks.Build([1, 98], contextN: 5, totalLines: 100);
        Assert.Equal([[0, 6], [93, 99]], Ranges(blocks));
    }

    [Fact]
    public void DuplicateHitLines_CollapseToOne()
    {
        var blocks = FilterBlocks.Build([10, 10, 10, 12], contextN: 0, totalLines: 100);
        Assert.Equal([[10, 10], [12, 12]], Ranges(blocks));
        Assert.Equal([10L], blocks[0].HitLines);
    }

    [Fact]
    public void EmptyHits_ReturnsEmpty()
    {
        Assert.Empty(FilterBlocks.Build([], 3, 100));
        Assert.Empty(FilterBlocks.Build([5], 3, 0));
    }

    [Fact]
    public void LineCount_IsInclusive()
    {
        var blocks = FilterBlocks.Build([5], contextN: 2, totalLines: 100);
        Assert.Equal(5, blocks[0].LineCount); // 3..7
    }
}
