using System;

namespace UwView.Core;

/// <summary>
/// UwView 公式サイト・外部リンクの URL を集約（UVF/UVP 共有）。
/// 指示書 2-ops/管理部/指示書_UVF-UVP_アプリ内ヘルプに公式サイトリンク追加.md（2026-07-20）。
///
/// サイトは日英2言語。英語ページは <c>/en/&lt;slug&gt;-en/</c> に実在するので、
/// アプリの表示言語に合わせて <see cref="For"/> で選ぶ（英語UIから日本語ページへ飛ばさない）。
/// URL は WP REST API で実在確認済み（2026-08-20）。
/// </summary>
public static class SiteLinks
{
    /// <summary>公式サイト・最新情報（トップ＝記事一覧）。</summary>
    public const string Official = "https://uvp.y42u.net/";
    public const string OfficialEn = "https://uvp.y42u.net/en/";

    /// <summary>概要（UwViewとは）。</summary>
    public const string About = "https://uvp.y42u.net/about/";
    public const string AboutEn = "https://uvp.y42u.net/en/about-en/";

    /// <summary>オンラインヘルプ（使い方・入手）。</summary>
    public const string Help = "https://uvp.y42u.net/help/";
    public const string HelpEn = "https://uvp.y42u.net/en/help-en/";

    /// <summary>お問い合わせ・サポート。</summary>
    public const string Support = "https://uvp.y42u.net/support/";
    public const string SupportEn = "https://uvp.y42u.net/en/support-en/";

    /// <summary>Pro 版の紹介。</summary>
    public const string Pro = "https://uvp.y42u.net/pro/";
    public const string ProEn = "https://uvp.y42u.net/en/pro-en/";

    /// <summary>ダウンロード。</summary>
    public const string Download = "https://uvp.y42u.net/download/";
    public const string DownloadEn = "https://uvp.y42u.net/en/download-en/";

    /// <summary>プライバシーポリシー。</summary>
    public const string Privacy = "https://uvp.y42u.net/privacy-policy/";
    public const string PrivacyEn = "https://uvp.y42u.net/en/privacy-policy-en/";

    /// <summary>GitHub リポジトリ（無料版 UVF の OSS ホーム）。</summary>
    public const string GitHubRepo = "https://github.com/amru195704/UwView";

    /// <summary>
    /// GitHub は英語版サイトが無いので、英語UIでは英語 README を直接開く。
    /// リポジトリのトップは日本語 README が表示されるため。
    /// </summary>
    public const string GitHubRepoEn = "https://github.com/amru195704/UwView/blob/main/README.en.md";

    /// <summary>作者 GitHub プロフィール。</summary>
    public const string GitHubProfile = "https://github.com/amru195704";

    /// <summary>プロフィールも既定表示は日本語 README なので、英語UIでは英語 README を直接開く。</summary>
    public const string GitHubProfileEn = "https://github.com/amru195704/amru195704/blob/main/README.en.md";

    /// <summary>WASM デモ。</summary>
    public const string WasmDemo = "https://amru195704.github.io/UwView/";

    /// <summary>
    /// 表示言語の判定。アプリ起動時に Localizer を見るデリゲートを差し込む
    /// （UwView.Core は Localizer を参照できないため）。言語切替に追従させるので、
    /// 値ではなく毎回評価する関数として持つ。
    /// </summary>
    public static Func<bool> IsJapanese { get; set; } = () => true;

    // 以下は「表示言語に合う URL」。リンクを開く側はこちらを使うこと
    // （定数を直に使うと、英語UIから日本語ページへ飛んでしまう）。
    public static string OfficialLink => Pick(Official, OfficialEn);
    public static string AboutLink => Pick(About, AboutEn);
    public static string HelpLink => Pick(Help, HelpEn);
    public static string SupportLink => Pick(Support, SupportEn);
    public static string ProLink => Pick(Pro, ProEn);
    public static string DownloadLink => Pick(Download, DownloadEn);
    public static string PrivacyLink => Pick(Privacy, PrivacyEn);
    public static string GitHubRepoLink => Pick(GitHubRepo, GitHubRepoEn);
    public static string GitHubProfileLink => Pick(GitHubProfile, GitHubProfileEn);

    private static string Pick(string ja, string en) => IsJapanese() ? ja : en;
}
