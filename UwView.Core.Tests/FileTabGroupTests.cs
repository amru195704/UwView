namespace UwView.Core.Tests;

/// <summary>ファイル一覧・検索結果から開くタブの組（実装指示書_uvf複数ファイルGUI表示とタブ §5.3〜§5.5）。</summary>
public class FileTabGroupTests
{
    private sealed class Tab(string name)
    {
        public override string ToString() => name;
    }

    [Fact]
    public void 追加は7個までで8個目は断る()
    {
        var group = new FileTabGroup<Tab>(new Tab("main")) { MainFile = 0 };
        for (int file = 1; file <= 7; file++)
        {
            Assert.Equal(FileOpenAction.AddTab, group.Decide(file, inTab: true));
            Assert.True(group.TryAdd(file, new Tab($"t{file}")));
        }
        Assert.Equal(FileTabGroup<Tab>.MaxTabs, group.Count);
        Assert.Equal(FileOpenAction.RefuseFull, group.Decide(8, inTab: true));
        Assert.False(group.TryAdd(8, new Tab("t8")));
        // 上限でもメインで開くのは断らない
        Assert.Equal(FileOpenAction.OpenInMain, group.Decide(8, inTab: false));
    }

    [Fact]
    public void 同じファイルは開いている場所に切り替えるだけ()
    {
        var group = new FileTabGroup<Tab>(new Tab("main")) { MainFile = 2 };
        var tab = new Tab("t5");
        group.TryAdd(5, tab);

        Assert.Equal(FileOpenAction.ShowMain, group.Decide(2, inTab: true));
        Assert.Equal(FileOpenAction.ShowTab, group.Decide(5, inTab: false));
        Assert.Same(tab, group.TabOf(5));
        Assert.False(group.TryAdd(5, new Tab("dup")));
        Assert.False(group.TryAdd(2, new Tab("dup-main")));
    }

    [Fact]
    public void 閉じたタブは数から外れる()
    {
        var group = new FileTabGroup<Tab>(new Tab("main"));
        var tab = new Tab("t1");
        group.TryAdd(1, tab);
        Assert.True(group.HasExtraTabs);
        Assert.True(group.Contains(tab));

        Assert.True(group.Remove(tab));
        Assert.False(group.HasExtraTabs);
        Assert.Equal(1, group.Count);
        Assert.Null(group.Placement(1));
        Assert.False(group.Remove(tab));
    }

    [Fact]
    public void 束ねた本文のメインはどのファイルにも印を付けない()
    {
        var group = new FileTabGroup<Tab>(new Tab("merged"));   // MainFile = -1
        Assert.Null(group.Placement(0));
        Assert.Equal(FileOpenAction.OpenInMain, group.Decide(0, inTab: false));
    }
}
