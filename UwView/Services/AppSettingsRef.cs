namespace UwView.Services;

/// <summary>
/// 共有 ViewModel（UVF/UVP 両用）から現在の AppSettings を参照するための間接ホルダ。
/// App（UVF）・ProApp（UVP）が起動時に自分の Settings を設定する。
/// </summary>
public static class AppSettingsRef
{
    public static AppSettings Current { get; set; } = new();
}
