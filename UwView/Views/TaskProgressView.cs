using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using UwView.Localization;

namespace UwView.Views;

/// <summary>
/// 時間のかかる処理（検索・置換・削除・保存・オープン）の状態表示。
///
/// 実行中は「どこまで進んだか」「何件目か」「経過時間」を出し、いつでも中止できる。
/// 終わったら結果（件数と所要時間）を残したまま、ボタンを「閉じる」に変えて
/// ユーザーが読んでから閉じられるようにする（勝手に消さない）。
///
/// 中身だけを持つ部品にしてあるのは、出し方が環境で違うため。
/// デスクトップは <see cref="EditProgressWindow"/>（別ウィンドウ）、
/// ブラウザ（WASM）はウィンドウが作れないのでアプリ内のオーバーレイに載せる。
/// </summary>
public sealed class TaskProgressView : UserControl
{
    private static bool Ja => Localizer.Instance.Culture.TwoLetterISOLanguageName == "ja";
    private static string T(string ja, string en) => Ja ? ja : en;

    private readonly TextBlock _count = new() { Foreground = Brushes.Black, FontSize = 15 };
    private readonly TextBlock _place = new() { Foreground = Brushes.Black };
    private readonly TextBlock _elapsed = new() { Foreground = Brushes.Black };
    private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1, Height = 8 };
    private readonly Button _button = new()
    {
        Width = 120,
        HorizontalAlignment = HorizontalAlignment.Right,
        HorizontalContentAlignment = HorizontalAlignment.Center,   // 既定は左寄せなので明示する
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private TimeSpan? _fixedElapsed;

    /// <summary>この処理の前にかかった時間（例: 置換のために先に走らせた検索）。</summary>
    private TimeSpan _preroll;
    private string? _prerollLabel;

    /// <summary>処理が終わって「閉じる」待ちになっているか。</summary>
    public bool IsFinished { get; private set; }

    /// <summary>見出し（ステータスバーの記録にも使う）。</summary>
    public string TitleText { get; }

    /// <summary>「中止」が押されたとき。呼び出し側が処理を止める。</summary>
    public event Action? Cancelled;

    /// <summary>完了後に「閉じる」が押されたとき。載せ先が自分を外す。</summary>
    public event Action? CloseRequested;

    /// <summary>
    /// 実行中に進捗を1度でも受け取ったか（自動テスト用）。
    /// 生成直後の「準備中…」のまま最後まで動かない配線ミスを検出するために見る。
    /// </summary>
    internal bool ProgressReported { get; private set; }

    /// <summary>いま出ている件数・割合の文言（自動テスト用）。</summary>
    internal string CountText => _count.Text ?? "";

    /// <summary>実行開始からの経過時間（前段があればそれも含む）。</summary>
    public TimeSpan Elapsed => _preroll + _watch.Elapsed;

    public TaskProgressView(string title)
    {
        TitleText = title;

        _button.Content = T("中止", "Cancel");
        ToolTip.SetTip(_button, T("処理を止める（途中まで進んだ分は元に戻します）",
                                  "Stop the operation (anything already applied is rolled back)"));
        _button.Click += (_, _) =>
        {
            if (IsFinished) { CloseRequested?.Invoke(); return; }
            SignalCancelling();
            Cancelled?.Invoke();
        };

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24, 20),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeight.Bold, Foreground = Brushes.Black },
                _count,
                _place,
                _elapsed,
                _bar,
                _button,
            },
        };

        _tick.Tick += (_, _) => UpdateElapsed();
        _tick.Start();
        Report(0, 0);
        UpdateElapsed();
    }

    /// <summary>「中止しています…」の見た目にする（×で閉じられたときも通る）。</summary>
    public void SignalCancelling()
    {
        _button.IsEnabled = false;
        _button.Content = T("中止しています…", "Cancelling…");
    }

    /// <summary>前段でかかった時間を足す（<paramref name="label"/> 例: 「検索」）。</summary>
    public void AddPreroll(TimeSpan elapsed, string label)
    {
        _preroll = elapsed;
        _prerollLabel = label;
        UpdateElapsed();
    }

    private void UpdateElapsed()
    {
        var t = _fixedElapsed ?? Elapsed;
        string text = T($"経過時間: {Format(t)}", $"Elapsed: {Format(t)}");
        if (_preroll > TimeSpan.Zero && _prerollLabel is { } label)
            text += T($"（うち{label} {Format(_preroll)}）", $" ({label} {Format(_preroll)})");
        _elapsed.Text = text;
    }

    /// <summary>
    /// 経過時間の表示。実体は <see cref="UwView.Services.OperationLog.Format"/>
    /// （ステータスバーの「直前: …」と同じ書式にそろえるため、1か所に置いてある）。
    /// </summary>
    public static string Format(TimeSpan t) => UwView.Services.OperationLog.Format(t);

    /// <summary>時計を止める（呼び出し側から畳むとき）。</summary>
    public void StopTicking() => _tick.Stop();

    /// <summary>件数つきの進捗。ワーカーから呼ばれても安全なように UI スレッドへ寄せる。</summary>
    public void Report(long done, long total)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => Report(done, total)); return; }
        if (IsFinished) return;

        _count.Text = total > 0
            ? (Ja ? $"{done:N0} / {total:N0} 件" : $"{done:N0} / {total:N0}")
            : (Ja ? "準備中…" : "Preparing…");
        _bar.Value = total > 0 ? (double)done / total : 0;
        if (total > 0) ProgressReported = true;
    }

    /// <summary>割合だけの進捗（検索・保存など、件数が出ない処理用）。</summary>
    public void ReportFraction(double fraction, string? note = null)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => ReportFraction(fraction, note)); return; }
        if (IsFinished) return;

        if (fraction < 0)
        {
            // 割合が出せない処理（段階だけ分かるもの）は、文言を主役にして流れるバーにする
            _bar.IsIndeterminate = true;
            _count.Text = note ?? "";
            return;
        }
        _bar.IsIndeterminate = false;
        _bar.Value = Math.Clamp(fraction, 0, 1);
        _count.Text = $"{fraction:P0}";
        if (note is not null) _place.Text = note;
        ProgressReported = true;
    }

    /// <summary>
    /// 完了を表示する。結果と所要時間を残したまま、ボタンを「閉じる」に変える
    /// （自動で消さないので、ユーザーが結果を読んでから閉じられる）。
    /// </summary>
    public void Finish(string summary, TimeSpan? elapsed = null)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => Finish(summary, elapsed)); return; }

        _fixedElapsed = elapsed;      // 外で計測した時間があればそれを出す
        _watch.Stop();
        _tick.Stop();
        IsFinished = true;

        _count.Text = summary;
        _place.Text = "";
        _bar.IsIndeterminate = false;
        _bar.Value = 1;
        UpdateElapsed();

        _button.IsEnabled = true;
        _button.Content = T("閉じる", "Close");
        ToolTip.SetTip(_button, T("この画面を閉じる", "Close this window"));
    }
}
