using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
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
            if (args.Contains("--verify-live-read"))
            {
                CheckLiveData();
                Console.WriteLine($"PASS: {checks} LocalDB read checks. No records were written.");
                return 0;
            }
            if (args.Contains("--verify-isolated-write"))
            {
                CheckIsolatedWrites();
                Console.WriteLine($"PASS: {checks} disposable LocalDB write checks.");
                return 0;
            }
            CheckValidation();
            CheckPermissions();
            CheckInventoryStatus();
            CheckRelatedPrice();
            CheckWindows(args.Contains("--render-preview"));
            Console.WriteLine($"PASS: {checks} product management checks.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void CheckValidation()
    {
        var valid = new ProductInput("4901234567890", "ノートPC", 1, 2, "MODEL-01", "黒", DateTime.Today, 88000, 5);
        Check(ProductService.ValidateInput(valid) == null, "valid product fields pass validation");
        Check(ProductService.ValidateInput(valid with { Name = "  " }) != null, "product name is required");
        Check(ProductService.ValidateInput(valid with { Name = new string('あ', 51) }) != null, "product name respects database length");
        Check(ProductService.ValidateInput(valid with { ModelNumber = "" }) != null, "model number is required");
        Check(ProductService.ValidateInput(valid with { ModelNumber = new string('A', 21) }) != null, "model number respects database length");
        Check(ProductService.ValidateInput(valid with { Color = new string('黒', 21) }) != null, "color respects database length");
        Check(ProductService.ValidateInput(valid with { MakerId = 0 }) != null, "manufacturer selection is required");
        Check(ProductService.ValidateInput(valid with { ClassificationId = 0 }) != null, "classification selection is required");
        Check(ProductService.ValidateInput(valid with { Price = -1 }) != null, "price cannot be negative");
        Check(ProductService.ValidateInput(valid with { Price = 10.5m }) != null, "price follows integer database precision");
        Check(ProductService.ValidateInput(valid with { Price = 1_000_000_000m }) != null, "price respects decimal database range");
        Check(ProductService.ValidateInput(valid with { SafetyStock = -1 }) != null, "safety stock cannot be negative");
        Check(ProductService.ValidateInput(valid with { ReleaseDate = default }) != null, "release date is required");
        Check(ProductService.ValidateInput(valid with { JanCode = new string('1', 14) }) != null, "JAN code respects database length");
        Check(ProductService.ValidateInput(valid with { JanCode = null }) == null, "JAN code is optional");
    }

    private static void CheckPermissions()
    {
        AppSession.Employee = Employee("物流");
        Check(ProductService.CanView && ProductService.CanEdit, "logistics can maintain products");
        Check(ProductService.CanViewHidden && !ProductService.CanViewOrders,
            "logistics can view hidden products and cannot view order details");
        AppSession.Employee = Employee("営業");
        var denied = ProductService.Create(1, new ProductInput(null, "", 0, 0, "", "", default, -1, -1));
        Check(!denied.Succeeded && denied.Message.Contains("権限"), "product write rejects unauthorized role before database access");
        Check(!ProductService.CanView && !ProductService.CanEdit && !ProductService.CanViewHidden, "sales cannot access product management");
        AppSession.Employee = Employee("管理者");
        Check(ProductService.CanEdit && ProductService.CanViewHidden, "administrator can maintain products and view hidden products");
        AppSession.Employee = null;
        Check(!ProductService.CanView && !ProductService.CanEdit, "missing session denies product access");
    }

    private static void CheckRelatedPrice()
    {
        var row = new ProductOrderRow(1, DateTime.Today, "", 3, 384_000, 1, 0);
        Check(row.RecordedUnitPrice == 128_000m && row.Amount == 384_000m, "history derives its unit amount from the recorded line");
        Check((row with { Quantity = 0 }).RecordedUnitPrice == null, "missing quantity never substitutes the master price");
        var unknown = new OrderDetailItemRow(new OrderDetailLine { Quantity = 0, Subtotal = 100, HasKnownUnitPrice = false }, 1);
        Check(unknown.DisplayUnitPrice == null && unknown.Subtotal == 100m, "shared detail preserves the amount and marks an unavailable unit price");
        Check(ProductService.HiddenFlag == 1 && ProductService.IsRestorable(1, null)
            && ProductService.IsRestorable(2, "旧WPFの記録") && !ProductService.IsRestorable(2, null) && !ProductService.IsRestorable(0, "理由"),
            "only recognized product hide states can be restored");
    }

    private static void CheckWindows(bool renderPreview)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        AppSession.Employee = Employee("管理者");
        try
        {
            var assembly = typeof(F_Main).Assembly;
            foreach (var dictionary in new[] { "Colors", "Typography", "Controls", "IconResources", "Layouts" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    { Source = new Uri($"/{assembly.GetName().Name};component/Resources/{dictionary}.xaml", UriKind.Relative) });
            var rows = Enumerable.Range(1, 47).Select(id => new ProductRowData(id, $"{4900000000000L + id}", $"試験商品 {id}", 1, "試験メーカー",
                id == 3 ? 2 : 1, id == 3 ? "パソコン / モバイル" : "周辺機器 / ケーブル", "MODEL", "黒", new DateTime(2025, 1, id % 10 + 1),
                id * 1000, 10, id == 3 ? 2 : id == 4 ? null : 30, id >= 46 ? ProductService.HiddenFlag : ProductService.ActiveFlag,
                id >= 46 ? "試験理由" : null)).ToArray();
            var catalog = new ProductCatalog(rows, [new(1, "試験メーカー")], [new(1, "周辺機器 / ケーブル"), new(2, "パソコン / モバイル")]);
            var vm = (ProductManagementViewModel)Create(typeof(ProductManagementViewModel), (Func<ProductCatalog>)(() => catalog));
            var view = (ProductManagementView)Create(typeof(ProductManagementView), vm);
            var host = new Window
            {
                Width = 870, Height = 530, ShowActivated = false, ShowInTaskbar = false,
                Left = -2000, WindowStartupLocation = WindowStartupLocation.Manual, Content = view
            };
            host.Show();
            WaitFor(() => vm.CanRegister, "authorized directory loads and enables registration");
            host.UpdateLayout();
            if (renderPreview) SavePreview(view, "product-list");
            var grid = (DataGrid)view.FindName("ProductGrid");
            var scroll = Descendants(grid).OfType<ScrollViewer>().First();
            Check(grid.IsReadOnly && scroll.ScrollableWidth < 1 && scroll.ScrollableHeight > 0 && !vm.IsFiltersExpanded,
                "compact grid fits the normal content width and scrolls while filters start collapsed");
            Check(grid.Columns.Single(x => Equals(x.Header, "カテゴリ")).ActualWidth > 100,
                "the category column uses spare room to keep names readable");
            Check(vm.AllCount == 47 && vm.VisibleCount == 45 && vm.HiddenCount == 2 && vm.Rows.Count == 20, "all counts and default page are data-driven");
            vm.PageNumber = 3;
            Check(vm.Rows.Count == 7 && !vm.CanGoForward, "last page shows its remaining records");
            vm.PageSize = 10;
            Check(vm.PageNumber == 1 && vm.Rows.Count == 10, "page size resets the first page");
            vm.IsFiltersExpanded = true;
            host.UpdateLayout();
            Check(grid.ActualHeight >= 85 && ((ScrollViewer)view.FindName("AdvancedFieldsScroll")).ScrollableHeight > 0,
                "expanded filters scroll independently and leave room for results in short windows");
            if (renderPreview) SavePreview(view, "product-filters");
            vm.IsFiltersExpanded = false;

            foreach (var name in new[] { "VisibleTab", "HiddenTab", "AllTab" })
            {
                var tab = (Button)view.FindName(name);
                tab.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                host.UpdateLayout();
                Check(((SolidColorBrush)tab.Background).Color == ((SolidColorBrush)app.FindResource("ThemePrimary")).Color
                    && ((SolidColorBrush)Descendants(tab).OfType<LucideIcon>().Single().Stroke).Color == Colors.White
                    && ((SolidColorBrush)Descendants(tab).OfType<TextBlock>().Single().Foreground).Color == Colors.White,
                    $"{name} shows navy with a white icon, label and count");
            }

            var search = (TextBox)view.FindName("QuickSearchBox");
            search.Text = "p-000003";
            WaitFor(() => vm.Rows.Count == 1 && vm.Rows[0].Id == 3, "quick search resolves padded product codes by ID");
            search.Text = "モバイル";
            vm.MinimumPrice = "2,000"; vm.MaximumPrice = "4,000"; vm.StockSelection = (int)InventoryCondition.Low;
            WaitFor(() => vm.Rows.Count == 1 && vm.Rows[0].Id == 3, "category search combines with price and inventory state");
            vm.MinimumPrice = "5000";
            WaitFor(() => vm.HasFilterError && vm.Rows.Count == 0, "reversed price range shows validation and no results");
            vm.MinimumPrice = "1000";
            vm.ReleaseFrom = new DateTime(2025, 1, 7); vm.ReleaseTo = new DateTime(2025, 1, 2);
            WaitFor(() => vm.FilterError.Contains("発売日") && vm.Rows.Count == 0, "reversed release date range shows validation");
            vm.ClearConditions();
            int selectionNotifications = 0;
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.SelectedCount)) selectionNotifications++; };
            vm.SelectAllVisible(true);
            Check(vm.Rows.Count == 10 && vm.PageNumber == 1 && vm.SelectedRows.Count == 45, "clear resets the page while select all covers filtered pages");
            Check(selectionNotifications == 1 && vm.VisibleSelectionState == true,
                "bulk selection publishes one final selection update across all pages");
            int pageResets = 0;
            vm.Rows.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) pageResets++; };
            var reload = vm.LoadAsync();
            WaitFor(() => reload.IsCompleted, "product catalog reload completes");
            reload.GetAwaiter().GetResult();
            Check(pageResets == 1 && vm.SelectedRows.Count == 45,
                "a catalog reload publishes the page once and retains eligible selections");
            Check(rows[3].Inventory.QuantityText == "未登録" && rows[2].Inventory.Condition == InventoryCondition.Low,
                "product stock display distinguishes missing balances and uses the inventory rules");

            var dialogType = assembly.GetType("SalesManagement_SysDev.ProductDetailDialog")!;
            var detail = (Window)Create(dialogType, rows[^1], true, host);
            detail.Show(); detail.UpdateLayout();
            var tabs = Descendants(detail).OfType<TabControl>().Single();
            Check(tabs.Items.Cast<TabItem>().Select(x => x.Header).SequenceEqual(new[] { "基本情報", "在庫情報", "取引履歴（受注）", "変更履歴" }),
                "product detail shows the four coordinated tabs");
            var related = (UserControl)((TabItem)tabs.Items[2]).Content;
            var relatedGrid = (DataGrid)related.FindName("OrdersGrid");
            Check(relatedGrid.IsReadOnly && relatedGrid.Columns.Count == 8 && relatedGrid.Columns.Any(x => Equals(x.Header, "顧客名")),
                "related orders reuse the read-only history grid with line-level columns");
            Check(Descendants(detail).OfType<Button>().Any(x => Equals(x.Content, "表示を復元"))
                && !Descendants(detail).OfType<Button>().Any(x => Equals(x.Content, "非表示にする")), "hidden detail offers restore only");
            tabs.SelectedIndex = 1; detail.UpdateLayout();
            Check(Descendants(detail).OfType<TextBlock>().Any(x => x.Text == "安全在庫数"), "stock tab exposes the stored threshold");
            if (renderPreview) SavePreview(detail, "product-stock");
            detail.Close();

            var editor = (Window)Create(assembly.GetType("SalesManagement_SysDev.ProductEditorDialog")!, vm, null, host);
            editor.Show(); editor.UpdateLayout();
            Check(!Descendants(editor).OfType<Button>().Single(x => Equals(x.Content, "商品を登録")).IsEnabled
                && !Descendants(editor).OfType<TextBox>().Any(x => System.Windows.Automation.AutomationProperties.GetName(x).Contains("現在庫")),
                "registration prevents incomplete save and has no current-stock input");
            if (renderPreview) SavePreview(editor, "product-registration");
            editor.Close();
            CheckInventoryScreen(host);
            host.Close();
        }
        finally { AppSession.Employee = null; app.Shutdown(); }
    }

    private static void CheckInventoryScreen(Window host)
    {
        var date = new DateTime(2025, 1, 1);
        var catalog = Enumerable.Range(1, 52).Select(id => new InventoryRowData(id, id, "",
            $"試験商品 {id}", "試験メーカー", "試験分類", "型番", "白", date,
            id == 6 ? 32 : id == 10 ? 40 : 60, 50, 0)).ToArray();
        Task<InventoryPage> LoadPage(InventoryFilters filters, int page, int pageSize, bool includeSummary, CancellationToken cancellation)
        {
            var matches = catalog.Where(row => !filters.Condition.HasValue || row.Condition == filters.Condition).ToArray();
            var counts = includeSummary ? new InventorySummary(52, 50, 2, 0, 3072) : null;
            return Task.FromResult(new InventoryPage(matches.Skip((page - 1) * pageSize).Take(pageSize).ToArray(), matches.Length, page, counts));
        }

        var vm = (InventoryManagementViewModel)Create(typeof(InventoryManagementViewModel),
            (Func<InventoryFilters, int, int, bool, CancellationToken, Task<InventoryPage>>)LoadPage);
        var view = (InventoryManagementView)Create(typeof(InventoryManagementView), vm);
        host.Width = 1040;
        host.Height = 560;
        host.Content = view;
        var grid = (DataGrid)view.FindName("InventoryGrid");
        WaitFor(() => grid.Items.Count == 20, "inventory starts with one page of active products");
        grid.ScrollIntoView(grid.Items[19]);
        host.UpdateLayout();
        var originalRows = vm.Rows;
        ((MenuItem)view.FindName("LowTab")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        WaitFor(() => grid.Items.Count == 2, "inventory shortage tab binds exactly two records");
        host.UpdateLayout();
        WaitFor(() => Descendants(grid).OfType<DataGridRow>()
            .Where(row => row.IsVisible && row.Item is InventoryRowData)
            .Select(row => ((InventoryRowData)row.Item).ProductId).SequenceEqual(new[] { 6, 10 }),
            "inventory displays each shortage row once after scrolling the previous page");
        Check(!ReferenceEquals(originalRows, vm.Rows) && originalRows.Count == 20,
            "inventory gives the grid a complete replacement page without mutating the previous page");
        var save = (Button)view.FindName("SaveButton");
        var saveMenu = save.ContextMenu;
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(saveMenu.IsOpen && ((MenuItem)saveMenu.Items[0]).IsEnabled && ((MenuItem)saveMenu.Items[2]).IsEnabled
            && !((MenuItem)saveMenu.Items[1]).IsEnabled, "inventory offers filtered and all rows, with no checked rows yet");
        saveMenu.IsOpen = false;
        vm.Rows[0].IsMarked = true;
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(((MenuItem)saveMenu.Items[1]).IsEnabled, "inventory enables saving checked rows on the current page");
        saveMenu.IsOpen = false;
        vm.QuickSearch = "新しい条件";
        Check(!save.IsEnabled, "inventory cannot save a stale page while the updated search is pending");
    }

    private static void CheckLiveData()
    {
        using var db = new SalesManagementContext();
        try
        {
            void ActAs(string role)
            {
                var employee = db.MEmployees.AsNoTracking().Where(x => x.EmFlag == 0 && x.Po.PoFlag == 0 && x.Po.PoName == role)
                    .Select(x => new { x.EmId, x.SoId }).First();
                AppSession.Employee = Employee(role);
                AppSession.Employee.EmId = employee.EmId;
                AppSession.Employee.SoId = employee.SoId;
            }
            ActAs("管理者");
            var catalog = ProductService.Load();
            Check(catalog.Products.Count == db.MProducts.Count() && catalog.Products.Any(x => x.ManagementFlag == ProductService.HiddenFlag),
                "live catalog uses the verified product status flags and real records");
            CheckSharedStock(db);
            var line = db.TOrderDetails.AsNoTracking().Where(x => x.OrQuantity > 0)
                .OrderBy(x => x.OrDetailId).Select(x => new { x.PrId, x.OrId }).First();
            var detail = ProductService.ReadDetail(line.PrId);
            var balances = db.TStocks.AsNoTracking().Where(x => x.PrId == line.PrId && x.StFlag == 0).Select(x => (long)x.StQuantity).ToList();
            Check(detail?.Id == line.PrId && detail.StockQuantity == (balances.Count > 0 ? balances.Sum() : (long?)null),
                "live product detail matches its existing active stock balances");
            var query = db.TOrderDetails.AsNoTracking().Where(x => x.PrId == line.PrId && (x.OrQuantity != 0 || x.OrTotalPrice != 0m));
            var expectedLines = query.OrderByDescending(x => x.Or.OrDate).ThenByDescending(x => x.OrId).ThenByDescending(x => x.OrDetailId)
                .Take(20).Select(x => new { x.OrId, x.OrQuantity, x.OrTotalPrice }).ToList();
            var history = ProductOrderHistory.ReadPage(line.PrId, 1);
            Check(history.Total == query.Count() && history.Rows.Select(x => (x.OrderId, x.Quantity, x.Amount))
                .SequenceEqual(expectedLines.Select(x => (x.OrId, x.OrQuantity, x.OrTotalPrice))), "live related history uses only recorded lines for this product");
            Check(ProductOrderHistory.ReadDetail(line.PrId, line.OrId)?.Lines.Any(x => x.ProductId == line.PrId) == true
                && ProductOrderHistory.ReadDetail(int.MaxValue, line.OrId) == null, "live detail navigation rechecks the product-order relationship");
            ActAs("営業");
            Check(!ProductService.CanViewOrders, "sales cannot access order history through product management");
        }
        finally { AppSession.Employee = null; }
    }

    private static void CheckSharedStock(SalesManagementContext db)
    {
        var active = db.MProducts.AsNoTracking().Where(x => x.PrFlag == 0).Select(x => x.PrId).ToArray();
        var stockRows = db.TStocks.AsNoTracking().ToLookup(x => x.PrId);
        var readStock = typeof(InventoryService).GetMethod("ReadCurrentStock", BindingFlags.NonPublic | BindingFlags.Static)!;
        var editorStock = (IReadOnlyDictionary<int, InventoryStockLookup>)readStock.Invoke(null, [db, active])!;
        var filters = new InventoryFilters("", "", "", "", "", null, null, null, InventorySort.Code, false);
        var inventory = InventoryService.LoadPageAsync(filters, 1, active.Length + 1, true, CancellationToken.None).GetAwaiter().GetResult();
        var differences = inventory.Rows.Where(row => editorStock[row.ProductId].State != row.StockState
            || editorStock[row.ProductId].Quantity != row.Quantity).ToArray();
        Check(inventory.Rows.Count == active.Length && differences.Length == 0,
            $"live inventory and order stock reads agree for every active product (rows {inventory.Rows.Count}/{active.Length}, "
            + $"differences {differences.Length}: {string.Join(", ", differences.Take(3).Select(row => $"{row.ProductId} {row.StockState}/{editorStock[row.ProductId].State}"))})");
        Check(inventory.Rows.Where(row => !stockRows.Contains(row.ProductId)).All(row =>
                row.StockState == InventoryStockState.MissingRecord && row.Quantity is null && row.QuantityText == "未登録")
            && inventory.Rows.Where(row => stockRows.Contains(row.ProductId) && stockRows[row.ProductId].All(stock => stock.StFlag != 0)).All(row =>
                row.StockState == InventoryStockState.InactiveRecord && row.Quantity is null && row.QuantityText == "—"),
            "live rows distinguish absent stock from inactive balances");
        Check(inventory.Rows.Where(row => row.StockState == InventoryStockState.Available).All(row =>
            stockRows[row.ProductId].Where(stock => stock.StFlag == 0).Single().StQuantity == row.Quantity),
            "live available quantities match the stored active stock rows");

        var fixture = db.TStocks.FromSqlRaw("""
            SELECT s.StID, s.PrID, s.StQuantity, s.StFlag
            FROM (VALUES (1,1,0,0), (2,2,12,0), (3,2,6,0), (4,3,-1,0), (5,4,50,2), (6,5,7,0), (7,5,50,2))
                AS s(StID, PrID, StQuantity, StFlag)
            """).AsNoTracking();
        var query = typeof(InventoryService).GetMethod("QueryStockBalances", BindingFlags.NonPublic | BindingFlags.Static)!;
        var rows = ((System.Collections.IEnumerable)query.Invoke(null, [fixture])!).Cast<object>().ToDictionary(
            row => (int)row.GetType().GetProperty("ProductId")!.GetValue(row)!,
            row => new InventoryStockLookup((InventoryStockState)row.GetType().GetProperty("State")!.GetValue(row)!,
                (int?)row.GetType().GetProperty("Quantity")!.GetValue(row)));
        Check(rows[1] == new InventoryStockLookup(InventoryStockState.Available, 0)
            && rows[2] == new InventoryStockLookup(InventoryStockState.DuplicateActiveRecords)
            && rows[3] == new InventoryStockLookup(InventoryStockState.InvalidQuantity)
            && rows[4] == new InventoryStockLookup(InventoryStockState.InactiveRecord)
            && rows[5] == new InventoryStockLookup(InventoryStockState.Available, 7),
            "SQL classifies zero, duplicate, negative and inactive balances without writing fixture rows");
    }

    private static object Create(Type type, params object?[] arguments) =>
        Activator.CreateInstance(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, arguments, null)!;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void WaitFor(Func<bool> condition, string name)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }
        Check(condition(), name);
    }

    private static void SavePreview(FrameworkElement element, string name)
    {
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),
            (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, name + ".png"));
        encoder.Save(stream);
    }

    private static void CheckInventoryStatus()
    {
        var baseRow = new InventoryRowData(1, 1, "490000000001", "商品", "メーカー", "分類", "型番", "黒", DateTime.Today, 10, 10, 0);
        Check(baseRow.Condition == InventoryCondition.Normal, "stock at the safety threshold is normal");
        Check((baseRow with { Quantity = 9 }).Condition == InventoryCondition.Low, "positive stock below the safety threshold is low");
        Check((baseRow with { SafetyStock = null }).Condition == InventoryCondition.Unavailable, "an unknown safety threshold does not classify positive stock");
        Check((baseRow with { Quantity = null }).Condition == InventoryCondition.Unavailable, "missing stock is not counted as zero");
        Check((baseRow with { Quantity = 11 }).Condition == InventoryCondition.Normal, "stock above reorder point is normal");
        Check((baseRow with { Quantity = 0, SafetyStock = 0 }).Condition == InventoryCondition.Empty, "zero stock is empty even when threshold is zero");
        Check((baseRow with { Quantity = 1, SafetyStock = 0 }).Condition == InventoryCondition.Normal, "zero reorder threshold does not mark positive stock low");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        checks++;
        Console.WriteLine("PASS " + name);
    }

    private static MEmployee Employee(string role) => new()
    {
        EmId = 1, EmFlag = 0, SoId = 4,
        Po = new MPosition { PoName = role },
        So = new MSalesOffice { SoId = 4, SoName = "北大阪" }
    };
}
