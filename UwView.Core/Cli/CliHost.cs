using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace UwView.Core.Cli;

/// <summary>
/// GUI 本体を CLI として動かすときの、プロセス環境まわり（UVF の uvf・UVP の uvp で共通）。
/// </summary>
public static class CliHost
{
    /// <summary>
    /// CLI の出力先を整える。Main の先頭で CLI と分かったら最初に呼ぶ。
    ///
    /// <b>Windows だけ必要。</b>GUI の exe は「コンソールを持たない種類（WinExe）」なので、
    /// コマンドプロンプトから起動しても標準出力がどこにもつながっていない。
    /// リダイレクト（<c>&gt; out.txt</c> や <c>| findstr</c>）されていればその先へ出るのでそのまま使い、
    /// つながっていない出力だけを、呼び出し元のコンソールにつなぎ直す。
    /// </summary>
    public static void PrepareConsole()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            bool outLost = IsUnattached(StdOutputHandle);
            bool errLost = IsUnattached(StdErrorHandle);
            if ((outLost || errLost) && AttachConsole(AttachParentProcess))
            {
                IntPtr console = CreateFileW("CONOUT$", GenericRead | GenericWrite, FileShareWrite,
                                             IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                if (console != InvalidHandle)
                {
                    if (outLost) SetStdHandle(StdOutputHandle, console);
                    if (errLost) SetStdHandle(StdErrorHandle, console);
                }
            }
            Console.OutputEncoding = new UTF8Encoding(false);   // 日本語の診断が化けないように
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or IOException) { }
    }

    /// <summary>
    /// GUI を起動する（-open）。<b>自分自身の実行ファイル</b>を、CLI の目印を付けずに起動し直す。
    /// 同じ実行ファイルなので「GUI が見つからない」は起きない。
    /// </summary>
    public static bool LaunchSelfAsGui(IEnumerable<string> guiArgs)
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return false;
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
            foreach (string a in guiArgs) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            return p is not null;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    // ── Windows のコンソール ─────────────────────────────────

    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;
    private const int AttachParentProcess = -1;
    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, FileShareWrite = 2, OpenExisting = 3;
    private const uint FileTypeUnknown = 0;
    private static readonly IntPtr InvalidHandle = new(-1);

    private static bool IsUnattached(int which)
    {
        IntPtr h = GetStdHandle(which);
        return h == IntPtr.Zero || h == InvalidHandle || GetFileType(h) == FileTypeUnknown;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AttachConsole(int processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetStdHandle(int which);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetStdHandle(int which, IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetFileType(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security,
                                             uint disposition, uint flags, IntPtr template);
}
