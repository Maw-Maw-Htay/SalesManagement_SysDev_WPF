using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using SalesManagement_SysDev;

internal static class Program
{
    private static int checks;
    [STAThread]
    private static int Main()
    {
        var app = new Application();
        try
        {
            var assemblyName = typeof(EmployeeManagementView).Assembly.GetName().Name;
            foreach (var dictionary in new[] { "Colors", "Typography", "Controls", "IconResources", "Layouts" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"/{assemblyName};component/Resources/{dictionary}.xaml", UriKind.Relative)
                });
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            CheckPasswordHashing();
            CheckValidationAndSearch();
            CheckPermissions();
            CheckListAndDialogs();
            Console.WriteLine($"PASS: {checks} employee management checks. No database connection was opened.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            AppSession.Employee = null;
            app.Shutdown();
        }
    }

    private static void CheckPasswordHashing()
    {
        const string password = "aB1234";
        var hash = EmployeePasswordHasher.Hash(password);
        Check(hash != password && !hash.Contains(password, StringComparison.Ordinal), "password is stored as a derived hash");
        Check(EmployeePasswordHasher.Verify(password, hash), "hash verifies the matching password");
        Check(!EmployeePasswordHasher.Verify("wrong", hash), "hash rejects a different password");
        Check(!EmployeePasswordHasher.Verify(password, "not-a-hash"), "malformed hash is rejected safely");
        Check(EmployeePasswordHasher.VerifyLegacy(password, password), "legacy credential comparison remains supported for migration");
        Check(!EmployeePasswordHasher.VerifyLegacy(password, "wrong"), "legacy credential comparison rejects a mismatch");
        var placeholder = EmployeePasswordHasher.CreateLegacyPlaceholder();
        Check(placeholder.Length == 10 && placeholder.All(char.IsAsciiLetterOrDigit), "legacy column placeholder respects its existing width");
    }

    private static void CheckValidationAndSearch()
    {
        Check(EmployeeService.Validate("田中 太郎", "06-1234-5678", "a123") == null, "valid employee and initial password pass validation");
        Check(EmployeeService.Validate("  ", "06-1234-5678", "a123") != null, "employee name is required");
        Check(EmployeeService.Validate("田中", new string('1', 14), "a123") != null, "phone respects database width");
        Check(EmployeeService.Validate("田中", "06-1234-5678", "日本語") != null, "initial password follows existing alphanumeric policy");
        Check(EmployeeService.ValidateHideReason("組織変更") == null, "employee hide requires and accepts a reason");
        Check(EmployeeService.ValidateHideReason("  ") != null, "employee hide rejects a blank reason");
        Check(EmployeeService.ValidateHideReason(new string('理', 501)) != null, "employee hide reason respects the database width");
        var row = new EmployeeRecord(123, "田中 太郎", "06-1234-5678", new DateTime(2020, 1, 1), 1,
            "北大阪営業所", 2, "営業", 0, 0, null);
        Check(EmployeeFilter.Matches(row, "E-0123", "田中", "北大阪", "営業", "5678", 0), "employee filters combine with AND and partial matching");
        Check(!EmployeeFilter.Matches(row, "E-0123", "田中", "京都", "営業", "5678", 0), "mismatched office excludes employee");
        Check(EmployeeService.RoleLabel("営業") == "営業担当" && EmployeeService.RoleLabel("物流") == "物流担当", "existing role names receive Japanese UI labels");
    }

    private static void CheckPermissions()
    {
        AppSession.Employee = Employee("営業");
        Check(!EmployeeService.CanManage, "sales role cannot administer employees");
        Check(!EmployeeService.Create(1, 2, "社員", "000", 1, 1, "a123").Succeeded, "employee creation rejects unauthorized callers before database access");
        AppSession.Employee = Employee("物流");
        Check(!EmployeeService.CanManage, "logistics role cannot administer employees");
        AppSession.Employee = Employee("管理者");
        Check(EmployeeService.CanManage, "administrator can open employee administration");
        AppSession.Employee = null;
        Check(!EmployeeService.CanManage, "missing session denies employee administration");
    }

    private static void CheckListAndDialogs()
    {
        AppSession.Employee = Employee("管理者");
        var rows = Enumerable.Range(1, 42).Select(id => new EmployeeRecord(id, id == 22 ? "テスト YamAda" : $"テスト{id}", "06-1234-5678",
            new DateTime(2026, 1, id % 10 + 1, 12, 0, 0), id % 2 + 1, id % 2 == 0 ? "北営業所" : "南営業所",
            2, "営業", id == 42 ? 1 : 0, id >= 40 && id <= 41 ? EmployeeService.HiddenFlag : EmployeeService.ActiveFlag, null)).ToArray();
        var directory = new EmployeeDirectory(rows, [new(1, "北営業所"), new(2, "南営業所")], [new(2, "営業", "営業担当")]);
        var assembly = typeof(EmployeeManagementView).Assembly;
        var vm = (EmployeeManagementViewModel)Create(typeof(EmployeeManagementViewModel), (Func<EmployeeDirectory>)(() => directory));
        var view = (EmployeeManagementView)Create(typeof(EmployeeManagementView), vm);
        var host = new Window
        {
            Width = 860, Height = 470, ShowActivated = false, ShowInTaskbar = false, Left = -2000,
            WindowStartupLocation = WindowStartupLocation.Manual, Content = view
        };
        host.Show();
        try
        {
            WaitFor(() => vm.CanCreate, "directory loads");
            host.UpdateLayout();
            SavePreview(view, "employee-list");
            var grid = (DataGrid)view.FindName("EmployeeGrid");
            var scroll = Descendants(grid).OfType<ScrollViewer>().First();
            Check(vm.PageSize == 20 && vm.Rows.Count == 20 && vm.PageCount == 3 && vm.CanGoForward,
                "list defaults to 20 of the 42 records with more pages available");
            Check(scroll.ScrollableHeight > 0 && scroll.ScrollableWidth < 1,
                "the grid provides vertical scrolling and fits at the normal sidebar content width");
            Check(vm.TotalCount == 42 && vm.VisibleCount == 40 && vm.HiddenCount == 2, "status counts use real row flags");
            vm.PageNumber = 3;
            Check(vm.Rows.Select(x => x.Data.Id).SequenceEqual(new[] { 41, 42 }) && !vm.CanGoForward, "last page shows its two remaining rows");
            vm.PageSize = 10;
            Check(vm.PageNumber == 1 && vm.Rows.Count == 10 && vm.PageCount == 5, "page size resets to the first page");
            vm.SortBy(nameof(EmployeeRow.Code), ListSortDirection.Descending);
            Check(vm.Rows[0].Data.Id == 42, "sort runs across every filtered page");

            foreach (var (name, expected) in new[] { ("HiddenTab", 2), ("VisibleTab", 40), ("AllTab", 42) })
            {
                var tab = (Button)view.FindName(name);
                tab.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                host.UpdateLayout();
                var label = Descendants(tab).OfType<TextBlock>().Single();
                var icon = Descendants(tab).OfType<LucideIcon>().Single();
                Check(((SolidColorBrush)tab.Background).Color == ((SolidColorBrush)Application.Current.FindResource("ThemePrimary")).Color
                    && ((SolidColorBrush)label.Foreground).Color == Colors.White && ((SolidColorBrush)icon.Stroke).Color == Colors.White,
                    $"{name} has a navy background and white label, count and icon");
                Check(vm.PageNumber == 1 && vm.PageCount == (expected + vm.PageSize - 1) / vm.PageSize, $"{name} shows the matching records");
            }

            var search = (TextBox)view.FindName("QuickSearchBox");
            search.Text = "aMaD";
            WaitFor(() => vm.Rows.Count == 1 && vm.Rows[0].Data.Id == 22, "partial name search is case insensitive");
            search.Text = "e-002";
            vm.IsFiltersExpanded = true;
            host.UpdateLayout();
            var officeFilter = Descendants(view).OfType<ComboBox>().Single(x => AutomationProperties.GetName(x) == "所属営業所");
            officeFilter.SetCurrentValue(Selector.SelectedValueProperty, 2);
            WaitFor(() => vm.Rows.Count == 5 && vm.Rows.All(x => x.Data.OfficeId == 2), "quick search and office filter intersect");
            search.Text = "42";
            vm.OfficeFilter = null;
            vm.LoginFilter = false;
            WaitFor(() => vm.Rows.Count == 1 && vm.Rows[0].Data.Id == 42, "login ID search includes an inactive role without mislabeling it as hidden");
            vm.HiredFrom = new DateTime(2026, 1, 4);
            vm.HiredTo = new DateTime(2026, 1, 3);
            WaitFor(() => vm.HasFilterError, "reversed hire date interval shows an error");
            Check(vm.Rows.Count == 0, "invalid hire date filters do not return rows");
            vm.HiredFrom = vm.HiredTo;
            WaitFor(() => vm.Rows.Count == 1, "equal hire date bounds include the whole day");
            vm.ClearConditions();
            Check(search.Text == "" && officeFilter.SelectedValue == null && vm.PageNumber == 1 && vm.Rows.Count == 10 && !vm.HasFilterError,
                "clearing conditions resets the inputs and the result page");
            vm.IsFiltersExpanded = false;
            host.Width = 520;
            host.UpdateLayout();
            Check(Grid.GetRow((UIElement)view.FindName("EmployeeTools")) == 1, "narrow layouts keep both history and status controls visible");
            host.Width = 860;
            host.UpdateLayout();

            var firstEligible = vm.Rows.Single(x => x.Data.Id == 42);
            firstEligible.IsSelected = true;
            Check(vm.CanHide, "a single visible non-self employee can be selected for the existing hide flow");
            vm.PageNumber = 2;
            Check(vm.SelectedCount == 0 && !vm.CanHide, "moving pages cannot leave a hidden action target selected");
            var eligibleRows = vm.Rows.Where(x => x.IsSelectable).Take(2).ToArray();
            foreach (var row in eligibleRows) row.IsSelected = true;
            Check(vm.SelectedCount == 2, "two eligible employees on the current page can be selected");
            int selectionNotifications = 0;
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.SelectedCount)) selectionNotifications++; };
            vm.ClearSelection();
            Check(selectionNotifications == 1 && vm.SelectedCount == 0 && !vm.CanHide,
                "clearing employee selection publishes one final selection update");

            var detail = new EmployeeDetail(rows[21], []);
            var detailDialog = (Window)Create(assembly.GetType("SalesManagement_SysDev.EmployeeDetailDialog")!, detail, true, host);
            detailDialog.Show();
            detailDialog.UpdateLayout();
            SavePreview(detailDialog, "employee-detail");
            var detailsTabs = Descendants(detailDialog).OfType<TabControl>().Single();
            Check(detailsTabs.Items.Cast<TabItem>().Select(x => x.Header).SequenceEqual(new[] { "基本情報", "権限情報", "変更履歴" }),
                "employee detail exposes separate basic, permission and history tabs");
            Check(((SolidColorBrush)((TabItem)detailsTabs.SelectedItem).Foreground).Color == Colors.White,
                "the selected detail tab uses the shared white foreground");
            detailDialog.Close();

            var editor = (Window)Create(assembly.GetType("SalesManagement_SysDev.EmployeeEditorDialog")!, vm, null, host);
            editor.Show();
            editor.UpdateLayout();
            SavePreview(editor, "employee-registration");
            Check(Descendants(editor).OfType<PasswordBox>().Count() == 2
                && !Descendants(editor).OfType<Button>().Single(x => Equals(x.Content, "社員を登録")).IsEnabled,
                "registration masks and confirms the initial password and blocks incomplete submission");
            editor.Close();

            var disable = (Window)Create(assembly.GetType("SalesManagement_SysDev.EmployeeHideDialog")!, rows[21], host);
            disable.Show();
            disable.UpdateLayout();
            var disableAction = Descendants(disable).OfType<Button>().Single(x => Equals(x.Content, "無効化する"));
            Check(!disableAction.IsEnabled, "disable confirmation requires a reason");
            Descendants(disable).OfType<TextBox>().Single().Text = "テストの理由";
            Check(disableAction.IsEnabled, "valid reason enables confirmation without changing an account");
            disable.Close();
        }
        finally { host.Close(); }
    }

    private static object Create(Type type, params object?[] arguments) =>
        Activator.CreateInstance(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, arguments, null)!;

    private static void SavePreview(FrameworkElement view, string name)
    {
        if (Environment.GetEnvironmentVariable("EMPLOYEE_UI_PREVIEW") != "1") return;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
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
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }
        Check(condition(), name);
    }

    private static MEmployee Employee(string role) => new()
    {
        EmId = 1, EmFlag = 0, SoId = 1,
        Po = new MPosition { PoName = role, PoFlag = 0 },
        So = new MSalesOffice { SoId = 1, SoName = "北大阪営業所" }
    };

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        checks++; Console.WriteLine("PASS " + name);
    }
}
