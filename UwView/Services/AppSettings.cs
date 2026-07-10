using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UwView.Services;

/// <summary>ユーザー設定（言語など）を JSON で永続化。Browser 等で保存不可な環境は握りつぶす。</summary>
public sealed class AppSettings
{
    /// <summary>UI 言語（"ja" / "en"）。null なら OS の UI カルチャに従う。</summary>
    public string? Language { get; set; }

    [JsonIgnore]
    public static string FilePath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "UwView");
            return Path.Combine(dir, "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJsonContext.Default.AppSettings)
                       ?? new AppSettings();
        }
        catch { /* 破損・アクセス不可時は既定へフォールバック */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, SettingsJsonContext.Default.AppSettings));
        }
        catch { /* 保存不可（WASM 等）は無視 */ }
    }
}

// トリミング/AOT 安全な source-generated シリアライザ
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
