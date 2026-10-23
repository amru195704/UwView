namespace UwView.Services;

/// <summary>
/// 共有 ViewModel から現在の AppSettings を参照するための間接ホルダ。
/// アプリが起動時に自分の Settings を設定する。
/// </summary>
public static class AppSettingsRef
{
    public static AppSettings Current { get; set; } = new();
}
