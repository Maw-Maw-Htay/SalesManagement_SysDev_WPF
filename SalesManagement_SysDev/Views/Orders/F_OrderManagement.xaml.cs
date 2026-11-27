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
            () => HideSelected(null, new RoutedEventArgs()), showHistory, ImportCsv, PrintFilteredOrders);
        orderRoot.DataContext = viewModel;
        viewModel.PropertyChanged += (_, e) =>
        {
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

    public void NavigateToAllOrders() => ShowOrdersByFlags(stateFlag: -1, managementFlagValue: -1);
    public void NavigateToPendingOrders() => ShowOrdersByFlags(stateFlag: 0, managementFlagValue: 0);
    public void NavigateToConfirmedOrders() => ShowOrdersByFlags(stateFlag: 1, managementFlagValue: 0);
    public void NavigateToHiddenOrders() => ShowOrdersByFlags(stateFlag: -1, managementFlagValue: 2);
    public void NavigateToHistory() => ShowOrdersByFlags(stateFlag: -1, managementFlagValue: -1, historyView: true);

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

    private void ShowAllOrders(object? sender, RoutedEventArgs e)
    {
        ShowOrdersByFlags(stateFlag: -1, managementFlagValue: -1);
        e.Handled = true;
    }

    private void ShowPendingOrders(object? sender, RoutedEventArgs e)
    {
        ShowOrdersByFlags(stateFlag: 0, managementFlagValue: 0);
        e.Handled = true;
    }

    private void ShowConfirmedOrders(object? sender, RoutedEventArgs e)
    {
        ShowOrdersByFlags(stateFlag: 1, managementFlagValue: 0);
        e.Handled = true;
    }

    private void ShowHiddenOrders(object? sender, RoutedEventArgs e)
    {
        ShowOrdersByFlags(stateFlag: -1, managementFlagValue: 2);
        e.Handled = true;
    }

    private void ShowOrderHistory(object? sender, RoutedEventArgs e)
    {
        ShowOrdersByFlags(stateFlag: -1, managementFlagValue: -1, historyView: true);
        e.Handled = true;
    }

    private void ShowOrdersByFlags(int stateFlag, int managementFlagValue, bool historyView = false)
    {
        keywordRefreshTimer.Stop();
        isHistoryView = historyView;
        viewModel.SelectTab(historyView ? OrderStatusTab.History
            : managementFlagValue == 2 ? OrderStatusTab.Hidden
            : stateFlag == 0 ? OrderStatusTab.Pending
            : stateFlag == 1 ? OrderStatusTab.Confirmed
            : OrderStatusTab.All);

        UpdateClearFiltersButton();
        LoadRows();
    }

    private void UpdateStatusMenuSelection()
    {
        allOrdersMenuItem.IsChecked = viewModel.SelectedTab == OrderStatusTab.All;
        pendingMenuItem.IsChecked = viewModel.SelectedTab == OrderStatusTab.Pending;
        confirmedMenuItem.IsChecked = viewModel.SelectedTab == OrderStatusTab.Confirmed;
        hiddenMenuItem.IsChecked = viewModel.SelectedTab == OrderStatusTab.Hidden;
        historyMenuItem.IsChecked = viewModel.SelectedTab == OrderStatusTab.History;
        pageTitle.Text = isHistoryView ? "受注履歴" : "受注一覧";
        UpdateHistoryViewState();
    }

    private void OrderRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wrapQuickSearch = e.NewSize.Width < 720;
        Grid.SetRow(searchActions, wrapQuickSearch ? 1 : 0);
        Grid.SetColumnSpan(quickSearchField, wrapQuickSearch ? 2 : 1);
        searchActions.Margin = new Thickness(0, wrapQuickSearch ? 6 : 0, 0, 0);
        var wrapActions = e.NewSize.Width < 440;
        Grid.SetRow(orderActions, wrapActions ? 1 : 0);
        Grid.SetColumn(orderActions, wrapActions ? 0 : 1);
        Grid.SetColumnSpan(orderActions, wrapActions ? 2 : 1);
        orderActions.Margin = new Thickness(0, wrapActions ? 8 : 0, 0, 0);
        listInteractionHint.Visibility = e.NewSize.Width >= 1000 ? Visibility.Visible : Visibility.Collapsed;
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
