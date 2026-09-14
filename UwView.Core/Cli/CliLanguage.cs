using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UwView.Core.Cli;

/// <summary>
/// CLI（uvf / uvp）の表示言語。<b>アプリの設定で選んだ言語</b>を使う（オーナー指示 2026-09-14）。
///
/// 規則は画面（App / ProApp の ResolveLanguage）と同じ: 設定ファイルの <c>Language</c>（"ja" / "en"）、
/// 未設定なら OS の表示言語（日本語以外は英語）。CLI は設定を<b>読むだけ</b>で書かない。
/// </summary>
public static class CliLanguage
{
    /// <summary>無料版 UwView の設定フォルダ名（%AppData%/UwView/settings.json）。</summary>
    public const string FreeSettingsFolder = "UwView";

    /// <param name="appDataFolder">設定フォルダ名（UVF は "UwView"、UVP は "UwViewPro"）。</param>
    public static bool IsJapanese(string appDataFolder)
        => IsJapaneseFromSettings(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appDataFolder, "settings.json"));

    /// <summary>設定ファイルを直接指定する版（テスト用に分けてある）。</summary>
    public static bool IsJapaneseFromSettings(string settingsPath)
    {
        try
        {
            if (File.Exists(settingsPath)
                && JsonNode.Parse(File.ReadAllText(settingsPath)) is JsonObject root
                && root["Language"] is JsonValue value
                && value.TryGetValue(out string? language)
                && !string.IsNullOrWhiteSpace(language))
                return language == "ja";
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja";
    }
}
