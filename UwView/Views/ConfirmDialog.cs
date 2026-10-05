using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace UwView.Views;

/// <summary>はい/いいえ の確認ダイアログ（Avalonia に標準 MessageBox が無いため自前）。</summary>
public static class ConfirmDialog
{
    public static async Task<bool> AskAsync(Window owner, string title, string message, string yesLabel, string noLabel)
    {
        bool result = false;
        var dialog = new Window
        {
            Title = title,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var yes = new Button { Content = yesLabel, MinWidth = 96, IsDefault = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        var no = new Button { Content = noLabel, MinWidth = 96, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        yes.Click += (_, _) => { result = true; dialog.Close(); };
        no.Click += (_, _) => { result = false; dialog.Close(); };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { yes, no },
                },
            },
        };

        await dialog.ShowDialog(owner);
        return result;
    }

    /// <summary>
    /// 3択（例: 登録し直す／解除する／閉じる）。戻り値は 1・2・0（0 は閉じる）。
    ///
    /// 「いったん解除してから登録し直す」を利用者にやらせると、mac / Linux では
    /// <b>管理者パスワードを2回</b>聞くことになる（9.23修正）。
    /// 選ばせて1回で済ませるために用意した。
    /// </summary>
    public static async Task<int> ChooseAsync(Window owner, string title, string message,
                                              string first, string second, string cancel)
    {
        int result = 0;
        var dialog = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var one = new Button { Content = first, MinWidth = 120, IsDefault = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        var two = new Button { Content = second, MinWidth = 120, HorizontalContentAlignment = HorizontalAlignment.Center };
        var no = new Button { Content = cancel, MinWidth = 96, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        one.Click += (_, _) => { result = 1; dialog.Close(); };
        two.Click += (_, _) => { result = 2; dialog.Close(); };
        no.Click += (_, _) => { result = 0; dialog.Close(); };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { one, two, no },
                },
            },
        };

        await dialog.ShowDialog(owner);
        return result;
    }

    /// <summary>
    /// 2 択＋「次から尋ねない」。戻り値は選んだもの（1・2。閉じたら 0）と、チェックの状態。
    /// </summary>
    public static async Task<(int Choice, bool DontAskAgain)> ChooseRememberAsync(Window owner, string title, string message,
                                                                                 string first, string second, string dontAsk)
    {
        int result = 0;
        var dialog = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        var one = new Button { Name = "ChoiceFirst", Content = first, MinWidth = 120, IsDefault = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        var two = new Button { Name = "ChoiceSecond", Content = second, MinWidth = 120, HorizontalContentAlignment = HorizontalAlignment.Center };
        var check = new CheckBox { Name = "ChoiceDontAsk", Content = dontAsk, Foreground = Brushes.Black };
        one.Click += (_, _) => { result = 1; dialog.Close(); };
        two.Click += (_, _) => { result = 2; dialog.Close(); };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black },
                check,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { one, two },
                },
            },
        };

        await dialog.ShowDialog(owner);
        return (result, result != 0 && check.IsChecked == true);
    }

    /// <summary>お知らせ（ボタンは1つ）。本文は選んでコピーできる（コマンド例を貼り付けて使うため）。</summary>
    public static async Task NoticeAsync(Window owner, string title, string message, string closeLabel)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        var close = new Button { Content = closeLabel, MinWidth = 96, IsDefault = true, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 16,
            Children =
            {
                new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { close } },
            },
        };
        await dialog.ShowDialog(owner);
    }
}
