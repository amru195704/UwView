using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using UwView.Core;

namespace UwView.Services;

/// <summary>定義済みフィルタ（実装指示書_その他4機能 §A）。名前付きの検索パターン。</summary>
public sealed class PredefinedFilter
{
    public string Name { get; set; } = "";
    public string Pattern { get; set; } = "";
    public bool IsRegex { get; set; }
    public bool IgnoreCase { get; set; }

    public override string ToString() => Name; // ComboBox 等の既定表示
}

/// <summary>ファイルごとの復元状態（実装指示書_その他4機能 §C）。キー=パス、妥当性=長さ+mtime。</summary>
public sealed class PerFileState
{
    public string PathKey { get; set; } = "";
    public long Length { get; set; }
    public long MtimeTicks { get; set; }
    public int Mode { get; set; }               // 0=Page, 1=Line
    public long TopByteOffset { get; set; }
    public long TopLine { get; set; }
    public string? Encoding { get; set; }        // EncodingChoice の名前
    public bool IsTailing { get; set; }
    public List<long> Bookmarks { get; set; } = new();
    public string? ActiveSetId { get; set; }     // 色分けハイライタのアクティブセット
}

/// <summary>最近使ったファイルの1件（V1.1.1 §2-2）。</summary>
public sealed class RecentEntry
{
    public string Path { get; set; } = "";
    public long LastOpenedUtcTicks { get; set; }
}

/// <summary>復元対象の開いていたドキュメント1件（V1.1.1 §2-2。軽いスクロール位置復元）。</summary>
public sealed class OpenDoc
{
    public string Path { get; set; } = "";
    /// <summary>前回の表示先頭行（行モード時。ページモードは 0）。</summary>
    public long LastTopLine { get; set; }
}

/// <summary>セッション復元（実装指示書_その他4機能 §D / V1.1.1 §2）。</summary>
public sealed class SessionState
{
    public List<OpenDoc> Docs { get; set; } = new();
    public int ActiveIndex { get; set; }
}

/// <summary>UVP ライセンス永続データ（Polar・実装指示書 §1-4）。UVF では未使用。</summary>
public sealed class LicenseData
{
    public string? Key { get; set; }
    public string? ActivationId { get; set; }
    /// <summary>最後の検証結果（"granted" / "revoked" / "disabled" / null）。</summary>
    public string? LastStatus { get; set; }
    /// <summary>最後にオンライン検証が成功した時刻（UTC ticks。0=未検証）。</summary>
    public long LastValidatedUtcTicks { get; set; }
    /// <summary>ライセンスの失効時刻（UTC ticks。0=無期限=買い切り）。</summary>
    public long ExpiresUtcTicks { get; set; }
    /// <summary>初回起動時刻（UTC ticks。トライアル起算。0=未設定）。</summary>
    public long FirstRunUtcTicks { get; set; }
}

/// <summary>ユーザー設定（言語・Ver1.1 機能・UVPライセンス）を JSON で永続化。保存不可な環境は握りつぶす。</summary>
public sealed class AppSettings
{
    // 上限（実装指示書の推奨値）
    public const int SearchHistoryLimit = 50;
    public const int PerFileStateLimit = 500;
    public const int RecentFilesLimit = 15; // V1.1.1 §3-5

    /// <summary>UI 言語（"ja" / "en"）。null なら OS の UI カルチャに従う。</summary>
    public string? Language { get; set; }

    // ── UVP: Polar ライセンス認証（実装指示書_UVP_Polarライセンス認証）。UVF では未使用 ──
    public LicenseData License { get; set; } = new();

    // ── Ver1.1: 色分けハイライタ ──
    public HighlighterConfig Highlighters { get; set; } = new();

    // ── Ver1.1: A 検索履歴・定義済みフィルタ ──
    public List<string> SearchHistory { get; set; } = new();
    public List<PredefinedFilter> PredefinedFilters { get; set; } = new();

    // ── Ver1.1: B Follow 中の自動更新 ──
    public bool FollowAutoRefresh { get; set; } = true;

    // ── Ver1.1: C ファイルごとの状態復元 ──
    public List<PerFileState> PerFileStates { get; set; } = new();

    // ── Ver1.1 / V1.1.1: D セッション・最近・お気に入り ──
    public SessionState? LastSession { get; set; }
    public bool RestoreSession { get; set; } = true;
    public List<RecentEntry> RecentFiles { get; set; } = new();
    public List<string> Favorites { get; set; } = new();

    /// <summary>
    /// 設定フォルダ名（%AppData%/&lt;この名前&gt;/settings.json）。
    /// 既定は "UwView"（UVF）。UVP は起動時に "UwViewPro" へ変更し、UVF と設定・ライセンスを分離する。
    /// </summary>
    public static string AppDataFolder = "UwView";

    private static string DirFor(string folder) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), folder);

    [JsonIgnore]
    public static string FilePath => Path.Combine(DirFor(AppDataFolder), "settings.json");

    /// <summary>
    /// 独自フォルダへ切替えた直後、現行ファイルが無ければ旧共有ファイルから一度だけ移行コピーする。
    /// （UVP を UwView 共有ファイルから UwViewPro 専用ファイルへ移す際にライセンス等を引き継ぐ）
    /// </summary>
    public static void SeedFromLegacyIfMissing(string legacyFolder)
    {
        try
        {
            var current = FilePath;
            if (File.Exists(current)) return;
            var legacy = Path.Combine(DirFor(legacyFolder), "settings.json");
            if (!File.Exists(legacy)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(current)!);
            File.Copy(legacy, current);
        }
        catch { /* 移行できなくても新規既定で続行 */ }
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
            // 破損に備え原子的書き込み（temp→rename・V1.1.1 §2-2）
            var json = JsonSerializer.Serialize(this, SettingsJsonContext.Default.AppSettings);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch { /* 保存不可（WASM 等）は無視 */ }
    }

    // ── 検索履歴・最近ファイルの MRU 更新ヘルパ ──

    public void PushSearchHistory(string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return;
        SearchHistory.RemoveAll(p => p == pattern);
        SearchHistory.Insert(0, pattern);
        if (SearchHistory.Count > SearchHistoryLimit)
            SearchHistory.RemoveRange(SearchHistoryLimit, SearchHistory.Count - SearchHistoryLimit);
    }

    /// <summary>最近使ったファイルへ追加（新しい順・重複排除・上限15・V1.1.1 §2-3）。</summary>
    public void PushRecentFile(string path, long nowUtcTicks)
    {
        if (string.IsNullOrEmpty(path)) return;
        RecentFiles.RemoveAll(e => PathEq(e.Path, path));
        RecentFiles.Insert(0, new RecentEntry { Path = path, LastOpenedUtcTicks = nowUtcTicks });
        if (RecentFiles.Count > RecentFilesLimit)
            RecentFiles.RemoveRange(RecentFilesLimit, RecentFiles.Count - RecentFilesLimit);
    }

    /// <summary>存在しなくなった Recent/Favorites を間引く（読込時・クリック時）。</summary>
    public void PruneMissing()
    {
        RecentFiles.RemoveAll(e => !File.Exists(e.Path));
        Favorites.RemoveAll(p => !File.Exists(p));
    }

    public bool IsFavorite(string path) => Favorites.Exists(p => PathEq(p, path));

    /// <summary>お気に入りを追加/解除（追加順保持・重複排除）。追加したら true。</summary>
    public bool ToggleFavorite(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        int i = Favorites.FindIndex(p => PathEq(p, path));
        if (i >= 0) { Favorites.RemoveAt(i); return false; }
        Favorites.Add(path);
        return true;
    }

    private static bool PathEq(string a, string b) =>
        string.Equals(a, b, System.OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>per-file 状態を upsert（LRU: 先頭が最新、上限超で末尾を捨てる）。</summary>
    public void UpsertPerFileState(PerFileState state)
    {
        PerFileStates.RemoveAll(s => s.PathKey == state.PathKey);
        PerFileStates.Insert(0, state);
        if (PerFileStates.Count > PerFileStateLimit)
            PerFileStates.RemoveRange(PerFileStateLimit, PerFileStates.Count - PerFileStateLimit);
    }

    public PerFileState? FindPerFileState(string pathKey, long length, long mtimeTicks)
    {
        var s = PerFileStates.Find(x => x.PathKey == pathKey);
        // 別物（長さ/mtime 不一致）なら復元しない（.uwvz の検証キーと同じ考え方）
        return s is not null && s.Length == length && s.MtimeTicks == mtimeTicks ? s : null;
    }
}

// トリミング/AOT 安全な source-generated シリアライザ（AppSettings から到達する型は自動包含）
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>ハイライタセットのエクスポート/インポート（.uwvhl・実装指示書 §4）。</summary>
public static class HighlighterIo
{
    /// <summary>セット群を .uwvhl（JSON 配列）で書き出す。</summary>
    public static string Export(IEnumerable<HlSet> sets)
        => JsonSerializer.Serialize(new List<HlSet>(sets), HlJsonContext.Default.ListHlSet);

    /// <summary>.uwvhl を読み、既存 ID と衝突しないものだけ返す（klogg 準拠・同一IDはスキップ）。</summary>
    public static List<HlSet> Import(string json, IEnumerable<string> existingIds)
    {
        var incoming = JsonSerializer.Deserialize(json, HlJsonContext.Default.ListHlSet) ?? new List<HlSet>();
        var have = new HashSet<string>(existingIds);
        var result = new List<HlSet>();
        foreach (var s in incoming)
        {
            if (string.IsNullOrEmpty(s.Id) || have.Contains(s.Id)) continue;
            s.ReadOnly = false; // 取り込んだセットは編集可
            result.Add(s);
            have.Add(s.Id);
        }
        return result;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<HlSet>))]
internal sealed partial class HlJsonContext : JsonSerializerContext
{
    // 生成器が List<HlSet> を ListHlSet として公開
}
