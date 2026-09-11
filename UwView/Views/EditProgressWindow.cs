using System;
using Avalonia.Controls;

namespace UwView.Views;

/// <summary>
/// 時間のかかる処理の状態ダイアログ（デスクトップ用の別ウィンドウ）。
/// 中身は <see cref="TaskProgressView"/> がすべて持っていて、ここは出し方だけを受け持つ。
///
/// 一括処理は UI スレッド上で少しずつ進むので、このウィンドウは<b>モーダルにしない</b>
/// （ShowDialog で待つと処理そのものが進まなくなる）。代わりに後から Show して位置を合わせる。
///
/// ブラウザ（WASM）ではウィンドウを作れないため、UVF は <see cref="TaskProgressView"/> を
/// そのままアプリ内のオーバーレイに載せる。
/// </summary>
public sealed class EditProgressWindow : Window
{
    private readonly TaskProgressView _view;
    private bool _closing;   // 呼び出し側からの「閉じる」。×を押されたのと区別する

    /// <summary>いま出ている進捗ダイアログ（自動テストから掴むため。親を持たないので窓一覧から辿れない）。</summary>
    internal static EditProgressWindow? Current { get; private set; }

    /// <summary>実行中に進捗を1度でも受け取ったか（自動テスト用）。</summary>
    internal bool ProgressReported => _view.ProgressReported;

    /// <summary>いま出ている件数・割合の文言（自動テスト用）。</summary>
    internal string CountText => _view.CountText;

    /// <summary>「中止」が押されたとき。呼び出し側が CancellationTokenSource を Cancel する。</summary>
    public event Action? Cancelled;

    /// <summary>実行開始からの経過時間（前段があればそれも含む）。</summary>
    public TimeSpan Elapsed => _view.Elapsed;

    public EditProgressWindow(string title)
    {
        _view = new TaskProgressView(title);
        _view.Cancelled += () => Cancelled?.Invoke();
        _view.CloseRequested += Close;

        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        // Topmost にしてはいけない: macOS ではこのウィンドウが画面に出ないまま処理が進み、
        // 終わってから現れる（実測で確認）。手前に出す代わりに、後から Show して位置を合わせる。
        Topmost = false;
        // 装飾は最初から通常のまま。実行中だけ枠だけ（BorderOnly）にすると、
        // 環境によっては実行中ずっと画面に出ず、完了して装飾を戻した瞬間に初めて見える。
        // 閉じるボタンで処理が消えないように、実行中の×は「中止」として扱う（下の Closing）。
        WindowDecorations = WindowDecorations.Full;
        Content = _view;

        Closing += (_, e) =>
        {
            if (_view.IsFinished || _closing) return;
            e.Cancel = true;              // ユーザーが実行中に×を押したときだけ中止として扱う
            _view.SignalCancelling();
            Cancelled?.Invoke();
        };

        Current = this;
        Closed += (_, _) => { if (ReferenceEquals(Current, this)) Current = null; };
    }

    /// <summary>前段でかかった時間を足す（<paramref name="label"/> 例: 「検索」）。</summary>
    public void AddPreroll(TimeSpan elapsed, string label) => _view.AddPreroll(elapsed, label);

    /// <summary>経過時間の表示。書式は <see cref="TaskProgressView.Format"/> に置いてある。</summary>
    public static string Format(TimeSpan t) => TaskProgressView.Format(t);

    /// <summary>
    /// 呼び出し側から閉じる（処理が終わって用済みになったとき）。
    /// <see cref="Window.Close"/> を直接呼ぶと、実行中の×押しと区別できず中止扱いになって閉じない。
    /// </summary>
    public void CloseNow()
    {
        _closing = true;
        _view.StopTicking();
        Close();
    }

    /// <summary>件数つきの進捗。ワーカーから呼ばれても安全。</summary>
    public void Report(long done, long total) => _view.Report(done, total);

    /// <summary>割合だけの進捗（検索・保存など、件数が出ない処理用）。</summary>
    public void ReportFraction(double fraction, string? note = null) => _view.ReportFraction(fraction, note);

    /// <summary>完了を表示する（結果と所要時間を残し、ボタンを「閉じる」に変える）。</summary>
    public void Finish(string summary, TimeSpan? elapsed = null) => _view.Finish(summary, elapsed);
}
