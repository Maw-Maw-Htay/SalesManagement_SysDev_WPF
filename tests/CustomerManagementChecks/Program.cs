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
            if (args.Contains("--verify-isolated-write"))
            {
                CheckIsolatedWrites();
                Console.WriteLine($"PASS: {checks} disposable LocalDB write checks.");
                return 0;
            }
            if (args.Contains("--verify-live-read"))
            {
                CheckLiveData();
                Console.WriteLine($"PASS: {checks} LocalDB read checks. No records were written.");
                return 0;
            }
            CheckFiltering();
            CheckEditorValidation();
            CheckRoleScopes();
            CheckVisibilityAndRelationships();
            CheckWindows(args.Contains("--render-preview"));
            Console.WriteLine($"PASS: {checks} customer management checks.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        checks++;
        Console.WriteLine("PASS " + name);
    }

    private static void CheckFiltering()
    {
        var visible = new CustomerRecord(123, 4, "北大阪", "株式会社山田電機", "06-1234-5678", "5300001", "大阪市北区", "06-1234-0000", 0, null);
        var hidden = visible with { Id = 124, ManagementFlag = CustomerService.HiddenFlag, HiddenReason = "重複" };
        var code = new CustomerSearchCriteria("C-012", "", "", "", null);
        Check(CustomerFilter.Matches(visible, code, CustomerStatusTab.Visible, true), "partial customer code matches");
        Check(!CustomerFilter.Matches(visible, new("", "山田", "9999", "北区", 4), CustomerStatusTab.Visible, true), "AND search rejects mismatched phone filter");
        Check(CustomerFilter.Matches(visible, new("", "山田", "5678", "北区", 4), CustomerStatusTab.Visible, true), "AND search matches name, phone, address and office");
        Check(!CustomerFilter.Matches(hidden, new("", "", "", "", null), CustomerStatusTab.Visible, true), "visible tab excludes hidden customers");
        Check(CustomerFilter.Matches(hidden, new("", "", "", "", null), CustomerStatusTab.Hidden, true), "authorized hidden tab includes hidden customers");
        Check(!CustomerFilter.Matches(hidden, new("", "", "", "", null), CustomerStatusTab.Hidden, false), "unauthorized hidden tab excludes hidden customers");
        Check(!CustomerFilter.Matches(visible, new("", "", "", "", 5), CustomerStatusTab.All, true), "office scope filter restricts results");
        Check(!CustomerFilter.Matches(hidden, new("", "", "", "", null), CustomerStatusTab.All, false), "all tab does not expose hidden customers without permission");
        var id = visible with { Id = 3 };
        var quick = new CustomerSearchCriteria("", "", "", "", null, "３");
        Check(CustomerFilter.Matches(id, quick, CustomerStatusTab.All, true) && !CustomerFilter.Matches(visible, quick, CustomerStatusTab.All, true),
            "short numeric quick search matches only that customer ID");
        Check(CustomerFilter.Matches(id, quick with { Keyword = "c-0003" }, CustomerStatusTab.All, true),
            "padded formatted IDs match case-insensitively");
        Check(CustomerFilter.Matches(id, quick with { Keyword = "1234-56", OfficeId = 4 }, CustomerStatusTab.All, true),
            "formatted phone search combines with office");
    }

    private static void CheckEditorValidation()
    {
        var offices = new[] { new CustomerOffice(4, "北大阪"), new CustomerOffice(5, "南大阪") };
        var create = new CustomerEditorViewModel(offices);
        Check(!create.IsValid && !create.CanSave, "registration requires name and phone");
        create.Name = "新規顧客"; create.Phone = "06-5555-1234";
        Check(create.IsValid && create.CanSave, "valid registration enables save");
        Check(create.PreviewName == "新規顧客" && create.PreviewOffice == "北大阪", "registration preview follows form values");
        create.Address = new string('あ', 51);
        Check(!create.IsValid && create.AddressError.Length > 0 && !create.CanSave, "address length follows existing database limit");
        create.Address = ""; create.Phone = "06-ABC";
        Check(!create.IsValid && create.PhoneError.Length > 0, "invalid phone formats fail field validation");
        create.Phone = "０６－５５５５－１２３４"; create.PostalCode = "530-0001";
        Check(create.IsValid && CustomerInput.NormalizePostalCode(create.PostalCode) == "5300001", "Japanese phone and postal formatting fit the stored columns");

        var existing = new CustomerRecord(12, 4, "北大阪", "既存顧客", "06-1111-2222", "5300001", "大阪市", "06-1111-0000", 0, null);
        var edit = new CustomerEditorViewModel(offices, existing);
        Check(!edit.HasChanges && !edit.CanSave, "unchanged customer cannot be saved");
        edit.Name = "更新顧客";
        Check(edit.HasChanges && edit.CanSave && edit.CustomerCode == "C-0012", "customer edit tracks changed fields and preserves code");
    }

    private static void CheckRoleScopes()
    {
        AppSession.Employee = Employee("営業", 4);
        Check(CustomerService.CanEdit && CustomerService.CanViewHidden, "sales can maintain own-office customers and view hidden tab");
        var salesEditor = new CustomerEditorViewModel([new CustomerOffice(4, "北大阪")]);
        Check(!salesEditor.CanChangeOffice && salesEditor.OfficeId == 4, "sales office is fixed to the logged-in office");
        AppSession.Employee = Employee("物流", 4);
        Check(!CustomerService.CanView && !CustomerService.CanEdit && !CustomerService.CanViewHidden, "logistics cannot access customer management");
        Check(!CustomerService.CanViewOrders && !CustomerService.Restore(1,
            new(12, 4, "", "", "", "", "", "", CustomerService.HiddenFlag, "理由")).Succeeded,
            "logistics cannot view orders or restore customers");
        AppSession.Employee = Employee("管理者", 4);
        Check(CustomerService.CanEdit && CustomerService.CanViewHidden, "administrator can maintain and view hidden customers");
        Check(new CustomerEditorViewModel([new CustomerOffice(4, "北大阪")]).CanChangeOffice, "administrator may assign an active office");
        Check(!CustomerService.Hide(1, 12, null).Succeeded, "hide rejects a missing reason before database access");
        Check(!CustomerService.Hide(1, 12, " \t\r\n ").Succeeded, "hide rejects a whitespace-only reason before database access");
        AppSession.Employee = null;
        Check(!CustomerService.CanEdit && !CustomerService.CanViewHidden, "missing session denies customer mutations and hidden view");
    }

    private static void CheckVisibilityAndRelationships()
    {
        Check(CustomerService.HiddenFlag == 1 && CustomerService.IsRestorable(1, "理由")
            && CustomerService.IsRestorable(2, "WPFで記録済み") && !CustomerService.IsRestorable(2, null)
            && !CustomerService.IsRestorable(0, "理由"),
            "restore handles recorded customer flags and leaves ambiguous legacy states hidden");
        var duplicate = typeof(CustomerInput).GetMethod("IsPossibleDuplicate", BindingFlags.Static | BindingFlags.NonPublic)!;
        bool IsDuplicate(string name, string phone, string address) => Equals(true, duplicate.Invoke(null,
            ["テスト本社", "06-1111-2222", "テスト市北区一丁目", name, phone, address]));
        Check(IsDuplicate("テスト 本社", "06-5555-6666", "別住所") && IsDuplicate("別名", "0611112222", "別住所")
            && IsDuplicate("別名", "06-5555-6666", "テスト市北区一丁目")
            && !IsDuplicate("テスト本社別法人", "06-5555-6666", "別住所"),
            "duplicate prompt recognizes matching fields without rejecting similar names");
        var sameName = new MClient { ClId = 3, SoId = 4, ClName = "同名" };
        var orders = new[]
        {
            new TOrder { ClId = 3, Cl = sameName, SoId = 4, OrId = 1 },
            new TOrder { ClId = 3, Cl = sameName, SoId = 5, OrId = 2 },
            new TOrder { ClId = 23, Cl = new MClient { ClId = 23, SoId = 4, ClName = "同名" }, SoId = 4, OrId = 3 }
        };
        var scope = typeof(CustomerOrderHistory).GetMethod("ForCustomer", BindingFlags.Static | BindingFlags.NonPublic)!;
        var scoped = (IQueryable<TOrder>)scope.Invoke(null, [orders.AsQueryable(), 3, 4])!;
        Check(scoped.Select(x => x.OrId).SequenceEqual(new[] { 1 }), "order scope uses customer foreign key and order office even for identical names");
    }

    private static void CheckWindows(bool renderPreview)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AppSession.Employee = Employee("管理者", 4);
        try
        {
            var assembly = typeof(F_Main).Assembly;
            foreach (var dictionary in new[] { "Colors", "Typography", "Controls", "IconResources", "Layouts" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri($"/{assembly.GetName().Name};component/Resources/{dictionary}.xaml", UriKind.Relative) });
            var rows = Enumerable.Range(1, 47).Select(id => new CustomerRecord(id, id % 2 + 4, "試験営業所",
                id == 3 ? "テスト Acme" : $"試験会社 {id}", "06-1111-2222", "5300001", "試験住所", "",
                id >= 46 ? CustomerService.HiddenFlag : CustomerService.ActiveFlag, id >= 46 ? "試験理由" : null)).ToArray();
            (IReadOnlyList<CustomerRecord>, IReadOnlyList<CustomerOffice>) LoadDirectory() => (rows, [new(4, "試験営業所"), new(5, "第二試験営業所")]);
            var vm = (CustomerManagementViewModel)Create(typeof(CustomerManagementViewModel),
                (Func<(IReadOnlyList<CustomerRecord>, IReadOnlyList<CustomerOffice>)>)LoadDirectory);
            var view = (CustomerManagementView)Create(typeof(CustomerManagementView), vm);
            var host = new Window { Width = 850, Height = 520, ShowActivated = false, ShowInTaskbar = false,
                Left = -2000, WindowStartupLocation = WindowStartupLocation.Manual, Content = view };
            host.Show();
            WaitFor(() => vm.CanRegister, "customer directory loads and allows authorized creation");
            host.UpdateLayout();
            if (renderPreview) SavePreview(view, "customer-list");
            var grid = (DataGrid)view.FindName("CustomerGrid");
            var scroll = Descendants(grid).OfType<ScrollViewer>().First();
            Check(scroll.ScrollableHeight > 0 && scroll.ScrollableWidth < 1 && grid.IsReadOnly && !vm.IsFiltersExpanded,
                "list scrolls vertically, fits the normal content width and starts with filters collapsed");
            Check(vm.AllCount == 47 && vm.VisibleCount == 45 && vm.HiddenCount == 2 && vm.Rows.Count == 20, "counts and default paging reflect the authorized directory");
            vm.PageNumber = 3;
            Check(vm.Rows.Count == 7 && !vm.CanGoForward, "last page contains only the remaining customers");
            var visibleIcon = Descendants((Button)view.FindName("VisibleTab")).OfType<LucideIcon>().Single();
            Check(((SolidColorBrush)visibleIcon.Stroke).Color == ((SolidColorBrush)app.FindResource("ThemeSuccessText")).Color,
                "unselected visible icon is green");
            foreach (var name in new[] { "VisibleTab", "HiddenTab", "AllTab" })
            {
                var button = (Button)view.FindName(name);
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                host.UpdateLayout();
                Check(((SolidColorBrush)button.Background).Color == ((SolidColorBrush)app.FindResource("ThemePrimary")).Color
                    && ((SolidColorBrush)Descendants(button).OfType<LucideIcon>().Single().Stroke).Color == Colors.White
                    && ((SolidColorBrush)Descendants(button).OfType<TextBlock>().Single().Foreground).Color == Colors.White,
                    $"{name} renders a navy background with white label, count and icon");
            }
            var search = (TextBox)view.FindName("QuickSearchBox");
            search.Text = "acme"; vm.OfficeFilterId = 4;
            WaitFor(() => vm.Rows.Count == 0, "primary and advanced filters both apply");
            vm.OfficeFilterId = 5;
            WaitFor(() => vm.Rows.Count == 1 && vm.Rows[0].Id == 3, "correcting the office finds the partial company name");
            vm.ClearConditions(); vm.SelectAllVisible(true);
            Check(vm.PageNumber == 1 && search.Text == "" && vm.SelectedRows.Count == 45, "clear resets the page and select all retains its all-page semantics");

            var detail = (Window)Create(assembly.GetType("SalesManagement_SysDev.CustomerDetailDialog")!, rows[^1], true, host);
            detail.Show();
            detail.UpdateLayout();
            if (renderPreview) SavePreview(detail, "customer-detail");
            var tabs = Descendants(detail).OfType<TabControl>().Single();
            Check(Descendants(detail).OfType<TextBlock>().Where(x => x.Text is "会社情報" or "取引・担当情報")
                .Count(x => x.Foreground is SolidColorBrush brush && brush.Color != Colors.White) == 2,
                "both detail headings stay readable on the white cards");
            Check(tabs.Items.Cast<TabItem>().Select(x => x.Header).SequenceEqual(new[] { "基本情報", "取引履歴（受注）", "変更履歴" })
                && ((TabItem)tabs.Items[1]).Content is CustomerOrderHistoryView, "detail tabs include the paged related-orders view");
            Check(Descendants(detail).OfType<Button>().Any(x => Equals(x.Content, "表示を復元"))
                && !Descendants(detail).OfType<Button>().Any(x => Equals(x.Content, "非表示にする")), "hidden detail only offers restore");
            detail.Close();

            var sampleOrder = new OrderDetailSnapshot { OrderId = 1, OfficeId = 4, CustomerId = 3 };
            var readOnlyDetail = (Window)Create(typeof(F_OrderDetail), sampleOrder, host, true);
            var orderVm = (OrderDetailViewModel)readOnlyDetail.DataContext;
            Check(!orderVm.EditCommand.CanExecute(null) && !orderVm.ConfirmCommand.CanExecute(null) && !orderVm.HideCommand.CanExecute(null)
                && ((Button)readOnlyDetail.FindName("ConfirmOrderButton")).Visibility == Visibility.Collapsed,
                "related order detail disables the workflow as well as hiding the buttons");
            readOnlyDetail.Close();
            var orderWorkspace = new OrderDetailViewModel(sampleOrder, () => { }, () => { }, () => { }, () => { }, () => { });
            Check(orderWorkspace.CanEdit && orderWorkspace.CanConfirm && orderWorkspace.CanHide, "standard order detail keeps its existing actions");
            host.Close();
        }
        finally { AppSession.Employee = null; app.Shutdown(); }
    }

    private static void CheckLiveData()
    {
        using var db = new SalesManagementContext();
        try
        {
            var admin = db.MEmployees.AsNoTracking().Where(x => x.EmFlag == 0 && x.Po.PoFlag == 0 && x.Po.PoName == "管理者")
                .Select(x => new { x.EmId, x.SoId }).First();
            AppSession.Employee = Employee("管理者", admin.SoId);
            AppSession.Employee.EmId = admin.EmId;
            var loaded = CustomerService.Load();
            Check(loaded.Customers.Count == db.MClients.Count() && loaded.Customers.Count > 0, "live customer list loads every authorized customer");
            Check(loaded.Customers.Count(x => x.ManagementFlag == CustomerService.HiddenFlag && x.HiddenReason != null) > 0,
                "live customer records use the verified hidden flag");
            var first = db.TOrders.AsNoTracking().OrderBy(x => x.OrId).Select(x => new { x.ClId, x.OrId }).First();
            var different = db.TOrders.AsNoTracking().Where(x => x.ClId != first.ClId).Select(x => x.OrId).First();
            var matching = db.TOrders.AsNoTracking().Where(x => x.ClId == first.ClId);
            var expectedIds = matching.OrderByDescending(x => x.OrDate).ThenByDescending(x => x.OrId).Take(20).Select(x => x.OrId).ToArray();
            var page = CustomerOrderHistory.ReadPage(first.ClId, 1);
            Check(page.Total == matching.Count() && page.Rows.Select(x => x.Id).SequenceEqual(expectedIds),
                "live order history counts and sorts only the selected customer's orders");
            Check(CustomerOrderHistory.ReadDetail(first.ClId, first.OrId)?.CustomerId == first.ClId
                && CustomerOrderHistory.ReadDetail(first.ClId, different) == null, "live related order detail checks both IDs");
            Check(CustomerService.ReadDetail(first.ClId)?.Id == first.ClId, "live detail reads the matching customer");

            var sales = db.MEmployees.AsNoTracking().Where(x => x.EmFlag == 0 && x.Po.PoFlag == 0 && x.Po.PoName == "営業")
                .Select(x => new { x.EmId, x.SoId }).First();
            AppSession.Employee = Employee("営業", sales.SoId);
            AppSession.Employee.EmId = sales.EmId;
            var officeCustomers = CustomerService.Load().Customers;
            var outside = loaded.Customers.First(x => x.OfficeId != sales.SoId);
            Check(officeCustomers.All(x => x.OfficeId == sales.SoId) && CustomerService.ReadDetail(outside.Id) == null
                && CustomerOrderHistory.ReadPage(outside.Id, 1).Total == 0, "live sales access cannot read customers or orders outside its office");
        }
        finally { AppSession.Employee = null; }
    }

    private static object Create(Type type, params object?[] arguments) =>
        Activator.CreateInstance(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, arguments, null)!;

    private static void SavePreview(FrameworkElement view, string name)
    {
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth),
            (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, name + ".png"));
        encoder.Save(stream);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
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

    private static MEmployee Employee(string role, int officeId) => new()
    {
        EmId = 1, EmFlag = 0, SoId = officeId, Po = new MPosition { PoName = role },
        So = new MSalesOffice { SoId = officeId, SoName = "北大阪" }
    };
}
