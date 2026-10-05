using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using UwView.Services;
using UwView.ViewModels;

namespace UwView.Views;

/// <summary>「コマンドライン」ダイアログの入口（v1.8.0 Finder Scope §2）。ツールバー・メニュー・Ctrl+Shift+K。</summary>
public partial class MainView
{
    /// <summary>ダイアログを開く（窓ごとに 1 つ。開いていれば前に出す）。</summary>
    internal void OpenCommandLine()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        CommandLineDialog.ShowFor(owner,
            () => new CommandLineViewModel(new UvfCommandBackend(), DefaultCommandFolder()),
            ShowCommandResultAsync);
    }

    /// <summary>基準フォルダーの既定：今のタブのファイルのフォルダー（§3.1）。</summary>
    private string? DefaultCommandFolder()
        => _vm?.ActiveTab?.FilePath is { Length: > 0 } path ? Path.GetDirectoryName(path) : null;

    /// <summary>行・集計・流れを本体の窓に出す（ViewModel が返した受け渡しを呼ぶ）。</summary>
    private static async Task ShowCommandResultAsync(CommandRunResult result)
    {
        if (result.ShowInGui is { } show) await show();
    }
}
