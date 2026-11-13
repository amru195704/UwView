using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwView.Core;
using UwView.Localization;

namespace UwView.Views;

/// <summary>
/// 支援の案内の帯（指示-20261113-05・案 A／C）。メイン画面の下（ステータスバーの上）に出す。
/// <b>モーダルにしない</b>（出ていても本文・検索はそのまま使える）。
///
/// ボタンは［支援する（GitHub Sponsors）］［後で］［もう表示しない］の 3 つ（この順）と、右端の ×。
/// ［後で］と × は同じ（閉じるだけ。出した時点で「出した」と記録済みなので、90 日後に 1 回だけ出る）。
/// 記録の書き換えと保存は呼び出し側（<see cref="MainView"/>）が <see cref="Chose"/> を受けて行う。
/// 文面は <see cref="SupportPromptText"/>（宣伝部の文案）。
/// </summary>
public sealed class SupportBannerView : Border
{
    /// <summary>押されたボタン。</summary>
    public enum Choice
    {
        /// <summary>［支援する］。Sponsors を開き、以後は出さない。</summary>
        Support,
        /// <summary>［後で］または ×（閉じるだけ）。</summary>
        Later,
        /// <summary>［もう表示しない］。</summary>
        Never,
    }

    /// <summary>ボタンが押されたとき（帯はもう閉じている）。</summary>
    public event Action<Choice>? Chose;

    /// <summary>リンクを開く係（既定はブラウザ。テストで差し替える）。</summary>
    public Action<string> OpenUrl { get; set; }

    /// <summary>出している帯の種類。</summary>
    public SupportPromptKind Kind { get; }

    private static bool Ja => Localizer.Instance.Culture.TwoLetterISOLanguageName == "ja";

    public SupportBannerView(SupportPromptKind kind)
    {
        Kind = kind;
        Name = "SupportBanner";
        OpenUrl = url =>
        {
            if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
                _ = launcher.LaunchUriAsync(new Uri(url));
        };
        Background = UwView.Services.ThemeColors.Get("Uv_F5F5F5", UwView.Services.ThemeColors.Surface);
        BorderBrush = UwView.Services.ThemeColors.Get("Uv_CCCCCC", Brushes.LightGray);
        BorderThickness = new Thickness(0, 1, 0, 0);
        Padding = new Thickness(10, 6);
        Child = Build();
    }

    private Control Build()
    {
        bool ja = Ja;
        var text = UwView.Services.ThemeColors.Text;

        var close = new Button
        {
            Name = "SupportCloseButton",
            Content = "×",
            Padding = new Thickness(8, 0),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        ToolTip.SetTip(close, SupportPromptText.LaterTip(ja));
        close.Click += (_, _) => Finish(Choice.Later);

        var support = new Button { Name = "SupportSponsorButton", Content = SupportPromptText.SupportButton(ja), Padding = new Thickness(12, 4) };
        support.Click += (_, _) =>
        {
            OpenUrl(SupportPrompt.SponsorsUrl);
            Finish(Choice.Support);
        };
        var later = new Button { Name = "SupportLaterButton", Content = SupportPromptText.LaterButton(ja), Padding = new Thickness(12, 4) };
        ToolTip.SetTip(later, SupportPromptText.LaterTip(ja));
        later.Click += (_, _) => Finish(Choice.Later);
        var never = new Button { Name = "SupportNeverButton", Content = SupportPromptText.NeverButton(ja), Padding = new Thickness(12, 4) };
        never.Click += (_, _) => Finish(Choice.Never);

        // 本文 4（Pro の購入）は［支援する］の横に小さく。押すと Pro の紹介ページ
        var pro = LinkText("SupportProLink", SupportPromptText.ProLine(ja), () => SiteLinks.PaidEditionLink);
        pro.FontSize = 12;

        var report = LinkText("SupportReportLink", SupportPromptText.ReportLink(ja), () => SupportPromptText.IssuesUrl);

        var body = new StackPanel { Spacing = 3 };
        body.Children.Add(new TextBlock
        {
            Name = "SupportTitle",
            Text = SupportPromptText.Title(Kind, ja),
            FontWeight = FontWeight.Bold,
            Foreground = text,
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new TextBlock
        {
            Name = "SupportBody1",
            Text = SupportPromptText.Body1(Kind, ja) + " " + SupportPromptText.Body2(ja),
            Foreground = text,
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new WrapPanel
        {
            Children =
            {
                new TextBlock { Text = SupportPromptText.Body3(ja) + " ", Foreground = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center },
                report,
            },
        });
        body.Children.Add(new WrapPanel
        {
            Margin = new Thickness(0, 4, 0, 0),
            Children =
            {
                WithMargin(support), WithMargin(pro), WithMargin(later), WithMargin(never),
            },
        });

        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(close, Dock.Right);
        dock.Children.Add(close);
        dock.Children.Add(body);
        return dock;
    }

    private static Control WithMargin(Control c)
    {
        c.Margin = new Thickness(0, 0, 10, 0);
        c.VerticalAlignment = VerticalAlignment.Center;
        return c;
    }

    /// <summary>リンクの見た目の文字（押すとブラウザで開く）。言語は押した時点のものを使う。</summary>
    private TextBlock LinkText(string name, string label, Func<string> url)
    {
        var t = new TextBlock
        {
            Name = name,
            Text = label,
            Foreground = UwView.Services.ThemeColors.Get("Uv_1A6FE8", Brushes.RoyalBlue),
            TextDecorations = TextDecorations.Underline,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(t, url());
        t.PointerPressed += (_, e) =>
        {
            OpenUrl(url());
            e.Handled = true;
        };
        return t;
    }

    private bool _finished;

    private void Finish(Choice choice)
    {
        if (_finished) return;
        _finished = true;
        IsVisible = false;
        Chose?.Invoke(choice);
    }
}
