using System;
using System.IO;
using System.Linq;
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
            () => new CommandLineViewModel(new UvfCommandBackend(new UvfGuiTarget(ShowCommandFileAsync, ShowCommandManyAsync)),
                                           DefaultCommandFolder()),
            ShowCommandResultAsync);
    }

    /// <summary>基準フォルダーの既定：今のタブのファイルのフォルダー（§3.1）。</summary>
    private string? DefaultCommandFolder()
        => _vm?.ActiveTab?.FilePath is { Length: > 0 } path ? Path.GetDirectoryName(path) : null;

    /// <summary>1 本のファイルの結果：開いて（開いていればそのタブで）、今の結果一覧に出す（§4）。</summary>
    internal async Task ShowCommandFileAsync(string file, string pattern, string? hits, string? options)
    {
        if (_vm is null) return;
        if (_vm.Tabs.FirstOrDefault(t => SamePath(t.FilePath, file)) is { } open) _vm.ActiveTab = open;
        else await OpenPathsAsync([file]);
        if (SamePath(_vm.ActiveTab?.FilePath, file)) await SearchFromCliAsync(pattern, hits, options);
    }

    /// <summary>複数ファイルの結果：前の結果を片付けてから、複数ファイルの結果として出す（§4）。</summary>
    internal async Task ShowCommandManyAsync(string handoffPath)
    {
        ForgetMultiResult();
        await StartMultiAsync(handoffPath);
    }

    private static bool SamePath(string? a, string? b)
        => a is { Length: > 0 } && b is { Length: > 0 }
           && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
                            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    /// <summary>行・集計・流れを本体の窓に出す（ViewModel が返した受け渡しを呼ぶ）。</summary>
    private static async Task ShowCommandResultAsync(CommandRunResult result)
    {
        if (result.ShowInGui is { } show) await show();
    }
}
