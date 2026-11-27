using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public partial class F_Main : Window
{
    private F_OrderManagement? activeOrdersWindow;
    private F_OrderRegistration? activeOrderRegistration;
    private UIElement? ordersView;
    private ManagementListPage? activeListPage;
    private UIElement homeView = null!;
    private Button? activeNavigationButton;
    private SidebarEntry? activeNavigationItem;
    private SidebarEntry? historyNavigationItem;
    private readonly Dictionary<string, SidebarEntry> navigationItems = [];

    public F_Main()
    {
        InitializeComponent();
        activeNavigationButton = homeButton;
        Closing += (_, e) =>
        {
            if (!TryApprovePageLeave()) e.Cancel = true;
        };
        Closed += (_, _) =>
        {
            HistoryNotificationService.UnreadCountChanged -= OnHistoryUnreadCountChanged;
            activeListPage?.Dispose();
            activeOrderRegistration?.Dispose();
            // This view is detached from the shell but remains a hidden Window.
            activeOrdersWindow?.Close();
        };
        homeView = (UIElement)pageHost.Content;
        ((FrameworkElement)homeView).DataContext = new HomeViewModel(NavigateFromHome);
        var employee = AppSession.Employee!;
        status.Text = $"社員: {employee.EmName}　役職: {employee.Po.PoName}　営業所: {employee.So.SoName}";
        if (SalesManagementContext.IsDemoMode)
        {
            Title += "［デモ］";
            status.Text = "［デモ］ " + status.Text;
        }
        loginName.Text = employee.EmName;
        loginId.Text = employee.EmId.ToString();
        loginPhone.Text = string.IsNullOrWhiteSpace(employee.EmPhone) ? "未登録" : employee.EmPhone;
        if (AppSession.IsAdmin || AppSession.IsSales)
        {
            AddCategory("販売管理");
            Add("受注管理", "Order", () => ShowOrders(false), AppSession.CanManageOrders);
            Add("注文管理", "Details", () => ShowListPage(() => new F_Chumon()), AppSession.IsAdmin || AppSession.IsSales);
            Add("売上管理", "Sales", () => ShowListPage(() => new F_Sale()), AppSession.IsAdmin || AppSession.IsSales);
        }

        AddCategory("在庫・物流管理");
        Add("在庫管理", "Inventory", () => ShowListPage(() => new F_Stock()), AppSession.IsAdmin || AppSession.IsLogistics);
        Add("発注管理", "Purchase", () => ShowListPage(() => new F_Hattyu()), AppSession.IsAdmin || AppSession.IsLogistics);
        Add("入庫管理", "Inbound", () => ShowListPage(() => new F_Warehousing()), AppSession.IsAdmin || AppSession.IsLogistics);
        Add("出庫管理", "Outbound", () => ShowListPage(() => new F_Syukko()), AppSession.IsAdmin || AppSession.IsLogistics);
        Add("入荷管理", "Arrival", () => ShowListPage(() => new F_Arrival()), AppSession.IsAdmin || AppSession.IsSales);
        Add("出荷管理", "Shipment", () => ShowListPage(() => new F_Shipment()), AppSession.IsAdmin || AppSession.IsSales);

        AddCategory("マスタ管理");
        Add("顧客管理", "Customer", () => ShowListPage(() => new F_Client()), CustomerService.CanView);
        Add("商品管理", "Product", () => ShowListPage(() => new F_Product()), ProductService.CanView);
        Add("社員管理", "Employee", () => ShowListPage(() => new F_Employee()), AppSession.IsAdmin);
        AddCategory("システム管理");
        Add("履歴管理", "History", ShowHistoryManagement, AppSession.IsAdmin || AppSession.IsSales, supportsUnreadBadge: true);
        if (navigationItems.TryGetValue("履歴管理", out historyNavigationItem))
        {
            historyNavigationItem.UnreadCount = HistoryNotificationService.UnreadCount;
            HistoryNotificationService.UnreadCountChanged += OnHistoryUnreadCountChanged;
        }
    }

    private void Add(string text, string icon, Action navigate, bool allowed, bool supportsUnreadBadge = false)
    {
        if (!allowed) return;
        var sidebar = (SidebarViewModel)mainSidebar.DataContext;
        SidebarEntry entry = null!;
        entry = new SidebarEntry(sidebar, text, icon, () =>
        {
            if (!TryApprovePageLeave()) return;
            navigate();
            SetActiveNavigation(entry);
        }, supportsUnreadBadge: supportsUnreadBadge);
        sidebar.MenuItems.Add(entry);
        navigationItems[text] = entry;
    }

    private void NavigateFromHome(string page)
    {
        if (navigationItems.TryGetValue(page, out var entry)) entry.NavigateCommand.Execute(null);
    }

    private void SetActiveNavigation(Button button)
    {
        if (activeNavigationButton != null) activeNavigationButton.Tag = null;
        if (activeNavigationItem != null) activeNavigationItem.IsActive = false;
        activeNavigationItem = null;
        activeNavigationButton = button;
        button.Tag = "Active";
    }

    private void SetActiveNavigation(SidebarEntry entry)
    {
        if (activeNavigationButton != null) activeNavigationButton.Tag = null;
        if (activeNavigationItem != null) activeNavigationItem.IsActive = false;
        activeNavigationButton = null;
        activeNavigationItem = entry;
        entry.IsActive = true;
    }

    private void AddCategory(string text)
    {
        var sidebar = (SidebarViewModel)mainSidebar.DataContext;
        sidebar.MenuItems.Add(new SidebarEntry(sidebar, text, hasSeparator: sidebar.MenuItems.Count > 0));
    }

    private void ShowOrders(bool showHistory, int? state = null, bool hidden = false)
    {
        var firstOrderView = activeOrdersWindow == null;
        if (activeOrdersWindow == null)
        {
            activeOrdersWindow = new F_OrderManagement(showHistory, navigationOwner: this);
            ordersView = activeOrdersWindow.DetachView();
        }

        ShowBusinessView(showHistory ? "受注履歴" : "受注管理", ordersView!);
        activeListPage?.Dispose();
        activeListPage = null;
        if (firstOrderView) return;
        if (showHistory) activeOrdersWindow.NavigateToHistory();
        else if (hidden) activeOrdersWindow.NavigateToHiddenOrders();
        else if (state == 0) activeOrdersWindow.NavigateToPendingOrders();
        else if (state == 1) activeOrdersWindow.NavigateToConfirmedOrders();
        else activeOrdersWindow.NavigateToAllOrders();
    }

    private void ShowListPage(Func<ManagementListPage> createPage)
    {
        var page = createPage();
        var previousPage = activeListPage;
        activeListPage = page;
        ShowBusinessView(page.PageTitle, page);
        previousPage?.Dispose();
    }

    private void ShowHistoryManagement()
        => ShowHistoryManagement(new HistoryManagementView(OpenOrderFromHistory, HistoryNotificationService.MarkAllRead));

    internal void ShowEmployeeHistory(int? employeeId)
    {
        if (!EmployeeService.CanManage || historyNavigationItem == null) return;
        ShowHistoryManagement(new HistoryManagementView(OpenOrderFromHistory, HistoryNotificationService.MarkAllRead,
            startInEmployeeHistory: true, employeeId: employeeId));
        SetActiveNavigation(historyNavigationItem);
    }

    private void ShowHistoryManagement(HistoryManagementView view)
    {
        ShowBusinessView("履歴管理", view);
        activeListPage?.Dispose();
        activeListPage = null;
    }

    private void OnHistoryUnreadCountChanged()
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        if (Dispatcher.CheckAccess()) RefreshHistoryUnreadCount();
        else Dispatcher.BeginInvoke((Action)RefreshHistoryUnreadCount);
    }

    private void RefreshHistoryUnreadCount()
    {
        if (historyNavigationItem != null)
            historyNavigationItem.UnreadCount = HistoryNotificationService.UnreadCount;
    }

    private void OpenOrderFromHistory(int orderId)
    {
        if (!AppSession.CanManageOrders || AppSession.Employee is not { } sessionEmployee)
        {
            F_OrderNotice.Show(this, "受注詳細", "受注詳細を参照する権限がありません。", OrderNoticeKind.Warning);
            return;
        }

        try
        {
            using var db = new SalesManagementContext();
            var actor = db.MEmployees.AsNoTracking().Include(x => x.Po)
                .SingleOrDefault(x => x.EmId == sessionEmployee.EmId && x.EmFlag == EmployeeService.ActiveFlag);
            if (actor == null || actor.Po.PoFlag != 0 || actor.Po.PoName is not ("管理者" or "営業"))
            {
                F_OrderNotice.Show(this, "受注詳細", "有効な受注参照権限を確認できません。再ログインしてください。", OrderNoticeKind.Warning);
                return;
            }

            var query = db.TOrders.AsNoTracking()
                .Include(x => x.Cl).Include(x => x.So).Include(x => x.Em)
                .Include(x => x.TOrderDetails).ThenInclude(x => x.Pr).AsQueryable();
            if (actor.Po.PoName != "管理者") query = query.Where(x => x.SoId == actor.SoId);
            var order = query.SingleOrDefault(x => x.OrId == orderId);
            if (order == null)
            {
                F_OrderNotice.Show(this, "受注詳細", "受注が削除されているか、現在の営業所では参照できません。操作時の記録は履歴詳細で確認できます。", OrderNoticeKind.Information);
                return;
            }

            var snapshot = new OrderDetailSnapshot
            {
                OrderId = order.OrId, OrderDate = order.OrDate, DeliveryDate = order.OrDeliveryDate,
                OfficeName = order.So.SoName, OfficeId = order.SoId, CustomerId = order.ClId,
                CustomerName = order.Cl.ClName, CustomerAddress = order.Cl.ClAddress, CustomerPhone = order.Cl.ClPhone,
                CustomerContact = order.ClCharge, EmployeeName = order.Em.EmName, EmployeeId = order.EmId,
                StateFlag = order.OrStateFlag ?? 0, ManagementFlag = order.OrFlag, HiddenReason = order.OrHidden,
                Lines = order.TOrderDetails.Where(x => x.OrQuantity != 0 || x.OrTotalPrice != 0m)
                    .OrderBy(x => x.OrDetailId)
                    .Select(x => new OrderDetailLine
                    {
                        DetailId = x.OrDetailId, ProductId = x.PrId,
                        ProductCode = x.Pr.PrJcode ?? x.Pr.PrModelNumber,
                        ProductName = x.Pr.PrName, UnitPrice = x.OrQuantity == 0 ? 0 : x.OrTotalPrice / x.OrQuantity,
                        Quantity = x.OrQuantity, Subtotal = x.OrTotalPrice
                    }).ToList()
            };
            new F_OrderDetail(snapshot, this, readOnly: true).ShowDialog();
        }
        catch (Exception)
        {
            F_OrderNotice.Show(this, "受注詳細", "受注詳細を開けませんでした。履歴詳細を確認してください。", OrderNoticeKind.Error);
        }
    }

    private void ShowBusinessView(string title, UIElement view)
    {
        DisposeOrderRegistrationIfLeaving(view);
        Title = title;
        var isOrderPage = title is "受注管理" or "受注履歴";
        var isRegistrationPage = title == "受注登録";
        mainHeaderTitle.Text = isOrderPage ? "受注管理" : title;
        ApplyStandardHeaderStyle();
        if (view is F_Product { Content: ProductManagementView product } && ProductService.CanEdit)
        {
            productHeaderRegisterButton.DataContext = product.DataContext;
            productHeaderRegisterButton.Visibility = Visibility.Visible;
        }
        if (view is F_Client { Content: CustomerManagementView customer } && CustomerService.CanEdit)
        {
            customerHeaderRegisterButton.DataContext = customer.DataContext;
            customerHeaderRegisterButton.Visibility = Visibility.Visible;
        }
        if (view is F_Employee { Content: EmployeeManagementView employee } && EmployeeService.CanManage)
        {
            employeeHeaderRegisterButton.DataContext = employee.DataContext;
            employeeHeaderRegisterButton.Visibility = Visibility.Visible;
        }
        if (view is F_Hattyu && PurchaseOrderService.CanRegister)
            purchaseHeaderRegisterButton.Visibility = Visibility.Visible;
        if (isOrderPage)
        {
            mainHeader.Background = (Brush)FindResource("ThemeSurface");
            mainHeader.BorderBrush = (Brush)FindResource("ThemeBorder");
            mainHeaderTitle.Foreground = (Brush)FindResource("ThemePrimary");
            status.Foreground = (Brush)FindResource("ThemeSecondaryText");
            orderHeaderRegisterButton.Visibility = title == "受注管理" && AppSession.CanManageOrders
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        else if (isRegistrationPage)
        {
            mainHeaderTitle.Foreground = (Brush)FindResource("ThemePrimary");
            mainHeaderTitle.FontSize = 20;
            orderHeaderIcon.Stroke = (Brush)FindResource("ThemePrimary");
            orderHeaderIcon.Visibility = Visibility.Visible;
            status.Foreground = (Brush)FindResource("ThemeSecondaryText");
        }
        HideLoginManagement();
        pageHost.Content = view;
        mainHeader.Visibility = Visibility.Visible;
        mainSidebar.Visibility = Visibility.Visible;
    }

    private void ApplyStandardHeaderStyle()
    {
        mainHeader.Background = (Brush)FindResource("ThemeSurface");
        mainHeader.BorderBrush = (Brush)FindResource("ThemeBorder");
        mainHeaderTitle.Foreground = (Brush)FindResource("ThemeText");
        mainHeaderTitle.FontSize = 18;
        orderHeaderIcon.Visibility = Visibility.Collapsed;
        orderHeaderSystemTitle.Visibility = Visibility.Collapsed;
        orderHeaderRegisterButton.Visibility = Visibility.Collapsed;
        purchaseHeaderRegisterButton.Visibility = Visibility.Collapsed;
        employeeHeaderRegisterButton.Visibility = Visibility.Collapsed;
        employeeHeaderRegisterButton.DataContext = null;
        customerHeaderRegisterButton.Visibility = Visibility.Collapsed;
        customerHeaderRegisterButton.DataContext = null;
        productHeaderRegisterButton.Visibility = Visibility.Collapsed;
        productHeaderRegisterButton.DataContext = null;
        status.Visibility = Visibility.Visible;
    }

    private void OpenOrderRegistration_Click(object sender, RoutedEventArgs e)
    {
        if (Title == "受注管理" && AppSession.CanManageOrders) ShowOrderRegistration();
    }

    private void OpenEmployeeRegistration_Click(object sender, RoutedEventArgs e)
    {
        if (pageHost.Content is F_Employee { Content: EmployeeManagementView employee })
            employee.OpenRegistration();
    }

    private void OpenCustomerRegistration_Click(object sender, RoutedEventArgs e)
    {
        if (pageHost.Content is F_Client { Content: CustomerManagementView customer })
            customer.OpenRegistration();
    }

    private void OpenProductRegistration_Click(object sender, RoutedEventArgs e)
    {
        if (pageHost.Content is F_Product { Content: ProductManagementView product })
            product.OpenRegistration();
    }

    private async void OpenPurchaseRegistration_Click(object sender, RoutedEventArgs e)
    {
        if (pageHost.Content is not F_Hattyu { Content: PurchaseOrderManagementView purchase }) return;
        purchaseHeaderRegisterButton.IsEnabled = false;
        try { await purchase.OpenRegistrationAsync(); }
        finally { purchaseHeaderRegisterButton.IsEnabled = true; }
    }

    private void ShowOrderRegistration()
    {
        if (!AppSession.CanManageOrders)
        {
            F_OrderNotice.Show(this, "権限エラー", "受注登録は管理者または営業担当者のみ利用できます。", OrderNoticeKind.Warning);
            return;
        }
        if (pageHost.Content is F_OrderRegistration) return;

        activeOrderRegistration?.Dispose();
        activeOrderRegistration = new F_OrderRegistration(
            () => ShowOrders(false),
            () => F_OrderNotice.Confirm(this, "受注登録をキャンセル", "入力中の内容が保存されていません。\n画面を閉じてもよろしいですか？", "破棄して戻る", "入力を続ける", destructive: true),
            ShowRegisteredOrderDetails,
            StartNextOrderRegistration);
        ShowBusinessView("受注登録", activeOrderRegistration);
    }

    private void StartNextOrderRegistration()
    {
        ShowOrders(false);
        ShowOrderRegistration();
    }

    private void ShowRegisteredOrderDetails(OrderDetailSnapshot snapshot)
    {
        new F_OrderDetail(snapshot, this).ShowDialog();
    }

    private bool TryApprovePageLeave() =>
        pageHost.Content is not F_OrderRegistration registration || registration.TryApproveNavigation();

    private void DisposeOrderRegistrationIfLeaving(UIElement nextView)
    {
        if (activeOrderRegistration == null
            || ReferenceEquals(nextView, activeOrderRegistration)
            || !ReferenceEquals(pageHost.Content, activeOrderRegistration)) return;
        activeOrderRegistration.Dispose();
        activeOrderRegistration = null;
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "ログアウトしますか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (!TryApprovePageLeave()) return;
        HistoryNotificationService.MarkAllRead();
        AppSession.Employee = null;
        new F_login().Show();
        Close();
    }

    private void LoginManagement_Click(object sender, RoutedEventArgs e)
    {
        if (!TryApprovePageLeave()) return;
        DisposeOrderRegistrationIfLeaving(homeView);
        SetActiveNavigation(accountButton);
        ResetPasswordForm();
        Title = "ログイン管理";
        mainHeaderTitle.Text = "アカウント設定";
        ApplyStandardHeaderStyle();
        pageHost.Content = homeView;
        activeListPage?.Dispose();
        activeListPage = null;
        mainHeader.Visibility = Visibility.Visible;
        mainSidebar.Visibility = Visibility.Visible;
        pageHost.Visibility = Visibility.Collapsed;
        loginManagementView.Visibility = Visibility.Visible;
    }

    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        PasswordInputsChanged(sender, e);
        if (!changePasswordButton.IsEnabled) return;

        try
        {
            var changed = BusinessService.ChangePassword(AppSession.Employee!.EmId, currentPassword.Password, newPassword.Password, out var message);
            if (changed)
            {
                currentPassword.Clear();
                newPassword.Clear();
                confirmPassword.Clear();
            }
            else
            {
                currentPassword.Clear();
            }
            passwordMessage.Foreground = changed ? AppTheme.SuccessText : AppTheme.DangerText;
            passwordMessage.Text = message;
        }
        catch
        {
            passwordMessage.Foreground = AppTheme.Danger;
            passwordMessage.Text = "パスワードを変更できませんでした。しばらくしてから再度お試しください。";
        }
    }

    private void PasswordInputsChanged(object sender, RoutedEventArgs e)
    {
        if (currentPassword == null || newPassword == null || confirmPassword == null || changePasswordButton == null) return;

        var current = currentPassword.Password;
        var candidate = newPassword.Password;
        var confirmation = confirmPassword.Password;
        string? guidance = null;

        if (candidate.Length == 0)
            guidance = "新しいパスワードを入力してください。";
        else if (candidate.Length < 4 || candidate.Length > 10 || !candidate.All(char.IsAsciiLetterOrDigit))
            guidance = "4～10文字の半角英数字で入力してください。";
        else if (string.Equals(current, candidate, StringComparison.Ordinal))
            guidance = "現在とは異なるパスワードを設定してください。";
        else if (confirmation.Length == 0)
            guidance = "確認用にも同じパスワードを入力してください。";
        else if (!string.Equals(candidate, confirmation, StringComparison.Ordinal))
            guidance = "新しいパスワードが一致していません。";
        else if (current.Length == 0)
            guidance = "現在のパスワードを入力してください。";

        var valid = current.Length > 0 && candidate.Length is >= 4 and <= 10
            && candidate.All(char.IsAsciiLetterOrDigit)
            && !string.Equals(current, candidate, StringComparison.Ordinal)
            && string.Equals(candidate, confirmation, StringComparison.Ordinal);
        changePasswordButton.IsEnabled = valid;
        passwordMessage.Foreground = valid ? AppTheme.SuccessText : AppTheme.Text;
        passwordMessage.Text = valid ? "入力内容を確認しました。変更できます。" : guidance ?? "入力内容を確認してください。";
    }

    private void HideLoginManagement()
    {
        loginManagementView.Visibility = Visibility.Collapsed;
        pageHost.Visibility = Visibility.Visible;
        ResetPasswordForm();
    }

    private void ResetPasswordForm()
    {
        currentPassword.Clear();
        newPassword.Clear();
        confirmPassword.Clear();
        passwordMessage.Text = string.Empty;
        changePasswordButton.IsEnabled = false;
    }

    private void BackToMainMenu()
    {
        if (!TryApprovePageLeave()) return;
        DisposeOrderRegistrationIfLeaving(homeView);
        SetActiveNavigation(homeButton);
        Title = "販売管理システム";
        mainHeaderTitle.Text = "販売管理システム";
        ApplyStandardHeaderStyle();
        mainHeader.Visibility = Visibility.Visible;
        mainSidebar.Visibility = Visibility.Visible;
        HideLoginManagement();
        pageHost.Content = homeView;
        activeListPage?.Dispose();
        activeListPage = null;
    }

    private void Home_Click(object sender, RoutedEventArgs e) => BackToMainMenu();
}
