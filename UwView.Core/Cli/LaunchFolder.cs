using System.Diagnostics;

namespace UwView.Core.Cli;

/// <summary>
/// ターミナルから <c>uvf / uvp … -open</c> で画面を起動したときの、コマンドを打ったフォルダー（オーナー指示 2026-10-06）。
/// 画面の「コマンドライン」の基準フォルダーの既定にする。引数で渡すと、ファイルをダブルクリックして開いたときと見分けられないので、
/// 起動する子にだけ環境変数で渡す。
/// </summary>
public static class LaunchFolder
{
    public const string Variable = "UV_LAUNCH_DIR";

    /// <summary>CLI：画面を起動する直前に、今のフォルダーを渡す。</summary>
    public static void Pass(ProcessStartInfo psi)
    {
        try { psi.Environment[Variable] = Directory.GetCurrentDirectory(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>画面：CLI から渡されたフォルダー（渡されていない・もう無ければ null）。</summary>
    public static string? FromCli { get; } = Read();

    private static string? Read()
    {
        string? folder = Environment.GetEnvironmentVariable(Variable);
        // この画面からさらに起動するものには引き継がない
        Environment.SetEnvironmentVariable(Variable, null);
        return folder is { Length: > 0 } && Directory.Exists(folder) ? folder : null;
    }
}
