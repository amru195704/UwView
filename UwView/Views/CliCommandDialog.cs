using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using UwView.Core.Cli;
using UwView.Localization;

namespace UwView.Views;

/// <summary>
/// メニュー「コマンドライン設定…」。uvf / uvp を、コンソールで名前だけ打って使えるようにする／解除する
/// （UVF・UVP 共通。中身は <see cref="CliCommandSetup"/>）。
/// </summary>
public static class CliCommandDialog
{
    // テスト用の差し替え（本物の /usr/local/bin やレジストリに触らない）
    internal static Func<string, CliCommandStatus>? InspectOverride { get; set; }
    internal static Func<CliCommandStatus, bool, Task<CliCommandResult>>? ApplyOverride { get; set; }
    internal static Func<string, Task<bool>>? AskOverride { get; set; }
    internal static Action<string>? NoticeOverride { get; set; }

    private static bool Ja => Localizer.Instance.Culture.TwoLetterISOLanguageName == "ja";

    public static async Task RunAsync(Window owner, string tool)
    {
        var status = InspectOverride?.Invoke(tool) ?? CliCommandSetup.Inspect(tool);

        switch (status.State)
        {
            case CliCommandState.Missing:
                await Notice(owner, Ja
                    ? $"この版には {tool} が入っていません。配布版（dmg / zip / tar.gz）のアプリで実行してください。"
                    : $"This build does not include {tool}. Run this from the released app (dmg / zip / tar.gz).");
                return;

            case CliCommandState.AppNotInPlace:
                await Notice(owner, Ja
                    ? "ディスクイメージ（dmg）から直接起動しています。\nアプリを「アプリケーション」フォルダへ移し、そこから起動してからもう一度実行してください。"
                    : "The app is running directly from the disk image (dmg).\nMove it to the Applications folder, launch it from there, and try again.");
                return;

            case CliCommandState.Installed:
                if (!await Ask(Ja
                        ? $"{tool} はコマンドラインで使えます。\n\n{Where(status)}\n\n登録を解除しますか？"
                        : $"{tool} is available on the command line.\n\n{Where(status)}\n\nRemove it?",
                        Ja ? "解除する" : "Remove", Ja ? "閉じる" : "Close", owner))
                    return;
                await Report(owner, status, await Apply(status, install: false), install: false);
                return;

            case CliCommandState.OtherTarget:
                if (!await Ask(Ja
                        ? $"{tool} は既に別の場所で登録されています:\n  {status.Existing}\n\nこのアプリの {tool} に置き換えますか？\n\n{Plan(status)}"
                        : $"{tool} is already registered from another place:\n  {status.Existing}\n\nReplace it with this app's {tool}?\n\n{Plan(status)}",
                        Ja ? "置き換える" : "Replace", Ja ? "やめる" : "Cancel", owner))
                    return;
                await Report(owner, status, await Apply(status, install: true), install: true);
                return;

            default:
                if (!await Ask(Ja
                        ? $"コンソールで {tool} と打つだけで使えるようにします。\n\n{Plan(status)}"
                        : $"Make {tool} available by name in a terminal.\n\n{Plan(status)}",
                        Ja ? "使えるようにする" : "Install", Ja ? "やめる" : "Cancel", owner))
                    return;
                await Report(owner, status, await Apply(status, install: true), install: true);
                return;
        }
    }

    /// <summary>何をするか（実行前に見せる）。</summary>
    private static string Plan(CliCommandStatus s)
    {
        if (OperatingSystem.IsWindows())
            return Ja
                ? $"ユーザーの環境変数 PATH に次のフォルダを追加します（管理者権限は要りません）:\n  {s.Location}\n\nこのフォルダを移動・削除すると使えなくなります。その場合は移した先でもう一度実行してください。"
                : $"This adds the following folder to your user PATH (no administrator rights needed):\n  {s.Location}\n\nIf you move or delete this folder, run this again from the new place.";
        if (OperatingSystem.IsMacOS())
            return Ja
                ? $"次のリンクを作ります:\n  {s.Location} → {s.Launcher}\n\n管理者のパスワードを聞かれることがあります。"
                : $"This creates the link:\n  {s.Location} → {s.Launcher}\n\nYou may be asked for an administrator password.";
        return Ja
            ? $"次のリンクを作ります:\n  {s.Location} → {s.Launcher}\n\n管理者のパスワードを聞かれることがあります。管理者になれない場合は ~/.local/bin に置きます（ログインし直すと使えます）。"
            : $"This creates the link:\n  {s.Location} → {s.Launcher}\n\nYou may be asked for an administrator password. Without administrator rights it goes into ~/.local/bin instead (usable after you log in again).";
    }

    private static string Where(CliCommandStatus s) => OperatingSystem.IsWindows()
        ? (Ja ? $"PATH に登録済みのフォルダ:\n  {s.Location}" : $"Folder in your PATH:\n  {s.Location}")
        : $"{s.Location} → {s.Launcher}";

    private static async Task Report(Window owner, CliCommandStatus s, CliCommandResult r, bool install)
    {
        if (r.Cancelled) return;
        if (!r.Ok)
        {
            await Notice(owner, Ja
                ? $"{(install ? "登録" : "解除")}できませんでした。\n{r.Error}"
                : $"Could not {(install ? "install" : "remove")} {s.Tool}.\n{r.Error}");
            return;
        }
        if (!install)
        {
            await Notice(owner, Ja ? $"{s.Tool} の登録を解除しました。" : $"Removed {s.Tool}.");
            return;
        }

        string examples = s.Tool == "uvf"
            ? (Ja ? "  uvf -open\n  uvf ファイル 検索パターン" : "  uvf -open\n  uvf file pattern")
            : (Ja ? "  uvp ファイル 検索パターン\n  uvp ファイル 検索パターン -open" : "  uvp file pattern\n  uvp file pattern -open");
        string fresh = OperatingSystem.IsWindows()
            ? (Ja ? "新しく開いたコマンドプロンプト／PowerShell から使えます（開いたままの画面には反映されません）。"
                  : "Open a new Command Prompt / PowerShell window (windows already open do not see the change).")
            : (Ja ? "新しく開いたターミナルから使えます。" : "Open a new terminal window to use it.");
        string placed = r.InstalledAt ?? s.Location;
        bool fellBack = r.InstalledAt is not null && r.InstalledAt != s.Location;
        string placedDir = Path.GetDirectoryName(placed)!;
        string pathNote = "";
        if (OperatingSystem.IsLinux() && (fellBack || !OnPath(placedDir)))
        {
            string why = fellBack
                ? (Ja ? $"管理者になれなかったので {placed} に置きました。" : $"Administrator rights were not available, so it was placed in {placed}. ")
                : "";
            pathNote = OnPath(placedDir)
                ? "\n\n" + why
                : Ja
                    ? $"\n\n{why}{placedDir} はまだ PATH に入っていません。ログインし直すと入ります（Ubuntu など）。"
                      + "入らない場合は ~/.profile に次の1行を足してください:\n  export PATH=\"$HOME/.local/bin:$PATH\""
                    : $"\n\n{why}{placedDir} is not in your PATH yet. Log in again to pick it up (Ubuntu and similar). "
                      + "If it still is not, add this line to ~/.profile:\n  export PATH=\"$HOME/.local/bin:$PATH\"";
        }

        await Notice(owner, Ja
            ? $"{s.Tool} を使えるようにしました（{placed}）。\n{fresh}\n\n{examples}{pathNote}"
            : $"{s.Tool} is installed ({placed}).\n{fresh}\n\n{examples}{pathNote}");
    }

    private static bool OnPath(string dir) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Any(p => string.Equals(p.TrimEnd('/'), dir.TrimEnd('/'), StringComparison.Ordinal));

    private static Task<CliCommandResult> Apply(CliCommandStatus s, bool install) =>
        ApplyOverride?.Invoke(s, install)
        ?? (install ? CliCommandSetup.InstallAsync(s) : CliCommandSetup.UninstallAsync(s));

    private static Task<bool> Ask(string message, string yes, string no, Window owner) =>
        AskOverride?.Invoke(message)
        ?? ConfirmDialog.AskAsync(owner, Title, message, yes, no);

    private static Task Notice(Window owner, string message)
    {
        if (NoticeOverride is { } stub) { stub(message); return Task.CompletedTask; }
        return ConfirmDialog.NoticeAsync(owner, Title, message, Ja ? "閉じる" : "Close");
    }

    private static string Title => Ja ? "コマンドライン設定" : "Command line setup";
}
