using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwView.Core;
using UwView.Localization;

namespace UwView.Views;

/// <summary>圧縮ファイルを開くときに選んだ方式。</summary>
public enum CompressedOpenMethod
{
    Cancel,
    /// <summary>テキストに展開してから通常オープン（無料・UVF/UVP 共通）。</summary>
    Expand,
    /// <summary>.uwvz へ直接変換して開く（Pro のみ・平文を作らない）。</summary>
    ConvertToUwvz,
}

/// <summary>
/// 圧縮ファイル（gz / zip）を開くときの方式選択。
///
/// 「テキストに展開して開く」は無料の基本機能——`gunzip` してから開く手作業を1クリックにする。
/// 「.uwvz に変換して開く」は Pro 機能（平文を一度も作らないのでディスクが約1/9・次回は瞬時）。
///
/// Pro 行は<b>公開リポジトリ側には実装を持たない</b>。UVP が起動時に
/// <see cref="ProConverterAvailable"/> を立てるまでは、行はグレーアウトのまま
/// クリックで Pro 案内を出す（UVF ビルドと未購入時はここに留まる）。
/// </summary>
public static class CompressedOpenDialog
{
    /// <summary>
    /// Pro の「.uwvz へ直接変換」が使えるか。UVP が起動時に設定する
    /// （ライセンスが閲覧可のときだけ true）。公開版は常に false。
    /// </summary>
    public static Func<bool>? ProConverterAvailable { get; set; }

    /// <summary>Pro 案内を開く（UVP 未購入時の案内。公開版は購入ページ）。</summary>
    public static Action? ShowProInfo { get; set; }

    /// <summary>前回選んだ方式（設定から復元して次回の既定にする）。</summary>
    public static CompressedOpenMethod LastChoice { get; set; } = CompressedOpenMethod.Expand;

    /// <summary>
    /// 自動テスト用の差し替え。<c>ShowDialog</c> は誰かが閉じるまで戻らないので、
    /// Headless テストからは本物のダイアログを開かずに「選んだ結果」を返させる。
    /// </summary>
    internal static Func<string, CompressedKind, Task<CompressedOpenMethod>>? AskOverride { get; set; }

    /// <summary>自動テスト用の差し替え（お知らせダイアログ）。文言はここに届く。</summary>
    internal static Action<string>? NoticeOverride { get; set; }

    /// <summary>
    /// 同じ差し替えを UVP 側のテストからも使うための口（別アセンブリなので internal では届かない）。
    /// </summary>
    public static Action<string>? NoticeOverrideForPro
    {
        get => NoticeOverride;
        set => NoticeOverride = value;
    }

    /// <summary>方式選択の差し替え（UVP 側のテスト用）。</summary>
    public static Func<string, CompressedKind, Task<CompressedOpenMethod>>? AskOverrideForPro
    {
        get => AskOverride;
        set => AskOverride = value;
    }

    private static bool Ja => Localizer.Instance.Culture.TwoLetterISOLanguageName == "ja";
    private static string T(string ja, string en) => Ja ? ja : en;

    /// <summary>受け付けられない理由の説明文（黙って失敗させないため、必ず理由を出す）。</summary>
    public static string RejectMessage(CompressedReject reject, string fileName) => reject switch
    {
        CompressedReject.NotGzip => T($"{fileName} は gzip ではありません（先頭の目印が違います）。",
                                      $"{fileName} is not a gzip file (wrong magic bytes)."),
        CompressedReject.NotZip => T($"{fileName} は zip ではありません（先頭の目印が違います）。",
                                     $"{fileName} is not a zip file (wrong magic bytes)."),
        CompressedReject.TarArchive => T(
            $"{fileName} は tar アーカイブです。tar は対応していません（先に展開してください）。",
            $"{fileName} is a tar archive. tar is not supported — please extract it first."),
        CompressedReject.NestedGzip => T(
            $"{fileName} は gzip が二重にかかっています。対応していません。",
            $"{fileName} is gzip-compressed twice, which is not supported."),
        CompressedReject.Corrupt => T($"{fileName} は壊れています（gzip として読めません）。",
                                      $"{fileName} is corrupted and cannot be read as gzip."),
        CompressedReject.Unreadable => T($"{fileName} を読めませんでした。",
                                         $"{fileName} could not be read."),
        _ => "",
    };

    /// <summary>
    /// zip はまだ開けない（エントリ選択は次の工程）。gz の展開に通すと
    /// 正しい zip でも「壊れています」と出てしまうので、理由を出して止める。
    /// </summary>
    public static string ZipNotSupportedMessage(string fileName) => T(
        $"{fileName} は zip です。zip を直接開く機能は準備中です（今後の版で対応します）。"
        + "いまは展開してから開いてください。",
        $"{fileName} is a zip file. Opening zip files directly is not available yet (coming in a later version). "
        + "Please extract it first.");

    /// <summary>展開が失敗したときの説明（切り詰め・破損）。</summary>
    public static string CorruptAfterExpandMessage(string fileName) => T(
        $"{fileName} は途中で切れているか壊れています（gzip の照合が合いません）。"
        + "途中までのファイルは残していません。",
        $"{fileName} is truncated or corrupted (the gzip trailer does not match). "
        + "No partial file was left behind.");

    /// <summary>空き容量が足りなさそうなときの警告（続行は禁止しない）。</summary>
    public static string LowSpaceMessage(long guessBytes, long freeBytes) => T(
        $"展開後は最大 {Mb(guessBytes)} 程度になる見込みですが、空きは {Mb(freeBytes)} です。"
        + "途中で容量が足りなくなる可能性があります。続けますか？",
        $"The expanded file may need up to about {Mb(guessBytes)}, but only {Mb(freeBytes)} is free. "
        + "It may run out of space. Continue?");

    private static string Mb(long bytes) => bytes >= 1L << 30
        ? $"{bytes / (double)(1L << 30):F1} GB"
        : $"{bytes / (double)(1L << 20):F0} MB";

    /// <summary>方式を選ばせる。閉じられたら <see cref="CompressedOpenMethod.Cancel"/>。</summary>
    public static async Task<CompressedOpenMethod> AskAsync(Window owner, string fileName, CompressedKind kind)
    {
        if (AskOverride is { } stub) return await stub(fileName, kind);

        var result = CompressedOpenMethod.Cancel;
        bool proOk = ProConverterAvailable?.Invoke() ?? false;

        var dialog = new Window
        {
            Title = T("圧縮ファイルを開く", "Open a compressed file"),
            Width = 560,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var expand = MethodButton(
            T("テキストに展開して開く", "Expand to text and open"),
            T("圧縮を解いたファイルを同じフォルダに作ってから開きます（`gunzip` してから開くのと同じ結果）。",
              "Writes the decompressed file next to the original, then opens it (same result as running `gunzip` first)."));
        expand.Click += (_, _) => { result = CompressedOpenMethod.Expand; dialog.Close(); };

        var convert = MethodButton(
            T(".uwvz に変換して開く（Pro）", "Convert to .uwvz and open (Pro)"),
            T("平文を一度も作らずに圧縮キャッシュへ直接変換します。ディスクは約1/9で、次回からは瞬時に開きます。",
              "Converts straight into the compressed cache without ever writing the plain text. About 1/9 the disk, and instant to reopen."));
        convert.IsEnabled = proOk;
        if (proOk)
            convert.Click += (_, _) => { result = CompressedOpenMethod.ConvertToUwvz; dialog.Close(); };

        // グレーアウトした行は押せないので、行ごと包んで案内を拾う
        Control convertRow = convert;
        if (!proOk)
        {
            var upsell = new Button
            {
                Content = convert.Content,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(14, 10),
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)),
            };
            ToolTip.SetTip(upsell, T("UwView Pro の機能です（クリックで案内）",
                                     "A UwView Pro feature — click for details"));
            upsell.Click += (_, _) => ShowProInfo?.Invoke();
            convertRow = upsell;
        }

        var cancel = new Button
        {
            Content = T("やめる", "Cancel"),
            MinWidth = 96,
            IsCancel = true,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        cancel.Click += (_, _) => { result = CompressedOpenMethod.Cancel; dialog.Close(); };

        string head = kind == CompressedKind.Zip
            ? T($"{fileName} は zip です。どう開きますか？", $"{fileName} is a zip file. How should it be opened?")
            : T($"{fileName} は gzip です。どう開きますか？", $"{fileName} is a gzip file. How should it be opened?");

        var body = new StackPanel { Margin = new Thickness(22), Spacing = 12 };
        body.Children.Add(new TextBlock
        {
            Text = head, TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Black, FontWeight = FontWeight.Bold,
        });
        // Pro が使えるときは「.uwvz に変換」を上（＝既定の勧め）に置く
        if (proOk) { body.Children.Add(convertRow); body.Children.Add(expand); }
        else { body.Children.Add(expand); body.Children.Add(convertRow); }
        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel },
        });
        dialog.Content = body;

        await dialog.ShowDialog(owner);
        if (result != CompressedOpenMethod.Cancel) LastChoice = result;
        return result;
    }

    private static Button MethodButton(string title, string detail) => new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Left,
        Padding = new Thickness(14, 10),
        Content = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeight.Bold },
                new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, FontSize = 12,
                                Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0x55, 0x60)) },
            },
        },
    };
}
