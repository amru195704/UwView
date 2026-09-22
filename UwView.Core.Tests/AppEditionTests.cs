using UwView.Core;

namespace UwView.Core.Tests;

/// <summary>
/// 試験用ビルドの呼び名と版数の見せ方（オーナー指示 2026-09-22）。
/// 手元に何本も置くので、<b>題名を見ただけでどれか分かる</b>ことが要る。
/// </summary>
public class AppEditionTests : IDisposable
{
    private readonly string _name = AppEdition.Name;
    private readonly string _version = AppEdition.Version;

    public void Dispose()
    {
        AppEdition.Name = _name;
        AppEdition.Version = _version;
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void 本番ビルドでは何も足さない()
    {
        AppEdition.Name = "";
        AppEdition.Version = "1.7.0.1";

        Assert.False(AppEdition.IsTestBuild);
        Assert.Equal("UwView(uvf)", AppEdition.TitleFor("UwView(uvf)"));
        Assert.Equal("UwView", AppEdition.Decorate("UwView"));
    }

    [Fact]
    public void 試験用ビルドは題名に呼び名と版数を出す()
    {
        AppEdition.Name = "Wide Field";
        AppEdition.Version = "1.7.0.1";

        Assert.True(AppEdition.IsTestBuild);
        Assert.Equal("UwView(uvf) - Wide Field(v1.7.0.1)", AppEdition.TitleFor("UwView(uvf)"));
        Assert.Equal("UwView Pro(uvp) - Wide Field(v1.7.0.1)", AppEdition.TitleFor("UwView Pro(uvp)"));
        Assert.Equal("UwView (Wide Field)", AppEdition.Decorate("UwView"));
    }

    [Fact]
    public void 版数が分からなければ呼び名だけ出す()
    {
        AppEdition.Name = "Wide Field";
        AppEdition.Version = "";

        Assert.Equal("UwView(uvf) - Wide Field", AppEdition.TitleFor("UwView(uvf)"));
    }
}
