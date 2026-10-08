using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UwView.Core;
using UwView.Localization;

namespace UwView.Services;

/// <summary>
/// 末尾追従と、外での変化（切り詰め・作り直し・ローテーション・削除）の試験用の口（v1.8.2 extFS E-0 §1.4）。
/// 画面は出さずに、画面と同じ開き方・同じ索引の作り方・同じ決まり（<see cref="FileFollow"/>）で動かし、
/// 決めた間隔で今の状態を 1 行の JSON で標準出力に書く。管理部の <c>uv_follow_test.sh</c> が、
/// 別のプロセスでファイルを切り詰めたり消したりしながら読む。
///
/// <c>UwView.Desktop --follow-test &lt;ファイル&gt; [書き出す間隔の秒 1] [続ける秒 60] [--no-follow]</c>
///
/// 書く中身: <c>{"t":2.0,"lines":10000,"length":588890,"last":"…","change":"None","following":true,"reloads":0,"banner":""}</c>
/// <list type="bullet">
/// <item><c>lines</c> は索引の行数（索引を作っている間は -1）。<c>last</c> は最後の行の本文。</item>
/// <item><c>change</c> は開いているセッションが見つけた変化（None・Truncated・Rewritten・Replaced・Deleted）。</item>
/// <item><c>banner</c> は画面なら帯に出る文（出ていなければ空）。追従中に読み直したあとは 4 秒だけ出る。</item>
/// </list>
/// 追従しないとき（<c>--no-follow</c>）は、標準入力に <c>reload</c> の 1 行を送ると［読み直す］と同じに読み直す。
/// <c>quit</c> で終わる。
/// </summary>
public static class FollowTest
{
    public const string Marker = "--follow-test";

    public static int Run(IDocumentOpener opener, Func<DocumentSession, Task> buildIndex, string[] args, bool japanese)
    {
        Localizer.Instance.SetLanguage(japanese ? "ja" : "en");
        bool follow = Array.IndexOf(args, "--no-follow") < 0;
        var rest = Array.FindAll(args, a => a != "--no-follow");
        if (rest.Length is < 1 or > 3 || !File.Exists(rest[0])
            || !TryNumber(rest, 1, 1, out double interval) || !TryNumber(rest, 2, 60, out double duration))
        {
            Console.Error.WriteLine(japanese
                ? $"使い方: {Marker} <ファイル> [書き出す間隔の秒 1] [続ける秒 60] [--no-follow]（ファイルは在るもの）"
                : $"usage: {Marker} <file> [report interval in seconds, 1] [duration in seconds, 60] [--no-follow] (the file must exist)");
            return 2;
        }
        return RunAsync(opener, buildIndex, Path.GetFullPath(rest[0]), interval, duration, follow).GetAwaiter().GetResult();
    }

    private static bool TryNumber(string[] args, int index, double fallback, out double value)
    {
        value = fallback;
        return index >= args.Length
               || double.TryParse(args[index], System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out value) && value > 0;
    }

    private static async Task<int> RunAsync(IDocumentOpener opener, Func<DocumentSession, Task> buildIndex,
                                            string path, double interval, double duration, bool follow)
    {
        var clock = Stopwatch.StartNew();
        int reloads = 0;
        string banner = "";
        double bannerUntil = double.MaxValue;
        int reloadRequested = 0;
        using var quit = new CancellationTokenSource();
        _ = Task.Run(() =>
        {
            for (string? line; (line = Console.In.ReadLine()) is not null;)
            {
                if (line.Trim() == "reload") Interlocked.Exchange(ref reloadRequested, 1);
                if (line.Trim() == "quit") { quit.Cancel(); return; }
            }
        });

        var session = await OpenAsync();
        if (session is null) return 2;
        double nextReport = interval;
        try
        {
            while (clock.Elapsed.TotalSeconds < duration && !quit.IsCancellationRequested)
            {
                var change = session.ExternalChange;
                var step = FileFollow.Decide(change, follow);
                bool manual = Interlocked.Exchange(ref reloadRequested, 0) == 1;
                if ((step is FileFollow.Step.Reload || manual && FileFollow.CanReload(change)) && File.Exists(path))
                {
                    var fresh = await OpenAsync();
                    if (fresh is not null)
                    {
                        await session.DisposeAsync();
                        session = fresh;
                        reloads++;
                        banner = follow ? FileFollow.Reloaded(change, path) : "";
                        bannerUntil = clock.Elapsed.TotalSeconds + FileFollow.ReloadedNoticeTime.TotalSeconds;
                    }
                }
                else if (step is FileFollow.Step.Banner or FileFollow.Step.Wait)
                {
                    banner = FileFollow.Notice(change, path, follow);
                    bannerUntil = double.MaxValue;
                }
                if (clock.Elapsed.TotalSeconds >= bannerUntil) { banner = ""; bannerUntil = double.MaxValue; }

                double now = clock.Elapsed.TotalSeconds;
                if (now >= nextReport)
                {
                    Report(now, session, follow, reloads, banner);
                    nextReport += interval;
                }
                try { await Task.Delay(100, quit.Token); }
                catch (OperationCanceledException) { break; }
            }
            Report(clock.Elapsed.TotalSeconds, session, follow, reloads, banner);
            return 0;
        }
        finally { await session.DisposeAsync(); }

        Task<DocumentSession?> OpenAsync()
        {
            DocumentSession? s;
            try { s = opener.OpenLocalPath(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { s = null; }
            if (s is null) { Console.Error.WriteLine($"cannot open: {path}"); return Task.FromResult<DocumentSession?>(null); }
            // 索引は待たずに裏で作る（作っている途中で切り詰められても落ちないかを見る。§1.4 F7）
            _ = buildIndex(s).ContinueWith(t => Console.Error.WriteLine(t.Exception?.GetBaseException().Message),
                                           TaskContinuationOptions.OnlyOnFaulted);
            if (follow) s.StartTail(); else s.StartWatch();
            return Task.FromResult<DocumentSession?>(s);
        }
    }

    private static void Report(double t, DocumentSession session, bool follow, int reloads, string banner)
    {
        long lines = session.IsIndexed && session.Index is { } index ? index.TotalLines : -1;
        string last = "";
        try { if (lines > 0) last = session.Document.GetLine(lines - 1); }
        catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException) { last = ""; }

        using var buffer = new MemoryStream();
        // 日本語はそのまま書く（\uXXXX にしない。読む側のスクリプトで見比べやすいように）
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions
               { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            json.WriteNumber("t", Math.Round(t, 1));
            json.WriteNumber("lines", lines);
            json.WriteNumber("length", session.Source.Length);
            json.WriteString("last", last);
            json.WriteString("change", session.ExternalChange.ToString());
            json.WriteBoolean("following", follow);
            json.WriteNumber("reloads", reloads);
            json.WriteString("banner", banner);
            json.WriteEndObject();
        }
        Console.Out.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
        Console.Out.Flush();
    }
}
