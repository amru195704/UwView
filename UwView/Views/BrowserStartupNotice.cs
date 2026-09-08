using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Linq;
using UwView.Core;
using UwView.Localization;
using UwView.ViewModels;

namespace UwView.Views;

/// <summary>
/// ブラウザ版（WASM）の起動時のお断り（オーナー指示 2026-09-09）。
///
/// ブラウザ版は「試しに触ってもらう」ための入口で、扱える大きさに限りがある。
/// 何も言わずに始めると、大きすぎるファイルを開いて「動かないアプリ」だと思われる。
/// 先に読んでもらってから使い始めてもらう。
///
/// WASM には<b>ウィンドウが無い</b>（ShowDialog が使えない）ので、
/// 画面そのものに重ねて、OK を押すまで下の操作をさせない作りにしている。
/// </summary>
public sealed class BrowserStartupNotice : UserControl
{
    private static bool Ja => Localizer.Instance.Culture.TwoLetterISOLanguageName == "ja";
    private static string T(string ja, string en) => Ja ? ja : en;

    private readonly Control _inner;
    private readonly Border _overlay;

    /// <summary>お断りの中身。言語を切り替えたら作り直す。</summary>
    private readonly ContentControl _card = new();

    /// <summary>OK が押されて、下の画面が使えるようになったとき。</summary>
    public event Action? Accepted;

    /// <summary>まだお断りを出しているか（自動テスト用）。</summary>
    public bool IsShowing => _overlay.IsVisible;

    public BrowserStartupNotice(Control inner)
    {
        _inner = inner;
        _inner.IsEnabled = false;      // 覆っていてもキーボードは通るので、明示的に止める

        _card.Content = BuildCard();
        _overlay = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x20, 0x24, 0x28)),
            Child = _card,
        };

        Content = new Panel { Children = { _inner, _overlay } };
    }

    private Control BuildCard()
    {
        var ok = new Button
        {
            Name = "NoticeOkButton",
            Content = "OK",
            Width = 140,
            Padding = new Thickness(16, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        ToolTip.SetTip(ok, T("閉じてブラウザ版を使い始める", "Close this and start using the browser version"));
        ok.Click += (_, _) => Dismiss();

        var card = new StackPanel { Spacing = 14 };

        // 見出しと言語の切り替え。ここで切り替えられないと、日本語が読めない人は
        // 何を言われているのか分からないまま OK を押すことになる
        card.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                new TextBlock
                {
                    Text = T("ブラウザ版 UwView へようこそ", "Welcome to UwView for the browser"),
                    FontSize = 20,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.Black,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    VerticalAlignment = VerticalAlignment.Center,
                    [Grid.ColumnProperty] = 1,
                    Children = { LangButton("日本語", "ja"), LangButton("English", "en") },
                },
            },
        });
        card.Children.Add(new TextBlock
        {
            Text = T("お使いになる前に3点だけ。", "Three things before you start."),
            Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap,
        });

        card.Children.Add(Bullet("1.",
            T("3GB（1億行）程度のファイルまでは、問題なく動きます。",
              "Files up to about 3 GB (100 million lines) work fine here.")));
        card.Children.Add(Bullet("2.",
            T("それ以上の大きさを扱うときは、デスクトップ版の UwView をお使いください。",
              "For anything larger, please use the UwView desktop application.")));
        card.Children.Add(Bullet("3.",
            T("多段階検索などの高度な検索には UwView Pro をご利用ください。編集できるアップグレードもあります。",
              "For advanced searching such as drill-down, use UwView Pro. An upgrade that adds editing is also available.")));

        card.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 4, 0, 0),
            Children =
            {
                LinkButton(T("デスクトップ版を入手（無料）", "Get the desktop version (free)"),
                           () => SiteLinks.DownloadLink,
                           T("無料版 UwView の入手ページを開く", "Open the download page for the free UwView")),
                LinkButton(T("UwView Pro を見る", "See UwView Pro"),
                           () => SiteLinks.ProLink,
                           T("UwView Pro の紹介ページを開く", "Open the UwView Pro product page")),
            },
        });

        card.Children.Add(ok);

        return new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(28, 24),
            MaxWidth = 620,
            Margin = new Thickness(20),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new ScrollViewer { Content = card },
        };
    }

    private static Control Bullet(string mark, string text) => new Grid
    {
        ColumnDefinitions = new ColumnDefinitions("26,*"),
        Children =
        {
            new TextBlock { Text = mark, Foreground = Brushes.Black, FontWeight = FontWeight.Bold },
            new TextBlock
            {
                Text = text,
                Foreground = Brushes.Black,
                TextWrapping = TextWrapping.Wrap,
                [Grid.ColumnProperty] = 1,
            },
        },
    };

    /// <summary>言語の切り替えボタン。いま選ばれている方は押せないようにして分かるようにする。</summary>
    private Button LangButton(string label, string code)
    {
        bool current = Localizer.Instance.Culture.TwoLetterISOLanguageName == code;
        var b = new Button
        {
            Name = "Lang" + code,
            Content = label,
            Padding = new Thickness(10, 4),
            IsEnabled = !current,
            FontWeight = current ? FontWeight.Bold : FontWeight.Normal,
        };
        ToolTip.SetTip(b, code == "ja" ? "日本語で表示する" : "Show in English");
        b.Click += (_, _) => SetLanguage(code);
        return b;
    }

    /// <summary>
    /// 表示言語を切り替える。
    ///
    /// 本体の言語選択と同じ経路（<see cref="MainViewModel.SelectedLanguage"/>）を通す——
    /// あちらの処理が設定の保存や文字コードラベルの作り直しまで面倒を見ているので、
    /// ここで別に書くと片方だけ古いままになる。本体が無い場合（自動テスト等）だけ直接切り替える。
    /// </summary>
    private void SetLanguage(string code)
    {
        if (_inner.DataContext is MainViewModel vm
            && vm.Languages.FirstOrDefault(l => l.Code == code) is { } option)
        {
            vm.SelectedLanguage = option;
        }

        if (Localizer.Instance.Culture.TwoLetterISOLanguageName != code)
        {
            Localizer.Instance.SetLanguage(code);
            var settings = Services.AppSettingsRef.Current;
            settings.Language = code;
            settings.Save();
        }

        _card.Content = BuildCard();   // 文言はコードで組んでいるので作り直す
    }

    /// <summary>リンクは押した時点の表示言語で開く（起動後に切り替えられても正しい方へ行く）。</summary>
    private Button LinkButton(string label, Func<string> url, string tip)
    {
        var b = new Button { Content = label, Padding = new Thickness(12, 6) };
        ToolTip.SetTip(b, tip);
        b.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
                await launcher.LaunchUriAsync(new Uri(url()));
        };
        return b;
    }

    /// <summary>お断りを畳んで、下の画面を使えるようにする。</summary>
    public void Dismiss()
    {
        if (!_overlay.IsVisible) return;
        _overlay.IsVisible = false;
        _inner.IsEnabled = true;
        _inner.Focus();
        Accepted?.Invoke();
    }
}
