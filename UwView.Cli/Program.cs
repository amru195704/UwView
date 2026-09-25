using System.Diagnostics;

// uvf — 無料版 UwView の CLI の入口。
//
// 作りは OS で2通りある:
//
// ■ mac（NativeAOT・IN_PROCESS_CLI）— CLI の中身（UwView.Core）をこの実行ファイルの中で直接動かす。
//   以前は「起動アプリ → 本体を --uvf 付きで起動し直す」で .NET の起動が2回走り、何もしなくても 0.14 秒かかった
//   （rg は 0.01 秒）。1GB の gz・zst・lz4 では、差のほとんどがこの一定時間だった（2026-09-24 実測）。
//   NativeAOT なら起動は 0.01 秒で、中身も 5MB ほど。-open だけは画面が要るので、本体を起動する。
//   NativeAOT は その OS の上でしかビルドできないので、当面は mac だけ（オーナー決定 2026-09-24）。
//
// ■ Linux・Windows（従来どおり）— 小さな起動アプリ。同じフォルダの本体を --uvf 付きで起動し、
//   標準入出力をそのまま引き継いで、終了コードを返すだけ。
//   Windows では、この起動アプリがコンソールアプリなので、GUI 本体はそのコンソールを受け継ぐ
//   （GUI の exe を直接コマンドプロンプトから呼ぶと、出力がどこにもつながらない）。

#if IN_PROCESS_CLI
return UwView.Cli.InProcess.Run(args);
#else
string? gui = UwView.Cli.GuiLocator.Find();
if (gui is null)
{
    Console.Error.WriteLine("uvf: UwView 本体が見つかりません（uvf は UwView と同じ場所に置いてください）"
                            + " / UwView was not found next to uvf.");
    return 2;
}
return UwView.Cli.MainApp.Run(gui, args);
#endif

namespace UwView.Cli
{
    /// <summary>本体を --uvf 付きで起動し、標準入出力をそのまま引き継いで、終了コードを返す。</summary>
    internal static class MainApp
    {
        private const string Marker = "--uvf";

        public static int Run(string gui, string[] args)
        {
            var psi = new ProcessStartInfo(gui) { UseShellExecute = false };   // 入出力は引き継ぐ（リダイレクトしない）
            psi.ArgumentList.Add(Marker);
            foreach (string a in args) psi.ArgumentList.Add(a);

            // Ctrl+C は本体にも同時に届く（同じコンソール／プロセスグループ）。本体の後始末を待ってから終わる
            Console.CancelKeyPress += (_, e) => e.Cancel = true;

            using var process = Process.Start(psi);
            if (process is null) return 2;
            process.WaitForExit();
            return process.ExitCode;
        }
    }

    /// <summary>同じフォルダにある UwView 本体（画面）を探す。</summary>
    internal static class GuiLocator
    {
        // 本体の名前。試験用ビルドは別名で入っている（UwView.DesktopWF など。オーナー指示 2026-09-22）ので、
        // 名前ちょうどが無ければ同じ名前で始まるものを探す
        private static readonly string[] GuiNames = ["UwView.Desktop", "UwView.Desktop.exe", "UwView", "UwView.exe"];
        private static readonly string[] GuiPrefixes = ["UwView.Desktop", "UwView"];

        /// <summary>自分の実体（シンボリックリンクなら先をたどる）と同じフォルダから探す。</summary>
        public static string? Find()
        {
            string? self = Environment.ProcessPath;
            if (string.IsNullOrEmpty(self)) return null;
            try
            {
                if (File.ResolveLinkTarget(self, returnFinalTarget: true) is { } target) self = target.FullName;
            }
            catch (IOException) { }

            string dir = Path.GetDirectoryName(self)!;
            foreach (string name in GuiNames)
            {
                string candidate = Path.Combine(dir, name);
                if (File.Exists(candidate) && !string.Equals(candidate, self, StringComparison.Ordinal)) return candidate;
            }

            // 別名（UwView.DesktopWF）。拡張子付き（.exe）とそれ以外を取り違えないよう、形をそろえて探す
            try
            {
                foreach (string prefix in GuiPrefixes)
                    foreach (string candidate in Directory.EnumerateFiles(dir, prefix + "*"))
                    {
                        if (string.Equals(candidate, self, StringComparison.Ordinal)) continue;
                        string name = Path.GetFileName(candidate);
                        if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                            || name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                            || name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
                        return candidate;
                    }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }
    }

#if IN_PROCESS_CLI
    /// <summary>
    /// CLI をこの実行ファイルの中で動かす（mac の NativeAOT 版）。
    /// 設定・言語・版数・-open の渡し方は、本体の CLI モード（UwView.Desktop の RunCli）と同じにする。
    /// </summary>
    internal static class InProcess
    {
        public static int Run(string[] args)
        {
            // 必須の文字列で絞れない正規表現を大きな入力に当てるときは、JIT で動く本体に任せる
            //（NativeAOT は正規表現をコンパイルできず約 2 倍遅い。CompiledRegexRoute）
            if (UwView.Core.Cli.CompiledRegexRoute.ForUvf(args) && GuiLocator.Find() is { } gui)
                return MainApp.Run(gui, args);

            UwView.Core.EncodingDetector.EnsureCodePagesRegistered();

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            UwView.Core.Cli.UvfEnvironment? env = null;
            env = new UwView.Core.Cli.UvfEnvironment
            {
                StdOut = Console.OpenStandardOutput(),
                StdErr = Console.Error,
                Japanese = UwView.Core.Cli.CliLanguage.IsJapanese(UwView.Core.Cli.CliLanguage.FreeSettingsFolder),
                AppVersion = VersionText(),
                // -open: 画面が要るので本体を起動する（検索パターンとファイル、CLI が見つけた結果の置き場所を渡す）
                LaunchGui = (file, pattern) => LaunchGui(
                    (pattern is not null && file is not null
                        ? new[] { UwView.Core.Cli.UvfCli.SearchArgument,
                                  Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(pattern)) }
                        : Array.Empty<string>())
                    .Concat(env!.SearchOptionLetters is { Length: > 0 } opts
                        ? new[] { UwView.Core.Cli.UvfCli.OptionsArgument, opts } : Array.Empty<string>())
                    .Concat(env!.HandoffPath is { } hits
                        ? new[] { UwView.Core.Cli.CliHandoff.Argument, hits } : Array.Empty<string>())
                    .Concat(file is null ? Array.Empty<string>() : new[] { file })),
            };
            return UwView.Core.Cli.UvfCli.RunAsync(args, env, cts.Token).GetAwaiter().GetResult();
        }

        /// <summary>本体（画面）を起動する。見つからなければ false（CLI が「見つかりません」と言う）。</summary>
        private static bool LaunchGui(IEnumerable<string> guiArgs)
        {
            if (GuiLocator.Find() is not { } gui) return false;
            try
            {
                var psi = new ProcessStartInfo(gui) { UseShellExecute = false };
                foreach (string a in guiArgs) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                return p is not null;
            }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }

        /// <summary>
        /// 表示する版数（本体の App.VersionText と同じ規則: 4つ目があれば4つ目まで）。
        /// 版数は bump-version.sh が本体と CLI の両方にそろえて入れている。
        /// </summary>
        private static string VersionText()
        {
            var v = typeof(InProcess).Assembly.GetName().Version;
            if (v is null) return "1.0";
            return v.Revision > 0 ? v.ToString(4) : v.ToString(3);
        }
    }
#endif
}
