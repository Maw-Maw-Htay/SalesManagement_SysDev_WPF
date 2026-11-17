using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SalesManagement_SysDev;

internal static partial class Program
{
    private static int checks;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--receiving-ui"))
            {
                CheckReceivingUi(args.Contains("--render-preview"));
                Console.WriteLine($"PASS: {checks} receiving UI checks.");
                return 0;
            }
            if (args is ["--receiving-read"])
            {
                CheckReceivingRead();
                Console.WriteLine($"PASS: {checks} receiving LocalDB read checks. No records were written.");
                return 0;
            }
            if (args is ["--receiving-isolated"])
            {
                CheckReceivingWrites();
                Console.WriteLine($"PASS: {checks} disposable receiving write checks.");
                return 0;
            }
            if (args is ["--read-db", var employeeId, var officeId])
            {
                CheckReadOnlyDatabase(int.Parse(employeeId), int.Parse(officeId));
                return 0;
            }
            if (args is ["--isolated-db"])
            {
                CheckIsolatedTransaction();
                return 0;
            }
            CheckSearchAndTabs();
            CheckValidationAndTransitions();
            CheckPermissionsAndHideReason();
            CheckOutboundUi();
            Console.WriteLine($"PASS: {checks} warehouse-movement checks.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { AppSession.Employee = null; }
    }

    private static void CheckReadOnlyDatabase(int employeeId, int officeId)
    {
        AppSession.Employee = Employee("管理者");
        AppSession.Employee.EmId = employeeId;
        AppSession.Employee.SoId = officeId;
        var list = OutboundService.Load();
        Check(list.Records.Count > 0 && list.Summary.Total == list.Records.Count(x => x.ManagementFlag == OutboundService.ActiveFlag),
            "LocalDB outbound totals match the authorized records");
        var row = list.Records.First(x => x.ManagementFlag == OutboundService.ActiveFlag);
        var detail = OutboundService.LoadDetail(row.Id)!;
        Check(detail.Record.ReferenceCode == row.ReferenceCode && detail.Record.ArrivalStatus == row.ArrivalStatus
            && detail.Record.RelatedArrival?.Id == row.RelatedArrival?.Id && detail.Lines.Sum(x => x.Quantity) == row.Quantity,
            "LocalDB list and detail use the same linked order, receipt and quantities");
        if (detail.Record.RelatedArrival is { } linked)
        {
            var arrival = ArrivalService.LoadDetail(linked.Id)!;
            Check(arrival.Arrival.OrderId == row.ReferenceId && arrival.Arrival.OfficeId == row.OfficeId
                && arrival.Arrival.CustomerId == row.CustomerId, "Linked ID opens the existing authorized arrival service");
        }
        var after = OutboundService.Load();
        Check(after.Summary == list.Summary && after.Records.Select(x => x.Id).SequenceEqual(list.Records.Select(x => x.Id)),
            "Reading the list and details leaves outbound records unchanged");
    }

    private static void CheckIsolatedTransaction()
    {
        var databaseName = $"OutboundChecks_{Guid.NewGuid():N}";
        using var db = new SalesManagementContext();
        var connection = new SqlConnectionStringBuilder(db.Database.GetConnectionString()) { InitialCatalog = databaseName };
        db.Database.SetConnectionString(connection.ConnectionString);
        if (!db.Database.EnsureCreated()) throw new InvalidOperationException("Could not create a fresh test database.");
        try
        {
            var office = new MSalesOffice { SoName = "テスト営業所", SoAddress = "", SoPhone = "", SoPostal = "", SoFax = "" };
            var employee = Employee("管理者");
            employee.So = office;
            employee.EmHiredate = DateTime.Today;
            employee.EmPhone = "";
            employee.EmPassword = "";
            var customer = new MClient { ClName = "テスト顧客", So = office, ClAddress = "", ClPhone = "", ClPostal = "", ClFax = "" };
            var product = new MProduct
            {
                PrName = "テスト商品", PrModelNumber = "TEST", PrColor = "", PrReleaseDate = DateTime.Today,
                Sc = new MSmallClassification { ScName = "分類", Mc = new MMajorClassification { McName = "分類" } },
                Ma = new MMaker { MaName = "仕入先", MaAddress = "", MaPhone = "", MaPostal = "", MaFax = "" }
            };
            var salesOrder = new TOrder { Cl = customer, So = office, Em = employee, OrDate = DateTime.Today, OrStateFlag = 1 };
            var order = new TChumon { Or = salesOrder, Cl = customer, So = office, ChStateFlag = 1,
                TChumonDetails = [new TChumonDetail { Pr = product, ChQuantity = 5 }] };
            var outbound = new TSyukko { Or = salesOrder, Cl = customer, So = office, SyStateFlag = 0,
                TSyukkoDetails = [new TSyukkoDetail { Pr = product, SyQuantity = 5 }] };
            db.AddRange(order, outbound, new TStock { Pr = product, StQuantity = 12 });
            db.SaveChanges();
            AppSession.Employee = employee;
            db.ChangeTracker.Clear();
            var confirm = typeof(OutboundService).GetMethod("ConfirmInContext", BindingFlags.Static | BindingFlags.NonPublic)!;
            WarehouseMovementOperationResult Confirm() => (WarehouseMovementOperationResult)confirm.Invoke(null, [db, employee.EmId, outbound.SyId])!;
            var result = Confirm();
            Check(result.Succeeded, $"Isolated outbound confirmation succeeds: {result.Message}");
            db.ChangeTracker.Clear();
            var arrival = db.TArrivals.AsNoTracking().Include(x => x.TArrivalDetails).Single();
            Check(arrival.ArId == result.RelatedRecordId && arrival.ArStateFlag == 0 && arrival.ArDate == null && arrival.EmId == null
                && arrival.OrId == salesOrder.OrId && arrival.ClId == customer.ClId && arrival.SoId == office.SoId
                && arrival.TArrivalDetails.Single().ArQuantity == 5 && arrival.TArrivalDetails.Single().PrId == product.PrId,
                "Confirmation creates exactly one pending arrival for the intended key and product");
            var repeated = Confirm();
            Check(!repeated.Succeeded && repeated.Message.Contains("すでに確定済み")
                && db.TArrivals.Count() == 1 && db.TStocks.AsNoTracking().Single().StQuantity == 12 && !db.TShipments.Any(),
                "Repeated confirmation changes neither stock nor downstream records");
            var pending = db.TSyukkos.Single();
            pending.SyStateFlag = 0;
            db.SaveChanges();
            db.ChangeTracker.Clear();
            Check(!Confirm().Succeeded && db.TArrivals.Count() == 1 && db.TStocks.AsNoTracking().Single().StQuantity == 12,
                "Existing arrival independently blocks another confirmation even for a pending outbound");
        }
        finally { db.Database.EnsureDeleted(); }
    }

    private static void CheckOutboundUi()
    {
        AppSession.Employee = Employee("管理者");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var name in new[] { "Colors", "Typography", "Controls", "IconResources", "Layouts" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/SalesManagement_SysDev;component/Resources/{name}.xaml", UriKind.Relative) });
        var rows = Enumerable.Range(1, 41).Select(i => Record(WarehouseMovementKind.Outbound, i, i % 2, 0) with
        {
            PartnerName = i == 5 ? "北浜電機" : "南港商事",
            RelatedOrderId = i + 100, ArrivalStatus = i % 2 == 0 ? OutboundArrivalStatus.None : OutboundArrivalStatus.Pending
        }).Append(Record(WarehouseMovementKind.Outbound, 42, 1, 2)).ToArray();
        var model = (WarehouseMovementViewModel)New(typeof(WarehouseMovementViewModel), WarehouseMovementKind.Outbound, false);
        typeof(WarehouseMovementViewModel).GetField("source", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(model, rows);
        var view = (OutboundManagementView)New(typeof(OutboundManagementView), model);
        var host = new Window { Width = 1070, Height = 720, Content = view, ShowInTaskbar = false };
        host.Show();
        try
        {
            Pump();
            var grid = (DataGrid)view.FindName("MovementsGrid");
            Check(!model.IsFiltersExpanded && grid.Columns.Count(x => x.Visibility == Visibility.Visible) == 11
                && model.Rows.Count == 20 && model.PageCount == 3, "Outbound starts collapsed with eleven columns and twenty records per page");
            Check(model.VisibleCount == 41 && model.PendingCount == 20 && model.ConfirmedCount == 21 && model.ArrivalPendingCount == 21
                && model.HiddenCount == 1, "Summary separates active, confirmed, receipt-pending and hidden records");
            var scroll = Visuals<ScrollViewer>(grid).First();
            Check(scroll.ScrollableHeight > 0 && scroll.ScrollableWidth < 2, "Grid scrolls vertically and fits its normal viewport");
            model.SetPage(2);
            Check(model.Page == 2 && model.Rows.First().Data.Id == 21, "Paging moves to the correct record");
            var tab = (MenuItem)view.FindName("ConfirmedTab");
            tab.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump();
            Check(model.Page == 1 && model.Rows.All(row => row.Data.IsConfirmed)
                && ((SolidColorBrush)tab.Background).Color == Color.FromRgb(6, 26, 61)
                && Visuals<TextBlock>(tab).All(t => ((SolidColorBrush)t.Foreground).Color == Colors.White)
                && ((SolidColorBrush)Visuals<LucideIcon>(tab).Single().Stroke).Color == Colors.White,
                "Selected confirmed tab filters data and draws all text and icons in white");
            ((TextBox)view.FindName("QuickSearch")).Text = "北浜";
            Pump(380);
            Check(model.Rows.Count == 1 && model.Rows[0].Data.Id == 5, "Debounced search finds a partial customer name");
            model.RelatedCode = "CH-000105";
            model.Product = "ノート";
            model.ArrivalSelection = (int)OutboundArrivalStatus.Pending;
            Pump(380);
            Check(model.Rows.Count == 1 && model.Rows[0].Data.Id == 5, "Advanced order, product and arrival filters combine");
            model.StartDate = new(2026, 12, 6); model.EndDate = new(2026, 12, 5);
            Pump(380);
            Check(model.Rows.Count == 0 && model.SearchError.Length > 0, "Reversed dates are rejected");
            ((Button)view.FindName("HistoryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            Check(model.ShowingOutboundHistory && model.Rows.All(x => x.Data.IsConfirmed)
                && Visuals<TextBlock>(view).Any(t => t.IsVisible && t.Text == "操作履歴は未記録です。"), "Business history contains completed records and identifies missing audit history");

            var arrival = new ArrivalRecord(73, 88, 44, "取引先", 7, "大阪", null, "未記録", null, 0, 0, null, 1, 5);
            var outbound = Record(WarehouseMovementKind.Outbound, 7, 1, 0) with { RelatedOrderId = 210, RelatedArrival = arrival };
            var detail = new WarehouseMovementDetail(outbound, [new(3, "P-0003", "商品", 5, null)]);
            var detailWindow = new OutboundDetailWindow(detail, readOnly: true) { Owner = host };
            detailWindow.Show();
            Pump();
            Check(Visuals<Button>(detailWindow).Any(b => b.IsVisible && b.IsEnabled && Equals(b.Content, "関連入荷情報"))
                && !Visuals<Button>(detailWindow).Any(b => b.IsVisible && Equals(b.Content, "非表示"))
                && Visuals<DataGrid>(detailWindow).Single().IsReadOnly, "History detail is read-only and exposes verified related information");
            detailWindow.Close();
            var related = new OutboundArrivalWindow(outbound) { Owner = host };
            related.Show();
            Pump();
            Check(((DataGrid)related.FindName("ArrivalsGrid")).Items.Count == 1, "Related arrival view shows exactly the linked record");
            related.Close();
            var arrivalVm = new ArrivalDetailViewModel(new(arrival, []), readOnly: true);
            Check(!arrivalVm.CanConfirm && !arrivalVm.CanHide && new ArrivalDetailViewModel(new(arrival, [])).CanConfirm,
                "Read-only related arrival removes mutation actions while ordinary arrival detail still permits confirmation");
            var pending = detail with { Record = outbound with { BusinessState = 0 } };
            var confirm = new WarehouseMovementConfirmViewModel(pending);
            Check(confirm.CanConfirm && !new WarehouseMovementConfirmViewModel(detail).CanConfirm
                && confirm.TargetText.Contains("明細") && confirm.ImpactText.Contains("再度減算"), "Confirmation shows the recorded quantities and checks eligibility");
            confirm.IsProcessing = true;
            Check(!confirm.CanConfirm, "Confirmation cannot submit twice while processing");
            host.Width = 790; host.Height = 550;
            ((Button)view.FindName("HistoryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            Check(grid.ActualHeight > 145 && scroll.ScrollableWidth < 3, "Compact list keeps its important columns inside a smaller window");
        }
        finally { host.Close(); app.Shutdown(); }
    }

    private static object New(Type type, params object[] arguments) =>
        Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, arguments, null)!;

    private static IEnumerable<T> Visuals<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T item) yield return item;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            foreach (var child in Visuals<T>(VisualTreeHelper.GetChild(parent, index))) yield return child;
    }

    private static void Pump(int wait = 60)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(wait) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void CheckSearchAndTabs()
    {
        var inbound = Record(WarehouseMovementKind.Inbound, id: 12, state: 0, flag: 0) with
        {
            MovementDate = new DateTime(2026, 10, 31, 23, 59, 0),
            PrimaryFilterId = 7,
            EmployeeId = 18,
            ProductSearchText = "P-100 ノートPC"
        };
        var filters = new WarehouseMovementSearchCriteria("ノート", new(2026, 10, 1), new(2026, 10, 31),
            7, 18, WarehouseBusinessStatusFilter.Unconfirmed);
        Check(WarehouseMovementRules.Matches(inbound, filters, WarehouseManagementTab.Visible, false),
            "combined partial product, maker, employee, status, and inclusive end-date filters match");
        Check(WarehouseMovementRules.Matches(inbound, filters with { Keyword = "IN-000012" }, WarehouseManagementTab.Visible, false),
            "inbound formatted record number is searchable");
        Check(!WarehouseMovementRules.Matches(inbound, filters with { EndDate = new(2026, 10, 30) }, WarehouseManagementTab.Visible, false),
            "end-date filtering excludes a later day");
        Check(WarehouseMovementRules.ValidateDateRange(filters with { StartDate = new(2026, 11, 1) }) != null,
            "reversed date range is rejected");
        Check(!WarehouseMovementRules.Matches(inbound with { ManagementFlag = 2 }, filters, WarehouseManagementTab.Visible, true),
            "visible tab excludes hidden records");
        Check(WarehouseMovementRules.Matches(inbound with { ManagementFlag = 2 }, filters with { Keyword = "" }, WarehouseManagementTab.Hidden, true),
            "authorized hidden tab includes hidden records");
        Check(!WarehouseMovementRules.Matches(inbound with { ManagementFlag = 2 }, filters with { Keyword = "" }, WarehouseManagementTab.Hidden, false),
            "unauthorized hidden tab excludes hidden records");

        var outbound = Record(WarehouseMovementKind.Outbound, id: 26, state: 1, flag: 0) with
        {
            PrimaryFilterId = 4,
            OfficeId = 4,
            OfficeName = "大阪営業所",
            PartnerName = "株式会社サンプル"
        };
        Check(WarehouseMovementRules.Matches(outbound, filters with { PrimaryId = 4, EmployeeId = null,
                Status = WarehouseBusinessStatusFilter.Confirmed, Keyword = "大阪" }, WarehouseManagementTab.Visible, false),
            "outbound destination branch and confirmed status are searchable");
    }

    private static void CheckValidationAndTransitions()
    {
        Check(WarehouseMovementRules.ValidateConfirmation(0, 0) == null, "visible unconfirmed records can be confirmed");
        Check(WarehouseMovementRules.ValidateConfirmation(2, 0) != null, "hidden records cannot be confirmed");
        Check(WarehouseMovementRules.ValidateConfirmation(0, 1) != null, "duplicate confirmation is rejected");
        Check(WarehouseMovementRules.ValidateConfirmation(0, null) != null, "unknown confirmation state is rejected");
        Check(WarehouseMovementRules.ValidateHide(0, 0) != null, "unconfirmed records cannot be hidden");
        Check(WarehouseMovementRules.ValidateHide(0, 1) == null, "confirmed records may be hidden");
        Check(WarehouseMovementRules.ValidateHide(2, 1) != null, "already hidden records cannot be hidden again");
        Check(WarehouseMovementRules.ValidateLines([]) != null, "empty movement details are rejected");
        Check(WarehouseMovementRules.ValidateLines([new(10, 0)]) != null, "zero line quantity is rejected");
        Check(WarehouseMovementRules.ValidateLines([new(10, -1)]) != null, "negative line quantity is rejected");
        Check(WarehouseMovementRules.LinesMatch([new(1, 2), new(1, 3), new(2, 1)], [new(2, 1), new(1, 5)]),
            "source order lines match after grouping duplicate product rows");
        Check(!WarehouseMovementRules.LinesMatch([new(1, 5)], [new(1, 4)]), "different source quantities do not match");
        Check(WarehouseMovementRules.ValidateUniqueSourceCount(1) == null, "one matching source order is accepted");
        Check(WarehouseMovementRules.ValidateUniqueSourceCount(0) != null, "missing source order is rejected");
        Check(WarehouseMovementRules.ValidateUniqueSourceCount(2) != null, "ambiguous source orders are rejected");
        var relation = typeof(OutboundService).GetMethod("IsUniqueArrivalMatch", BindingFlags.Static | BindingFlags.NonPublic)!;
        IReadOnlyList<WarehouseMovementLineInput> outbound = [new(7, 2), new(7, 3)];
        bool IsUnique(ArrivalQuantityInput[] arrival, IReadOnlyList<WarehouseMovementLineInput>[] candidates)
            => (bool)relation.Invoke(null, [outbound, arrival, candidates])!;
        Check(IsUnique([new(7, 5)], [outbound]) && !IsUnique([new(7, 5)], [outbound, outbound])
            && !IsUnique([new(8, 5)], [outbound]) && !IsUnique([new(7, 4)], [outbound]),
            "Arrival matching requires the same product quantities and a unique confirmed source");
    }

    private static void CheckPermissionsAndHideReason()
    {
        AppSession.Employee = Employee("物流");
        Check(WarehousingService.CanView && OutboundService.CanConfirm, "logistics can view and confirm warehouse movements");
        Check(!WarehousingService.CanHide && !OutboundService.CanViewHidden, "logistics cannot hide records or view hidden records");
        Check(!WarehousingService.Hide(10, 1, "理由").Succeeded, "unauthorized hide is denied before database access");
        Check(!OutboundService.Confirm(10, 0).Succeeded, "authorized movement confirm rejects an invalid id before database access");

        AppSession.Employee = Employee("営業");
        Check(!WarehousingService.CanView && !OutboundService.CanView, "sales role is denied warehouse movement access");
        Check(!OutboundService.Confirm(10, 0).Succeeded, "unauthorized confirm is denied before database access");

        AppSession.Employee = Employee("管理者");
        Check(WarehousingService.CanView && WarehousingService.CanHide && OutboundService.CanViewHidden,
            "administrator can view, hide, and review hidden movements");
        Check(WarehousingService.ValidateHideReason(" ") != null && OutboundService.ValidateHideReason(null) != null,
            "missing hide reasons are rejected");
        Check(WarehousingService.ValidateHideReason("誤登録のため") == null
            && OutboundService.ValidateHideReason("重複確認済み") == null, "nonempty hide reasons are accepted");
        Check(!WarehousingService.Hide(10, 1, " ").Succeeded, "empty inbound hide reason is denied before database access");
        Check(!OutboundService.Hide(10, 1, "").Succeeded, "empty outbound hide reason is denied before database access");

        AppSession.Employee!.EmFlag = 2;
        Check(!WarehousingService.CanView && !OutboundService.CanConfirm, "inactive employee loses warehouse permissions");
        AppSession.Employee = null;
        Check(!WarehousingService.CanView && !OutboundService.CanHide, "missing session denies warehouse access");
    }

    private static WarehouseMovementRecord Record(WarehouseMovementKind kind, int id, int state, int flag) => new(
        kind, id, 88, 7, "メーカーA", "大阪営業所", 7, 18, "担当者", new(2026, 10, 20), state, flag, null,
        2, 5, "P-100 ノートPC キーボード");

    private static MEmployee Employee(string role) => new()
    {
        EmId = 10,
        EmName = "担当者",
        EmFlag = 0,
        SoId = 7,
        Po = new MPosition { PoName = role, PoFlag = 0 }
    };

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        checks++;
        Console.WriteLine("PASS " + name);
    }
}
