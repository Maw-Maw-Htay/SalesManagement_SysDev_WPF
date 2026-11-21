using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace SalesManagement_SysDev;

public sealed partial class F_OrderManagement
{
    private bool isHistoryView;
    private readonly OrderManagementViewModel viewModel;
    public Window? NavigationOwner { get; }

    public F_OrderManagement() : this(showHistory: false, navigationOwner: null)
    {
    }

    public F_OrderManagement(bool showHistory, Window? navigationOwner = null)
    {
        isHistoryView = showHistory;
        NavigationOwner = navigationOwner;
        InitializeComponent();
        Closed += (_, _) => { keywordRefreshTimer.Stop(); activeOrderQuery?.Cancel(); };
        viewModel = new OrderManagementViewModel(
            () => ClearFilters(null, new RoutedEventArgs()),
            () => ConfirmSelected(null, new RoutedEventArgs()),
            () => HideSelected(null, new RoutedEventArgs()), showHistory, ImportCsv, PrintFilteredOrders,
            page => _ = LoadRowsAsync(page));
        orderRoot.DataContext = viewModel;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OrderManagementViewModel.SortDirection)) UpdateSortHeaders();
            if (e.PropertyName != nameof(OrderManagementViewModel.IsFiltersExpanded) || viewModel.IsFiltersExpanded) return;
            customerSuggestionPopup.IsOpen = false;
            employeeSuggestionPopup.IsOpen = false;
        };
        Title = "受注管理（一覧・検索・確定・非表示・履歴）";
        WindowState = WindowState.Maximized;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = AppTypography.FontFamily;

        if (!AppSession.CanManageOrders)
        {
            Loaded += (_, _) =>
            {
                F_OrderNotice.Show(NavigationOwner ?? this, "権限エラー", "受注管理は管理者または営業担当者のみ利用できます。", OrderNoticeKind.Warning);
                Close();
            };
            return;
        }

        keywordRefreshTimer.Tick += (_, _) =>
        {
            keywordRefreshTimer.Stop();
            if (filterEventsEnabled) LoadRows();
        };
        officeFilter.SelectionChanged += FilterSelectionChanged;


        search.KeyDown += SearchKeyDown;
        search.TextChanged += SearchTextChanged;
        orderIdFilter.KeyDown += SearchKeyDown;
        orderIdFilter.TextChanged += SearchTextChanged;


        customerNameFilter.TextChanged += CustomerNameTextChanged;
        customerNameFilter.PreviewKeyDown += SuggestionInputKeyDown;
        customerNameFilter.GotKeyboardFocus += SuggestionInputGotFocus;
        employeeNameFilter.TextChanged += EmployeeNameTextChanged;
        employeeNameFilter.PreviewKeyDown += SuggestionInputKeyDown;
        employeeNameFilter.GotKeyboardFocus += SuggestionInputGotFocus;
        deliveryUnsetOnly.Checked += DeliveryUnsetChanged;
        deliveryUnsetOnly.Unchecked += DeliveryUnsetChanged;

        ConfigureOrderGrid(grid);
        ConfigureHistoryControls();
        _ = InitializeFilterDataAsync();
    }

    public UIElement DetachView()
    {
        if (Content is not Grid shell) throw new InvalidOperationException("受注管理画面を読み込めませんでした。");
        // The view is hosted by F_Main after detaching from this Window. Merge
        // the dictionaries that contain the actual resources into the hosted
        // root so deferred templates can still resolve keys such as OrderCanvas.
        foreach (var resourceDictionary in Resources.MergedDictionaries)
        {
            if (!shell.Resources.MergedDictionaries.Contains(resourceDictionary))
                shell.Resources.MergedDictionaries.Add(resourceDictionary);
        }
        Content = null;
        var nameScope = new System.Windows.NameScope();
        nameScope.RegisterName("historySearch", historySearch);
        nameScope.RegisterName("search", search);
        System.Windows.NameScope.SetNameScope(shell, nameScope);
        return shell;
    }

    public void NavigateToAllOrders() => ShowTab(OrderStatusTab.All);
    public void NavigateToPendingOrders() => ShowTab(OrderStatusTab.Pending);
    public void NavigateToConfirmedOrders() => ShowTab(OrderStatusTab.Confirmed);
    public void NavigateToHiddenOrders() => ShowTab(OrderStatusTab.Hidden);
    public void NavigateToHistory() => ShowTab(OrderStatusTab.History);

    private void SearchKeyDown(object? sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            keywordRefreshTimer.Stop();
            LoadRows();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape && sender is TextBox field && field.Text.Length > 0)
        {
            field.Clear();
            e.Handled = true;
        }
    }

    private void OrderRootPreviewKeyDown(object? sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.F || (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0) return;
        if (isHistoryView)
        {
            historySearch.Focus();
            historySearch.SelectAll();
        }
        else
        {
            Dispatcher.BeginInvoke(() =>
            {
                search.Focus();
                search.SelectAll();
            }, DispatcherPriority.Input);
        }
        e.Handled = true;
    }

    private void ClearKeyword(object? sender, RoutedEventArgs e)
    {
        search.Clear();
        search.Focus();
    }

    private void SearchOrders(object? sender, RoutedEventArgs e)
    {
        keywordRefreshTimer.Stop();
        LoadRows();
    }

    private void TodayDateRange(object? sender, RoutedEventArgs e) => SetDateRange(DateTime.Today, DateTime.Today);

    private void LastSevenDaysRange(object? sender, RoutedEventArgs e) => SetDateRange(DateTime.Today.AddDays(-6), DateTime.Today);

    private void ThisMonthRange(object? sender, RoutedEventArgs e) => SetDateRange(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), DateTime.Today);

    private void AllDateRange(object? sender, RoutedEventArgs e) => SetDateRange(null, null);

    private void StatusTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: OrderStatusTab tab }) return;
        ShowTab(tab);
        e.Handled = true;
    }

    private void ShowTab(OrderStatusTab tab)
    {
        keywordRefreshTimer.Stop();
        if (viewModel.SelectedTab != tab)
        {
            appliedOrderRequest = null;
            viewModel.ClearSelection();
            viewModel.ShowPage([], [], 0, 1);
            resultCount.Text = "読込中…";
            orderEmptyState.Text = "読込中…";
        }
        isHistoryView = tab == OrderStatusTab.History;
        viewModel.SelectTab(tab);

        UpdateClearFiltersButton();
        LoadRows();
    }

    private void UpdateStatusMenuSelection()
    {
        allOrdersMenuItem.IsChecked = viewModel.SelectedTab == OrderStatusTab.All;
        pendingMenuItem.IsChecked = viewModel.SelectedTab == OrderStatusTab.Pending;
        confirmedMenuItem.IsChecked = viewModel.SelectedTab == OrderStatusTab.Confirmed;
        draftsMenuItem.IsChecked = viewModel.IsDraftTab;
        hiddenMenuItem.IsChecked = viewModel.SelectedTab == OrderStatusTab.Hidden;
        historyMenuItem.IsChecked = viewModel.SelectedTab == OrderStatusTab.History;
        pageTitle.Text = isHistoryView ? "受注履歴" : viewModel.IsDraftTab ? "下書き一覧" : "受注一覧";
        UpdateHistoryViewState();
    }

    private void OrderRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        CloseRowMenu();
        var wrapQuickSearch = e.NewSize.Width < 560;
        Grid.SetRow(searchActions, wrapQuickSearch ? 1 : 0);
        Grid.SetColumn(searchActions, wrapQuickSearch ? 0 : 1);
        Grid.SetColumnSpan(searchActions, wrapQuickSearch ? 2 : 1);
        Grid.SetColumnSpan(quickSearchField, wrapQuickSearch ? 2 : 1);
        searchActions.Margin = wrapQuickSearch ? new Thickness(0, 6, 0, 0) : new Thickness(8, 0, 0, 0);
        var wrapStatusActions = e.NewSize.Width < 850;
        Grid.SetColumnSpan(statusMenu, wrapStatusActions ? 2 : 1);
        Grid.SetRow(statusActions, wrapStatusActions ? 1 : 0);
        statusActions.Margin = new Thickness(wrapStatusActions ? 0 : 8, 0, 0, 4);
        var wrapActions = e.NewSize.Width < 760;
        Grid.SetRow(listActions, wrapActions ? 1 : 0);
        Grid.SetColumn(listActions, wrapActions ? 0 : 1);
        Grid.SetColumnSpan(listActions, wrapActions ? 2 : 1);
        listActions.Margin = new Thickness(0, wrapActions ? 6 : 0, 0, 0);
        var wrapPagination = e.NewSize.Width < 710;
        Grid.SetRow(pageControls, wrapPagination ? 1 : 0);
        Grid.SetColumn(pageControls, wrapPagination ? 0 : 1);
        Grid.SetColumnSpan(pageControls, wrapPagination ? 3 : 1);
        Grid.SetColumnSpan(pageRangeText, wrapPagination ? 2 : 1);
        pageControls.Margin = new Thickness(0, wrapPagination ? 5 : 0, 0, 0);
        filterScroller.MaxHeight = Math.Clamp(e.NewSize.Height * 0.42, 100, 285);
    }

    private void FilterDetailsSizeChanged(object sender, SizeChangedEventArgs e) => ReflowFilterFields(e.NewSize.Width);

    private void ReflowFilterFields(double width)
    {
        if (width <= 0) return;
        var columns = width >= 760 ? 4 : width >= 560 ? 3 : 2;
        if (filterDetails.ColumnDefinitions.Count != columns)
        {
            filterDetails.ColumnDefinitions.Clear();
            for (var index = 0; index < columns; index++) filterDetails.ColumnDefinitions.Add(new ColumnDefinition());
        }
        var row = 0;
        var column = 0;
        foreach (var field in filterDetails.Children.OfType<FrameworkElement>().Where(field => field.Visibility != Visibility.Collapsed))
        {
            var span = field == orderDateField || field == deliveryDateField ? 2 : 1;
            if (column + span > columns) { row++; column = 0; }
            Grid.SetRow(field, row);
            Grid.SetColumn(field, column);
            Grid.SetColumnSpan(field, span);
            if (span == 1 && field is Border { Child: Grid inputs })
            {
                var inline = columns == 2;
                for (var index = 0; index < inputs.Children.Count; index++)
                {
                    var input = inputs.Children[index];
                    Grid.SetRow(input, inline || index == 0 ? 0 : 1);
                    Grid.SetColumn(input, inline && index > 0 ? 1 : 0);
                    Grid.SetColumnSpan(input, inline ? 1 : 2);
                }
                if (inputs.Children[0] is TextBlock label)
                {
                    label.TextWrapping = TextWrapping.Wrap;
                    label.VerticalAlignment = VerticalAlignment.Center;
                    label.Margin = inline ? new Thickness(0, 0, 12, 0) : new Thickness(0, 0, 0, 4);
                }
            }
            column += span;
            if (column == columns) { row++; column = 0; }
        }
        var rows = row + (column > 0 ? 1 : 0);
        if (filterDetails.RowDefinitions.Count == rows) return;
        filterDetails.RowDefinitions.Clear();
        for (var index = 0; index < rows; index++) filterDetails.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    }

    private static void SelectFilterItem(ComboBox comboBox, int id)
    {
        comboBox.SelectedItem = comboBox.Items
            .OfType<FilterItem>()
            .FirstOrDefault(item => item.Id == id);
    }
}
