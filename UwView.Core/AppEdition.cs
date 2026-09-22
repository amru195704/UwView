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
    /// <summary>呼び名（例 <c>Wide Field</c>）。本番は空。</summary>
    public static string Name { get; } = Read();

    /// <summary>試験用ビルドか。</summary>
    public static bool IsTestBuild => Name.Length > 0;

    /// <summary>名前に呼び名を添える（<c>UwView</c> → <c>UwView (Wide Field)</c>）。本番はそのまま。</summary>
    public static string Decorate(string name) => IsTestBuild ? $"{name} ({Name})" : name;

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
