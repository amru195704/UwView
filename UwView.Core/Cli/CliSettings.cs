using System.Text.Json;
using System.Text.Json.Nodes;

namespace UwView.Core.Cli;

/// <summary>
/// CLI（uvf / uvp）がアプリの設定ファイルを<b>読むだけ</b>の係。書き込みはしない（設定は画面で変える）。
/// 読めない・無い・形が違うときは既定値を返す（CLI を設定ファイルの状態で止めない）。
/// </summary>
public static class CliSettings
{
    /// <summary>画面の設定「1回の検索で保持する最大ヒット数」（AppSettings.SearchMaxHits と同じ名前）。</summary>
    public const string SearchMaxHitsKey = "SearchMaxHits";

    public static string PathFor(string appDataFolder) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appDataFolder, "settings.json");

    /// <summary>
    /// 設定の「1回の検索で保持する最大ヒット数」（0＝無制限）。設定が無ければ無制限。
    /// v1.6.0 の既定（<see cref="SearchService.LegacyDefaultMaxHits"/>＝100万件）が保存されているだけなら、
    /// 新しい既定＝無制限として読む（オーナー裁定 2026-09-16）。
    /// </summary>
    public static int ReadSearchMaxHits(string appDataFolder) => ReadSearchMaxHitsFrom(PathFor(appDataFolder));

    public static int ReadSearchMaxHitsFrom(string settingsPath)
    {
        int n = ReadIntFrom(settingsPath, SearchMaxHitsKey, 0);
        return n == SearchService.LegacyDefaultMaxHits ? 0 : n;
    }

    public static int ReadInt(string appDataFolder, string key, int fallback)
        => ReadIntFrom(PathFor(appDataFolder), key, fallback);

    public static int ReadIntFrom(string settingsPath, string key, int fallback)
        => Read(settingsPath, key) is JsonValue v && v.TryGetValue(out int n) ? n : fallback;

    public static string? ReadStringFrom(string settingsPath, string key)
        => Read(settingsPath, key) is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    private static JsonNode? Read(string settingsPath, string key)
    {
        try
        {
            if (File.Exists(settingsPath) && JsonNode.Parse(File.ReadAllText(settingsPath)) is JsonObject root)
                return root[key];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return null;
    }
}
