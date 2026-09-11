using System;
using System.Collections.Generic;
using System.Linq;
using UwView.Localization;

namespace UwView.Services;

/// <summary>直前に終わった処理の記録（1件）。</summary>
/// <param name="Name">操作の名前（「検索」など・表示言語で入る）。</param>
/// <param name="Elapsed">かかった時間。</param>
/// <param name="Detail">件数などの補足（無ければ null）。</param>
/// <param name="At">終わった時刻。</param>
public sealed record OperationEntry(string Name, TimeSpan Elapsed, string? Detail, DateTime At);

/// <summary>
/// 時間のかかる処理の所要時間を残す場所（オーナー要望 2026-09-11）。
///
/// 所要時間は進捗ダイアログや一時メッセージにしか出ておらず、閉じると消えていた。
/// 「さっきの検索は何秒だったか」を後から見られないので、
/// 直前の1件をステータスバーへ出し、直近の数件はそのヒントで読めるようにする。
///
/// 窓をまたいで記録したい（Pro の多段階検索の窓からも積む）ので静的に置く。
/// UVF / UVP の共通機能なのでここ（共有プロジェクト）に置く。
/// </summary>
public static class OperationLog
{
    /// <summary>ヒントに出す件数。多すぎると読めないので、ひと目で追える数に留める。</summary>
    public const int Keep = 10;

    private static readonly List<OperationEntry> _recent = [];

    /// <summary>記録が増えたとき（ステータスバーの更新用）。</summary>
    public static event Action? Changed;

    /// <summary>直前の1件（まだ何もしていなければ null）。</summary>
    public static OperationEntry? Last => _recent.Count > 0 ? _recent[0] : null;

    /// <summary>新しい順。</summary>
    public static IReadOnlyList<OperationEntry> Recent => _recent;

    public static void Record(string name, TimeSpan elapsed, string? detail = null)
    {
        if (string.IsNullOrEmpty(name)) return;
        _recent.Insert(0, new OperationEntry(name, elapsed, detail, DateTime.Now));
        if (_recent.Count > Keep) _recent.RemoveRange(Keep, _recent.Count - Keep);
        Changed?.Invoke();
    }

    /// <summary>
    /// 記録を捨てる。
    ///
    /// 表示言語を切り替えたときに呼ぶ——記録は「そのとき表示していた言語」の文字列なので、
    /// 残したままだと英語UIに日本語が混ざる。次の操作ですぐ埋まるので実害は小さい。
    /// 自動テストでも、前のテストの記録を持ち越さないために使う。
    /// </summary>
    public static void Clear()
    {
        _recent.Clear();
        Changed?.Invoke();
    }

    /// <summary>
    /// 経過時間の表示。値の大きさで単位を変える
    /// （一瞬で終わる処理が「0.0 秒」になって意味を失わないように）。
    ///   1分以上   → 5:28.3
    ///   1秒以上   → 16.83 秒
    ///   1ミリ秒以上 → 340 ミリ秒
    ///   それ未満  → 0.42 ミリ秒（マイクロ秒相当まで見える）
    /// </summary>
    public static string Format(TimeSpan t)
    {
        bool ja = Localizer.Instance.Culture.TwoLetterISOLanguageName == "ja";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes}:{t.Seconds:D2}.{t.Milliseconds / 100}";

        string ms = ja ? " ミリ秒" : " ms";
        if (t.TotalSeconds >= 1) return $"{t.TotalSeconds:F2}{(ja ? " 秒" : " s")}";
        if (t.TotalMilliseconds >= 1) return $"{t.TotalMilliseconds:F0}{ms}";
        return $"{t.TotalMilliseconds:F2}{ms}";
    }

    /// <summary>ステータスバーの1行（「直前: 検索 8.32 秒（1,396,454 件）」）。</summary>
    public static string Summary(bool ja)
    {
        if (Last is not { } e) return "";
        string head = ja ? $"直前: {e.Name} {Format(e.Elapsed)}" : $"Last: {e.Name} {Format(e.Elapsed)}";
        return e.Detail is { Length: > 0 } d ? $"{head}（{d}）" : head;
    }

    /// <summary>ヒントに出す直近の一覧（時刻つき）。</summary>
    public static string Tip(bool ja)
    {
        if (_recent.Count == 0) return ja ? "まだ何も実行していません" : "Nothing has run yet";
        var lines = _recent.Select(e =>
        {
            string d = e.Detail is { Length: > 0 } s ? $"（{s}）" : "";
            return $"{e.At:HH:mm:ss}  {e.Name}  {Format(e.Elapsed)}{d}";
        });
        string head = ja ? $"直近 {_recent.Count} 件の処理時間" : $"Last {_recent.Count} operations";
        return head + "\n" + string.Join("\n", lines);
    }
}
