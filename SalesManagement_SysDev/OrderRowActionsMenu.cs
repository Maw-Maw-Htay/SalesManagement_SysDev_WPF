using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SalesManagement_SysDev;

public sealed partial class F_OrderManagement
{
    private ContextMenu? openRowMenu;

    private void RowActionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button) OpenRowMenu(button);
        e.Handled = true;
    }

    private void RowActionsOpening(object sender, ContextMenuEventArgs e)
    {
        // The context-menu key and right-click use the same anchor as the three-dot click.
        if (sender is Button button) OpenRowMenu(button);
        e.Handled = true;
    }

    private void OpenRowMenu(Button button)
    {
        if (!viewModel.CanUseOrderList || !button.IsVisible || !button.IsLoaded
            || button is not { ContextMenu: { } menu, DataContext: OrderListRow row }
            || !viewModel.Rows.Contains(row) || GetWindow(button)?.Content is not FrameworkElement client) return;

        CloseRowMenu();
        menu.PlacementTarget = button;
        menu.DataContext = row;
        OrderRowMenuPlacement.Configure(menu, button, client);
        openRowMenu = menu;
        menu.IsOpen = true;
    }

    private void CloseRowMenu()
    {
        if (openRowMenu is { IsOpen: true } menu) menu.IsOpen = false;
    }

    private void RowMenuClosed(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu { IsOpen: false } menu || !ReferenceEquals(menu, openRowMenu)) return;
        openRowMenu = null;
        menu.PlacementTarget = null;
        menu.CustomPopupPlacementCallback = null;
    }

    private void RowMenuAnchorUnloaded(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(openRowMenu?.PlacementTarget, sender)) CloseRowMenu();
    }

    private void RowMenuAnchorChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (ReferenceEquals(openRowMenu?.PlacementTarget, sender)) CloseRowMenu();
    }

    private void RowMenuQueryCursor(object sender, QueryCursorEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        // The enabled parent receives the cursor query when the pointer is over a disabled item.
        var hit = VisualTreeHelper.HitTest(menu, e.GetPosition(menu))?.VisualHit;
        if (hit == null || ItemsControl.ContainerFromElement(menu, hit) is not MenuItem { IsEnabled: false }) return;
        e.Cursor = Cursors.No;
        e.Handled = true;
    }

    private OrderListRow? TakeRowAction(object? sender)
    {
        if (!viewModel.CanUseOrderList || sender is not MenuItem { Visibility: Visibility.Visible, IsEnabled: true, DataContext: OrderListRow row }
            || !viewModel.Rows.Contains(row)) return null;
        CloseRowMenu();
        return row;
    }
}

internal static class OrderRowMenuPlacement
{
    private const double Inset = 4;

    internal static void Configure(ContextMenu menu, FrameworkElement anchor, FrameworkElement client)
    {
        var origin = anchor.PointToScreen(new Point());
        var center = anchor.PointToScreen(new Point(anchor.ActualWidth / 2, anchor.ActualHeight / 2));
        var clientArea = new Rect(client.PointToScreen(new Point()), client.PointToScreen((Point)client.RenderSize));
        var workArea = Rect.Intersect(clientArea, RegistrationWindowPlacement.ReadWorkAreaAt(center));
        var bounds = workArea.IsEmpty ? clientArea : workArea;
        var dpi = VisualTreeHelper.GetDpi(anchor);
        menu.MaxWidth = Math.Max(1, Math.Floor(bounds.Width / dpi.DpiScaleX) - 2 * Inset);
        menu.MaxHeight = Math.Max(1, Math.Floor(bounds.Height / dpi.DpiScaleY) - 2 * Inset);
        // WPF passes the callback sizes in screen pixels and applies the anchor origin to the result.
        var gap = new Size(Inset * dpi.DpiScaleX, Inset * dpi.DpiScaleY);
        menu.CustomPopupPlacementCallback = (menuSize, anchorSize, _) =>
            [new CustomPopupPlacement(Calculate(bounds, new Rect(origin, anchorSize), menuSize, gap), PopupPrimaryAxis.None)];
    }

    internal static Point Calculate(Rect bounds, Rect anchor, Size menuSize, Size gap)
    {
        var left = bounds.Left + gap.Width;
        var top = bounds.Top + gap.Height;
        var maxLeft = Math.Max(left, bounds.Right - gap.Width - menuSize.Width);
        var maxTop = Math.Max(top, bounds.Bottom - gap.Height - menuSize.Height);
        var below = anchor.Bottom + gap.Height;
        var above = anchor.Top - gap.Height - menuSize.Height;
        var preferBelow = below <= maxTop || above < top && bounds.Bottom - anchor.Bottom >= anchor.Top - bounds.Top;
        return new Point(Math.Clamp(anchor.Right - menuSize.Width, left, maxLeft) - anchor.Left,
            Math.Clamp(preferBelow ? below : above, top, maxTop) - anchor.Top);
    }
}
