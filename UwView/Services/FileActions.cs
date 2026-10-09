using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace UwView.Services;

/// <summary>
/// ファイルについての OS の操作（フォルダーを開く・ターミナルを開く）と、アプリで開く先の決め方（2026-10-10 オーナー依頼）。
/// ステータスバーのファイル名・ファイル一覧の右クリック・「コマンドライン」の［フォルダーを表示］で共通に使う。
/// </summary>
public static class FileActions
{
    /// <summary>自動テスト用: OS の操作の代わりに呼ぶ（操作の名前 reveal・terminal とパス。開けたら true）。</summary>
    internal static Func<string, string, bool>? Override { get; set; }

    /// <summary>自動テスト用: 外部のアプリで開く代わりに呼ぶ（開けたら true）。</summary>
    internal static Func<string, Task<bool>>? LaunchOverride { get; set; }

    /// <summary>ファイルを、拡張子に合った OS の既定のアプリで開く。</summary>
    public static async Task<bool> LaunchAsync(TopLevel? top, string path)
    {
        if (LaunchOverride is { } fake) return await fake(path);
        try
        {
            return top?.Launcher is { } launcher && await launcher.LaunchFileInfoAsync(new FileInfo(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>ファイルのあるフォルダーを OS のファイル画面で開く（mac・Windows はそのファイルを選んだ状態で）。</summary>
    public static bool Reveal(string path)
    {
        if (Override is { } fake) return fake("reveal", path);
        bool file = File.Exists(path);
        string? folder = file ? Path.GetDirectoryName(path) : Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (folder is null || !Directory.Exists(folder)) return false;
        var start = OperatingSystem.IsMacOS() ? new ProcessStartInfo("open", file ? ["-R", path] : [folder])
            : OperatingSystem.IsWindows() ? new ProcessStartInfo("explorer.exe", file ? $"/select,\"{path}\"" : $"\"{folder}\"")
            : new ProcessStartInfo("xdg-open", [folder]);
        return TryStart(start);
    }

    /// <summary>そのフォルダーに移ったターミナルを開く（mac は Terminal、Windows は Windows ターミナル→cmd、Linux は入っているもの）。</summary>
    public static bool OpenTerminal(string folder)
    {
        if (Override is { } fake) return fake("terminal", folder);
        if (!Directory.Exists(folder)) return false;
        if (OperatingSystem.IsMacOS()) return TryStart(new ProcessStartInfo("open", ["-a", "Terminal", folder]));
        if (OperatingSystem.IsWindows())
            return TryStart(new ProcessStartInfo("wt.exe", ["-d", folder]))
                   || TryStart(new ProcessStartInfo("cmd.exe") { WorkingDirectory = folder, UseShellExecute = true });
        // Linux：よくある端末を順に試す（どれも今のフォルダーを作業フォルダーとして開く）
        foreach (var start in new[]
                 {
                     new ProcessStartInfo("x-terminal-emulator"),
                     new ProcessStartInfo("gnome-terminal", [$"--working-directory={folder}"]),
                     new ProcessStartInfo("konsole", ["--workdir", folder]),
                     new ProcessStartInfo("xfce4-terminal", [$"--working-directory={folder}"]),
                     new ProcessStartInfo("xterm"),
                 })
        {
            start.WorkingDirectory = folder;
            if (TryStart(start)) return true;
        }
        return false;
    }

    /// <summary>
    /// アプリで開くファイル。索引（<c>report.pdf.uwvz</c>）なら隣の元のファイル（<c>report.pdf</c>）。
    /// 開けないときは null と理由（zip の中・見つからない・元のファイルの無い索引）。
    /// </summary>
    public static string? AppTarget(string path, out AppTargetProblem problem)
    {
        problem = AppTargetProblem.None;
        if (path.EndsWith(".uwvz", StringComparison.OrdinalIgnoreCase))
        {
            string original = path[..^".uwvz".Length];
            if (File.Exists(original)) return original;
            problem = AppTargetProblem.IndexOnly;
            return null;
        }
        if (!File.Exists(path)) { problem = AppTargetProblem.Missing; return null; }
        return path;
    }

    private static bool TryStart(ProcessStartInfo start)
    {
        try
        {
            if (!start.UseShellExecute) start.UseShellExecute = false;
            using var process = Process.Start(start);
            return process is not null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}

/// <summary>アプリで開けない理由。</summary>
public enum AppTargetProblem
{
    None,
    /// <summary>ファイルが見つからない（消された・移された）。</summary>
    Missing,
    /// <summary>索引（.uwvz）だけで、元のファイルが隣に無い（束ねた索引など）。</summary>
    IndexOnly,
    /// <summary>zip の中のファイル。</summary>
    ZipEntry,
}
