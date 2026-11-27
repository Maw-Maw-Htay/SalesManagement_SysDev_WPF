namespace SalesManagement_SysDev;

/// <summary>Presentation state shared by the existing 受注管理 window and its code-behind workflow.</summary>
public sealed class OrderManagementViewModel : ViewModelBase
{
    private bool isFiltersExpanded;
    private bool isBusy;
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

    public OrderManagementViewModel(Action clearFilters, Action confirmSelected, Action hideSelected, bool showHistory, Action importCsv, Action printOrders)
    {
        IsFiltersExpanded = false;
        SelectedTab = showHistory ? OrderStatusTab.History : OrderStatusTab.All;
        ToggleFiltersCommand = new RelayCommand(() => IsFiltersExpanded = !IsFiltersExpanded);
        ClearFiltersCommand = new RelayCommand(clearFilters);
        ConfirmSelectedCommand = new RelayCommand(confirmSelected, () => CanConfirmSelection && !IsBusy);
        HideSelectedCommand = new RelayCommand(hideSelected, () => CanHideSelection && !IsBusy);
        ImportCsvCommand = new RelayCommand(importCsv, () => AppSession.CanManageOrders && !IsBusy);
        PrintCommand = new RelayCommand(printOrders, () => !IsBusy);
    }

    public bool IsFiltersExpanded { get => isFiltersExpanded; set { if (Set(ref isFiltersExpanded, value)) Notify(nameof(FiltersToggleLabel)); } }
    public bool IsBusy
    {
        get => isBusy;
        set
        {
            if (!Set(ref isBusy, value)) return;
            ConfirmSelectedCommand.Refresh(); HideSelectedCommand.Refresh(); ImportCsvCommand.Refresh(); PrintCommand.Refresh();
        }
    }
    public OrderStatusTab SelectedTab { get => selectedTab; private set => Set(ref selectedTab, value); }
    public string FiltersToggleLabel => IsFiltersExpanded ? "詳細条件を閉じる" : "詳細条件を展開";
    public bool CanConfirmSelection { get; private set; }
    public bool CanHideSelection { get; private set; }
    public RelayCommand ToggleFiltersCommand { get; }
    public RelayCommand ClearFiltersCommand { get; }
    public RelayCommand ConfirmSelectedCommand { get; }
    public RelayCommand HideSelectedCommand { get; }
    public RelayCommand ImportCsvCommand { get; }
    public RelayCommand PrintCommand { get; }

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

    public void SelectTab(OrderStatusTab tab) => SelectedTab = tab;

    public void UpdateSelection(bool canConfirm, bool canHide)
    {
        CanConfirmSelection = canConfirm;
        CanHideSelection = canHide;
        Notify(nameof(CanConfirmSelection)); Notify(nameof(CanHideSelection));
        ConfirmSelectedCommand.Refresh(); HideSelectedCommand.Refresh(); ImportCsvCommand.Refresh(); PrintCommand.Refresh();
    }
}