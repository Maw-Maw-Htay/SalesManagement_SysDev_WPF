using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SalesManagement_SysDev;

internal static class Program
{
    private static int checks;

    [STAThread]
    private static int Main(string[] args)
    {
        var app = new App();
        try
        {
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            CheckSalesScopeAndHistoryView();
            CheckAdminScopeAndNavigation();
            CheckExistingOrderHistoryBaseline();
            CheckRecordedProductComparison();
            CheckStatusTabColors();
            if (args.Contains("--read-only-localdb"))
            {
                Task.Run(CheckLocalDbAsync).GetAwaiter().GetResult();
                CheckRenderedAuditView();
            }
            AppSession.Employee = null;
            app.Shutdown();
            Console.WriteLine($"PASS: {checks} history checks. " + (args.Contains("--read-only-localdb") ? "LocalDB was queried read-only." : "No database connection was opened."));
            return 0;
        }
        catch (Exception ex)
        {
            AppSession.Employee = null;
            foreach (Window window in app.Windows.Cast<Window>().ToArray()) window.Close();
            app.Shutdown();
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void CheckSalesScopeAndHistoryView()
    {
        HistoryNotificationService.MarkAllRead();
        AppSession.Employee = Employee("営業");
        var view = new HistoryManagementView(_ => { });
        var modules = (ComboBox)view.FindName("ModuleFilter")!;
        var operations = (ComboBox)view.FindName("OperationFilter")!;
        var grid = (DataGrid)view.FindName("HistoryGrid")!;

        Check(modules.Items.Contains(HistoryManagementService.OrdersModule) && modules.Items.Contains("注文管理") && !modules.Items.Contains("発注管理"),
            "sales history exposes authorized module choices, including modules without an audit source");
        Check(!modules.Items.Contains(HistoryManagementService.EmployeesModule),
            "employee history is hidden from sales employees");
        Check(!operations.Items.Contains(HistoryManagementService.EmployeeOperations[0]),
            "sales employees cannot filter for employee operation types");
        Check(operations.Items.Contains(OrderHistoryOperations.Baseline),
            "record-start snapshots remain an explicit filter value");
        Check(grid.Columns.Count == 8 && grid.IsReadOnly && !grid.CanUserSortColumns,
            "central history grid has the read-only columns and stable server ordering");
        Check(grid.EnableRowVirtualization && grid.EnableColumnVirtualization,
            "central history grid virtualizes rows and columns");
        Check(((TextBlock)view.FindName("SearchPlaceholder")!).Text == "伝票番号・商品名・顧客名・担当者・変更内容で検索...",
            "central history keeps one primary search field with the requested prompt");

        var fMain = new F_Main();
        var sidebar = (FrameworkElement)fMain.FindName("mainSidebar")!;
        var model = (SidebarViewModel)sidebar.DataContext;
        var entry = model.MenuItems.Single(x => x.Label == "履歴管理");
        var masterCategoryIndex = model.MenuItems.IndexOf(model.MenuItems.Single(x => x.IsCategory && x.Label == "マスタ管理"));
        var systemCategoryIndex = model.MenuItems.IndexOf(model.MenuItems.Single(x => x.IsCategory && x.Label == "システム管理"));
        var historyIndex = model.MenuItems.IndexOf(entry);
        Check(masterCategoryIndex < systemCategoryIndex && systemCategoryIndex + 1 == historyIndex
            && model.MenuItems[systemCategoryIndex].HasSeparator,
            "history is grouped beneath the system management divider after master management");
        Check(entry.IconName == "History", "history navigation reuses the existing history icon");
        Check(!entry.ShowUnreadBadge, "history unread badge is hidden when no unread count is available");
        CheckSidebarRendering(fMain, model, entry);
        HistoryNotificationService.RecordNewManagementEdit();
        Check(entry.UnreadCount == 1 && entry.ShowUnreadBadge,
            "a new audited management edit updates the sidebar badge");
        HistoryNotificationService.MarkAllRead();
        Check(entry.UnreadCount == 0 && !entry.ShowUnreadBadge,
            "marking history as viewed clears the live session count");
        entry.UnreadCount = 23;
        Check(entry.ShowUnreadBadge && entry.UnreadCountText == "23", "history unread badge uses its supplied count");
        entry.UnreadCount = 100;
        Check(entry.UnreadCountText == "99+", "history unread badge caps counts above 99");
        entry.UnreadCount = 23;
        model.IsSidebarExpanded = false;
        Check(!entry.IsSidebarExpanded && entry.ShowUnreadBadge,
            "history unread badge stays visible with the collapsed sidebar");
        model.IsSidebarExpanded = true;
        Check(entry.IsSidebarExpanded && entry.ShowUnreadBadge,
            "history unread badge stays visible with the expanded sidebar");
        entry.UnreadCount = 0;
        Check(!entry.ShowUnreadBadge, "history unread badge hides again at zero");
        fMain.Close();
    }

    private static void CheckSidebarRendering(F_Main fMain, SidebarViewModel model, SidebarEntry entry)
    {
        fMain.Show();
        fMain.UpdateLayout();

        var historyButton = Descendants<Button>(fMain).Single(button => ReferenceEquals(button.DataContext, entry));
        var productEntry = model.MenuItems.Single(x => x.Label == "商品管理");
        var shipmentEntry = model.MenuItems.Single(x => x.Label == "出荷管理");
        var productButton = Descendants<Button>(fMain).Single(button => ReferenceEquals(button.DataContext, productEntry));
        var shipmentButton = Descendants<Button>(fMain).Single(button => ReferenceEquals(button.DataContext, shipmentEntry));
        var historyIcon = Descendants<LucideIcon>(historyButton).Single();
        var badge = Descendants<Border>(historyButton).Single(border => AutomationProperties.GetName(border) == "未読履歴件数");
        var badgeText = Descendants<TextBlock>(badge).Single();
        var historyLabel = Descendants<TextBlock>(historyButton).Single(text => text.Text == "履歴管理");
        var sidebar = (FrameworkElement)fMain.FindName("mainSidebar")!;
        var navigationStyle = (Style)fMain.Resources["NavigationButton"];

        Check(historyButton.ActualHeight == productButton.ActualHeight
            && historyButton.ActualHeight == shipmentButton.ActualHeight
            && historyButton.ActualHeight == 36,
            "history uses the same compact row height as product and shipment navigation");
        Check(historyIcon.ActualWidth == 18 && historyIcon.ActualHeight == 18,
            "history uses the standard 18-pixel sidebar icon size");
        Check(ReferenceEquals(historyButton.Style.BasedOn, navigationStyle),
            "history inherits the shared sidebar hover, selected, spacing and foreground styles");
        Check(historyButton.ToolTip as string == "履歴管理"
            && productButton.ToolTip as string == "商品管理",
            "history and other management rows use the same label tooltip behavior");

        entry.UnreadCount = 1;
        fMain.UpdateLayout();
        Check(badge.Visibility == Visibility.Visible && badgeText.Text == "1",
            "sidebar badge displays a single unread history item");
        CheckBadgeBounds(historyButton, sidebar, historyIcon, badge, "1");

        entry.UnreadCount = 23;
        fMain.UpdateLayout();
        Check(badge.Visibility == Visibility.Visible && badgeText.Text == "23",
            "sidebar badge displays the actual two-digit unread count");
        CheckBadgeBounds(historyButton, sidebar, historyIcon, badge, "23");

        entry.UnreadCount = 100;
        fMain.UpdateLayout();
        Check(badge.Visibility == Visibility.Visible && badgeText.Text == "99+",
            "sidebar badge caps counts above 99 without clipping");
        CheckBadgeBounds(historyButton, sidebar, historyIcon, badge, "99+");

        entry.UnreadCount = 0;
        fMain.UpdateLayout();
        Check(badge.Visibility == Visibility.Collapsed,
            "sidebar badge is hidden when the unread count returns to zero");

        entry.UnreadCount = 23;
        model.IsSidebarExpanded = false;
        fMain.UpdateLayout();
        Check(badge.IsVisible && historyIcon.IsVisible && historyLabel.Visibility == Visibility.Collapsed
            && historyButton.ToolTip as string == "履歴管理",
            "collapsed sidebar keeps the normal history icon and badge with its tooltip");
        CheckBadgeBounds(historyButton, sidebar, historyIcon, badge, "collapsed 23");

        model.IsSidebarExpanded = true;
        fMain.UpdateLayout();
        Check(badge.IsVisible && historyIcon.IsVisible && historyLabel.IsVisible,
            "expanded sidebar shows the icon, badge and label in one navigation row");
        CheckBadgeBounds(historyButton, sidebar, historyIcon, badge, "expanded 23");

        var template = (ControlTemplate)navigationStyle.Setters.OfType<Setter>()
            .Single(setter => setter.Property == Control.TemplateProperty).Value;
        Check(template.Triggers.OfType<Trigger>().Any(trigger => trigger.Property == UIElement.IsMouseOverProperty)
            && template.Triggers.OfType<Trigger>().Any(trigger => trigger.Property == FrameworkElement.TagProperty),
            "history retains the shared hover and selected visual states");

        entry.IsActive = true;
        fMain.UpdateLayout();
        var activeBorder = (Border)historyButton.Template.FindName("navBorder", historyButton);
        Check(historyButton.Tag as string == "Active"
            && Equals(activeBorder.Background, fMain.FindResource("ThemeSidebarActive")),
            "history selected state uses the standard active background");
        entry.IsActive = false;
        entry.UnreadCount = 0;
        model.IsSidebarExpanded = true;
        fMain.UpdateLayout();
    }

    private static void CheckBadgeBounds(Button button, FrameworkElement sidebar, LucideIcon icon, Border badge, string label)
    {
        var iconBounds = icon.TransformToAncestor(button).TransformBounds(new Rect(new Point(), icon.RenderSize));
        var badgeBounds = badge.TransformToAncestor(button).TransformBounds(new Rect(new Point(), badge.RenderSize));
        var sidebarBounds = badge.TransformToAncestor(sidebar).TransformBounds(new Rect(new Point(), badge.RenderSize));
        var sidebarRect = new Rect(0, 0, sidebar.ActualWidth, sidebar.ActualHeight);

        Check(badgeBounds.Top <= iconBounds.Top && badgeBounds.Bottom > iconBounds.Top
            && badgeBounds.Left < iconBounds.Right && badgeBounds.Right > iconBounds.Right,
            $"{label} badge overlaps the icon's top-right corner");
        Check(sidebarRect.Contains(sidebarBounds), $"{label} badge stays inside sidebar clipping bounds");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void CheckAdminScopeAndNavigation()
    {
        AppSession.Employee = Employee("管理者");
        var view = new HistoryManagementView(_ => { });
        var modules = (ComboBox)view.FindName("ModuleFilter")!;
        var operations = (ComboBox)view.FindName("OperationFilter")!;
        Check(modules.Items.Contains(HistoryManagementService.OrdersModule)
            && modules.Items.Contains(HistoryManagementService.EmployeesModule),
            "administrator history filters expose both recorded sources");
        Check(operations.Items.Contains(HistoryManagementService.EmployeeOperations[0]),
            "administrator history filters include recorded employee operations");
        var menu = (Menu)view.FindName("ModuleTabs")!;
        Check(menu.Items.Count == 11 && menu.Items.Cast<MenuItem>().Count(x => x.IsChecked) == 1
            && ((ComboBox)view.FindName("PageSizePicker")!).Items.Cast<int>().SequenceEqual(new[] { 20, 50 }),
            "central history exposes all administrator tabs and 20/50-row pagination with one selected module");
        Check(((FrameworkElement)view.FindName("AdvancedFilters")!).Visibility == Visibility.Collapsed,
            "advanced history conditions start collapsed");

        var fMain = new F_Main();
        var navigation = (FrameworkElement)fMain.FindName("mainSidebar")!;
        var model = (SidebarViewModel)navigation.DataContext;
        var entry = model.MenuItems.Single(x => x.Label == "履歴管理");
        entry.NavigateCommand.Execute(null);
        var pageHost = (ContentControl)fMain.FindName("pageHost")!;
        Check(pageHost.Content is HistoryManagementView,
            "history navigation opens in the existing shell page host");
        Check(((TextBlock)fMain.FindName("mainHeaderTitle")!).Text == "履歴管理",
            "shared shell header names the centralized history page");
        Check(entry.IsActive, "central history navigation receives the standard selected state");
        typeof(F_Main).GetMethod("ShowEmployeeHistory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(fMain, [123]);
        var employeeHistory = (HistoryManagementView)pageHost.Content;
        Check(((ComboBox)employeeHistory.FindName("ModuleFilter")!).Items.Count == 1
            && Equals(((ComboBox)employeeHistory.FindName("ModuleFilter")!).SelectedItem, HistoryManagementService.EmployeesModule)
            && ((TextBox)employeeHistory.FindName("SearchBox")!).Text == "E-0123"
            && entry.IsActive, "employee detail deep-links to the same history page with an employee number");
        fMain.Close();
    }

    private static void CheckRecordedProductComparison()
    {
        var entry = new OrderOperationHistory { Operation = OrderHistoryOperations.Updated, BeforeState = 0, AfterState = 0,
            Details = "変更前明細：モニター（商品 171）：8 点、¥80,000 / ケーブル（商品 10）：2 点、¥1,000\n明細：モニター（商品 171）：9 点、¥90,000 / ケーブル（商品 10）：2 点、¥1,000" };
        var vm = new OrderHistoryDetailViewModel(entry, () => { }, () => { });
        var line = vm.ProductChanges.Single();
        Check(line.ProductId == 171 && line.BeforeQuantity == "8 点" && line.AfterQuantity == "9 点" && line.QuantityChanged
            && line.BeforeAmount == "¥80,000" && line.AfterAmount == "¥90,000" && line.AmountChanged && !vm.HasRelevantComparisons,
            "recorded product changes omit unchanged lines and distinguish quantity and amount differences");
        var snapshot = new OrderOperationHistory { Operation = OrderHistoryOperations.Confirmed, Details = "明細：モニター（商品 171）：9 点、¥90,000" };
        Check(!new OrderHistoryDetailViewModel(snapshot, () => { }, () => { }).HasProductChanges,
            "confirmation snapshots without a prior line list do not invent product additions");
    }

    private static async Task CheckLocalDbAsync()
    {
        using var db = new SalesManagementContext();
        var actors = await db.MEmployees.AsNoTracking().Include(x => x.Po).Include(x => x.So)
            .Where(x => x.EmFlag == 0 && x.Po.PoFlag == 0).OrderBy(x => x.EmId).ToListAsync();
        var admin = actors.First(x => x.Po.PoName == "管理者");
        var sales = actors.First(x => x.Po.PoName == "営業");
        AppSession.Employee = admin;
        var auditBefore = await db.OrderOperationHistories.CountAsync();
        var employeeBefore = await db.EmployeeAssignmentHistories.CountAsync();
        var stockBefore = await db.TStocks.CountAsync();
        var first = await HistoryManagementService.ReadAsync(db, new() { PageSize = 20 });
        var second = await HistoryManagementService.ReadAsync(db, new() { PageIndex = 1, PageSize = 20 });
        Check(first.Total == auditBefore + employeeBefore && first.Rows.Count == Math.Min(20, first.Total)
            && first.Counts[HistoryManagementService.OrdersModule] == auditBefore,
            "read-only LocalDB: administrator counts match the two stored audit sources");
        Check(first.Total > 20 && !first.Rows.Select(x => (x.Module, x.EventId)).Intersect(second.Rows.Select(x => (x.Module, x.EventId))).Any()
            && first.Rows.Zip(first.Rows.Skip(1)).All(pair => pair.First.RecordedAt >= pair.Second.RecordedAt),
            "read-only LocalDB: audit pages sort newest first without overlapping event IDs");
        var catalogSample = await db.OrderOperationHistories.AsNoTracking().FirstAsync(x => x.ActorName != "");
        var day = catalogSample.RecordedAt.ToOffset(OrderHistoryService.JapanOffset).Date;
        var match = await HistoryManagementService.ReadAsync(db, new() { Keyword = $"ORD-{catalogSample.OrderId:000000}",
            TargetName = catalogSample.CustomerName, Actor = catalogSample.ActorName, From = day, To = day, Module = HistoryManagementService.OrdersModule, PageSize = 50 });
        Check(match.Rows.Any(x => x.EventId == catalogSample.Id) && match.Rows.All(x => x.OrderId == catalogSample.OrderId
            && x.RecordedAt.ToOffset(OrderHistoryService.JapanOffset).Date == day),
            "read-only LocalDB: document, employee, customer and Japanese-date filters combine");
        var timeline = await HistoryManagementService.ReadTimelineAsync(db, match.Rows.First());
        Check(timeline.All(x => x.OrderId == catalogSample.OrderId) && timeline.Zip(timeline.Skip(1)).All(pair => pair.First.RecordedAt <= pair.Second.RecordedAt),
            "read-only LocalDB: the timeline contains only that record and sorts oldest first");
        var unsupported = await HistoryManagementService.ReadAsync(db, new() { Module = "出荷管理" });
        Check(unsupported.Total == 0 && !unsupported.Counts.ContainsKey("出荷管理"), "read-only LocalDB: unsupported audit modules do not invent counts");
        if (employeeBefore > 0)
        {
            var assignment = await db.EmployeeAssignmentHistories.AsNoTracking().FirstAsync();
            var master = await HistoryManagementService.ReadAsync(db, new() { Module = HistoryManagementService.MasterModule,
                EmployeeId = assignment.EmployeeId, TargetNumber = $"E-{assignment.EmployeeId:0000}" });
            Check(master.Total > 0 && master.Rows.All(x => x.EmployeeId == assignment.EmployeeId), "read-only LocalDB: master history stays scoped to the selected employee");
        }
        foreach (var module in BusinessHistoryService.GetModules("管理者"))
        {
            var transactions = await BusinessHistoryService.ReadAsync(module);
            Check(transactions.All(x => x.Module == module) && transactions.Select(x => x.Id).Distinct().Count() == transactions.Count,
                $"read-only LocalDB: business history reads existing distinct records for {module} ({transactions.Count})");
        }
        AppSession.Employee = sales;
        var scoped = await HistoryManagementService.ReadAsync(db, new());
        Check(!scoped.Counts.ContainsKey(HistoryManagementService.EmployeesModule)
            && scoped.Total == await db.OrderOperationHistories.CountAsync(x => x.OfficeId == sales.SoId)
            && scoped.Rows.All(x => x.OrderEntry?.OfficeId == sales.SoId), "read-only LocalDB: sales audit is limited to the current office");
        try { await HistoryManagementService.ReadAsync(db, new() { Module = HistoryManagementService.EmployeesModule }); Check(false, "employee access denied"); }
        catch (UnauthorizedAccessException) { Check(true, "read-only LocalDB: the service denies employee audit access to sales"); }
        try { await BusinessHistoryService.ReadAsync("発注管理"); Check(false, "purchase access denied"); }
        catch (UnauthorizedAccessException) { Check(true, "read-only LocalDB: the service denies purchase history access to sales"); }
        Check(auditBefore == await db.OrderOperationHistories.CountAsync() && employeeBefore == await db.EmployeeAssignmentHistories.CountAsync()
            && stockBefore == await db.TStocks.CountAsync(), "read-only LocalDB: browsing did not add audit or stock rows");
        AppSession.Employee = admin;
    }

    private static void CheckRenderedAuditView()
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var view = new HistoryManagementView(_ => { });
        var window = new Window { Width = 1060, Height = 670, Left = -30000, Top = -30000,
            ShowActivated = false, ShowInTaskbar = false, Content = view };
        try
        {
            window.Show();
            var grid = (DataGrid)view.FindName("HistoryGrid")!;
            var count = (TextBlock)view.FindName("ResultCount")!;
            var next = (Button)view.FindName("NextPageButton")!;
            WaitFor(() => grid.Items.Count == 20 && next.IsEnabled);
            Check(count.Text.StartsWith("1～20件") && Descendants<ScrollViewer>(grid).All(x => x.ScrollableWidth < 1),
                "rendered central history displays the first page without horizontal grid scrolling");
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var errorOverlay = (FrameworkElement)view.FindName("ErrorOverlay")!;
            try { WaitFor(() => count.Text.StartsWith("21～", StringComparison.Ordinal) || errorOverlay.Visibility == Visibility.Visible); }
            catch (TimeoutException ex) { throw new InvalidOperationException($"Next page stalled at '{count.Text}', rows={grid.Items.Count}, next={next.IsEnabled}.", ex); }
            Check(grid.Items.Count == 20 && count.Text.StartsWith("21～", StringComparison.Ordinal),
                $"rendered next-page control loads the requested page (count '{count.Text}', rows {grid.Items.Count})");

            var menu = (Menu)view.FindName("ModuleTabs")!;
            var all = (MenuItem)menu.Items[0];
            window.UpdateLayout();
            var header = (StackPanel)all.Header;
            Check(all.IsChecked && IsBrushColor(((LucideIcon)header.Children[0]).Stroke, Colors.White)
                && IsBrushColor(((TextBlock)header.Children[1]).Foreground, Colors.White)
                && IsBrushColor(((TextBlock)((Border)header.Children[2]).Child).Foreground, Colors.White),
                "rendered selected history tab shows white icon, label and loaded count");

            var modules = (ComboBox)view.FindName("ModuleFilter")!;
            modules.SelectedItem = "出荷管理";
            WaitFor(() => ((FrameworkElement)view.FindName("EmptyState")!).Visibility == Visibility.Visible);
            Check(grid.Items.Count == 0 && count.Text == "—" && ((TextBlock)view.FindName("EmptyTitle")!).Text.Contains("記録されていません"),
                "unsupported module renders an unavailable-history message instead of a zero count");
            var from = (DatePicker)view.FindName("FromDate")!;
            var to = (DatePicker)view.FindName("ToDate")!;
            from.SelectedDate = new DateTime(2026, 11, 30); to.SelectedDate = new DateTime(2026, 11, 1);
            var error = (FrameworkElement)view.FindName("ErrorOverlay")!;
            Check(error.Visibility == Visibility.Visible && !next.IsEnabled, "reversed date range is rejected and cannot page stale results");
        }
        finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    private static void WaitFor(Func<bool> condition)
    {
        if (condition()) return;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) => { if (condition() || DateTime.UtcNow >= deadline) { timer.Stop(); frame.Continue = false; } };
        timer.Start(); Dispatcher.PushFrame(frame);
        if (!condition()) throw new TimeoutException("The rendered history view did not reach the expected state.");
    }

    private static void CheckExistingOrderHistoryBaseline()
    {
        var details = "履歴記録の開始時点です。過去の操作日時・実行者・変更前の値は未記録です。";
        var entry = new OrderOperationHistory
        {
            Id = 731,
            RecordedAt = new DateTimeOffset(2026, 11, 13, 3, 0, 0, TimeSpan.Zero),
            Operation = OrderHistoryOperations.Baseline,
            OrderId = 253,
            CustomerId = 41,
            CustomerName = "検証顧客",
            OfficeId = 1,
            AfterState = 0,
            AfterManagement = 0,
            ActorName = "システム（初期記録）",
            Details = details
        };
        var viewModel = new OrderHistoryDetailViewModel(entry, () => { }, () => { });
        var state = viewModel.Comparisons.Single(row => row.Field == "受注状態");

        Check(viewModel.EventId == "731" && viewModel.OrderNumber == "ORD-000253",
            "existing order history detail retains its event ID and order number");
        Check(state.Before == "変更前は未記録" && state.After == "未確定" && !state.Changed,
            "baseline order snapshots do not invent previous state values");
        Check(viewModel.Display.ChangeSummary == "保存済みの状態を記録。過去の変更値は未記録です。",
            "baseline history remains distinguishable from a recorded operation");
        Check(viewModel.OriginalDetails == details,
            "existing order history keeps the original audit text available");
        var owner = new Window();
        owner.Show();
        owner.Hide();
        var dialog = new F_OrderHistoryDetail(entry, owner, () => { });
        Check(((OrderHistoryDetailViewModel)dialog.DataContext).EventId == "731",
            "existing order history detail dialog exposes the recorded event ID");
        owner.Close();
    }

    private static void CheckStatusTabColors()
    {
        var projectRoot = FindProjectRoot();
        var applicationProject = Path.Combine(projectRoot, "SalesManagement_SysDev");
        var menuTabFiles = new Dictionary<string, int>
        {
            ["Views/OrderFulfillment/OrderManagementView.xaml"] = 4, // 注文管理: this view uses ChumonManagementViewModel.
            ["Views/Purchasing/PurchaseOrderManagementView.xaml"] = 6,
            ["Views/Warehouse/WarehouseMovementManagementView.xaml"] = 4,
            ["Views/Arrival/ArrivalManagementView.xaml"] = 3,
            ["Views/Outbound/OutboundManagementView.xaml"] = 4,
            ["Views/Shipping/ShipmentManagementView.xaml"] = 3,
            ["Views/Inventory/InventoryManagementView.xaml"] = 4,
            ["Views/Sales/SalesManagementView.xaml"] = 3
        };

        var totalMenuTabs = 0;
        foreach (var (file, expectedCount) in menuTabFiles)
        {
            var document = XDocument.Load(Path.Combine(applicationProject, file));
            var menuItems = document.Descendants().Where(element =>
                element.Name.LocalName == "MenuItem"
                && ((string?)element.Attribute("Style"))?.Contains("StatusMenuItem", StringComparison.Ordinal) == true).ToArray();
            Check(menuItems.Length == expectedCount, $"{file} exposes all {expectedCount} existing status tabs");

            foreach (var item in menuItems)
            {
                var name = (string?)item.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))
                    ?? item.Descendants().FirstOrDefault(child => child.Name.LocalName == "TextBlock")?.Attribute("Text")?.Value
                    ?? "status tab";
                var icons = item.Descendants().Where(child => child.Name.LocalName == "LucideIcon").ToArray();
                var countBadges = item.Descendants().Where(child =>
                    child.Name.LocalName == "Border" && child.Attribute("CornerRadius") != null).ToArray();
                Check(icons.Length > 0 && icons.All(icon =>
                        ((string?)icon.Attribute("Style"))?.Contains("StatusTabIcon", StringComparison.Ordinal) == true
                        && icon.Attribute("Tag") != null),
                    $"{file} {name} icon retains its module color and supports selected white");
                Check(countBadges.Length == 1
                    && ((string?)countBadges[0].Attribute("Style"))?.Contains("StatusTabCountBadge", StringComparison.Ordinal) == true
                    && countBadges[0].Attribute("Tag") != null,
                    $"{file} {name} count uses the selected-state badge style");
                Check(!item.Descendants().Any(child =>
                        child.Name.LocalName == "TextBlock" && child.Attribute("Foreground") != null)
                    && ((string?)item.Attribute("Click")) is { Length: > 0 },
                    $"{file} {name} has no hardcoded child foreground and retains its filter command");
                totalMenuTabs++;
            }
        }
        Check(totalMenuTabs == 31, "all 31 shared-menu status tabs are covered");

        var buttonTabFiles = new[]
        {
            "Views/Customers/CustomerManagementView.xaml", "Views/Products/ProductManagementView.xaml", "Views/Employees/EmployeeManagementView.xaml"
        };
        foreach (var file in buttonTabFiles)
        {
            var document = XDocument.Load(Path.Combine(applicationProject, file));
            var buttons = document.Descendants().Where(element =>
                element.Name.LocalName == "Button"
                && ((string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")))
                    is "AllTab" or "VisibleTab" or "HiddenTab").ToArray();
            Check(buttons.Length == 3 && buttons.All(button =>
                    ((string?)button.Attribute("Style"))?.Contains("StatusFilterButton", StringComparison.Ordinal) == true
                    && ((string?)button.Attribute("Click")) is { Length: > 0 }),
                $"{file} keeps its three filter commands on the shared status-button style");
        }

        Check(File.ReadAllText(Path.Combine(applicationProject, "Views/OrderFulfillment/OrderManagementView.xaml.cs"))
                .Contains("new ChumonManagementViewModel()", StringComparison.Ordinal),
            "the styled OrderManagementView is 注文管理, not the excluded 受注管理 screen");
        var orderManagementMarkup = File.ReadAllText(Path.Combine(applicationProject, "Views/Orders/F_OrderManagement.xaml"));
        Check(!orderManagementMarkup.Contains("StatusTabIcon", StringComparison.Ordinal)
            && !orderManagementMarkup.Contains("StatusFilterButton", StringComparison.Ordinal),
            "受注管理 keeps its existing status-tab markup unchanged");

        CheckStatusTabRuntimeColors();
    }

    private static void CheckStatusTabRuntimeColors()
    {
        var app = Application.Current;
        var assemblyName = typeof(F_Main).Assembly.GetName().Name;
        var orderStyles = (ResourceDictionary)Application.LoadComponent(
            new Uri($"/{assemblyName};component/Resources/OrderManagement.xaml", UriKind.Relative));
        var window = new Window { Width = 560, Height = 150, ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow };
        window.Resources.MergedDictionaries.Add(orderStyles);

        var menu = new Menu { Background = Brushes.Transparent, Padding = new Thickness(0), IsMainMenu = false };
        var menuItem = new MenuItem
        {
            Style = (Style)orderStyles["StatusMenuItem"],
            IsCheckable = true,
            Header = new StackPanel { Orientation = Orientation.Horizontal }
        };
        var header = (StackPanel)menuItem.Header;
        var icon = new LucideIcon
        {
            Data = new RectangleGeometry(new Rect(0, 0, 16, 16)),
            Tag = orderStyles["OrderDanger"],
            Style = (Style)orderStyles["StatusTabIcon"],
            Width = 16,
            Height = 16
        };
        var label = new TextBlock
        {
            Text = "非表示",
            Tag = orderStyles["OrderDanger"],
            Style = (Style)orderStyles["StatusTabColoredText"],
            Margin = new Thickness(8, 0, 8, 0)
        };
        var count = new TextBlock { Text = "23" };
        var badge = new Border
        {
            Tag = orderStyles["OrderDangerSurface"],
            Style = (Style)orderStyles["StatusTabCountBadge"],
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 0, 5, 0),
            Child = count
        };
        header.Children.Add(icon);
        header.Children.Add(label);
        header.Children.Add(badge);
        menu.Items.Add(menuItem);

        var button = new Button
        {
            Style = (Style)app.FindResource("StatusFilterButton"),
            Tag = "StatusActive",
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new LucideIcon
                    {
                        Data = new RectangleGeometry(new Rect(0, 0, 15, 15)),
                        Style = (Style)app.FindResource("StatusFilterTabIcon"),
                        Width = 15,
                        Height = 15
                    },
                    new TextBlock { Text = "すべて" }
                }
            }
        };
        var buttonContent = (StackPanel)button.Content;
        var buttonIcon = (LucideIcon)buttonContent.Children[0];
        var buttonText = (TextBlock)buttonContent.Children[1];

        var root = new StackPanel();
        root.Children.Add(menu);
        root.Children.Add(button);
        window.Content = root;
        window.Show();
        window.UpdateLayout();

        menuItem.IsChecked = true;
        window.UpdateLayout();
        Check(menuItem.Background.Equals(orderStyles["OrderHeader"])
            && menuItem.Foreground.Equals(Brushes.White),
            "selected shared menu tab uses the master navy background and white foreground");
        Check(IsBrushColor(icon.Stroke, Colors.White), "selected shared menu tab renders its icon white");
        Check(IsBrushColor(label.Foreground, Colors.White), "selected shared menu tab renders its colored label white");
        Check(badge.Background.Equals(orderStyles["OrderHeader"]), "selected shared menu tab renders its count badge navy");
        Check(IsBrushColor(count.Foreground, Colors.White), "selected shared menu tab renders its count white");

        menuItem.IsChecked = false;
        window.UpdateLayout();
        Check(icon.Stroke.Equals(orderStyles["OrderDanger"])
            && label.Foreground.Equals(orderStyles["OrderDanger"])
            && badge.Background.Equals(orderStyles["OrderDangerSurface"]),
            "unselected shared menu tab restores its original module colors");

        window.UpdateLayout();
        Check(button.Background.Equals(app.FindResource("ThemePrimary"))
            && IsBrushColor(button.Foreground, Colors.White)
            && IsBrushColor(buttonText.Foreground, Colors.White)
            && IsBrushColor(buttonIcon.Stroke, Colors.White),
            "selected button tab renders its navy background, label and icon white");
        button.Tag = null;
        window.UpdateLayout();
        Check(button.Background.Equals(app.FindResource("ThemeSurface"))
            && button.Foreground.Equals(app.FindResource("ThemeSecondaryText"))
            && buttonIcon.Stroke.Equals(app.FindResource("ThemeIconDefault")),
            "unselected button tab restores its shared surface and icon colors");

        var menuStyle = (Style)orderStyles["StatusMenuItem"];
        var menuTemplate = (ControlTemplate)menuStyle.Setters.OfType<Setter>()
            .Single(setter => setter.Property == Control.TemplateProperty).Value;
        var highlight = menuTemplate.Triggers.OfType<Trigger>().Single(trigger => trigger.Property == MenuItem.IsHighlightedProperty);
        var checkedTrigger = menuTemplate.Triggers.OfType<Trigger>().Single(trigger => trigger.Property == MenuItem.IsCheckedProperty);
        Check(highlight.Setters.OfType<Setter>().All(setter => setter.Property != Control.ForegroundProperty)
            && checkedTrigger.Setters.OfType<Setter>().Any(setter =>
                setter.Property == Control.ForegroundProperty && Equals(setter.Value, Brushes.White)),
            "shared menu hover cannot override the selected white foreground");

        var buttonStyle = (Style)app.FindResource("StatusFilterButton");
        var buttonTemplate = (ControlTemplate)buttonStyle.Setters.OfType<Setter>()
            .Single(setter => setter.Property == Control.TemplateProperty).Value;
        var buttonHover = buttonTemplate.Triggers.OfType<Trigger>().Single(trigger => trigger.Property == UIElement.IsMouseOverProperty);
        var buttonPressed = buttonTemplate.Triggers.OfType<Trigger>().Single(trigger => trigger.Property == Button.IsPressedProperty);
        Check(buttonHover.Setters.OfType<Setter>().All(setter => setter.Property != Control.ForegroundProperty)
            && buttonPressed.Setters.OfType<Setter>().All(setter => setter.Property != Control.ForegroundProperty),
            "button hover and pressed states do not replace the selected white foreground");
        button.Tag = "StatusActive";
        window.UpdateLayout();
        Keyboard.Focus(button);
        Check(button.IsKeyboardFocused && IsBrushColor(button.Foreground, Colors.White),
            "keyboard focus preserves the selected white foreground");
        button.IsEnabled = false;
        Check(IsBrushColor(button.Foreground, Colors.White),
            "disabled state preserves the selected white foreground value");
        window.Close();
    }

    private static string FindProjectRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SalesManagement_SysDev.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate SalesManagement_SysDev.sln from the test output directory.");
    }

    private static bool IsBrushColor(Brush? brush, Color color) => brush is SolidColorBrush solid && solid.Color == color;

    private static MEmployee Employee(string role) => new()
    {
        EmId = 1,
        EmName = "検証担当者",
        EmPhone = "06-1234-5678",
        EmFlag = EmployeeService.ActiveFlag,
        SoId = 1,
        Po = new MPosition { PoName = role, PoFlag = 0 },
        So = new MSalesOffice { SoId = 1, SoName = "検証営業所" }
    };

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        checks++;
        Console.WriteLine("PASS " + name);
    }
}
