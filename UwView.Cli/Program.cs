using System.Diagnostics;
using UwView.Cli;

// uvf — 無料版 UwView の CLI。中身は UvfCli。ここはプロセス環境との接続だけ。
UwView.Core.EncodingDetector.EnsureCodePagesRegistered();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

return await UvfCli.RunAsync(args, new UvfEnvironment
{
    StdOut = Console.OpenStandardOutput(),
    StdErr = Console.Error,
    LaunchGui = GuiLauncher.Launch,
}, cts.Token);

/// <summary>
/// GUI（UwView）を起動する。探す順: 環境変数 UVF_GUI → uvf と同じフォルダの GUI 実行ファイル
/// → macOS はアプリ名で open。
///
/// 同じフォルダの実行ファイルを直接起動するのは、macOS の <c>open -a … --args</c> が
/// <b>起動中のアプリには引数を渡さない</b>ため（検索パターンが届かなくなる）。
/// </summary>
internal static class GuiLauncher
{
    public static bool Launch(string? file, string? pattern)
    {
        var args = new List<string>();
        if (pattern is not null && file is not null)
        {
            args.Add(UvfCli.SearchArgument);
            args.Add(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(pattern)));
        }
        if (file is not null) args.Add(file);

        string? exe = Environment.GetEnvironmentVariable("UVF_GUI");
        if (string.IsNullOrEmpty(exe))
        {
            foreach (string name in new[] { "UwView.Desktop", "UwView.Desktop.exe", "UwView", "UwView.exe" })
            {
                string candidate = Path.Combine(AppContext.BaseDirectory, name);
                if (File.Exists(candidate)) { exe = candidate; break; }
            }
        }

        try
        {
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe)) return Start(exe, args);
            if (OperatingSystem.IsMacOS())
                return Start("/usr/bin/open", ["-a", "UwView", "--args", .. args]);
        }
        catch (System.ComponentModel.Win32Exception) { }
        return false;
    }

    private static bool Start(string exe, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        return p is not null;
    }
}
