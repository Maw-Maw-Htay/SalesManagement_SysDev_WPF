using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;

namespace SalesManagement_SysDev;

public sealed class SalesRow : ViewModelBase
{
    private bool isSelected;
    public SalesRecord Data { get; }
    public string Code => Data.Code;
    public string SaleDateText => Data.SaleDate.ToString("yyyy/MM/dd");
    public string OrderCode => Data.OrderCode;
    public string CustomerName => Data.CustomerName;
    public string EmployeeName => Data.EmployeeName;
    public string OfficeName => Data.OfficeName;
    public string AmountText => SalesManagementViewModel.FormatCurrency(Data.Amount);
    public string ManagementLabel => Data.ManagementLabel;
    public bool IsSelectable => SalesService.CanHide && Data.ManagementFlag == SalesService.ActiveFlag;
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (!Set(ref isSelected, value)) return;
            SelectionChanged?.Invoke(this, value);
        }
    }

    internal Action<SalesRow, bool>? SelectionChanged { get; set; }
    internal void SetSelectedFromViewModel(bool value) => Set(ref isSelected, value, nameof(IsSelected));
    public SalesRow(SalesRecord data) => Data = data;
}

public sealed class SalesDetailDialogViewModel
{
    public SalesRecord Sale { get; }
    public IReadOnlyList<SalesLineRecord> Lines { get; }
    public bool CanHideSale => CanHide && Sale.ManagementFlag == SalesService.ActiveFlag;
    private bool CanHide { get; }
    public SalesDetailDialogViewModel(SalesDetail detail, bool canHide)
    { Sale = detail.Sale; Lines = detail.Lines; CanHide = canHide; }
}

public sealed class SalesHideDialogViewModel : ViewModelBase
{
    private readonly Action confirm;
    private string reason = "";
    public SalesRecord Sale { get; }
    public string Reason
    {
        get => reason;
        set
        {
            if (!Set(ref reason, value ?? "")) return;
            Notify(nameof(CanConfirm)); Notify(nameof(ValidationMessage)); ConfirmCommand.Refresh();
        }
    }
    public string ValidationMessage => SalesService.ValidateHideReason(Reason) ?? "";
    public bool CanConfirm => ValidationMessage.Length == 0;
    public RelayCommand ConfirmCommand { get; }

    public SalesHideDialogViewModel(SalesRecord sale, Action confirm)
    {
        Sale = sale; this.confirm = confirm;
        ConfirmCommand = new RelayCommand(() => { if (CanConfirm) this.confirm(); }, () => CanConfirm);
    }
}

public sealed class SalesManagementViewModel : ViewModelBase, IDisposable
{
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private List<SalesRow> sourceRows = [];
    private IReadOnlyList<SalesEmployeeOption> employeeOptions = [];
    private IReadOnlyList<SalesOfficeOption> officeOptions = [];
    private SalesSummary summary = new(0, 0m);
    private SalesStatusTab activeTab = SalesStatusTab.Visible;
    private SalesRow? selectedRow;
    private string keyword = "", minimumAmount = "", maximumAmount = "", feedback = "", searchError = "";
    private DateTime? startDate, endDate;
    private int employeeSelection, officeSelection;
    private bool isFiltersExpanded = true, isLoading, isBusy, isDisposed;

    public ObservableCollection<SalesRow> Rows { get; } = [];
    public IReadOnlyList<SalesEmployeeOption> EmployeeOptions => [new SalesEmployeeOption(0, "すべて"), .. employeeOptions];
    public IReadOnlyList<SalesOfficeOption> OfficeOptions => [new SalesOfficeOption(0, "すべて"), .. officeOptions];
    public string Keyword { get => keyword; set { if (Set(ref keyword, value ?? "")) ScheduleSearch(); } }
    public DateTime? StartDate { get => startDate; set { if (Set(ref startDate, value?.Date)) ScheduleSearch(); } }
    public DateTime? EndDate { get => endDate; set { if (Set(ref endDate, value?.Date)) ScheduleSearch(); } }
    public string MinimumAmount { get => minimumAmount; set { if (Set(ref minimumAmount, value ?? "")) ScheduleSearch(); } }
    public string MaximumAmount { get => maximumAmount; set { if (Set(ref maximumAmount, value ?? "")) ScheduleSearch(); } }
    public int EmployeeSelection { get => employeeSelection; set { if (Set(ref employeeSelection, value)) ScheduleSearch(); } }
    public int OfficeSelection { get => officeSelection; set { if (Set(ref officeSelection, value)) ScheduleSearch(); } }
    public string Feedback { get => feedback; private set => Set(ref feedback, value); }
    public string SearchError { get => searchError; private set { if (Set(ref searchError, value)) { Notify(nameof(EmptyTitle)); Notify(nameof(EmptyDescription)); } } }
    public SalesStatusTab ActiveTab { get => activeTab; private set { if (Set(ref activeTab, value)) NotifyTabs(); } }
    public SalesRow? SelectedRow { get => selectedRow; private set { if (Set(ref selectedRow, value)) NotifySelection(); } }
    public bool IsFiltersExpanded { get => isFiltersExpanded; set { if (Set(ref isFiltersExpanded, value)) Notify(nameof(FiltersToggleLabel)); } }
    public string FiltersToggleLabel => IsFiltersExpanded ? "詳細条件を折りたたむ" : "詳細条件を展開";
    public bool IsLoading { get => isLoading; private set { if (Set(ref isLoading, value)) NotifyActions(); } }
    public bool IsBusy { get => isBusy; private set { if (Set(ref isBusy, value)) NotifyActions(); } }
    public bool CanReload => !IsLoading && !IsBusy;
    public bool CanHideSales => SalesService.CanHide;
    public bool CanHideSelected => CanHideSales && CanReload && SelectedRow is { IsSelectable: true }
        && ActiveTab != SalesStatusTab.Hidden;
    public bool CanClearSelection => SelectedRow != null;
    public int SelectedCount => SelectedRow == null ? 0 : 1;
    public string SelectionText => $"選択 {SelectedCount:N0} 件";
    public bool HasConditions => !string.IsNullOrWhiteSpace(Keyword) || StartDate.HasValue || EndDate.HasValue
        || EmployeeSelection > 0 || OfficeSelection > 0 || !string.IsNullOrWhiteSpace(MinimumAmount)
        || !string.IsNullOrWhiteSpace(MaximumAmount);
    public int TotalCount => sourceRows.Count;
    public int VisibleCount => sourceRows.Count(x => x.Data.ManagementFlag == SalesService.ActiveFlag);
    public int HiddenCount => sourceRows.Count(x => x.Data.ManagementFlag == SalesService.HiddenFlag);
    public int CurrentTabCount => ActiveTab switch
    {
        SalesStatusTab.Visible => VisibleCount,
        SalesStatusTab.Hidden => HiddenCount,
        _ => TotalCount
    };
    public string CountText => $"{Rows.Count:N0} 件表示 / 全 {CurrentTabCount:N0} 件";
    public string AllTabText => $"すべて　{TotalCount:N0}";
    public string VisibleTabText => $"表示中　{VisibleCount:N0}";
    public string HiddenTabText => $"非表示　{HiddenCount:N0}";
    public bool IsEmpty => Rows.Count == 0;
    public bool ShowEmptyState => !IsLoading && IsEmpty;
    public string EmptyTitle => string.IsNullOrWhiteSpace(SearchError) ? "一致する売上がありません" : "検索条件を確認してください";
    public string EmptyDescription => string.IsNullOrWhiteSpace(SearchError)
        ? "検索条件を変更するか、条件をクリアしてください。" : SearchError;
    public string SummaryScope => $"{DateTime.Today:yyyy年M月}・表示中売上（許可範囲内）";
    public string SummaryAmountText => FormatCurrency(summary.TotalAmount);
    public string SummaryCountText => $"{summary.Count:N0} 件";
    public string SummaryAverageText => FormatCurrency(summary.AverageAmount);

    public SalesManagementViewModel()
    {
        debounce.Tick += (_, _) => { debounce.Stop(); ApplyFilters(); };
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (IsLoading || IsBusy || isDisposed) return;
        await LoadCoreAsync();
    }

    private async Task LoadCoreAsync()
    {
        IsLoading = true;
        Feedback = "売上一覧を読み込んでいます…";
        try
        {
            var directory = await Task.Run(SalesService.Load);
            sourceRows = directory.Sales.Select(x => new SalesRow(x)).ToList();
            foreach (var row in sourceRows) row.SelectionChanged = OnSelectionChanged;
            employeeOptions = directory.Employees;
            officeOptions = directory.Offices;
            summary = directory.MonthlyVisibleSummary;
            Notify(nameof(EmployeeOptions)); Notify(nameof(OfficeOptions)); Notify(nameof(SummaryScope));
            Notify(nameof(SummaryAmountText)); Notify(nameof(SummaryCountText)); Notify(nameof(SummaryAverageText));
            NotifyCounts();
            Feedback = "";
            ApplyFilters();
        }
        catch (UnauthorizedAccessException ex)
        {
            sourceRows = []; Rows.Clear(); Feedback = ex.Message; NotifyCounts(); ApplyFilters();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Sales list load failed: {ex}");
            if (sourceRows.Count == 0) Rows.Clear();
            Feedback = "売上一覧を読み込めませんでした。データベースの接続状態を確認してください。";
            NotifyCounts(); ApplyFilters();
        }
        finally { IsLoading = false; }
    }

    public void SelectTab(SalesStatusTab tab)
    {
        ActiveTab = tab;
        ApplyFilters();
    }

    public void ToggleFilters() => IsFiltersExpanded = !IsFiltersExpanded;

    public void ClearSelection()
    {
        if (SelectedRow != null) SelectedRow.SetSelectedFromViewModel(false);
        SelectedRow = null;
    }

    public void SelectOnly(SalesRow row)
    {
        if (!row.IsSelectable) return;
        foreach (var other in sourceRows.Where(x => !ReferenceEquals(x, row) && x.IsSelected))
            other.SetSelectedFromViewModel(false);
        row.SetSelectedFromViewModel(true);
        SelectedRow = row;
    }

    public void ClearConditions()
    {
        debounce.Stop();
        Keyword = ""; StartDate = null; EndDate = null; EmployeeSelection = 0; OfficeSelection = 0;
        MinimumAmount = ""; MaximumAmount = "";
        debounce.Stop();
        ApplyFilters();
    }

    public async Task<SalesOperationResult> HideAsync(SalesRow row, string reason)
    {
        if (!CanHideSelected || !ReferenceEquals(SelectedRow, row))
            return new(false, "非表示にできる売上を1件選択してください。");
        IsBusy = true;
        try
        {
            var result = await Task.Run(() => SalesService.Hide(AppSession.Employee!.EmId, row.Data.Id, reason));
            if (result.Succeeded)
            {
                ClearSelection();
                await LoadCoreAsync();
            }
            return result;
        }
        finally { IsBusy = false; }
    }

    private void ScheduleSearch()
    {
        Notify(nameof(HasConditions));
        debounce.Stop();
        if (!isDisposed) debounce.Start();
    }

    private void ApplyFilters()
    {
        var parseError = SalesFilter.ParseAmountRange(MinimumAmount, MaximumAmount, out var minimum, out var maximum);
        var criteria = new SalesSearchCriteria(Keyword, StartDate, EndDate,
            EmployeeSelection > 0 ? EmployeeSelection : null,
            OfficeSelection > 0 ? OfficeSelection : null, minimum, maximum);
        SearchError = parseError ?? SalesFilter.Validate(criteria) ?? "";
        IEnumerable<SalesRow> filtered = string.IsNullOrWhiteSpace(SearchError)
            ? sourceRows.Where(row => SalesFilter.Matches(row.Data, criteria, ActiveTab))
            : [];
        var matches = filtered.ToArray();
        var ids = matches.Select(x => x.Data.Id).ToHashSet();
        if (SelectedRow != null && !ids.Contains(SelectedRow.Data.Id)) ClearSelection();
        Rows.Clear(); foreach (var row in matches) Rows.Add(row);
        Notify(nameof(CountText)); Notify(nameof(IsEmpty)); Notify(nameof(ShowEmptyState)); Notify(nameof(HasConditions));
        NotifySelection();
    }

    private void OnSelectionChanged(SalesRow row, bool selected)
    {
        if (selected && !row.IsSelectable) { row.SetSelectedFromViewModel(false); return; }
        if (selected)
        {
            foreach (var other in sourceRows.Where(x => !ReferenceEquals(x, row) && x.IsSelected))
                other.SetSelectedFromViewModel(false);
            SelectedRow = row;
        }
        else if (ReferenceEquals(SelectedRow, row)) SelectedRow = null;
        NotifySelection();
    }

    private void NotifySelection()
    {
        Notify(nameof(SelectedCount)); Notify(nameof(SelectionText)); Notify(nameof(CanClearSelection)); Notify(nameof(CanHideSelected));
    }

    private void NotifyTabs()
    {
        Notify(nameof(AllTabText)); Notify(nameof(VisibleTabText)); Notify(nameof(HiddenTabText));
        Notify(nameof(CurrentTabCount)); Notify(nameof(CountText)); Notify(nameof(CanHideSelected));
    }

    private void NotifyCounts()
    {
        Notify(nameof(TotalCount)); Notify(nameof(VisibleCount)); Notify(nameof(HiddenCount)); NotifyTabs();
    }

    private void NotifyActions()
    {
        Notify(nameof(CanReload)); Notify(nameof(CanHideSelected)); Notify(nameof(ShowEmptyState));
    }

    public static string FormatCurrency(decimal amount) => "¥" + amount.ToString("#,##0;-#,##0", CultureInfo.GetCultureInfo("ja-JP"));

    public void Dispose()
    {
        isDisposed = true;
        debounce.Stop();
    }
}
