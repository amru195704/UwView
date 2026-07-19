using UwView.Core;
using Xunit;

namespace UwView.Core.Tests;

/// <summary>クイック着色の語切り出し・セル計算（V1.1.1 §5-1: 全角/タブ/行端/単語境界）。</summary>
public class TextCellsTests
{
    [Fact]
    public void CellOffset_HalfFullTab()
    {
        Assert.Equal(3, TextCells.CellOffset("abc", 3));
        Assert.Equal(6, TextCells.CellOffset("東京駅", 3));   // 全角×3 = 6セル
        Assert.Equal(4, TextCells.CellOffset("a東b", 3));     // 1+2+1
        Assert.Equal(8, TextCells.CellOffset("ab\t", 3));      // タブで 2→8
    }

    [Fact]
    public void CharIndexAtCell_MapsBack()
    {
        Assert.Equal(0, TextCells.CharIndexAtCell("abc", 0));
        Assert.Equal(2, TextCells.CharIndexAtCell("abc", 2));
        Assert.Equal(1, TextCells.CharIndexAtCell("東京", 2)); // セル2 = 2文字目「京」
        Assert.Equal(3, TextCells.CharIndexAtCell("abc", 99));  // 範囲外 → 行末
    }

    [Fact]
    public void WordAt_NonWhitespaceRun()
    {
        var w = TextCells.WordAt("2026 ERROR boom", 6); // "ERROR" 内
        Assert.Equal((5, 10), w);
    }

    [Fact]
    public void WordAt_LogTokenIncludesPunctuation()
    {
        // ログトークンは非空白の塊で取る（req-8f2a / IP 等をまとめて着色できる）
        Assert.Equal((0, 8), TextCells.WordAt("req-8f2a done", 3));
        Assert.Equal((0, 11), TextCells.WordAt("192.168.1.1 x", 4));
    }

    [Fact]
    public void WordAt_WhitespaceAndBounds_ReturnNull()
    {
        Assert.Null(TextCells.WordAt("a b", 1));    // 空白の位置
        Assert.Null(TextCells.WordAt("abc", 3));    // 行端（範囲外）
        Assert.Null(TextCells.WordAt("", 0));       // 空行
        Assert.Null(TextCells.WordAt("abc", -1));
    }

    [Fact]
    public void WordAt_FullWidth()
    {
        // 全角語も非空白として1語で取れる
        Assert.Equal((0, 3), TextCells.WordAt("東京都 x", 1));
    }

    [Fact]
    public void WordAtNarrow_ShortestVsToken()
    {
        // 最短語＝英数字の塊（区切り記号で止まる）
        Assert.Equal((0, 3), TextCells.WordAtNarrow("req-8f2a done", 1)); // "req"
        Assert.Equal((0, 8), TextCells.WordAt("req-8f2a done", 1));       // "req-8f2a"
        // 区切り記号上をクリックすると区切りの塊
        Assert.Equal((3, 4), TextCells.WordAtNarrow("req-8f2a", 3));       // "-"
        // アンダースコアは語の一部（"trace_id" = [0,8)）
        Assert.Equal((0, 8), TextCells.WordAtNarrow("trace_id=1", 2));
    }
}
