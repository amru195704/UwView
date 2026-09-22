using System.Reflection;

namespace UwView.Core;

/// <summary>
/// この実行ファイルの<b>呼び名</b>（試験用ビルドだけに付く。本番は空）。
///
/// 既に入れてある版と並べて置いて試すとき、<b>見た目で区別が付かないと取り違える</b>
/// （オーナー指摘 2026-09-22「名称には Wide Field をあちこち付けて」）。
/// ビルド時に <c>EDITION="Wide Field"</c> を与えると、UwView.Core に
/// <c>AssemblyMetadata("Edition", "Wide Field")</c> が焼き込まれ、ここから読める。
///
/// <b>本番ビルドでは何も変わらない</b>（EDITION が無ければ空文字で、題名も版数もこれまでどおり）。
/// </summary>
public static class AppEdition
{
    /// <summary>
    /// 呼び名（例 <c>Wide Field</c>）。本番は空。
    /// 通常はビルド時に焼き込まれた値が入る（書き換えるのはテストだけ）。
    /// </summary>
    public static string Name { get; set; } = Read();

    /// <summary>試験用ビルドか。</summary>
    public static bool IsTestBuild => Name.Length > 0;

    /// <summary>
    /// 版数（本体が起動時に入れる。例 <c>1.7.0.1</c>）。
    /// <b>dist を作るたびに上げる</b>ので、これが題名に出ていれば試験物を取り違えない
    /// （オーナー指示 2026-09-22）。
    /// </summary>
    public static string Version { get; set; } = "";

    /// <summary>名前に呼び名を添える（<c>UwView</c> → <c>UwView (Wide Field)</c>）。本番はそのまま。</summary>
    public static string Decorate(string name) => IsTestBuild ? $"{name} ({Name})" : name;

    /// <summary>
    /// ウィンドウの題名（<c>UwView(uvf)</c> → <c>UwView(uvf) - Wide Field(v1.7.0.1)</c>）。本番はそのまま。
    /// </summary>
    public static string TitleFor(string name)
        => IsTestBuild
            ? $"{name} - {Name}" + (Version.Length > 0 ? $"(v{Version})" : "")
            : name;

    private static string Read()
    {
        try
        {
            foreach (var a in typeof(AppEdition).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
                if (a.Key == "Edition" && a.Value is { Length: > 0 } value) return value;
        }
        catch (TypeLoadException) { /* 焼き込まれていない＝本番 */ }
        return "";
    }
}
