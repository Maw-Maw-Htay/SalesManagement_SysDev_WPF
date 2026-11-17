using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SalesManagement_SysDev;
using static SalesManagement.Demo.Program;

namespace SalesManagement.Demo;

internal static class DemoUiChecks
{
    internal static void Run(string password)
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        F_Main? shell = null;
        try
        {
            var loggedIn = false;
            var login = new LoginViewModel(() => loggedIn = true, () => { });
            Require(login.PrepareDatabase(), "The real login view did not recognize the ready demo database.");
            login.EmployeeId = DemoData.Admin.ToString();
            login.Password = password;
            login.LoginCommand.Execute(null);
            Require(loggedIn && AppSession.IsAdmin, "The real login view did not open an administrator session.");
            Console.WriteLine("PASS WPF login and demo readiness check.");

            shell = new F_Main
            {
                Width = 1330, Height = 830, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000,
                ShowActivated = false, ShowInTaskbar = false
            };
            shell.Show();
            var sidebar = (SidebarViewModel)((FrameworkElement)shell.FindName("mainSidebar")).DataContext;
            var host = (ContentControl)shell.FindName("pageHost");
            string[] modules = ["受注管理", "注文管理", "発注管理", "入庫管理", "出庫管理", "入荷管理", "出荷管理", "売上管理",
                "在庫管理", "顧客管理", "商品管理", "社員管理", "履歴管理"];
            foreach (var module in modules)
            {
                var entry = sidebar.MenuItems.Single(item => item.Label == module);
                entry.NavigateCommand.Execute(null);
                var body = WaitForPage(host, module);
                Require(entry.IsActive && body.Items.Count > 0, $"{module} did not show its populated list.");
                Console.WriteLine($"PASS WPF {module} loaded {body.Items.Count} rows on its opening view.");
                if (module == "在庫管理") VerifyInventoryPagination(body, Descendants<InventoryManagementView>(host).Single());
                if (module == "履歴管理") VerifyHistoryPagination(body, (HistoryManagementView)host.Content);
            }
        }
        finally
        {
            shell?.Close();
            app.Shutdown();
        }
    }

    private static DataGrid WaitForPage(ContentControl host, string module)
    {
        DataGrid? grid = null;
        WaitFor(() => (grid = Descendants<DataGrid>(host).FirstOrDefault(control => control.IsVisible && control.Items.Count > 0)) != null,
            $"{module} did not load a populated DataGrid through the real navigation command.");
        return grid!;
    }

    private static void VerifyInventoryPagination(DataGrid grid, InventoryManagementView page)
    {
        var view = (InventoryManagementViewModel)grid.DataContext;
        var next = Descendants<Button>(page).Single(button => Equals(button.Content, "次へ"));
        var before = grid.Items.Cast<InventoryRowData>().Select(row => row.ProductId).ToHashSet();
        Require(next.IsEnabled, "The inventory list should have a second page.");
        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WaitFor(() => view.CurrentPage == 2 && grid.Items.Count == 20,
            "The inventory Next button did not show the second page.");
        Require(grid.Items.Cast<InventoryRowData>().All(row => !before.Contains(row.ProductId)), "Inventory page 2 repeats rows from page 1.");
        Console.WriteLine("PASS WPF 在庫管理 Next displays a distinct second page.");
    }

    private static void VerifyHistoryPagination(DataGrid grid, HistoryManagementView view)
    {
        var next = (Button)view.FindName("NextPageButton");
        var count = (TextBlock)view.FindName("ResultCount");
        var before = grid.Items.Cast<HistoryEventRow>().Select(row => (row.Module, row.EventId)).ToHashSet();
        Require(next.IsEnabled, "The audit list should have a second page.");
        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WaitFor(() => count.Text.StartsWith("21～40件", StringComparison.Ordinal) && grid.Items.Count == 20,
            "The audit Next button did not show the second page.");
        Require(grid.Items.Cast<HistoryEventRow>().All(row => !before.Contains((row.Module, row.EventId))), "Audit page 2 repeats rows from page 1.");
        Console.WriteLine("PASS WPF 履歴管理 Next displays 21～40件 on a distinct second page.");
    }

    private static void WaitFor(Func<bool> condition, string failure)
    {
        if (condition()) return;
        var timeout = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        var frame = new DispatcherFrame();
        timer.Tick += (_, _) =>
        {
            if (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(12)) return;
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        Require(condition(), failure);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) yield return result;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
