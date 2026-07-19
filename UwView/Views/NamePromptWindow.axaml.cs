using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UwView.Views;

/// <summary>
/// 名前入力プロンプト（ハイライタセットの「保存」用）。候補（既存セット名＋デフォルト）から
/// 選ぶか自由入力し、OK で名前を返す。ShowDialog&lt;string?&gt; で結果を受け取る。
/// </summary>
public partial class NamePromptWindow : Window
{
    public NamePromptWindow()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<Button>("OkButton")!.Click += (_, _) =>
            Close(this.FindControl<AutoCompleteBox>("NameBox")!.Text?.Trim());
        this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close(null);
    }

    public NamePromptWindow(IEnumerable<string> candidates, string? initial) : this()
    {
        var box = this.FindControl<AutoCompleteBox>("NameBox")!;
        box.ItemsSource = new List<string>(candidates);
        box.Text = initial ?? "";
    }
}
