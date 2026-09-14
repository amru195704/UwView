using UwView.Core.Cli;

namespace UwView.Core.Tests;

/// <summary>
/// uvf / uvp をコンソールで名前だけ打って使えるようにする（オーナー指示 2026-09-14）。
/// 配布はインストーラではないので、アプリ自身が PATH（Windows）かリンク（macOS / Linux）を登録する。
/// 本物の /usr/local/bin やレジストリには触らず、一時フォルダと文字列で確かめる。
/// </summary>
public class CliCommandSetupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "uvf-pathsetup-" + Guid.NewGuid().ToString("N"));

    public CliCommandSetupTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string MakeLauncher(string folder, string tool = "uvf")
    {
        string dir = Path.Combine(_dir, folder);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, OperatingSystem.IsWindows() ? tool + ".exe" : tool);
        File.WriteAllText(path, "launcher");
        File.WriteAllText(Path.Combine(dir, OperatingSystem.IsWindows() ? "UwView.Desktop.exe" : "UwView.Desktop"), "gui");
        return path;
    }

    [Fact]
    public void 本体の隣の起動アプリを見つける()
    {
        string launcher = MakeLauncher("app");
        string gui = Path.Combine(Path.GetDirectoryName(launcher)!, "UwView.Desktop");
        Assert.Equal(launcher, CliCommandSetup.FindLauncher("uvf", gui));
        Assert.Null(CliCommandSetup.FindLauncher("uvp", gui));
    }

    // ── macOS / Linux: リンク ──

    [Fact]
    public void リンクを作ると登録済みになり_解除で消える()
    {
        if (OperatingSystem.IsWindows()) return;
        string launcher = MakeLauncher("app");
        string bin = Path.Combine(_dir, "bin");   // まだ無いフォルダ（作る）

        var before = CliCommandSetup.InspectLink("uvf", launcher, bin);
        Assert.Equal(CliCommandState.NotInstalled, before.State);
        Assert.Equal(Path.Combine(bin, "uvf"), before.Location);

        Assert.True(CliCommandSetup.InstallLink(launcher, before.Location).Ok);
        Assert.Equal(launcher, new FileInfo(before.Location).LinkTarget);
        Assert.Equal(CliCommandState.Installed, CliCommandSetup.InspectLink("uvf", launcher, bin).State);

        Assert.True(CliCommandSetup.UninstallLink(before.Location).Ok);
        Assert.False(File.Exists(before.Location));
        Assert.Equal(CliCommandState.NotInstalled, CliCommandSetup.InspectLink("uvf", launcher, bin).State);
    }

    [Fact]
    public void 古い置き場所を指すリンクは別の場所として知らせ_置き換えられる()
    {
        if (OperatingSystem.IsWindows()) return;
        string oldLauncher = MakeLauncher("old");
        string launcher = MakeLauncher("new");
        string bin = Path.Combine(_dir, "bin");
        CliCommandSetup.InstallLink(oldLauncher, Path.Combine(bin, "uvf"));

        var status = CliCommandSetup.InspectLink("uvf", launcher, bin);
        Assert.Equal(CliCommandState.OtherTarget, status.State);
        Assert.Equal(oldLauncher, status.Existing);

        CliCommandSetup.InstallLink(launcher, status.Location);
        Assert.Equal(CliCommandState.Installed, CliCommandSetup.InspectLink("uvf", launcher, bin).State);
    }

    [Fact]
    public void 相対パスのリンクもたどって判定する()
    {
        if (OperatingSystem.IsWindows()) return;
        string launcher = MakeLauncher("app");
        string bin = Path.Combine(_dir, "bin");
        Directory.CreateDirectory(bin);
        File.CreateSymbolicLink(Path.Combine(bin, "uvf"), Path.Combine("..", "app", "uvf"));
        Assert.Equal(CliCommandState.Installed, CliCommandSetup.InspectLink("uvf", launcher, bin).State);
    }

    [Fact]
    public void 同じ名前の普通のファイルは解除で消さない()
    {
        if (OperatingSystem.IsWindows()) return;
        string launcher = MakeLauncher("app");
        string bin = Path.Combine(_dir, "bin");
        Directory.CreateDirectory(bin);
        string mine = Path.Combine(bin, "uvf");
        File.WriteAllText(mine, "#!/bin/sh\necho 利用者の自作\n");

        var status = CliCommandSetup.InspectLink("uvf", launcher, bin);
        Assert.Equal(CliCommandState.OtherTarget, status.State);
        Assert.False(CliCommandSetup.UninstallLink(mine).Ok);
        Assert.True(File.Exists(mine));
    }

    [Fact]
    public void Linuxはusr_local_binを正としlocal_binの登録も見る()
    {
        if (OperatingSystem.IsWindows()) return;
        string launcher = MakeLauncher("app");
        string system = Path.Combine(_dir, "usr-local-bin");
        string user = Path.Combine(_dir, "home-local-bin");

        // どちらにも無い → /usr/local/bin に置く予定
        var none = CliCommandSetup.InspectLinux("uvf", launcher, system, user);
        Assert.Equal(CliCommandState.NotInstalled, none.State);
        Assert.Equal(Path.Combine(system, "uvf"), none.Location);

        // 管理者になれず ~/.local/bin に置いた登録も、登録済みと見る（解除もそこを消す）
        CliCommandSetup.InstallLink(launcher, Path.Combine(user, "uvf"));
        var inUser = CliCommandSetup.InspectLinux("uvf", launcher, system, user);
        Assert.Equal(CliCommandState.Installed, inUser.State);
        Assert.Equal(Path.Combine(user, "uvf"), inUser.Location);

        // /usr/local/bin にもあれば、そちらを正とする
        CliCommandSetup.InstallLink(launcher, Path.Combine(system, "uvf"));
        Assert.Equal(Path.Combine(system, "uvf"), CliCommandSetup.InspectLinux("uvf", launcher, system, user).Location);
    }

    [Fact]
    public void Linuxでlocal_binに別の場所を指すリンクが残っていればそれを置き換える()
    {
        // Ubuntu は ~/.local/bin を PATH の先頭に足すので、そこの古いリンクが先に見つかってしまう
        if (OperatingSystem.IsWindows()) return;
        string oldLauncher = MakeLauncher("old");
        string launcher = MakeLauncher("new");
        string system = Path.Combine(_dir, "usr-local-bin");
        string user = Path.Combine(_dir, "home-local-bin");
        CliCommandSetup.InstallLink(oldLauncher, Path.Combine(user, "uvf"));
        CliCommandSetup.InstallLink(launcher, Path.Combine(system, "uvf"));

        var status = CliCommandSetup.InspectLinux("uvf", launcher, system, user);
        Assert.Equal(CliCommandState.OtherTarget, status.State);
        Assert.Equal(Path.Combine(user, "uvf"), status.Location);
        Assert.Equal(oldLauncher, status.Existing);

        // 利用者が自分で置いた普通のファイルは対象にしない
        File.Delete(Path.Combine(user, "uvf"));
        File.WriteAllText(Path.Combine(user, "uvf"), "#!/bin/sh\n");
        Assert.Equal(CliCommandState.Installed, CliCommandSetup.InspectLinux("uvf", launcher, system, user).State);
    }

    [Theory]
    [InlineData(0, true, false, false)]
    [InlineData(126, false, true, false)]    // パスワード画面を閉じた
    [InlineData(127, false, false, false)]   // 管理者になれなかった → ~/.local/bin へ
    [InlineData(1, false, false, true)]      // コマンド自体の失敗
    public void pkexecの終了コードを読み分ける(int exit, bool ok, bool cancelled, bool error)
    {
        var r = CliCommandSetup.FromPkexecExit(exit, "ln: failed");
        Assert.Equal((ok, cancelled, error), (r.Ok, r.Cancelled, r.Error is not null));
    }

    [Fact]
    public void 書ける場所のアプリは置き場所として使える()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.False(CliCommandSetup.IsNotInPlace(MakeLauncher("UwView.app/Contents/MacOS")));
        Assert.True(CliCommandSetup.IsNotInPlace(
            "/private/var/folders/x/AppTranslocation/ABC/d/UwView.app/Contents/MacOS/uvf"));
    }

    [Fact]
    public void 管理者実行へ渡す文字列は空白と引用符を守る()
    {
        string path = "/Applications/UwView Pro.app/Contents/MacOS/uvp";
        Assert.Equal("'/Applications/UwView Pro.app/Contents/MacOS/uvp'", CliCommandSetup.ShellQuote(path));
        Assert.Equal("'it'\\''s'", CliCommandSetup.ShellQuote("it's"));
        Assert.Equal("\"ln -s 'a\\\\b' \\\"c\\\"\"", CliCommandSetup.AppleScriptString("ln -s 'a\\b' \"c\""));
    }

    // ── Windows: ユーザーの PATH（文字列だけを扱う部分）──

    [Fact]
    public void PATHへの追加は末尾に1回だけ()
    {
        Assert.Equal(@"C:\Apps\UwView", CliCommandSetup.AddToPathList("", @"C:\Apps\UwView"));
        Assert.Equal(@"%USERPROFILE%\bin;C:\Apps\UwView",
            CliCommandSetup.AddToPathList(@"%USERPROFILE%\bin;", @"C:\Apps\UwView"));

        string once = CliCommandSetup.AddToPathList(@"C:\Tools", @"C:\Apps\UwView");
        Assert.Equal(once, CliCommandSetup.AddToPathList(once, @"C:\Apps\UwView"));
    }

    [Fact]
    public void PATHからの削除は自分のフォルダだけを外し他の書き方は残す()
    {
        if (!OperatingSystem.IsWindows()) return;   // 大文字小文字・末尾の \ の同一視は Windows のパス規則
        string list = @"%USERPROFILE%\bin;""C:\Apps\UwView\"";C:\Tools";
        Assert.True(CliCommandSetup.PathListContains(list, @"c:\apps\uwview"));
        Assert.Equal(@"%USERPROFILE%\bin;C:\Tools", CliCommandSetup.RemoveFromPathList(list, @"C:\Apps\UwView"));
    }

    [Fact]
    public void PATHの状態を見分ける()
    {
        string launcher = MakeLauncher("new");
        string dir = Path.GetDirectoryName(launcher)!;
        string oldDir = Path.GetDirectoryName(MakeLauncher("old"))!;

        Assert.Equal(CliCommandState.NotInstalled, CliCommandSetup.InspectWindows("uvf", launcher, "/usr/bin").State);
        Assert.Equal(CliCommandState.Installed, CliCommandSetup.InspectWindows("uvf", launcher, "/usr/bin;" + dir).State);

        if (!OperatingSystem.IsWindows()) return;   // 古い置き場所の検出は uvf.exe の有無で見る
        var other = CliCommandSetup.InspectWindows("uvf", launcher, oldDir);
        Assert.Equal(CliCommandState.OtherTarget, other.State);
        Assert.Equal(oldDir, other.Existing);
    }
}
