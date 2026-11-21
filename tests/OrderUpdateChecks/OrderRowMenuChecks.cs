using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using SalesManagement_SysDev;

internal static partial class Program
{
    private static MenuItem[] VisibleMenuItems(ContextMenu menu) => menu.Items.OfType<MenuItem>()
        .Where(item => item.Visibility == Visibility.Visible).ToArray();

    private static bool HasColor(Brush? brush, Color color) => brush is SolidColorBrush solid && solid.Color == color;

    private static void CheckOrderRowMenus(FrameworkElement root, DataGrid grid, OrderManagementViewModel model)
    {
        CheckOrderMenuPlacement();
        CheckBulkButtons(root, [false, false, false]);
        model.Rows.First(row => row.CanConfirm).IsChecked = true;
        CheckBulkButtons(root, [true, true, true]);
        model.ClearSelectionCommand.Execute(null);
        CheckBulkButtons(root, [false, false, false]);

        var menu = OpenOrderMenu(grid, model.Rows.First(row => row.CanConfirm));
        var card = (Border)menu.Template.FindName("menuCard", menu);
        var items = VisibleMenuItems(menu);
        Check(card.ActualWidth is >= 190 and <= 210 && card.CornerRadius.TopLeft == 8
            && HasColor(card.Background, Colors.White) && HasColor(card.BorderBrush, Color.FromRgb(0xCB, 0xD5, 0xE1))
            && items.Select(item => item.Header).SequenceEqual(new[] { "詳細を見る", "編集する", "受注を確定する", "非表示にする" })
            && items.All(item => item.IsEnabled && item.ActualHeight == 38
                && Descendants<LucideIcon>(item).Single() is { Width: 16, Height: 16, Data: not null }),
            "pending menu uses the white rounded card and four consistently sized, enabled actions");
        Check(menu.Items.OfType<Separator>().Single().IsVisible, "a divider separates ordinary and visibility-changing actions");
        Render(menu, "order-row-menu-pending");

        foreach (var row in model.Rows)
            Check(ReferenceEquals(OpenOrderMenu(grid, row), menu), "all row buttons reuse one menu with the current row context");
        var tableScroll = Descendants<ScrollViewer>(grid).First();
        tableScroll.ScrollToTop();
        grid.UpdateLayout();
        FlushBindings();
        Check(!menu.IsOpen, "scrolling the table closes the popup anchored to the previous row");

        menu = OpenOrderMenu(grid, model.Rows[0]);
        model.SortAscendingCommand.Execute(nameof(OrderListSort.OrderId));
        WaitUntil(() => !model.IsLoading);
        Check(!menu.IsOpen && model.SortColumn == OrderListSort.OrderId, "sorting dismisses the old popup before replacing rows");
        model.SortDescendingCommand.Execute(nameof(OrderListSort.OrderDate));
        WaitUntil(() => !model.IsLoading);
    }

    private static void CheckOrderMenuPlacement()
    {
        var placement = assembly.GetType("SalesManagement_SysDev.OrderRowMenuPlacement")!
            .GetMethod("Calculate", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var scale in new[] { 1d, 1.25d, 1.5d })
        {
            var window = new Rect(-1620, 130, 1000 * scale, 600 * scale);
            var menu = new Size(208 * scale, 175 * scale);
            var gap = new Size(4 * scale, 4 * scale);
            Rect Place(Rect anchor)
            {
                var offset = (Point)placement.Invoke(null, [window, anchor, menu, gap])!;
                return new Rect(anchor.TopLeft + (Vector)offset, menu);
            }
            var first = new Rect(window.Right - 34 * scale, window.Top + 116 * scale, 32 * scale, 28 * scale);
            var last = new Rect(window.Right - 34 * scale, window.Bottom - 54 * scale, 32 * scale, 28 * scale);
            Check(window.Contains(Place(first)) && Place(first).Top >= first.Bottom + gap.Height,
                $"menu fits below an upper row and stays inside the client bounds at {scale:P0} scaling");
            Check(window.Contains(Place(last)) && Place(last).Bottom <= last.Top - gap.Height,
                $"menu flips above a lower row and stays inside the client bounds at {scale:P0} scaling");
        }
    }

    private static void CheckBulkButtons(FrameworkElement root, bool[] enabled)
    {
        FlushBindings();
        var buttons = Descendants<WrapPanel>(root).Single(panel => panel.Name == "listActions").Children.OfType<Button>().ToArray();
        Check(buttons.Select(button => button.IsEnabled).SequenceEqual(enabled)
            && buttons.All(button => button.ActualHeight == 32 && Descendants<LucideIcon>(button).Count() == 1)
            && buttons.Where(button => !button.IsEnabled).All(button => HasColor(button.Background, Color.FromRgb(0xF1, 0xF5, 0xF9))
                && HasColor(button.Foreground, Color.FromRgb(0x94, 0xA3, 0xB8)) && HasColor(button.BorderBrush, Color.FromRgb(0xE2, 0xE8, 0xF0))),
            $"bulk actions update immediately with visible enabled and disabled styles ({string.Join(", ", enabled)})");
    }

    private static (int Dialogs, bool ResubmissionLocked) AcceptOrderConfirmation(F_Main owner, OrderManagementViewModel model, Action invoke)
    {
        var dialogs = 0;
        var locked = false;
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            var notice = owner.OwnedWindows.OfType<F_OrderNotice>().FirstOrDefault(window => window.IsVisible);
            if (notice == null) return;
            notice.Opacity = 0;
            var dialog = (OrderNoticeViewModel)notice.DataContext;
            dialogs++;
            if (dialog.Title == "受注を確定") locked = !model.ConfirmSelectedCommand.CanExecute(null);
            dialog.ConfirmCommand.Execute(null);
        };
        timer.Start();
        try
        {
            invoke();
            WaitUntil(() => !model.IsBusy);
            return (dialogs, locked);
        }
        finally { timer.Stop(); }
    }

    private static void CheckOrderMenuWrites(SalesManagementContext db, F_Main owner, DataGrid grid,
        OrderManagementViewModel model, int orderId)
    {
        var originalStock = db.TStocks.AsNoTracking().Sum(stock => stock.StQuantity);
        var menu = OpenOrderMenu(grid, model.Rows.Single());
        var confirm = VisibleMenuItems(menu).Single(item => Equals(item.Header, "受注を確定する"));
        var (dialogs, locked) = AcceptOrderConfirmation(owner, model, () => confirm.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)));
        Check(dialogs == 2 && locked && !menu.IsOpen && model.Rows.Single().IsConfirmed
            && db.TChumons.Count(order => order.OrId == orderId) == 1 && db.TStocks.AsNoTracking().Sum(stock => stock.StQuantity) == originalStock,
            "row confirmation uses the existing dialog, commits once, refreshes status and leaves inventory untouched");

        menu = OpenOrderMenu(grid, model.Rows.Single());
        var hideItem = VisibleMenuItems(menu).Single(item => Equals(item.Header, "非表示にする"));
        InteractWithModal(owner, () => hideItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)), modal =>
        {
            var hide = (OrderHideViewModel)modal.DataContext;
            Check(!menu.IsOpen, "row hide closes the menu before opening its existing reason dialog");
            hide.Reason = "行操作メニューの検証";
            hide.HideCommand.Execute(null);
            WaitUntil(() => hide.HasChanges && hide.CanClose);
            modal.Close();
        });
        WaitUntil(() => !model.IsBusy);
        Check(model.Rows.Count == 0 && db.TOrders.AsNoTracking().Any(order => order.OrId == orderId && order.OrFlag == 2),
            "row hide refreshes the active list after the existing service commits");
    }

    private static void ShowInteractiveOrderMenu()
    {
        var shell = new F_Main { Width = 1200, Height = 690, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        var (root, _, _) = OpenOrderList(shell);
        shell.Title = "受注管理（操作メニュー検証）";
        var menu = (ContextMenu)Descendants<Grid>(root).Single(grid => grid.Name == "orderRoot").FindResource("OrderRowActions");
        menu.Opened += (_, _) =>
        {
            var button = (Button)menu.PlacementTarget;
            var number = ((OrderListRow)menu.DataContext).OrderNumber;
            var popup = ScreenBounds(menu);
            var anchor = ScreenBounds(button);
            var bounds = ScreenBounds((FrameworkElement)shell.Content);
            var placement = popup.Top >= anchor.Bottom ? "below" : popup.Bottom <= anchor.Top ? "above" : "overlaps";
            Console.WriteLine($"Interactive menu opened for {number}, {placement}, inside client: {bounds.Contains(popup)}");
        };
        menu.Closed += (_, _) => Console.WriteLine("Interactive menu closed");
        menu.PreviewKeyDown += (_, e) => Console.WriteLine($"Interactive menu key {e.Key}");
        var frame = new DispatcherFrame();
        shell.Closed += (_, _) => frame.Continue = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        timer.Tick += (_, _) => shell.Close();
        timer.Start();
        Console.WriteLine("Interactive order menu fixture ready.");
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }

        static Rect ScreenBounds(FrameworkElement element) => new(element.PointToScreen(new Point()), element.PointToScreen((Point)element.RenderSize));
    }
}
