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
}
