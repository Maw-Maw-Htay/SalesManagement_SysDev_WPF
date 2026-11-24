using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;

namespace SalesManagement_SysDev;

public sealed class ArrivalRow : ViewModelBase
{
    public ArrivalRecord Data { get; }
    public string Code => Data.Code;
    public string OrderCode => Data.OrderCode;
    public string ArrivalDateText => Data.ArrivalDateText;
    public string CustomerName => Data.CustomerName;
    public string OfficeName => Data.OfficeName;
    public string ProductSummary => $"{Data.LineCount:N0} 行";
    public string QuantityText => Data.Quantity.ToString("N0");
    public string ArrivalStateLabel => Data.ArrivalStateLabel;
    public string ManagementLabel => Data.ManagementLabel;
    public Brush ArrivalStateBackground => Data.ArrivalState switch
    {
        ArrivalService.ConfirmedState => AppTheme.SuccessSurface,
        ArrivalService.UnconfirmedState => AppTheme.WarningSurface,
        _ => AppTheme.DangerSurface
    };
    public Brush ArrivalStateForeground => Data.ArrivalState switch
    {
        ArrivalService.ConfirmedState => AppTheme.SuccessText,
        ArrivalService.UnconfirmedState => AppTheme.WarningText,
        _ => AppTheme.DangerText
    };
    public Brush ManagementBackground => Data.ManagementFlag switch
    {
        ArrivalService.ActiveFlag => AppTheme.SuccessSurface,
        ArrivalService.HiddenFlag => AppTheme.Subtle,
        _ => AppTheme.WarningSurface
    };
    public Brush ManagementForeground => Data.ManagementFlag switch
    {
        ArrivalService.ActiveFlag => AppTheme.SuccessText,
        ArrivalService.HiddenFlag => AppTheme.SecondaryText,
        _ => AppTheme.WarningText
    };

    public ArrivalRow(ArrivalRecord data) => Data = data;
}

public sealed class ArrivalManagementViewModel : ViewModelBase, IDisposable
{
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private IReadOnlyList<ArrivalRecord> source = [];
    private IReadOnlyList<ArrivalOfficeOption> offices = [];
    private IReadOnlyList<ArrivalEmployeeOption> employees = [];
    private ArrivalSummary summary = new(0, 0, 0);
    private ArrivalManagementTab activeTab = ArrivalManagementTab.Visible;
    private ArrivalStateFilter selectedState;
    private string keyword = "", feedback = "", searchError = "";
    private DateTime? startDate, endDate;
    private int officeSelection, employeeSelection, searchRevision;
    private bool filtersExpanded = true, isLoading, isDisposed;

    public ObservableCollection<ArrivalRow> Rows { get; } = [];
    public IReadOnlyList<ArrivalOfficeOption> OfficeOptions => [new(0, "すべて"), .. offices];
    public IReadOnlyList<ArrivalEmployeeOption> EmployeeOptions => [new(0, "すべて"), .. employees];
    public IReadOnlyList<ArrivalStateFilterOption> StateOptions { get; } =
    [
        new(ArrivalStateFilter.All, "すべて"),
        new(ArrivalStateFilter.Unconfirmed, "未確定"),
        new(ArrivalStateFilter.Confirmed, "確定済")
    ];

    public string Keyword { get => keyword; set { if (Set(ref keyword, value ?? "")) ScheduleSearch(); } }
    public DateTime? StartDate { get => startDate; set { if (Set(ref startDate, value?.Date)) ScheduleSearch(); } }
    public DateTime? EndDate { get => endDate; set { if (Set(ref endDate, value?.Date)) ScheduleSearch(); } }
    public int OfficeSelection { get => officeSelection; set { if (Set(ref officeSelection, value)) ScheduleSearch(); } }
    public int EmployeeSelection { get => employeeSelection; set { if (Set(ref employeeSelection, value)) ScheduleSearch(); } }
    public ArrivalStateFilter SelectedState { get => selectedState; set { if (Set(ref selectedState, value)) ScheduleSearch(); } }
    public string Feedback { get => feedback; private set => Set(ref feedback, value); }
    public string SearchError { get => searchError; private set { if (Set(ref searchError, value)) NotifyEmptyState(); } }
    public bool ShowSearchError => !string.IsNullOrWhiteSpace(SearchError);
    public ArrivalManagementTab ActiveTab { get => activeTab; private set { if (Set(ref activeTab, value)) NotifyTabCounts(); } }
    public bool IsFiltersExpanded { get => filtersExpanded; set { if (Set(ref filtersExpanded, value)) Notify(nameof(FiltersToggleLabel)); } }
    public string FiltersToggleLabel => IsFiltersExpanded ? "詳細条件を折りたたむ" : "詳細条件を展開";
    public bool IsLoading { get => isLoading; private set { if (Set(ref isLoading, value)) NotifyActions(); } }
    public bool CanReload => !IsLoading;
    public bool CanViewHidden => ArrivalService.CanViewHidden;
    public int TotalCount => source.Count;
    public int VisibleCount => source.Count(x => x.ManagementFlag == ArrivalService.ActiveFlag);
    public int HiddenCount => CanViewHidden ? source.Count(x => x.ManagementFlag == ArrivalService.HiddenFlag) : 0;
    public string CountText => $"{Rows.Count:N0} 件表示 / 全 {CurrentTabCount:N0} 件";
    public string AllTabText => $"すべて　{TotalCount:N0}";
    public string VisibleTabText => $"表示中　{VisibleCount:N0}";
    public string HiddenTabText => $"非表示　{HiddenCount:N0}";
    public string SummaryTotalText => $"{summary.Total:N0} 件";
    public string SummaryUnconfirmedText => $"{summary.Unconfirmed:N0} 件";
    public string SummaryConfirmedText => $"{summary.Confirmed:N0} 件";
    public string SummaryScope => "全期間・表示中入荷（許可された営業所の範囲）";
    public string EmptyTitle => ShowSearchError ? "検索条件を確認してください" : "入荷データがありません";
    public string EmptyDescription => ShowSearchError ? SearchError : "検索条件を変更するか、条件をクリアしてください。";
    public bool IsEmpty => Rows.Count == 0;
    public bool ShowEmptyState => !IsLoading && IsEmpty;
    public bool HasConditions => !string.IsNullOrWhiteSpace(Keyword) || StartDate.HasValue || EndDate.HasValue
        || OfficeSelection > 0 || EmployeeSelection > 0 || SelectedState != ArrivalStateFilter.All;
    private int CurrentTabCount => ActiveTab switch
    {
        ArrivalManagementTab.Visible => VisibleCount,
        ArrivalManagementTab.Hidden => HiddenCount,
        _ => TotalCount
    };

    public ArrivalManagementViewModel()
    {
        debounce.Tick += async (_, _) => { debounce.Stop(); await ApplyFiltersAsync(); };
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (IsLoading || isDisposed) return;
        IsLoading = true;
        Feedback = "入荷一覧を読み込んでいます…";
        try
        {
            var directory = await Task.Run(ArrivalService.Load);
            if (isDisposed) return;
            source = directory.Arrivals;
            offices = directory.Offices;
            employees = directory.Employees;
            summary = directory.Summary;
            Notify(nameof(OfficeOptions)); Notify(nameof(EmployeeOptions));
            Notify(nameof(SummaryTotalText)); Notify(nameof(SummaryUnconfirmedText));
            Notify(nameof(SummaryConfirmedText)); Notify(nameof(SummaryScope));
            NotifyTabCounts();
            Feedback = source.Count == 0 ? "表示できる入荷はありません。" : "";
            await ApplyFiltersAsync();
        }
        catch (UnauthorizedAccessException ex)
        {
            source = []; Rows.Clear(); Feedback = ex.Message; NotifyTabCounts(); NotifyEmptyState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Arrival load failed: {ex}");
            source = []; Rows.Clear();
            Feedback = "入荷一覧を読み込めませんでした。LocalDB の起動状態と接続を確認してください。";
            NotifyTabCounts(); NotifyEmptyState();
        }
        finally { IsLoading = false; NotifyEmptyState(); }
    }

    public void SelectTab(ArrivalManagementTab tab)
    {
        if (tab == ArrivalManagementTab.Hidden && !CanViewHidden) tab = ArrivalManagementTab.Visible;
        ActiveTab = tab;
        _ = ApplyFiltersAsync();
    }

    public void ToggleFilters() => IsFiltersExpanded = !IsFiltersExpanded;

    public void ClearConditions()
    {
        Keyword = ""; StartDate = null; EndDate = null; OfficeSelection = 0; EmployeeSelection = 0;
        SelectedState = ArrivalStateFilter.All;
        debounce.Stop();
        _ = ApplyFiltersAsync();
    }

    private void ScheduleSearch()
    {
        if (isDisposed) return;
        Notify(nameof(HasConditions));
        debounce.Stop();
        debounce.Start();
    }

    private async Task ApplyFiltersAsync()
    {
        var revision = Interlocked.Increment(ref searchRevision);
        var criteria = new ArrivalSearchCriteria(Keyword, StartDate, EndDate,
            OfficeSelection > 0 ? OfficeSelection : null,
            EmployeeSelection > 0 ? EmployeeSelection : null, SelectedState);
        SearchError = ArrivalFilter.Validate(criteria) ?? "";
        if (SearchError.Length > 0)
        {
            if (revision == Volatile.Read(ref searchRevision) && !isDisposed) Rows.Clear();
            NotifyCounts(); NotifyEmptyState();
            return;
        }

        // Filtering is applied to a single, branch-scoped database snapshot. Every value is text/input data,
        // not dynamically generated SQL, and the debounce prevents an update for every keystroke.
        await Task.Yield();
        if (revision != Volatile.Read(ref searchRevision) || isDisposed) return;
        var filtered = source.Where(x => ArrivalFilter.Matches(x, criteria, ActiveTab, CanViewHidden)).ToArray();
        Rows.Clear();
        foreach (var arrival in filtered) Rows.Add(new ArrivalRow(arrival));
        SearchError = "";
        NotifyCounts(); NotifyEmptyState();
    }

    private void NotifyTabCounts()
    {
        Notify(nameof(TotalCount)); Notify(nameof(VisibleCount)); Notify(nameof(HiddenCount));
        Notify(nameof(AllTabText)); Notify(nameof(VisibleTabText)); Notify(nameof(HiddenTabText));
        Notify(nameof(CountText)); Notify(nameof(CanViewHidden)); NotifyEmptyState();
    }

    private void NotifyCounts() { Notify(nameof(CountText)); Notify(nameof(IsEmpty)); }
    private void NotifyEmptyState()
    {
        Notify(nameof(ShowSearchError)); Notify(nameof(EmptyTitle)); Notify(nameof(EmptyDescription)); Notify(nameof(ShowEmptyState));
    }
    private void NotifyActions() => Notify(nameof(CanReload));

    public void Dispose()
    {
        isDisposed = true;
        debounce.Stop();
    }
}

public sealed record ArrivalStateFilterOption(ArrivalStateFilter Value, string Name);

public sealed class ArrivalDetailViewModel
{
    private readonly bool readOnly;
    public ArrivalRecord Arrival { get; }
    public IReadOnlyList<ArrivalLineRecord> Lines { get; }
    public bool CanConfirm => !readOnly && ArrivalService.CanConfirm && Arrival.IsUnconfirmed;
    public bool CanHide => !readOnly && ArrivalService.CanHide && (Arrival.IsConfirmed || Arrival.IsUnconfirmed)
        && Arrival.ManagementFlag == ArrivalService.ActiveFlag;
    public bool HasHiddenReason => Arrival.ManagementFlag == ArrivalService.HiddenFlag
        && !string.IsNullOrWhiteSpace(Arrival.HiddenReason);
    public string QuantityText => Arrival.Quantity.ToString("N0");
    public string LineCountText => $"{Arrival.LineCount:N0} 行";
    public string ArrivalStateLabel => Arrival.ArrivalStateLabel;
    public string ManagementLabel => Arrival.ManagementLabel;
    public Brush ArrivalStateBackground => Arrival.ArrivalState switch
    {
        ArrivalService.ConfirmedState => AppTheme.SuccessSurface,
        ArrivalService.UnconfirmedState => AppTheme.WarningSurface,
        _ => AppTheme.DangerSurface
    };
    public Brush ArrivalStateForeground => Arrival.ArrivalState switch
    {
        ArrivalService.ConfirmedState => AppTheme.SuccessText,
        ArrivalService.UnconfirmedState => AppTheme.WarningText,
        _ => AppTheme.DangerText
    };

    public ArrivalDetailViewModel(ArrivalDetail detail, bool readOnly = false)
    {
        this.readOnly = readOnly;
        Arrival = detail.Arrival;
        Lines = detail.Lines;
    }
}

public sealed class ArrivalHideDialogViewModel : ViewModelBase
{
    private string reason = "";
    public ArrivalRecord Arrival { get; }
    public string Reason { get => reason; set { if (Set(ref reason, value ?? "")) { Notify(nameof(CanConfirm)); Notify(nameof(ValidationMessage)); } } }
    public string TargetText => $"{Arrival.Code}　注文 {Arrival.OrderCode}\n{Arrival.OfficeName}　{Arrival.CustomerName}　入荷数量 {Arrival.Quantity:N0}";
    public bool CanConfirm => ArrivalService.ValidateHideReason(Reason) == null;
    public string ValidationMessage => ArrivalService.ValidateHideReason(Reason) ?? "";

    public ArrivalHideDialogViewModel(ArrivalRecord arrival) => Arrival = arrival;
}

public sealed class ArrivalConfirmDialogViewModel : ViewModelBase
{
    private bool isProcessing;
    public ArrivalRecord Arrival { get; }
    public string QuantityText => Arrival.Quantity.ToString("N0");
    public string LineCountText => $"{Arrival.LineCount:N0} 行";
    public bool IsProcessing { get => isProcessing; set { if (Set(ref isProcessing, value)) { Notify(nameof(CanConfirm)); Notify(nameof(ProgressText)); } } }
    public bool CanConfirm => !IsProcessing;
    public string ProgressText => IsProcessing ? "入荷と出荷情報を保存しています…" : "";

    public ArrivalConfirmDialogViewModel(ArrivalRecord arrival) => Arrival = arrival;
}
