using System.Reflection;

namespace UwView.Core;

/// <summary>
/// この実行ファイルの<b>呼び名</b>（試験用ビルドだけに付く。本番は空）。
///
/// 既に入れてある版と並べて置いて試すとき、<b>見た目で区別が付かないと取り違える</b>
/// （9.22修正「名称には Wide Field をあちこち付けて」）。
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

    /// <summary>ビルド番号（csproj の <c>AssemblyMetadata("BuildNumber")</c>＝コンパイル日時 yy.MM.dd.HH）。無ければ空。</summary>
    public static string BuildNumberOf(Assembly assembly)
        => assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(m => m.Key == "BuildNumber")?.Value ?? "";

    /// <summary>
    /// <c>--version</c> の 1 行（<c>uvf 1.7.3.6.9 (build 26.10.04.19)</c>）。版数は 2 つ目の語のまま（読むスクリプトを壊さない）。
    /// ビルド番号は画面の About と同じ値（10.4修正）。
    /// </summary>
    public static string VersionLine(string tool, string version, string buildNumber)
        => Decorate($"{tool} {version}") + (buildNumber.Length > 0 ? $" (build {buildNumber})" : "");

    /// <summary>
    /// 版数（本体が起動時に入れる。例 <c>1.7.0.1</c>）。
    /// <b>dist を作るたびに上げる</b>ので、これが題名に出ていれば試験物を取り違えない
    /// （9.22修正）。
    /// </summary>
    public static string Version { get; set; } = "";

    /// <summary>名前に呼び名を添える（<c>UwView</c> → <c>UwView (Wide Field)</c>）。本番はそのまま。</summary>
    public static string Decorate(string name) => IsTestBuild ? $"{name} ({Name})" : name;

    /// <summary>この系列の呼び名（ペット名）。1.7 系は Wide Field、1.8 系は Finder Scope。</summary>
    public const string PetName = "Finder Scope";

    /// <summary>
    /// ウィンドウの題名（<c>UwView(uvf)</c> → <c>UwView(uvf)-Wide Field(v1.7.3.3)</c>）。
    /// 正式名のビルドでもペット名と版数を出す（9.28修正。どの版を見ているか題名で分かるように）。
    /// 試験用の呼び名が焼き込まれていれば、ペット名の代わりにそれを出す。
    /// </summary>
    public static string TitleFor(string name)
        => $"{name}-{(IsTestBuild ? Name : PetName)}" + (Version.Length > 0 ? $"(v{Version})" : "");

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
