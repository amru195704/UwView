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
    /// <summary>テキストに展開してから通常オープン。</summary>
    Expand,
    /// <summary>差し込まれた変換で開く（<see cref="CompressedOpenDialog.ConverterAvailable"/> が立っているときだけ）。</summary>
    Convert,
}

/// <summary>
/// 圧縮ファイル（gz / zip）を開くときの方式選択。
///
/// 「テキストに展開して開く」は基本機能——`gunzip` してから開く手作業を1クリックにする。
///
/// 2つ目の行は<b>ここには実装を持たない</b>。呼び手が起動時に
/// <see cref="ConverterAvailable"/> を立て、<see cref="ConverterLabel"/> を入れるまでは、
/// 行はグレーアウトのまま、クリックで有料版の案内を出す。
/// </summary>
public static class CompressedOpenDialog
{
    /// <summary>差し込まれた変換が使えるか（呼び手が起動時に設定する。null なら使えない）。</summary>
    public static Func<bool>? ConverterAvailable { get; set; }

    /// <summary>
    /// 2つ目の行の見出しと説明（日本語・英語）。null なら製品名だけの案内を出す。
    /// </summary>
    public static (string Ja, string En, string DetailJa, string DetailEn)? ConverterLabel { get; set; }

    /// <summary>有料版の案内を開く（グレーアウトした行を押したとき）。</summary>
    public static Action? ShowUpgradeInfo { get; set; }

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
    /// 同じ差し替えを別のアセンブリのテストからも使うための口（internal では届かない）。
    /// </summary>
    public static Action<string>? NoticeOverridePublic
    {
        get => NoticeOverride;
        set => NoticeOverride = value;
    }

    /// <summary>方式選択の差し替え（別のアセンブリのテスト用）。</summary>
    public static Func<string, CompressedKind, Task<CompressedOpenMethod>>? AskOverridePublic
    {
        get => AskOverride;
        set => AskOverride = value;
    }

    private static bool Ja => Localizer.Instance.Culture.TwoLetterISOLanguageName == "ja";
    private static string T(string ja, string en) => Ja ? ja : en;

    /// <summary>
    /// 受け付けられない理由の説明文（黙って失敗させないため、必ず理由を出す）。
    ///
    /// <b>「壊れています」とは言わない。</b>ここで弾くのはほとんどが「1つのファイルを1回 gzip したもの」
    /// という想定と形が違うだけで、ファイル自体は正しい（tar.gz・二重 gzip・別形式など）。
    /// 壊れていると言われた利用者が元のファイルを消してしまう恐れがあるので、
    /// 「読めません（どう違うか）」と伝え、消さないよう添える（9.21修正）。
    /// </summary>
    /// <param name="format">形式名（先頭は読めたが展開できなかったときに言う。既定は gzip）。</param>
    public static string RejectMessage(CompressedReject reject, string fileName, string format = "gzip") => reject switch
    {
        CompressedReject.NotGzip => T(
            $"{fileName} は gzip として読めません（名前は .gz ですが、中身が gzip の形ではありません）。"
            + "ファイルが壊れているとは限りません。別の形式かもしれないので、消さずに確かめてください。",
            $"{fileName} cannot be read as gzip (the name ends with .gz but the contents are not gzip). "
            + "This does not necessarily mean the file is damaged — it may be another format. Please keep it and check."),
        CompressedReject.NotZip => T(
            $"{fileName} は zip として読めません（名前は .zip ですが、中身が zip の形ではありません）。"
            + "ファイルが壊れているとは限りません。別の形式かもしれないので、消さずに確かめてください。",
            $"{fileName} cannot be read as zip (the name ends with .zip but the contents are not zip). "
            + "This does not necessarily mean the file is damaged — it may be another format. Please keep it and check."),
        CompressedReject.TarArchive => T(
            $"{fileName} は複数のファイルをまとめた tar です。このアプリが開けるのは"
            + "「1つのテキストを gzip したもの」だけです（先に展開してください）。",
            $"{fileName} is a tar archive holding several files. This app opens a single gzip-compressed text file "
            + "(please extract it first)."),
        CompressedReject.WrongFormat => T(
            $"{fileName} は名前のとおりの圧縮形式として読めません（中身がその形ではありません）。"
            + "別の形式かもしれません。ファイルは消さないでください。",
            $"{fileName} cannot be read in the format its name suggests (the contents are not in that form). "
            + "It may be another format. Please keep the file."),
        CompressedReject.NestedCompression => T(
            $"{fileName} は圧縮が二重にかかっています。1回だけ圧縮したものを開けます（一度展開してから開いてください）。",
            $"{fileName} is compressed twice. This app opens a file compressed once (please decompress it once first)."),
        CompressedReject.NotText => T(
            $"{fileName} の中身はテキストではありません（画像やデータベースなどを gzip したものに見えます）。"
            + "このアプリで開けるのはテキストだけです。ファイルは消さないでください。",
            $"{fileName} does not contain text (it looks like an image or a database compressed with gzip). "
            + "This app opens text only. Please keep the file."),
        CompressedReject.NestedGzip => T(
            $"{fileName} は gzip が二重にかかっています。1回だけ gzip したものを開けます"
            + "（一度 gunzip してから開いてください）。",
            $"{fileName} is gzip-compressed twice. This app opens a file compressed once "
            + "(please gunzip it once first)."),
        CompressedReject.Corrupt => T(
            $"{fileName} は {format} として読めません（先頭を展開できませんでした）。"
            + "別の形式か、途中で切れている可能性があります。ファイルは消さずに、"
            + $"`{format} -t` などで確かめてください。",
            $"{fileName} cannot be read as {format} (the beginning could not be decompressed). "
            + $"It may be another format, or cut short. Please keep the file and check it, e.g. with `{format} -t`."),
        CompressedReject.Unreadable => T($"{fileName} を読めませんでした（アクセスできないか、使用中かもしれません）。",
                                         $"{fileName} could not be read (no access, or it may be in use)."),
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

    /// <summary>
    /// Word・Excel・PDF を開けないときの説明。古い形式・パスワード付きはその理由
    ///（v1.8.3.2 から uvf でも読めるものは文字を取り出して開くので、読めるものの文は使わない）。
    /// </summary>
    public static string OfficeMessage(UwView.Core.OfficeProbe office, string fileName) => office.IsRejected
        ? T(UwView.Core.OfficeDocumentFile.RejectText(office.Reject, fileName, true),
            UwView.Core.OfficeDocumentFile.RejectText(office.Reject, fileName, false))
        : T(UwView.Core.OfficeDocumentFile.UnsupportedText(fileName, true),
            UwView.Core.OfficeDocumentFile.UnsupportedText(fileName, false));

    /// <summary>展開が最後まで進まなかったときの説明（切り詰め・別形式）。</summary>
    public static string CorruptAfterExpandMessage(string fileName, string format = "gzip") => T(
        $"{fileName} を最後まで読めませんでした（{format} として読み切れません）。"
        + "途中で切れているか、作り方が想定と違うファイルかもしれません。"
        + "展開しかけたファイルは残していません。元のファイルは消さないでください。",
        $"{fileName} could not be read to the end (it could not be read through as {format}). "
        + "It may be cut short, or made in a way this app does not expect. "
        + "No half-expanded file was left behind. Please keep the original file.");

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
        bool proOk = ConverterAvailable?.Invoke() ?? false;

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

        var label = ConverterLabel ?? ("有料版（UwView Pro）で開く", "Open with the paid edition, UwView Pro",
                                       "有料版（UwView Pro）の機能です。", "A feature of the paid edition, UwView Pro.");
        var convert = MethodButton(T(label.Ja, label.En), T(label.DetailJa, label.DetailEn));
        convert.IsEnabled = proOk;
        if (proOk)
            convert.Click += (_, _) => { result = CompressedOpenMethod.Convert; dialog.Close(); };

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
                Foreground = UwView.Services.ThemeColors.Get("Uv_8A8A8A", new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A))),
            };
            ToolTip.SetTip(upsell, T("有料版（UwView Pro）の機能です（クリックで案内）",
                                     "A feature of the paid edition, UwView Pro — click for details"));
            upsell.Click += (_, _) => ShowUpgradeInfo?.Invoke();
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
            : T($"{fileName} は {CompressedFormats.Name(kind)} です。どう開きますか？",
                $"{fileName} is a {CompressedFormats.Name(kind)} file. How should it be opened?");

        var body = new StackPanel { Margin = new Thickness(22), Spacing = 12 };
        body.Children.Add(new TextBlock
        {
            Text = head, TextWrapping = TextWrapping.Wrap,
            Foreground = UwView.Services.ThemeColors.Text, FontWeight = FontWeight.Bold,
        });
        // 差し込まれた変換が使えるときは、それを上（＝既定の勧め）に置く
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
                                Foreground = UwView.Services.ThemeColors.Get("Uv_4A5560", new SolidColorBrush(Color.FromRgb(0x4A, 0x55, 0x60))) },
            },
        },
    };
}
