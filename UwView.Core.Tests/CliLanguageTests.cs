using System.Globalization;
using UwView.Core.Cli;

namespace UwView.Core.Tests;

/// <summary>CLI の表示言語はアプリの設定で選んだ言語（オーナー指示 2026-09-14）。</summary>
public class CliLanguageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "uvf-clilang-" + Guid.NewGuid().ToString("N"));

    public CliLanguageTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Settings(string json)
    {
        string path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void 設定で選んだ言語がOSより優先される()
    {
        var saved = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ja-JP");
            Assert.False(CliLanguage.IsJapaneseFromSettings(Settings("""{ "Language": "en", "License": {} }""")));
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            Assert.True(CliLanguage.IsJapaneseFromSettings(Settings("""{ "Language": "ja" }""")));
        }
        finally { CultureInfo.CurrentUICulture = saved; }
    }

    [Fact]
    public void 未設定や読めない設定ならOSの表示言語に従う()
    {
        var saved = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var (culture, ja) in new[] { ("ja-JP", true), ("en-US", false), ("fr-FR", false) })
            {
                CultureInfo.CurrentUICulture = new CultureInfo(culture);
                Assert.Equal(ja, CliLanguage.IsJapaneseFromSettings(Path.Combine(_dir, "missing.json")));
                Assert.Equal(ja, CliLanguage.IsJapaneseFromSettings(Settings("""{ "Language": null }""")));
                Assert.Equal(ja, CliLanguage.IsJapaneseFromSettings(Settings("""{ "Language": "" }""")));
                Assert.Equal(ja, CliLanguage.IsJapaneseFromSettings(Settings("{ broken")));
            }
        }
        finally { CultureInfo.CurrentUICulture = saved; }
    }
}
