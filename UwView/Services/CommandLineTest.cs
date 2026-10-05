using System;
using System.IO;
using UwView.Localization;
using UwView.ViewModels;

namespace UwView.Services;

/// <summary>
/// 「コマンドライン」ダイアログの試験用の口（v1.8.0 Finder Scope §9）。画面を出さずに、
/// ダイアログの［検索実行］と同じ入口（<see cref="CommandLineViewModel"/>）で走らせ、出力欄に出るものを標準出力に書く。
/// 管理部の <c>uv_gui_cmd_test.sh</c> が、同じ指定のコマンドの標準出力と <c>cmp</c> で突き合わせる。
///
/// <c>UwView.Desktop --cmd-test &lt;基準フォルダー&gt; &lt;ファイル指定&gt; &lt;検索パターン&gt;</c>
/// 検索も窓には出さず、文字で書く（窓が無いので）。標準エラーには、先頭にコマンドの行、続けて知らせを書く。
/// </summary>
public static class CommandLineTest
{
    public const string Marker = "--cmd-test";

    public static int Run(ICommandLineBackend backend, string[] args, bool japanese)
    {
        Localizer.Instance.SetLanguage(japanese ? "ja" : "en");
        if (args.Length != 3 || !Directory.Exists(args[0]))
        {
            Console.Error.WriteLine(japanese
                ? $"使い方: {Marker} <基準フォルダー> <ファイル指定> <検索パターン>（基準フォルダーは在るもの）"
                : $"usage: {Marker} <base folder> <file specification> <search pattern> (the base folder must exist)");
            return 2;
        }

        var vm = new CommandLineViewModel(backend, Path.GetFullPath(args[0])) { FilePattern = args[1], SearchPattern = args[2] };
        Console.Error.WriteLine(vm.CommandText);
        if (vm.ErrorText.Length > 0)
        {
            Console.Error.WriteLine(vm.ErrorText);
            return 2;
        }
        try
        {
            var result = vm.RunAsync().GetAwaiter().GetResult();
            if (vm.OutputFile is { } file)
            {
                using var stdout = Console.OpenStandardOutput();
                using var output = File.OpenRead(file);
                output.CopyTo(stdout);
            }
            if (vm.Notice.Length > 0) Console.Error.WriteLine(vm.Notice);
            return result?.ExitCode ?? 2;
        }
        finally { vm.Dispose(); }
    }
}
