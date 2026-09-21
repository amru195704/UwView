namespace UwView.Core;

/// <summary>
/// 検索・走査に使うスレッド（シャード）の本数を決める係（Wide Field v1.7.0 段階1-a）。
///
/// これまでは 8 本固定だった。32コア級の機械で使い切れないので、設定できるようにする。
/// <b>既定は 8 のまま</b>（<c>--tune</c> の実測が3台ぶん揃うまで変えない。指示書 §3 段階1-a）。
///
/// 決め方は「環境変数 &gt; 設定ファイル &gt; 既定」。上限は論理プロセッサ数で、
/// 超える指定は丸めて<b>知らせる</b>（黙って変えると、設定した本数で動いていると誤解される）。
/// </summary>
public static class ThreadBudget
{
    /// <summary>当面の既定。3台の実測が出るまで動かさない。</summary>
    public const int Default = 8;

    /// <summary>設定ファイルのキー名（画面の設定と同じ名前）。</summary>
    public const string SettingsKey = "MaxThreads";

    /// <summary>uvf 側の環境変数名。</summary>
    public const string FreeEnvironmentVariable = "UVF_MAX_THREADS";

    /// <summary>uvp 側の環境変数名。</summary>
    public const string ProEnvironmentVariable = "UVP_MAX_THREADS";

    /// <summary>この機械の論理プロセッサ数（上限）。テストで差し替えられる。</summary>
    public static int LogicalProcessors { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// 使う本数を決める。
    /// </summary>
    /// <param name="environmentValue">環境変数の値（未設定なら null）。</param>
    /// <param name="configured">設定ファイルの値（無ければ 0 以下）。</param>
    /// <param name="warn">読み飛ばした・丸めたときの知らせ先（null なら黙る）。</param>
    public static int Resolve(string? environmentValue, int configured, Action<string>? warn = null)
    {
        if (environmentValue is { Length: > 0 })
        {
            if (int.TryParse(environmentValue.Trim(), out int fromEnv) && fromEnv > 0)
                return Clamp(fromEnv, warn);
            warn?.Invoke($"スレッド数の指定が読めません: {environmentValue}（1 以上の数を指定してください）");
        }
        return configured > 0 ? Clamp(configured, warn) : Clamp(Default, warn: null);
    }

    /// <summary>環境変数と設定ファイルから決める（CLI 用の入口）。</summary>
    public static int Resolve(bool pro, string appDataFolder, Action<string>? warn = null)
        => Resolve(Environment.GetEnvironmentVariable(pro ? ProEnvironmentVariable : FreeEnvironmentVariable),
                   Cli.CliSettings.ReadInt(appDataFolder, SettingsKey, 0), warn);

    private static int Clamp(int value, Action<string>? warn)
    {
        int max = Math.Max(1, LogicalProcessors);
        if (value <= max) return Math.Max(1, value);
        warn?.Invoke($"スレッド数 {value} はこの機械の論理プロセッサ数 {max} を超えるので {max} にします");
        return max;
    }
}
