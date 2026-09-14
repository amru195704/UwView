using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace UwView.Core.Cli;

/// <summary>いまの状態（<see cref="CliCommandSetup.Inspect"/> の結果）。</summary>
public enum CliCommandState
{
    /// <summary>アプリの隣に uvf / uvp が無い（開発中のビルドなど）。</summary>
    Missing,
    /// <summary>ディスクイメージや隔離された場所（App Translocation）から起動している。移してからでないと、リンク先がすぐ消える。</summary>
    AppNotInPlace,
    NotInstalled,
    Installed,
    /// <summary>同じ名前が別の場所を指している（古い置き場所・別のファイル）。</summary>
    OtherTarget,
}

/// <param name="Location">
/// 登録する場所。Windows は PATH に足すフォルダ、macOS / Linux はリンクのパス（/usr/local/bin/uvf など）。
/// </param>
/// <param name="Existing">OtherTarget のとき、今そこにあるもの（リンク先・別のフォルダ）。</param>
public sealed record CliCommandStatus(
    CliCommandState State, string Tool, string? Launcher, string Location, string? Existing = null);

/// <param name="Cancelled">管理者パスワードの画面で「キャンセル」された。</param>
public sealed record CliCommandResult(bool Ok, bool Cancelled = false, string? Error = null)
{
    public static readonly CliCommandResult Success = new(true);
}

/// <summary>
/// uvf / uvp を、コンソールで名前だけ打って使えるようにする（UVF・UVP 共通）。
///
/// 配布はインストーラではなく dmg / zip / tar.gz なので、置き場所はアプリ自身が登録する。
/// <list type="bullet">
/// <item>Windows: <b>ユーザーの</b>環境変数 PATH に、uvf.exe のあるフォルダを足す（管理者権限は要らない）。</item>
/// <item>macOS: <c>/usr/local/bin/uvf</c> にリンクを作る（標準の PATH に入っている。書けなければ管理者パスワードを尋ねる）。</item>
/// <item>Linux: <c>~/.local/bin/uvf</c> にリンクを作る。</item>
/// </list>
/// uvf / uvp の起動アプリはリンクをたどって本体を探すので、リンク経由でもそのまま動く。
/// </summary>
public static class CliCommandSetup
{
    public const string MacLinkDirectory = "/usr/local/bin";

    public static string LinuxLinkDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin");

    /// <summary>アプリ本体の隣にある起動アプリ（uvf / uvf.exe）。無ければ null。</summary>
    public static string? FindLauncher(string tool, string? processPath = null)
    {
        processPath ??= Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath)) return null;
        string dir = Path.GetDirectoryName(processPath)!;
        string candidate = Path.Combine(dir, OperatingSystem.IsWindows() ? tool + ".exe" : tool);
        return File.Exists(candidate) ? candidate : null;
    }

    public static CliCommandStatus Inspect(string tool, string? processPath = null)
    {
        string? launcher = FindLauncher(tool, processPath);

        if (OperatingSystem.IsWindows())
        {
            string dir = launcher is null ? "" : Path.GetDirectoryName(launcher)!;
            if (launcher is null) return new(CliCommandState.Missing, tool, null, dir);
            return InspectWindows(tool, launcher, ReadUserPath() ?? "");
        }

        string linkDir = OperatingSystem.IsMacOS() ? MacLinkDirectory : LinuxLinkDirectory;
        if (launcher is null) return new(CliCommandState.Missing, tool, null, Path.Combine(linkDir, tool));
        if (OperatingSystem.IsMacOS() && IsNotInPlace(launcher))
            return new(CliCommandState.AppNotInPlace, tool, launcher, Path.Combine(linkDir, tool));
        return InspectLink(tool, launcher, linkDir);
    }

    public static Task<CliCommandResult> InstallAsync(CliCommandStatus status) => Task.Run(() =>
    {
        if (status.Launcher is null) return new CliCommandResult(false, Error: "launcher not found");
        try
        {
            if (OperatingSystem.IsWindows()) return InstallWindows(status);
            string linkDir = Path.GetDirectoryName(status.Location)!;
            if (OperatingSystem.IsMacOS() && !CanWriteDirectory(linkDir))
                return RunAsAdministrator(
                    $"/bin/mkdir -p {ShellQuote(linkDir)} && /bin/ln -sfn {ShellQuote(status.Launcher)} {ShellQuote(status.Location)}");
            return InstallLink(status.Launcher, status.Location);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new CliCommandResult(false, Error: e.Message);
        }
    });

    public static Task<CliCommandResult> UninstallAsync(CliCommandStatus status) => Task.Run(() =>
    {
        try
        {
            if (OperatingSystem.IsWindows()) return UninstallWindows(status);
            string linkDir = Path.GetDirectoryName(status.Location)!;
            if (OperatingSystem.IsMacOS() && !CanWriteDirectory(linkDir))
                return RunAsAdministrator($"/bin/rm -f {ShellQuote(status.Location)}");
            return UninstallLink(status.Location);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new CliCommandResult(false, Error: e.Message);
        }
    });

    // ── macOS / Linux: リンク ─────────────────────────────────

    /// <summary>リンクの状態を見る（テストでは linkDir を一時フォルダにする）。</summary>
    public static CliCommandStatus InspectLink(string tool, string launcher, string linkDir)
    {
        string link = Path.Combine(linkDir, tool);
        var info = new FileInfo(link);
        if (info.LinkTarget is { } target)
        {
            string resolved = Path.GetFullPath(target, linkDir);
            return SamePath(resolved, launcher, ignoreCase: false)
                ? new(CliCommandState.Installed, tool, launcher, link)
                : new(CliCommandState.OtherTarget, tool, launcher, link, resolved);
        }
        if (info.Exists || Directory.Exists(link))
            return new(CliCommandState.OtherTarget, tool, launcher, link, link);
        return new(CliCommandState.NotInstalled, tool, launcher, link);
    }

    /// <summary>リンクを作る（既にあるリンク・ファイルは置き換える）。書けるフォルダ用。</summary>
    public static CliCommandResult InstallLink(string launcher, string link)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        if (new FileInfo(link).LinkTarget is not null || File.Exists(link)) File.Delete(link);
        File.CreateSymbolicLink(link, launcher);
        return CliCommandResult.Success;
    }

    /// <summary>リンクを消す。リンクでない普通のファイルは、利用者の物かもしれないので消さない。</summary>
    public static CliCommandResult UninstallLink(string link)
    {
        var info = new FileInfo(link);
        if (info.LinkTarget is null) return info.Exists ? new CliCommandResult(false, Error: $"{link} is not a link") : CliCommandResult.Success;
        File.Delete(link);
        return CliCommandResult.Success;
    }

    /// <summary>
    /// ディスクイメージ（読み取り専用の /Volumes/…）や App Translocation から起動しているか。
    /// その場所へのリンクは、取り出し・再起動で行き先が無くなる。
    /// </summary>
    public static bool IsNotInPlace(string launcher)
    {
        if (launcher.Contains("/AppTranslocation/", StringComparison.Ordinal)) return true;
        if (!launcher.StartsWith("/Volumes/", StringComparison.Ordinal)) return false;
        // 外付けディスクに置いたアプリは使える。書けない場所（マウントした dmg）だけを弾く
        int app = launcher.IndexOf(".app/", StringComparison.Ordinal);
        string folder = app > 0 ? Path.GetDirectoryName(launcher[..(app + 4)])! : Path.GetDirectoryName(launcher)!;
        return !CanWriteDirectory(folder);
    }

    /// <summary>シェルの単一引用符で包む（中の ' は '\'' にする）。</summary>
    public static string ShellQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>AppleScript の文字列リテラルにする。</summary>
    public static string AppleScriptString(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>macOS 標準の管理者パスワード画面を出して、シェルのコマンドを実行する。</summary>
    private static CliCommandResult RunAsAdministrator(string shellCommand)
    {
        var psi = new ProcessStartInfo("/usr/bin/osascript")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add($"do shell script {AppleScriptString(shellCommand)} with administrator privileges");
        using var p = Process.Start(psi);
        if (p is null) return new CliCommandResult(false, Error: "osascript could not start");
        string err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode == 0) return CliCommandResult.Success;
        // -128 = 利用者がキャンセル
        return err.Contains("-128") ? new CliCommandResult(false, Cancelled: true) : new CliCommandResult(false, Error: err.Trim());
    }

    /// <summary>書けるか（まだ無いフォルダは、作ることになる一番近い親で見る）。</summary>
    private static bool CanWriteDirectory(string dir)
    {
        for (string? d = dir; !string.IsNullOrEmpty(d); d = Path.GetDirectoryName(d))
            if (Directory.Exists(d)) return access(d, WriteOk) == 0;
        return false;
    }

    private const int WriteOk = 2;
    [DllImport("libc", SetLastError = true)] private static extern int access(string path, int mode);

    // ── Windows: ユーザーの PATH ─────────────────────────────────

    /// <summary>PATH の文字列を見て状態を決める（テスト用に分けてある）。</summary>
    public static CliCommandStatus InspectWindows(string tool, string launcher, string userPath)
    {
        string dir = Path.GetDirectoryName(launcher)!;
        if (PathListContains(userPath, dir)) return new(CliCommandState.Installed, tool, launcher, dir);

        // 以前に別の場所へ展開した UwView を登録していれば、そちらが先に見つかってしまう
        foreach (string entry in SplitPathList(userPath))
        {
            string expanded = ExpandEntry(entry);
            if (File.Exists(Path.Combine(expanded, tool + ".exe")))
                return new(CliCommandState.OtherTarget, tool, launcher, dir, expanded);
        }
        return new(CliCommandState.NotInstalled, tool, launcher, dir);
    }

    public static IEnumerable<string> SplitPathList(string list) =>
        list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool PathListContains(string list, string dir) =>
        SplitPathList(list).Any(e => SamePath(ExpandEntry(e), dir, ignoreCase: true));

    /// <summary>末尾に足す（既にあれば何もしない）。</summary>
    public static string AddToPathList(string list, string dir)
    {
        if (PathListContains(list, dir)) return list;
        string trimmed = list.TrimEnd(';', ' ');
        return trimmed.Length == 0 ? dir : trimmed + ";" + dir;
    }

    /// <summary>そのフォルダを指す項目だけを外す（他の項目の書き方はそのまま残す）。</summary>
    public static string RemoveFromPathList(string list, string dir) =>
        string.Join(';', SplitPathList(list).Where(e => !SamePath(ExpandEntry(e), dir, ignoreCase: true)));

    private static string ExpandEntry(string entry) =>
        Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"'));

    private static bool SamePath(string a, string b, bool ignoreCase)
    {
        static string Norm(string p)
        {
            try { p = Path.GetFullPath(p); } catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { }
            return p.Length > 1 ? p.TrimEnd('\\', '/') : p;
        }
        return string.Equals(Norm(a), Norm(b), ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    [SupportedOSPlatform("windows")]
    private static CliCommandResult InstallWindows(CliCommandStatus status)
    {
        string list = ReadUserPath() ?? "";
        // 古い置き場所の UwView は外す（残すと、そちらが先に見つかる）
        if (status.State == CliCommandState.OtherTarget && status.Existing is { } old)
            list = RemoveFromPathList(list, old);
        WriteUserPath(AddToPathList(list, status.Location));
        return CliCommandResult.Success;
    }

    [SupportedOSPlatform("windows")]
    private static CliCommandResult UninstallWindows(CliCommandStatus status)
    {
        WriteUserPath(RemoveFromPathList(ReadUserPath() ?? "", status.Location));
        return CliCommandResult.Success;
    }

    /// <summary>
    /// 登録値をそのまま読む。<c>%USERPROFILE%</c> などを展開して書き戻すと、利用者の設定を壊すため
    /// （Environment.SetEnvironmentVariable は展開済みの値を REG_SZ で書くので使わない）。
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static string? ReadUserPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey("Environment");
        return key?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    [SupportedOSPlatform("windows")]
    private static void WriteUserPath(string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey("Environment", writable: true);
        var kind = key.GetValue("Path") is null ? RegistryValueKind.ExpandString : key.GetValueKind("Path");
        if (kind != RegistryValueKind.String) kind = RegistryValueKind.ExpandString;
        key.SetValue("Path", value, kind);
        // 新しく開くコンソールへ伝える（エクスプローラーが環境を読み直す）
        SendMessageTimeoutW(HwndBroadcast, WmSettingChange, UIntPtr.Zero, "Environment", SmtoAbortIfHung, 5000, out _);
    }

    private static readonly IntPtr HwndBroadcast = new(0xffff);
    private const uint WmSettingChange = 0x001A, SmtoAbortIfHung = 0x0002;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeoutW(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam,
                                                     uint flags, uint timeout, out UIntPtr result);
}
