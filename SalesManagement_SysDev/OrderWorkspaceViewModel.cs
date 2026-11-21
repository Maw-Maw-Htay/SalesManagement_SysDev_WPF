using System.ComponentModel;

namespace SalesManagement_SysDev;

public sealed record OrderPageLink(int Number, bool IsCurrent)
{
    public bool IsPage => Number > 0;
}

/// <summary>Presentation state shared by the existing 受注管理 window and its code-behind workflow.</summary>
public sealed class OrderManagementViewModel : ViewModelBase
{
    private bool isFiltersExpanded;
    private bool isBusy;
    private bool isLoading;
    private OrderStatusTab selectedTab;
    private string quickSearchText = "";
    private string orderNumberFilter = "";
    private string customerFilter = "";
    private string employeeFilter = "";
    private int selectedOfficeId = -1;
    private DateTime? orderDateFrom;
    private DateTime? orderDateTo;
    private DateTime? deliveryDateFrom;
    private DateTime? deliveryDateTo;
    private bool deliveryDateUnsetOnly;
    private readonly Action<int> requestPage;
    private readonly Dictionary<(int? OrderId, Guid? DraftId), OrderListRow> checkedRows = [];
    private bool updatingChecks;
    private int pageSize = 20;
    private OrderListSort sortColumn = OrderListSort.OrderDate;
    private ListSortDirection sortDirection = ListSortDirection.Descending;

    public OrderManagementViewModel(Action clearFilters, Action confirmSelected, Action hideSelected, bool showHistory,
        Action importCsv, Action printOrders, Action<int> requestPage)
    {
        this.requestPage = requestPage;
        selectedTab = showHistory ? OrderStatusTab.History : OrderStatusTab.All;
        ToggleFiltersCommand = new RelayCommand(() => IsFiltersExpanded = !IsFiltersExpanded);
        ClearFiltersCommand = new RelayCommand(clearFilters);
        ConfirmSelectedCommand = new RelayCommand(confirmSelected, () => CanUseRegisteredOrders && checkedRows.Values.Any(row => row.CanConfirm));
        HideSelectedCommand = new RelayCommand(hideSelected, () => CanUseRegisteredOrders && checkedRows.Values.Any(row => row.CanHide));
        ImportCsvCommand = new RelayCommand(importCsv, () => AppSession.CanManageOrders && !IsBusy);
        PrintCommand = new RelayCommand(printOrders, () => CanUseRegisteredOrders);
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => CanUseOrderList && HasSelection);
        TogglePageSelectionCommand = new RelayCommand(TogglePageSelection, () => CanUseOrderList && Rows.Count > 0);
        PreviousPageCommand = new RelayCommand(() => requestPage(PageNumber - 1), () => CanUseOrderList && PageNumber > 1);
        NextPageCommand = new RelayCommand(() => requestPage(PageNumber + 1), () => CanUseOrderList && PageNumber < PageCount);
        GoToPageCommand = new RelayCommand(value =>
        {
            if (value is OrderPageLink link && link.Number != PageNumber) requestPage(link.Number);
        }, value => CanUseOrderList && value is OrderPageLink { IsPage: true });
        SortAscendingCommand = new RelayCommand(value => SortBy(value as string, ListSortDirection.Ascending), value => CanSort(value as string));
        SortDescendingCommand = new RelayCommand(value => SortBy(value as string, ListSortDirection.Descending), value => CanSort(value as string));
    }

    public bool IsFiltersExpanded { get => isFiltersExpanded; set { if (Set(ref isFiltersExpanded, value)) Notify(nameof(FiltersToggleLabel)); } }
    public bool IsBusy
    {
        get => isBusy;
        set
        {
            if (!Set(ref isBusy, value)) return;
            UpdateAvailability();
        }
    }
    public bool IsLoading
    {
        get => isLoading;
        set { if (Set(ref isLoading, value)) UpdateAvailability(); }
    }
    public OrderStatusTab SelectedTab { get => selectedTab; private set => Set(ref selectedTab, value); }
    public bool CanUseOrderList => !IsBusy && !IsLoading && SelectedTab != OrderStatusTab.History && AppSession.CanManageOrders;
    public bool IsDraftTab => SelectedTab == OrderStatusTab.Draft;
    public bool CanUseRegisteredOrders => CanUseOrderList && !IsDraftTab;
    public string FiltersToggleLabel => IsFiltersExpanded ? "詳細条件を閉じる" : "詳細条件を展開";
    public RelayCommand ToggleFiltersCommand { get; }
    public RelayCommand ClearFiltersCommand { get; }
    public RelayCommand ConfirmSelectedCommand { get; }
    public RelayCommand HideSelectedCommand { get; }
    public RelayCommand ImportCsvCommand { get; }
    public RelayCommand PrintCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand TogglePageSelectionCommand { get; }
    public RelayCommand PreviousPageCommand { get; }
    public RelayCommand NextPageCommand { get; }
    public RelayCommand GoToPageCommand { get; }
    public RelayCommand SortAscendingCommand { get; }
    public RelayCommand SortDescendingCommand { get; }

    public IReadOnlyList<OrderListRow> Rows { get; private set; } = [];
    public bool HasSelection => checkedRows.Count > 0;
    public string SelectionText => $"{checkedRows.Count:N0}件選択中";
    public string SelectionHelp => IsDraftTab ? "全ページの下書きの選択件数"
        : $"全ページの選択 · 確定可能 {checkedRows.Values.Count(row => row.CanConfirm):N0} 件 / 非表示可能 {checkedRows.Values.Count(row => row.CanHide):N0} 件";
    public bool? AllPageSelected => Rows.All(row => !row.IsChecked) ? false : Rows.All(row => row.IsChecked) ? true : null;
    internal IReadOnlyList<OrderListRow> SelectedOrders => checkedRows.Values.Where(row => row.IsRegisteredOrder).ToArray();
    internal IReadOnlyList<int> SelectedOrderIds => checkedRows.Keys.Where(key => key.OrderId.HasValue)
        .Select(key => key.OrderId.GetValueOrDefault()).ToArray();
    internal IReadOnlyList<Guid> SelectedDraftIds => checkedRows.Keys.Where(key => key.DraftId.HasValue)
        .Select(key => key.DraftId.GetValueOrDefault()).ToArray();
    public int PageNumber { get; private set; } = 1;
    public int TotalCount { get; private set; }
    public int PageCount => OrderListQuery.PageCount(TotalCount, PageSize);
    public IReadOnlyList<int> PageSizes { get; } = [10, 20, 50, 100];
    public int PageSize
    {
        get => pageSize;
        set
        {
            if (PageSizes.Contains(value) && Set(ref pageSize, value)) requestPage(1);
        }
    }
    public IReadOnlyList<OrderPageLink> PageLinks => BuildPageLinks().ToArray();
    public string PageRange => Rows.Count == 0 ? "0件を表示"
        : $"{TotalCount:N0}件中 {(PageNumber - 1) * PageSize + 1:N0}～{(PageNumber - 1) * PageSize + Rows.Count:N0}件を表示";
    private bool UseDraftDefaultSort => IsDraftTab && sortColumn == OrderListSort.OrderId;
    public OrderListSort SortColumn => UseDraftDefaultSort ? OrderListSort.OrderDate : sortColumn;
    public ListSortDirection SortDirection => UseDraftDefaultSort ? ListSortDirection.Descending : sortDirection;

    public string QuickSearchText { get => quickSearchText; set => Set(ref quickSearchText, value ?? ""); }
    public string OrderNumberFilter { get => orderNumberFilter; set => SetDetailedFilter(ref orderNumberFilter, value ?? ""); }
    public string CustomerFilter { get => customerFilter; set => SetDetailedFilter(ref customerFilter, value ?? ""); }
    public string EmployeeFilter { get => employeeFilter; set => SetDetailedFilter(ref employeeFilter, value ?? ""); }
    public int SelectedOfficeId { get => selectedOfficeId; set => SetDetailedFilter(ref selectedOfficeId, value); }
    public DateTime? OrderDateFrom { get => orderDateFrom; set => SetDetailedFilter(ref orderDateFrom, value); }
    public DateTime? OrderDateTo { get => orderDateTo; set => SetDetailedFilter(ref orderDateTo, value); }
    public DateTime? DeliveryDateFrom { get => deliveryDateFrom; set => SetDetailedFilter(ref deliveryDateFrom, value); }
    public DateTime? DeliveryDateTo { get => deliveryDateTo; set => SetDetailedFilter(ref deliveryDateTo, value); }
    public bool DeliveryDateUnsetOnly { get => deliveryDateUnsetOnly; set => SetDetailedFilter(ref deliveryDateUnsetOnly, value); }

    public int ActiveDetailedFilterCount =>
        (!string.IsNullOrWhiteSpace(OrderNumberFilter) ? 1 : 0)
        + (!string.IsNullOrWhiteSpace(CustomerFilter) ? 1 : 0)
        + (!string.IsNullOrWhiteSpace(EmployeeFilter) ? 1 : 0)
        + (SelectedOfficeId >= 0 ? 1 : 0)
        + (OrderDateFrom.HasValue || OrderDateTo.HasValue ? 1 : 0)
        + (DeliveryDateUnsetOnly || DeliveryDateFrom.HasValue || DeliveryDateTo.HasValue ? 1 : 0);

    public bool HasActiveDetailedFilters => ActiveDetailedFilterCount > 0;

    private bool SetDetailedFilter<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (!Set(ref field, value, propertyName)) return false;
        Notify(nameof(ActiveDetailedFilterCount));
        Notify(nameof(HasActiveDetailedFilters));
        return true;
    }

    public void SelectTab(OrderStatusTab tab)
    {
        SelectedTab = tab;
        Notify(nameof(IsDraftTab));
        Notify(nameof(SortColumn));
        Notify(nameof(SortDirection));
        UpdateAvailability();
    }

    private bool CanSort(string? name) => CanUseOrderList && name is not null && (!IsDraftTab || name != nameof(OrderListSort.OrderId));

    internal void SortBy(string? name, ListSortDirection? direction = null)
    {
        if (!CanSort(name) || !Enum.TryParse<OrderListSort>(name, out var column)) return;
        var next = direction ?? (SortColumn == column && SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending : ListSortDirection.Ascending);
        if (SortColumn == column && SortDirection == next) return;
        sortColumn = column;
        sortDirection = next;
        Notify(nameof(SortColumn));
        Notify(nameof(SortDirection));
        requestPage(1);
    }

    internal void ShowPage(IReadOnlyList<OrderListRow> rows, IReadOnlyList<OrderListRow> checkedOffPage, int total, int number)
    {
        foreach (var row in Rows) row.PropertyChanged -= RowChecked;
        var checkedOnPage = rows.Where(row => checkedRows.ContainsKey(row.SelectionKey));
        var currentSelection = checkedOnPage.Concat(checkedOffPage).ToArray();
        checkedRows.Clear();
        foreach (var row in currentSelection) checkedRows.Add(row.SelectionKey, row);
        Rows = rows;
        PageNumber = number;
        TotalCount = total;
        foreach (var row in rows)
        {
            row.IsChecked = checkedRows.ContainsKey(row.SelectionKey);
            row.PropertyChanged += RowChecked;
        }
        Notify(nameof(Rows));
        Notify(nameof(PageNumber));
        Notify(nameof(PageLinks));
        Notify(nameof(PageRange));
        UpdateAvailability();
    }

    internal void ClearSelection()
    {
        checkedRows.Clear();
        UpdateVisibleChecks();
    }

    internal void UncheckOrders(IEnumerable<int> ids)
    {
        foreach (var id in ids) checkedRows.Remove((id, null));
        UpdateVisibleChecks();
    }

    private void UpdateVisibleChecks() => UpdateChecks(() =>
    {
        foreach (var row in Rows) row.IsChecked = checkedRows.ContainsKey(row.SelectionKey);
    });

    private void TogglePageSelection()
    {
        var select = AllPageSelected != true;
        UpdateChecks(() =>
        {
            foreach (var row in Rows)
            {
                row.IsChecked = select;
                RememberCheck(row);
            }
        });
    }

    private void UpdateChecks(Action update)
    {
        updatingChecks = true;
        try { update(); }
        finally { updatingChecks = false; }
        NotifySelection();
    }

    private void RowChecked(object? sender, PropertyChangedEventArgs args)
    {
        if (updatingChecks || args.PropertyName != nameof(OrderListRow.IsChecked) || sender is not OrderListRow row) return;
        RememberCheck(row);
        NotifySelection();
    }

    private void RememberCheck(OrderListRow row)
    {
        if (row.IsChecked) checkedRows[row.SelectionKey] = row;
        else checkedRows.Remove(row.SelectionKey);
    }

    private void NotifySelection()
    {
        Notify(nameof(HasSelection)); Notify(nameof(SelectionText)); Notify(nameof(SelectionHelp)); Notify(nameof(AllPageSelected));
        ConfirmSelectedCommand.Refresh(); HideSelectedCommand.Refresh(); ClearSelectionCommand.Refresh(); TogglePageSelectionCommand.Refresh();
    }

    private void UpdateAvailability()
    {
        Notify(nameof(CanUseOrderList));
        NotifySelection();
        ImportCsvCommand.Refresh(); PrintCommand.Refresh();
        PreviousPageCommand.Refresh(); NextPageCommand.Refresh(); GoToPageCommand.Refresh();
        SortAscendingCommand.Refresh(); SortDescendingCommand.Refresh();
    }

    private IEnumerable<OrderPageLink> BuildPageLinks()
    {
        var start = Math.Clamp(PageNumber - 2, 1, Math.Max(1, PageCount - 4));
        var numbers = Enumerable.Range(start, Math.Min(5, PageCount)).Append(1).Append(PageCount).Distinct().Order();
        var previous = 0;
        foreach (var number in numbers)
        {
            if (number - previous > 1) yield return new OrderPageLink(0, false);
            yield return new OrderPageLink(number, number == PageNumber);
            previous = number;
        }
    }
}
