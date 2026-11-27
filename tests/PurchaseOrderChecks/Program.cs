using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using SalesManagement_SysDev;

internal static class Program
{
    private static int checks;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--read-db"))
            {
                CheckDatabase(int.Parse(args[^1]));
                return 0;
            }
            if (args.Contains("--print-preview"))
            {
                ShowSamplePrintPreview();
                return 0;
            }
            CheckProgressAndSummaries();
            CheckSearch();
            CheckRegistrationValidation();
            CheckPermissionsAndHideReason();
            CheckPurchaseScreens();
            Console.WriteLine($"PASS: {checks} purchase-order checks.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { AppSession.Employee = null; }
    }

    private static void CheckProgressAndSummaries()
    {
        Check(PurchaseProgress.GetLabel(0, 0, 0) == "明細なし", "empty order details are not reported received");
        Check(PurchaseProgress.GetLabel(1, 8, 0) == "入庫待ち", "no shelf-confirmed quantities remain outstanding");
        Check(PurchaseProgress.GetLabel(2, 20, 7) == "一部入庫", "partial shelf-confirmed quantities are distinguished");
        Check(PurchaseProgress.GetLabel(1, 5, 5) == "入庫済", "matching received quantity completes the order");
        Check(PurchaseProgress.GetLabel(1, 5, 6) == "数量超過", "over-receipt is surfaced instead of hidden");

        var orders = new[]
        {
            Order(1, 0, ordered: 10, received: 3),
            Order(5, 0, ordered: 2, received: 0),
            Order(2, 0, ordered: 4, received: 4),
            Order(3, 2, ordered: 8, received: 0),
            Order(4, 0, ordered: 5, received: 7)
        };
        var summary = PurchaseProgress.Summarize(orders);
        Check(summary == new PurchaseOrderSummary(4, 1, 1), "summary uses visible orders and excludes hidden orders from progress counts");
        Check(!orders[4].IsFullyReceived, "over-receipt is not counted as a normal completed purchase");
        Check(orders[0].RemainingQuantity == 7, "remaining quantity is the nonnegative difference");
        Check(Order(4, 0, ordered: 5, received: 7).RemainingQuantity == 0, "excess received quantity never creates negative remainder");
        var beforeArrival = Order(6, 0, 4, 0);
        var atWarehouse = beforeArrival with { WarehouseFlag = 1 };
        Check(PurchaseOrderFilter.MatchesProgress(beforeArrival, PurchaseProgressFilter.Waiting)
            && !PurchaseOrderFilter.MatchesProgress(atWarehouse, PurchaseProgressFilter.Waiting)
            && !PurchaseOrderFilter.MatchesProgress(beforeArrival with { WarehouseFlag = null }, PurchaseProgressFilter.Waiting),
            "only warehouse flag zero appears in the outstanding list");
        Check(atWarehouse.ProgressLabel == "棚入れ待ち" && atWarehouse.ReceivedQuantity == 0,
            "warehouse arrival precedes shelf confirmation");
        Check(PurchaseOrderService.FormatCode(8) == "PO-000008", "database identity is formatted as a purchase code");
        Check(PurchaseProgress.Classify([(5, 7), (5, 3)], 2) == PurchaseReceiptStatus.Excess,
            "An over-receipt cannot offset a shortage on another product");
        Check(PurchaseProgress.Classify([(5, 5), (5, 1)], 2) == PurchaseReceiptStatus.Partial,
            "Each product must be fully received to complete the purchase");
    }

    private static void CheckSearch()
    {
        var order = Order(145, 0, ordered: 12, received: 5) with
        {
            OrderDate = new DateTime(2026, 10, 31, 23, 59, 0),
            MakerId = 7,
            MakerName = "山田電機",
            EmployeeId = 11,
            EmployeeName = "物流担当者"
        };
        var criteria = new PurchaseOrderSearchCriteria("P-0007", new DateTime(2026, 10, 1),
            new DateTime(2026, 10, 31), 7, 11, PurchaseProgressFilter.Partial);
        Check(PurchaseOrderFilter.Matches(order, criteria, PurchaseManagementTab.Visible, false, new HashSet<int> { 145 }),
            "product-code match combines with date, maker, employee, and receipt filters");
        Check(PurchaseOrderFilter.Matches(order, criteria with { Keyword = "PO-0001" }, PurchaseManagementTab.Visible, false),
            "purchase-order code supports partial matching");
        Check(PurchaseOrderFilter.Matches(order, criteria with { Keyword = "山田" }, PurchaseManagementTab.Visible, false),
            "manufacturer supports partial matching");
        Check(!PurchaseOrderFilter.Matches(order, criteria with { EndDate = new DateTime(2026, 10, 30) }, PurchaseManagementTab.Visible, false),
            "end-date filter excludes a later calendar date");
        Check(PurchaseOrderFilter.Validate(criteria with { StartDate = new DateTime(2026, 11, 1) }) != null,
            "reversed date range is rejected");
        Check(!PurchaseOrderFilter.Matches(order with { ManagementFlag = PurchaseOrderService.HiddenFlag }, criteria,
            PurchaseManagementTab.Visible, true, new HashSet<int> { 145 }), "visible tab excludes hidden orders");
        Check(PurchaseOrderFilter.Matches(order with { ManagementFlag = PurchaseOrderService.HiddenFlag }, criteria with { Keyword = "" },
            PurchaseManagementTab.Hidden, true), "hidden tab is visible to an authorized viewer");
        Check(!PurchaseOrderFilter.Matches(order with { ManagementFlag = PurchaseOrderService.HiddenFlag }, criteria with { Keyword = "" },
            PurchaseManagementTab.Hidden, false), "hidden records are denied to logistics viewers");
        Check(PurchaseOrderFilter.Matches(order, criteria with { Progress = PurchaseProgressFilter.Waiting },
            PurchaseManagementTab.Visible, false) == false, "receipt progress filter excludes partial orders from waiting");
    }

    private static void CheckRegistrationValidation()
    {
        var validLines = new[] { new PurchaseLineInput(20, 5), new PurchaseLineInput(21, 2) };
        var date = new DateTime(2026, 10, 26);
        Check(PurchaseOrderService.ValidateRegistration(1, date, validLines) == null,
            "valid multi-product purchase order passes validation");
        Check(PurchaseOrderService.ValidateRegistration(0, date, validLines) != null, "manufacturer is required");
        Check(PurchaseOrderService.ValidateRegistration(1, null, validLines) != null, "order date is required");
        Check(PurchaseOrderService.ValidateRegistration(1, date, []) != null, "at least one detail is required");
        Check(PurchaseOrderService.ValidateRegistration(1, date, [new(20, 0)]) != null, "zero quantity is rejected");
        Check(PurchaseOrderService.ValidateRegistration(1, date, [new(20, -2)]) != null, "negative quantity is rejected");
        Check(PurchaseOrderService.ValidateRegistration(1, date, [new(20, 1), new(20, 3)]) != null,
            "duplicate product lines are rejected to avoid accidental double ordering");
    }

    private static void CheckPermissionsAndHideReason()
    {
        AppSession.Employee = Employee("物流");
        Check(PurchaseOrderService.CanView && PurchaseOrderService.CanRegister, "logistics can view and register purchases");
        Check(PurchaseOrderService.CanHide && !PurchaseOrderService.CanViewHidden, "logistics can hide orders in their office");
        Check(!PurchaseOrderService.Hide(999, 1, "理由").Succeeded, "unauthorized hide is denied before database access");

        AppSession.Employee = Employee("営業");
        Check(!PurchaseOrderService.CanView && !PurchaseOrderService.CanRegister, "sales role is denied purchase management");
        Check(!PurchaseOrderService.Register(10, 1, DateTime.Today, [new(1, 1)]).Succeeded,
            "unauthorized registration is denied before database access");

        AppSession.Employee = Employee("管理者");
        Check(PurchaseOrderService.CanView && PurchaseOrderService.CanRegister && PurchaseOrderService.CanHide
            && PurchaseOrderService.CanViewHidden, "administrator can view, register, and hide");
        Check(PurchaseOrderService.ValidateHideReason(" ") != null, "empty hide reason is rejected");
        Check(PurchaseOrderService.ValidateHideReason("重複登録") == null, "nonempty hide reason is accepted");
        Check(PurchaseOrderService.ValidateHideReason(new string('理', 501)) != null, "hide reason length is bounded");
        Check(!PurchaseOrderService.Hide(10, 1, " ").Succeeded, "missing hide reason is denied before database access");

        AppSession.Employee!.EmFlag = 2;
        Check(!PurchaseOrderService.CanView && !PurchaseOrderService.CanRegister && !PurchaseOrderService.CanHide,
            "inactive employees lose purchase permissions");
        AppSession.Employee = null;
        Check(!PurchaseOrderService.CanView && !PurchaseOrderService.CanRegister, "missing session denies purchase access");
    }

    private static void CheckPurchaseScreens()
    {
        AppSession.Employee = new MEmployee { EmId = 1, EmFlag = 0, Po = new MPosition { PoName = "管理者" }, EmName = "検証担当者" };
        var app = CreateApplication();
        var orders = Enumerable.Range(1, 51).Select(i => Order(i, 0, 6, (i % 3) * 3) with { MakerName = "仕入先テスト" }).ToArray();
        var model = (PurchaseOrderManagementViewModel)New(typeof(PurchaseOrderManagementViewModel), false);
        SetField(model, "source", orders);
        var view = (PurchaseOrderManagementView)New(typeof(PurchaseOrderManagementView), model);
        var host = new Window { Width = 1030, Height = 700, Content = view, ShowInTaskbar = false };
        host.Show();
        try
        {
            model.SelectTab(PurchaseManagementTab.Visible);
            Pump();
            var grid = (DataGrid)view.FindName("OrdersGrid");
            Check(grid.Columns.Count == 10 && model.Rows.Count == 20 && model.PageCount == 3, "Purchase list has ten columns and defaults to 20 rows per page");
            var scroll = Descendants<ScrollViewer>(grid).First();
            Check(scroll.ScrollableHeight > 0 && scroll.ScrollableWidth < 2, "Purchase list scrolls vertically inside its card");
            model.GoToPage(2);
            Check(model.Rows[0].Data.Id == 21 && model.Page == 2, "Page two starts at the correct purchase order");
            var tab = (MenuItem)view.FindName("PartialTab");
            tab.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump();
            Check(model.Page == 1 && model.Rows.Count == 17 && model.ExportRows.Count == 17
                && model.Rows.All(r => r.Data.ReceiptStatus == PurchaseReceiptStatus.Partial), "Receipt tabs reset paging and export the filtered result");
            Check(((SolidColorBrush)tab.Background).Color == Color.FromRgb(6, 26, 61)
                && Descendants<TextBlock>(tab).All(t => ((SolidColorBrush)t.Foreground).Color == Colors.White)
                && ((SolidColorBrush)Descendants<LucideIcon>(tab).Single().Stroke).Color == Colors.White, "Selected tab has navy background and white label, count and icon");
            model.OrderCode = "PO-000001";
            Pump(400);
            Check(model.Rows.Count == 1 && model.Rows[0].Data.Id == 1, "Live advanced order-code search combines with receipt status");
            model.StartDate = new DateTime(2026, 12, 10);
            model.EndDate = new DateTime(2026, 12, 9);
            model.SelectTab(PurchaseManagementTab.Visible);
            Check(model.ShowSearchError && model.Rows.Count == 0 && !model.CanExport, "Invalid date ranges clear the results and disable export");

            var registerModel = (PurchaseOrderRegisterViewModel)New(typeof(PurchaseOrderRegisterViewModel), false);
            var item = new PurchaseProductOption(1, 1, "", "商品", "仕入先", 5, 3, 0);
            registerModel.SelectedMaker = new PurchaseMakerOption(1, "仕入先");
            registerModel.SelectedProduct = item with { OutstandingPurchaseQuantity = 3, PendingShelvingQuantity = 7 };
            Check(registerModel.InventoryReferenceText.Contains("倉庫到着待ち 3")
                && registerModel.InventoryReferenceText.Contains("棚入れ待ち 7"),
                "registration shows warehouse and shelving quantities separately");
            registerModel.SelectedProduct = item with { PendingShelvingQuantity = 7 };
            Check(registerModel.InventoryWarning.Contains("重複"),
                "shelving-only pending quantities still show the duplicate order warning");
            registerModel.SelectedProduct = item;
            registerModel.AddSelectedLine();
            registerModel.Lines[0].QuantityText = "0";
            Check(!registerModel.CanRegister, "Registration blocks invalid direct line editing");
            registerModel.Lines[0].QuantityText = "27";
            Check(registerModel.CanRegister && registerModel.TotalQuantity == 27, "Edited line quantity updates the registration total");
            registerModel.RemoveLineCommand.Execute(registerModel.Lines[0]);
            registerModel.UndoRemoveCommand.Execute(null);
            Check(registerModel.Lines.Count == 1 && registerModel.TotalQuantity == 27, "Undo restores the same line and quantity");
            registerModel.RemoveLineCommand.Execute(registerModel.Lines[0]);
            var register = (Window)New(typeof(F_HattyuRegister), registerModel);
            register.Owner = host;
            register.Show();
            Pump();
            Check(Descendants<DataGrid>(register).Single().IsReadOnly, "Registration entry loads with a controlled line template");
            register.Close();

            var purchase = new PurchaseOrderDetail(orders[0], Enumerable.Range(1, 44)
                .Select(i => new PurchaseOrderLineRecord(i, i, $"P-{i:0000}", "試験商品", 1, 0, 5, 1)).ToArray())
            {
                Receipts = [new(1, 1, new DateTime(2026, 11, 10), "仕入先", "担当者", 1, 0, 3)]
            };
            var detail = new F_HattyuDetail(purchase) { Owner = host };
            detail.Show();
            Pump();
            Check(Descendants<DataGrid>(detail).Count(g => g.IsReadOnly) == 2, "Detail displays read-only order lines and linked receipts");
            detail.Close();
            var history = new PurchaseReceiptHistoryWindow(purchase.Receipts) { Owner = host };
            history.Show();
            Pump();
            Check(((DataGrid)history.FindName("ReceiptsGrid")).Items.Count == 1, "History shows the supplied receipt only");
            history.Close();

            var printing = typeof(PurchaseOrderService).Assembly.GetType("SalesManagement_SysDev.PurchaseOrderPrintPreview")!;
            var document = (FixedDocument)printing.GetMethod("CreateDocument", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [purchase])!;
            Check(document.Pages.Count == 3 && document.DocumentPaginator.PageSize.Height > document.DocumentPaginator.PageSize.Width,
                "A4 portrait pagination keeps 44 product rows across three pages");
            var printerPreview = (Window)New(printing, purchase);
            printerPreview.Owner = host;
            printerPreview.Show();
            Pump();
            Check(Descendants<DocumentViewer>(printerPreview).Single().Document is FixedDocument, "Print preview hosts the selected purchase document");
            printerPreview.Close();
            var csv = typeof(PurchaseOrderService).Assembly.GetType("SalesManagement_SysDev.PurchaseOrderCsv")!;
            using var output = new StringWriter();
            csv.GetMethod("Write", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [output, new[] { orders[0] with { MakerName = "=危険,\"名称\"" } }]);
            Check(output.ToString().Contains("\"'=危険,\"\"名称\"\"\"") && output.ToString().Contains(orders[0].Code), "CSV escapes values and exports the supplied rows only");

            var inbound = new WarehouseMovementRecord(WarehouseMovementKind.Inbound, 1, 1, 1, "仕入先", "", null, null,
                "担当者", DateTime.Today, 0, 0, null, 1, 3, "");
            var readOnly = new WarehouseMovementDetailViewModel(new(inbound, []), readOnly: true);
            Check(!readOnly.CanConfirm && !readOnly.CanHide, "Linked history detail cannot mutate inventory or visibility");
        }
        finally { host.Close(); app.Shutdown(); }
    }

    private static Application CreateApplication()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var name in new[] { "Colors", "Typography", "Controls", "IconResources", "Layouts" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/SalesManagement_SysDev;component/Resources/{name}.xaml", UriKind.Relative) });
        return app;
    }

    private static void ShowSamplePrintPreview()
    {
        var app = CreateApplication();
        AppSession.Employee = new MEmployee { EmId = 1, EmFlag = 0, Po = new MPosition { PoName = "管理者" } };
        var detail = new PurchaseOrderDetail(Order(1, 0, 25, 0) with
        {
            MakerName = "印刷レイアウト確認用",
            ProductLineCount = 25
        }, Enumerable.Range(1, 25).Select(i => new PurchaseOrderLineRecord(i, i, $"P-{i:0000}", $"試験商品 {i}", 1, 0, 0, 0)).ToArray());
        var type = typeof(PurchaseOrderService).Assembly.GetType("SalesManagement_SysDev.PurchaseOrderPrintPreview")!;
        var preview = (Window)New(type, detail);
        preview.Title = "発注書 PDF 出力テスト";
        preview.Closed += (_, _) => app.Shutdown();
        app.Run(preview);
    }

    private static void CheckDatabase(int employeeId)
    {
        AppSession.Employee = new MEmployee { EmId = employeeId, EmFlag = 0, Po = new MPosition { PoName = "管理者" } };
        var directory = PurchaseOrderService.Load();
        var receipts = PurchaseOrderService.LoadReceipts();
        Check(directory.Orders.Count > 0 && receipts.Count > 0, "LocalDB purchase directory and related receipts load through the authorized service");
        var order = directory.Orders.First(x => x.ManagementFlag == 0 && receipts.Any(r => r.OrderId == x.Id));
        var detail = PurchaseOrderService.LoadDetail(order.Id)!;
        Check(detail.Order.ReceiptStatus == order.ReceiptStatus && detail.Order.ReceivedQuantity == order.ReceivedQuantity
            && detail.Receipts.All(r => r.OrderId == order.Id), "List and detail agree using actual associated receipts");
        var line = detail.Lines.First();
        Check(PurchaseOrderService.FindProductOrderIds($"P-{line.ProductId:0000}").Contains(order.Id)
            && PurchaseOrderService.FindProductOrderIds(line.ProductName[..Math.Min(2, line.ProductName.Length)]).Contains(order.Id),
            "SQL finds actual product-code and partial product-name matches");
        Check(PurchaseOrderService.LoadRegistrationCatalog().Makers.All(m => m.Id > 0), "Registration catalog shows allowed supplier records");
    }

    private static object New(Type type, params object[] arguments) =>
        Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, arguments, null)!;

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T item) yield return item;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(parent, index))) yield return child;
    }

    private static void Pump(int wait = 50)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(wait) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static PurchaseOrderRecord Order(int id, int flag, int ordered, int received) =>
        new(id, new DateTime(2026, 10, 20), 1, "メーカー", 10, "物流担当者", flag, null, 1, ordered, received) { WarehouseFlag = received == 0 ? 0 : 1 };

    private static MEmployee Employee(string role) => new()
    {
        EmId = 10,
        EmName = "物流担当者",
        EmFlag = 0,
        Po = new MPosition { PoName = role, PoFlag = 0 }
    };

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        checks++;
        Console.WriteLine("PASS " + name);
    }
}
