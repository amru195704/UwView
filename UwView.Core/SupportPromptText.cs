namespace UwView.Core;

/// <summary>
/// 支援の案内の文面（宣伝部 UwView宣伝課の文案 A-1〜A-5。2026-11-12）。指示-20261113-05。
/// 金額は書かない。受け皿は GitHub Sponsors だけ（<see cref="SupportPrompt.SponsorsUrl"/>）。
/// 画面の帯（UwView の SupportBannerView）と CLI の 1 行がここを使う。
/// </summary>
public static class SupportPromptText
{
    /// <summary>［報告する］の行き先（GitHub Issues）。</summary>
    public const string IssuesUrl = "https://github.com/amru195704/UwView/issues";

    /// <summary>帯の見出し（A-1／C の時期は A-5）。</summary>
    public static string Title(SupportPromptKind kind, bool ja) => kind == SupportPromptKind.Season
        ? (ja ? "今年もお使いいただき、ありがとうございました" : "Thank you for using UwView this year")
        : (ja ? "UwView をお使いいただき、ありがとうございます" : "Thanks for using UwView");

    /// <summary>
    /// 本文 1（省かない）。C の時期は A-5 の「来年も無料です」。
    /// A-5 の後半（「今年直したことを [1 年の報告] に…」）は、記事の公開を確かめた実 URL が無いので入れていない
    /// （社内規程 §3-3。12/24 公開予定の記事より前に出る版のため）。
    /// </summary>
    public static string Body1(SupportPromptKind kind, bool ja) => kind == SupportPromptKind.Season
        ? (ja ? "UwView（無料版）は来年も無料です。" : "The free edition stays free next year too.")
        : (ja ? "UwView（無料版）は今後も無料です。" : "The free edition of UwView will stay free.");

    /// <summary>本文 2（何に使うか）。</summary>
    public static string Body2(bool ja) => ja
        ? "支援は、3 つの OS（Windows・macOS・Linux）での試験、試験に使う巨大なデータ、配布の費用に使います。"
        : "Support pays for testing on three OSes (Windows, macOS, Linux), the huge test data, and distribution.";

    /// <summary>本文 3（お金以外）。後ろに［報告する］のリンクを付ける。</summary>
    public static string Body3(bool ja) => ja
        ? "お金でなくても、うまく動かなかった報告や、手元の巨大ファイルで測った結果が助かります →"
        : "Not money? Reports of what didn't work and measurements from your own huge files help too →";

    /// <summary>本文 3 のリンクの文字。</summary>
    public static string ReportLink(bool ja) => ja ? "報告する" : "Report";

    /// <summary>本文 4（小さめ。［支援する］の横）。リンク先は <see cref="SiteLinks.PaidEditionLink"/>。</summary>
    public static string ProLine(bool ja) => ja
        ? "会社でお使いなら、UwView Pro の購入が支援になります。"
        : "Using it at work? Buying UwView Pro supports it.";

    /// <summary>ボタン 1。</summary>
    public static string SupportButton(bool ja) => ja ? "支援する（GitHub Sponsors）" : "Support (GitHub Sponsors)";

    /// <summary>ボタン 2。</summary>
    public static string LaterButton(bool ja) => ja ? "後で" : "Later";

    /// <summary>ボタン 2 のツールチップ。</summary>
    public static string LaterTip(bool ja) => ja ? "90 日後にもう一度だけ表示します" : "We'll show this once more in 90 days.";

    /// <summary>ボタン 3。</summary>
    public static string NeverButton(bool ja) => ja ? "もう表示しない" : "Don't show again";

    /// <summary>設定画面の項目名（A-3）。</summary>
    public static string SettingLabel(bool ja) => ja ? "支援の案内を表示しない" : "Don't show support messages";

    /// <summary>
    /// 設定画面の説明（A-3）。文案の「年 1 回まで」は、実装（最初の帯と［後で］の 90 日後の 1 回、年末の呼びかけ）と
    /// 1 年目に合わないので「間は 90 日以上あけます」に言い換えた（判断メモ・指示-20261113-05）。
    /// </summary>
    public static string SettingHint(bool ja) => ja
        ? "1 か月ほどお使いいただいた後と年末に、画面の下に支援の案内を表示します（間は 90 日以上あけます）。使った日数はこのパソコンの中だけで数え、外部には送りません。"
        : "After about a month of use, and at the end of the year, UwView shows a support message at the bottom of the window (at least 90 days apart). Days of use are counted only on this computer and are never sent anywhere.";

    /// <summary>
    /// CLI（<c>uvf --version</c>／<c>--help</c>）の最後の 1 行（A-4）。URL は行末に置く。
    /// 日英は設定の言語で出し分ける（1 行だけにするため。判断メモ）。
    /// </summary>
    public static string CliLine(bool ja) => ja
        ? "UwView（無料版）は今後も無料です。支援（任意）: " + SupportPrompt.SponsorsUrl
        : "UwView stays free. Support (optional): " + SupportPrompt.SponsorsUrl;
}
