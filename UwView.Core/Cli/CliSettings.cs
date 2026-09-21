using System.Text.Json;
using System.Text.Json.Nodes;

namespace UwView.Core.Cli;

/// <summary>
/// CLI（uvf / uvp）がアプリの設定ファイルを読む係。
/// 読めない・無い・形が違うときは既定値を返す（CLI を設定ファイルの状態で止めない）。
///
/// 書き込みは <c>--tune --apply</c> だけが使う（Wide Field v1.7.0 段階1-b）。
/// 表を読んで画面に打ち込む手順を挟むとやらない人が出るため、CLI から保存できるようにしてある。
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

    /// <summary>
    /// 設定ファイルの1つのキーを書き替える（ほかのキーはそのまま）。書けたら true。
    /// 設定ファイルがまだ無ければ作る。画面が開いていると、画面の保存で上書きされることがある。
    /// </summary>
    public static bool WriteInt(string appDataFolder, string key, int value)
        => WriteIntTo(PathFor(appDataFolder), key, value);

    public static bool WriteIntTo(string settingsPath, string key, int value)
    {
        try
        {
            var root = File.Exists(settingsPath) && JsonNode.Parse(File.ReadAllText(settingsPath)) is JsonObject existing
                ? existing : new JsonObject();
            root[key] = value;
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            // 書き切ってから置き換える（途中で落ちても設定を壊さない）
            string tmp = settingsPath + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
            File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, settingsPath, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

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
