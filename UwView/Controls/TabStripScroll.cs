using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace UwView.Controls;

/// <summary>
/// タブを1行のまま左右にスクロールさせる（オーナー指示 2026-09-27）。
/// ホイールの縦の回転で横に送り、選んだタブは見える位置まで送る（開いたタブが端に隠れないように）。
/// </summary>
public static class TabStripScroll
{
    private const double WheelStep = 48;

    public static void Attach(ScrollViewer scroll, TabStrip tabs)
    {
        scroll.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) =>
        {
            double delta = e.Delta.X != 0 ? e.Delta.X : e.Delta.Y;
            if (delta == 0) return;
            double max = System.Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width);
            double x = System.Math.Clamp(scroll.Offset.X - delta * WheelStep, 0, max);
            scroll.Offset = new Vector(x, scroll.Offset.Y);
            e.Handled = true;
        }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

        tabs.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (tabs.SelectedIndex >= 0 && tabs.ContainerFromIndex(tabs.SelectedIndex) is { } item)
                item.BringIntoView();
        }, DispatcherPriority.Loaded);
    }
}
