using System.Diagnostics;

// uvf — 無料版 UwView の CLI の入口（小さな起動アプリ）。
//
// CLI の中身は UwView 本体にある（本体を --uvf 付きで起動すると、画面を作らずに CLI として動く）。
// ここは本体を探して起動し、標準入出力をそのまま引き継いで、終了コードを返すだけ。
// 本体と同じ .NET 一式をもう1つ抱えないので、配布物がほとんど大きくならない。
//
// Windows では、この起動アプリがコンソールアプリなので、GUI 本体はそのコンソールを受け継ぐ
// （GUI の exe を直接コマンドプロンプトから呼ぶと、出力がどこにもつながらない）。

const string Marker = "--uvf";
string[] guiNames = ["UwView.Desktop", "UwView.Desktop.exe", "UwView", "UwView.exe"];

string? gui = FindGui();
if (gui is null)
{
    Console.Error.WriteLine("uvf: UwView 本体が見つかりません（uvf は UwView と同じ場所に置いてください）"
                            + " / UwView was not found next to uvf.");
    return 2;
}

var psi = new ProcessStartInfo(gui) { UseShellExecute = false };   // 入出力は引き継ぐ（リダイレクトしない）
psi.ArgumentList.Add(Marker);
foreach (string a in args) psi.ArgumentList.Add(a);

// Ctrl+C は本体にも同時に届く（同じコンソール／プロセスグループ）。本体の後始末を待ってから終わる
Console.CancelKeyPress += (_, e) => e.Cancel = true;

using var process = Process.Start(psi);
if (process is null) return 2;
process.WaitForExit();
return process.ExitCode;

// 本体を探す: 自分の実体（シンボリックリンクなら先をたどる）と同じフォルダ
string? FindGui()
{
    string? self = Environment.ProcessPath;
    if (string.IsNullOrEmpty(self)) return null;
    try
    {
        if (File.ResolveLinkTarget(self, returnFinalTarget: true) is { } target) self = target.FullName;
    }
    catch (IOException) { }

    string dir = Path.GetDirectoryName(self)!;
    foreach (string name in guiNames)
    {
        string candidate = Path.Combine(dir, name);
        if (File.Exists(candidate) && !string.Equals(candidate, self, StringComparison.Ordinal)) return candidate;
    }
    return null;
}
