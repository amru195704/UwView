using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UwView.Core.Cli;

namespace UwView.Services;

/// <summary>［ファイル展開］の一覧の 1 行。</summary>
/// <param name="Label">番号（開いたあとの行番号欄・ファイル一覧の番号と同じ。uvp の zip の中は <c>n-m</c>）。</param>
/// <param name="Name">基準フォルダーからの相対パス（書いたとおり）。</param>
/// <param name="FullPath">絶対パス（ツールチップ）。</param>
/// <param name="Size">大きさ（読めなければ null）。</param>
/// <param name="Readable">読めるか（読めなければ ⚠）。</param>
public sealed record CommandFileRow(string Label, string Name, string FullPath, long? Size, bool Readable);

/// <summary>［ファイル展開］の結果（<c>--files</c> と同じ広げ方）。</summary>
/// <param name="Notices">広げたときの知らせ（当たらなかった断片・除外の本数・多すぎる本数など。コマンドの標準エラーと同じ言葉）。</param>
/// <param name="Ignored">除外したファイル・フォルダーの数。</param>
/// <param name="IgnoredNames">除外したものの名前（分かる範囲で）。</param>
/// <param name="Index">束ねる前の予告（uvp で複数ファイルのとき。Reuse・Append・Rebuild・Keep・新規。§3.3）。</param>
/// <param name="OffersRebuild">［作り直す…］を出すか（Keep のとき）。</param>
public sealed record CommandFileList(IReadOnlyList<CommandFileRow> Rows, IReadOnlyList<string> Notices, int Ignored,
                                     IReadOnlyList<string> IgnoredNames, string? Index = null, bool OffersRebuild = false);

/// <summary>コマンドの出力先（ダイアログの出力欄につなぐ）。</summary>
public sealed class CommandOutput
{
    public required Stream StdOut { get; init; }
    public required TextWriter StdErr { get; init; }
}

/// <summary>コマンドを走らせた結果。</summary>
/// <param name="ExitCode">grep と同じ（0＝あり・1＝なし・2＝誤りまたは不完全）。</param>
/// <param name="ShowInGui">結果を本体の窓に出す受け渡し（無ければ null＝出力欄だけ）。</param>
/// <param name="Written">書いたファイル（<c>-out</c>・<c>convert</c>）。「書きました」＋［開く］［フォルダーを表示］を出す。</param>
/// <param name="OutputTabName">出力を新しいタブで開くときの名前（<c>-replace</c>・<c>cat</c> を <c>-out</c> なしで。§5.1）。</param>
public sealed record CommandRunResult(int ExitCode, Func<Task>? ShowInGui = null, string? Written = null,
                                      string? OutputTabName = null);

/// <summary>［保存…］で書くときの走らせ方（§7.3：コマンドの <c>-out</c> と同じ書き方で。形式は拡張子で決まる）。</summary>
/// <param name="Argv">走らせる引数。</param>
/// <param name="WritesItself">コマンドが自分で書く（<c>-out</c>）。false なら標準出力を保存先に書く。</param>
public sealed record CommandSavePlan(IReadOnlyList<string> Argv, bool WritesItself);

/// <summary>
/// 「コマンドライン」ダイアログ（v1.8.0 Finder Scope）の、uvf と uvp で違う所。画面は共通で、解釈・展開・実行をここに任せる。
/// <b>新しい検索の処理は作らない</b>——コマンドの部品（解釈・展開・判定・実行）をそのまま呼ぶ（§7）。
/// </summary>
public interface ICommandLineBackend
{
    /// <summary>コマンドの名前（<c>uvf</c>・<c>uvp</c>）。コマンドの行の先頭に出す。</summary>
    string Tool { get; }

    /// <summary>検索パターンがファイルを取らないもの（<c>--help</c> <c>--version</c> <c>--tune</c>）か。</summary>
    bool TakesNoFile(IReadOnlyList<string> searchArgs);

    /// <summary>検索パターンの先頭に書くと、ファイルより前に置く語か（uvp の <c>convert</c> <c>cat</c> <c>search</c>）。</summary>
    bool IsSubcommand(string word) => false;

    /// <summary>
    /// 検索パターンに検索語も段も無いときの引数（§4「検索語なし」）。uvf は一覧だけ（<c>--files</c>）、
    /// uvp は束ねて（1 本ならそのまま）開くだけ（先頭の <c>-open</c>）。
    /// </summary>
    IReadOnlyList<string> WithoutSearch(string file);

    /// <summary>
    /// 走らせる前の支度（UI のスレッドで呼ぶ。尋ねることがあればここで尋ねる）。
    /// uvp は、束ねる索引を基準フォルダーに置けないとき保存先を尋ね（§6）、既にある <c>-out</c> 先は上書きしてよいか、
    /// <c>--rebuild</c> は作り直してよいかを尋ねる（§5.1）。
    /// 書き換えた引数を返す（上書きの確認で「はい」なら <c>--force</c> を足す など）。null なら走らせない。
    /// </summary>
    Task<IReadOnlyList<string>?> PrepareAsync(IReadOnlyList<string> argv, string baseFolder, bool japanese)
        => Task.FromResult<IReadOnlyList<string>?>(argv);

    /// <summary>［保存…］で、この引数の結果を path に書く走らせ方（無ければ出力欄をそのまま写す）。</summary>
    CommandSavePlan? SavePlan(IReadOnlyList<string> argv, string path) => null;

    /// <summary>書き方の誤り（コマンドと同じ言葉）。無料版で Pro の書き方を書いたときの案内もここ。無ければ null。</summary>
    (string Ja, string En)? Check(IReadOnlyList<string> argv);

    /// <summary>ファイル指定パターンを広げる（作業フォルダーは呼び手が基準フォルダーにしてある）。</summary>
    CommandFileList Expand(string specification, IgnoreOptions ignore, bool japanese);

    /// <summary>
    /// コマンドを走らせる（作業フォルダーは呼び手が基準フォルダーにしてある）。
    /// 行・集計・流れを窓に出すものは、<see cref="CommandRunResult.ShowInGui"/> を返す（呼び手が UI のスレッドで呼ぶ）。
    /// </summary>
    /// <param name="toWindow">結果を窓に出してよいか（［保存…］で書くときは false＝文字で出す）。</param>
    Task<CommandRunResult> RunAsync(IReadOnlyList<string> argv, CommandOutput output, bool japanese, CancellationToken ct,
                                    bool toWindow = true);
}

/// <summary>ファイル一覧を作る共通の手順（uvf・uvp とも、番号は広げた順）。</summary>
public static class CommandFileLists
{
    public static CommandFileList From(FileSet.Result found, bool japanese, string tool)
    {
        var rows = new List<CommandFileRow>(found.Files.Count);
        for (int i = 0; i < found.Files.Count; i++)
        {
            string name = found.Files[i];
            string full = Path.GetFullPath(StripEntry(name));
            long? size = null;
            bool readable = true;
            try
            {
                var info = new FileInfo(full);
                if (info.Exists) size = info.Length; else readable = false;
                if (readable) { using var _ = File.OpenRead(full); }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { readable = false; }
            rows.Add(new CommandFileRow((i + 1).ToString(), name, full, size, readable));
        }
        var notices = found.Missing.Select(m => found.MissingNotice(m, japanese, tool)).ToList();
        if (found.IgnoredNotice(japanese, tool) is { } ignored) notices.Add(ignored);
        return new CommandFileList(rows, notices, found.Ignored, []);
    }

    /// <summary>zip の中の指定（<c>a.zip!logs/*.log</c>）は、zip そのものの大きさを見る。</summary>
    private static string StripEntry(string name)
    {
        int bang = name.IndexOf('!');
        return bang > 0 && name[..bang].EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? name[..bang] : name;
    }
}
