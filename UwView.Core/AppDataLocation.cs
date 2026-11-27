namespace UwView.Core;

/// <summary>
/// 設定・ライセンス・試用の記録の置き場所の根（2026-11-27 検査の指摘 C）。
///
/// <c>Environment.GetFolderPath(ApplicationData)</c> は、Linux で <c>~/.config</c> が無いと空の文字列を返す。
/// それにフォルダ名をつなぐと相対パスになり、<b>今いるフォルダ</b>に試用の記録が書かれていた
/// ＝フォルダを変えるたびに試用をやり直せた。ここでは:
/// <list type="number">
///   <item>フォルダが無くても名前を返す形（<see cref="Environment.SpecialFolderOption.DoNotVerify"/>）で取る。</item>
///   <item>それでも絶対パスでなければ、HOME から組み立てる（Linux・mac の ApplicationData は ~/.config、LocalApplicationData は ~/.local/share）。</item>
///   <item>それも無ければ例外（今いるフォルダには書かない）。</item>
/// </list>
/// </summary>
public static class AppDataLocation
{
    /// <summary>その種類の置き場所の根（必ず絶対パス）。決められなければ <see cref="InvalidOperationException"/>。</summary>
    public static string Root(Environment.SpecialFolder folder = Environment.SpecialFolder.ApplicationData)
    {
        string p = Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
        if (!string.IsNullOrEmpty(p) && Path.IsPathFullyQualified(p)) return p;
        string? home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrEmpty(home) && Path.IsPathFullyQualified(home))
            return folder == Environment.SpecialFolder.LocalApplicationData
                ? Path.Combine(home, ".local", "share")
                : Path.Combine(home, ".config");
        throw new InvalidOperationException(UnusableMessage);
    }

    /// <summary>置き場所が決められないときの文言。</summary>
    public const string UnusableMessage = "設定の置き場所（ホームフォルダ）が決められません。環境変数 HOME を絶対パスで設定してください。";

    /// <summary>
    /// 入口で先に確かめる（2026-11-27 検査の軽微: HOME が相対パスなどのとき、例外を受けずスタックトレースを出して落ちていた）。
    /// 使えれば null、使えなければ文言。入口はこれを見て、文言を出して終わる。
    /// </summary>
    public static string? Problem()
    {
        try
        {
            Root(Environment.SpecialFolder.ApplicationData);
            Root(Environment.SpecialFolder.LocalApplicationData);
            return null;
        }
        catch (InvalidOperationException e) { return e.Message; }
    }

    /// <summary>根の下のフォルダ（例: Root()/UwVCmp）。</summary>
    public static string Folder(string name, Environment.SpecialFolder folder = Environment.SpecialFolder.ApplicationData)
        => Path.Combine(Root(folder), name);
}
