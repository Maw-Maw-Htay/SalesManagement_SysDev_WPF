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
            Order(2, 0, ordered: 4, received: 4),
            Order(3, 2, ordered: 8, received: 0),
            Order(4, 0, ordered: 5, received: 7)
        };
        var summary = PurchaseProgress.Summarize(orders);
        Check(summary == new PurchaseOrderSummary(3, 1, 1), "summary uses visible orders and excludes hidden orders from progress counts");
        Check(!orders[3].IsFullyReceived, "over-receipt is not counted as a normal completed purchase");
        Check(orders[0].RemainingQuantity == 7, "remaining quantity is the nonnegative difference");
        Check(Order(4, 0, ordered: 5, received: 7).RemainingQuantity == 0, "excess received quantity never creates negative remainder");
        Check(PurchaseOrderService.FormatCode(8) == "PO-000008", "database identity is formatted as a purchase code");
        Check(PurchaseProgress.Classify([(5, 7), (5, 3)], 2) == PurchaseReceiptStatus.Excess,
            "An over-receipt cannot offset a shortage on another product");
        Check(PurchaseProgress.Classify([(5, 5), (5, 1)], 2) == PurchaseReceiptStatus.Partial,
            "Each product must be fully received to complete the purchase");

        var released = Order(5, 0, ordered: 8, received: 0);
        var draft = released with { ConfirmationState = PurchaseOrderService.RegisteredState };
        var legacy = released with { ConfirmationState = 0 };
        var linkedReceipt = legacy with { HasRelatedReceipt = true, HasPendingReceipt = true };
        var hiddenReceipt = legacy with { HasRelatedReceipt = true };
        var waiting = new PurchaseOrderSearchCriteria("", null, null, null, null, PurchaseProgressFilter.Waiting);
        Check(draft.ProgressLabel == "発注未確定" && legacy.ProgressLabel == "発注未確定"
            && !PurchaseOrderFilter.Matches(legacy, waiting, PurchaseManagementTab.Visible, true),
            "new drafts and unlinked legacy orders are not displayed or filtered as waiting for receiving");
        Check(released.ProgressLabel == "入庫待ち" && linkedReceipt.ProgressLabel == "入庫待ち"
            && hiddenReceipt.ProgressLabel == "入庫要確認" && !hiddenReceipt.IsAwaitingConfirmation,
            "only released orders and visible pending receipts report waiting for receiving");
        Check(new PurchaseOrderDetailViewModel(new(legacy, []), canHide: false, canConfirm: true).CanConfirm
            && !new PurchaseOrderDetailViewModel(new(hiddenReceipt, []), canHide: false, canConfirm: true).CanConfirm,
            "an authorized user can explicitly confirm an unlinked legacy order only");
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
        Check(!PurchaseOrderService.CanHide && !PurchaseOrderService.CanViewHidden, "logistics cannot view hidden records or hide orders");
        Check(!PurchaseOrderService.Hide(10, 1, "理由").Succeeded, "unauthorized hide is denied before database access");

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

            CheckPurchaseRegistrationScreen(host);

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

    private static void CheckPurchaseRegistrationScreen(Window host)
    {
        var first = new PurchaseMakerOption(1, "同名の試験仕入先");
        var other = new PurchaseMakerOption(2, "同名の試験仕入先");
        var empty = new PurchaseMakerOption(3, "商品なし");
        var zeroStock = new PurchaseProductOption(10, first.Id, "", "在庫なし商品", first.Name, 0, 3, 0);
        var secondProduct = new PurchaseProductOption(11, first.Id, "", "追加商品", first.Name, 5, 3, 0);
        var unknownStock = new PurchaseProductOption(12, other.Id, "", "残高未登録商品", other.Name, null, 3, 0);
        var catalog = new PurchaseCatalog([first, other, empty], [zeroStock, secondProduct, unknownStock]);

        var model = (PurchaseOrderRegisterViewModel)New(typeof(PurchaseOrderRegisterViewModel), (Func<PurchaseCatalog>)(() => catalog));
        var register = (Window)New(typeof(F_HattyuRegister), model);
        register.Owner = host;
        register.Show();
        try
        {
            for (var attempt = 0; model.IsLoading && attempt < 40; attempt++) Pump();
            var maker = (ComboBox)register.FindName("MakerPicker");
            var product = (ComboBox)register.FindName("ProductPicker");
            var save = (Button)register.FindName("RegisterButton");
            bool HintVisible(string text) => Descendants<TextBlock>(register).Any(block => block.Text == text && block.IsVisible);
            void SelectMaker(PurchaseMakerOption option) => maker.SetCurrentValue(ComboBox.SelectedItemProperty, option);
            void SelectProduct(PurchaseProductOption option) => product.SetCurrentValue(ComboBox.SelectedItemProperty, option);

            Check(!model.IsLoading && maker.IsEnabled && !product.IsEnabled && !save.IsEnabled
                && model.SelectedProduct == null && model.Lines.Count == 0,
                "new registration starts with supplier enabled and product and save disabled");
            SelectMaker(first);
            Check(product.IsEnabled && product.IsEditable && product.IsTextSearchEnabled
                && product.Items.Cast<PurchaseProductOption>().SequenceEqual([zeroStock, secondProduct]),
                "supplier A shows only its products and keeps name search");
            product.Text = "前の検索入力";
            SelectMaker(other);
            Check(product.Items.Cast<PurchaseProductOption>().SequenceEqual([unknownStock]) && product.Text.Length == 0,
                "supplier B filters by ID despite the same display name and clears prior product input");
            model.SelectedProduct = zeroStock;
            Check(model.SelectedProduct == null && !model.CanAddLine, "a stale product selection from another supplier is rejected");
            SelectProduct(unknownStock);
            Check(model.CanAddLine && model.InventoryReferenceText.Contains("現在庫 未登録") && !model.IsBelowReorderPoint,
                "missing stock is displayed as unknown and remains eligible");
            SelectMaker(empty);
            Check(!product.IsEnabled && !model.CanAddLine && HintVisible("選択した仕入先に登録されている商品がありません。"),
                "a supplier with no eligible products shows the empty state");
            SelectMaker(first);
            SelectMaker(other);
            SelectMaker(first);
            Check(product.Items.Cast<PurchaseProductOption>().SequenceEqual([zeroStock, secondProduct]),
                "rapid supplier switching ends with the current supplier's catalog");

            SelectProduct(zeroStock);
            Check(model.CanAddLine && model.InventoryReferenceText.Contains("現在庫 0"), "a product with zero stock is selectable");
            model.QuantityText = "9";
            model.AddLineCommand.Execute(null);
            Check(model.IsMakerLocked && !maker.IsEnabled && ReferenceEquals(maker.SelectedItem, first) && product.IsEnabled
                && model.Lines.Single().Quantity == 9 && HintVisible("商品が追加されているため、仕入先は変更できません。"),
                "first line locks the visible supplier, preserving its quantity and product access");
            model.SelectedMaker = other;
            model.SelectedMaker = null;
            SelectMaker(other);
            Pump();
            Check(model.SelectedMaker == first && ReferenceEquals(maker.SelectedItem, first),
                "programmatic changes cannot replace the locked supplier");
            SelectProduct(zeroStock);
            Check(!model.CanAddLine, "adding the same product twice remains blocked");
            SelectProduct(secondProduct);
            model.QuantityText = "2";
            model.AddLineCommand.Execute(null);
            Check(model.Lines.Count == 2 && !maker.IsEnabled, "adding another eligible product keeps the supplier locked");

            var firstLine = model.Lines[0];
            firstLine.QuantityText = "0";
            Check(!save.IsEnabled, "registration blocks invalid direct line editing");
            firstLine.QuantityText = "27";
            Check(save.IsEnabled && model.TotalQuantity == 29, "edited line quantity updates the total");
            model.RemoveLineCommand.Execute(firstLine);
            Check(!maker.IsEnabled && model.Lines.Count == 1, "removing one of two lines keeps the supplier locked");
            var remaining = model.Lines.Single();
            model.RemoveLineCommand.Execute(remaining);
            Check(maker.IsEnabled && ReferenceEquals(maker.SelectedItem, first) && !save.IsEnabled
                && !HintVisible("商品が追加されているため、仕入先は変更できません。"),
                "removing the last line unlocks the supplier and keeps its selection");
            model.UndoRemoveCommand.Execute(null);
            Check(!maker.IsEnabled && model.Lines.Single() == remaining && model.TotalQuantity == 2,
                "Undo restores the same line and quantity and relocks the supplier");
            model.RemoveLineCommand.Execute(remaining);
            SelectMaker(other);
            Check(!model.CanUndoRemove && product.Items.Cast<PurchaseProductOption>().SequenceEqual([unknownStock]),
                "switching an empty draft cannot undo a product from the old supplier");

            SelectProduct(unknownStock);
            model.AddLineCommand.Execute(null);
            var clear = Descendants<Button>(register).Single(button => Equals(button.Content, "すべて削除"));
            void AnswerClear(bool confirmed)
            {
                F_OrderNotice? notice = null;
                var response = register.Dispatcher.InvokeAsync(() =>
                {
                    notice = register.OwnedWindows.OfType<F_OrderNotice>().SingleOrDefault(window => window.IsVisible);
                    if (notice != null) notice.DialogResult = confirmed;
                }, DispatcherPriority.Background);
                clear.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                response.Abort();
                Check(notice != null, "clearing all lines opens a confirmation dialog");
            }
            AnswerClear(false);
            Check(model.Lines.Count == 1 && !maker.IsEnabled, "canceling clear-all keeps the draft and supplier lock");
            AnswerClear(true);
            Check(model.Lines.Count == 0 && maker.IsEnabled && ReferenceEquals(maker.SelectedItem, other),
                "confirming clear-all unlocks the current supplier");
            Check(Descendants<DataGrid>(register).Single().IsReadOnly, "registration keeps its controlled line template");
        }
        finally
        {
            typeof(PurchaseOrderRegisterViewModel).GetMethod("ClearLines", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(model, null);
            register.Close();
        }
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
        new(id, new DateTime(2026, 10, 20), 1, "メーカー", 10, "物流担当者", flag, null, 1, ordered, received)
        { ConfirmationState = PurchaseOrderService.ConfirmedState };

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
