using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;

namespace SalesManagement_SysDev;

public sealed class ShipmentRow : ViewModelBase
{
    public ShipmentRecord Data { get; }
    public string Code => Data.Code;
    public string OrderCode => Data.OrderCode;
    public string ConfirmedDateText => Data.ConfirmedDateText;
    public string CustomerName => Data.CustomerName;
    public string OfficeName => Data.OfficeName;
    public string ProductSummary => $"{Data.LineCount:N0} 行";
    public string QuantityText => Data.Quantity.ToString("N0");
    public string StateLabel => Data.StateLabel;
    public string ManagementLabel => Data.ManagementLabel;
    public Brush StateBackground => Data.State switch
    {
        ShipmentService.ConfirmedState => AppTheme.SuccessSurface,
        ShipmentService.UnconfirmedState => AppTheme.WarningSurface,
        _ => AppTheme.DangerSurface
    };
    public Brush StateForeground => Data.State switch
    {
        ShipmentService.ConfirmedState => AppTheme.SuccessText,
        ShipmentService.UnconfirmedState => AppTheme.WarningText,
        _ => AppTheme.DangerText
    };
    public Brush ManagementBackground => Data.ManagementFlag switch
    {
        ShipmentService.ActiveFlag => AppTheme.SuccessSurface,
        ShipmentService.HiddenFlag => AppTheme.Subtle,
        _ => AppTheme.WarningSurface
    };
    public Brush ManagementForeground => Data.ManagementFlag switch
    {
        ShipmentService.ActiveFlag => AppTheme.SuccessText,
        ShipmentService.HiddenFlag => AppTheme.SecondaryText,
        _ => AppTheme.WarningText
    };

    public ShipmentRow(ShipmentRecord data) => Data = data;
}

public sealed record ShipmentStateFilterOption(ShipmentStateFilter Value, string Name);

public sealed class ShipmentManagementViewModel : ViewModelBase, IDisposable
{
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private IReadOnlyList<ShipmentRecord> source = [];
    private ShipmentSummary summary = new(0, 0, 0);
    private ShipmentManagementTab activeTab = ShipmentManagementTab.Visible;
    private ShipmentStateFilter selectedState;
    private string keyword = "", feedback = "", searchError = "";
    private DateTime? startDate, endDate;
    private int searchRevision;
    private bool filtersExpanded = true, isLoading, isDisposed;

    public ObservableCollection<ShipmentRow> Rows { get; } = [];
    public IReadOnlyList<ShipmentStateFilterOption> StateOptions { get; } =
    [
        new(ShipmentStateFilter.All, "すべて"),
        new(ShipmentStateFilter.Unconfirmed, "未確定"),
        new(ShipmentStateFilter.Confirmed, "確定済")
    ];
    public string Keyword { get => keyword; set { if (Set(ref keyword, value ?? "")) ScheduleSearch(); } }
    public DateTime? StartDate { get => startDate; set { if (Set(ref startDate, value?.Date)) ScheduleSearch(); } }
    public DateTime? EndDate { get => endDate; set { if (Set(ref endDate, value?.Date)) ScheduleSearch(); } }
    public ShipmentStateFilter SelectedState { get => selectedState; set { if (Set(ref selectedState, value)) ScheduleSearch(); } }
    public string Feedback { get => feedback; private set => Set(ref feedback, value); }
    public string SearchError { get => searchError; private set { if (Set(ref searchError, value)) NotifyEmptyState(); } }
    public bool ShowSearchError => !string.IsNullOrWhiteSpace(SearchError);
    public ShipmentManagementTab ActiveTab { get => activeTab; private set { if (Set(ref activeTab, value)) NotifyTabCounts(); } }
    public bool IsFiltersExpanded { get => filtersExpanded; set { if (Set(ref filtersExpanded, value)) Notify(nameof(FiltersToggleLabel)); } }
    public string FiltersToggleLabel => IsFiltersExpanded ? "詳細条件を折りたたむ" : "詳細条件を展開";
    public bool IsLoading { get => isLoading; private set { if (Set(ref isLoading, value)) Notify(nameof(CanReload)); } }
    public bool CanReload => !IsLoading;
    public bool CanViewHidden => ShipmentService.CanViewHidden;
    public int TotalCount => source.Count;
    public int VisibleCount => source.Count(x => x.ManagementFlag == ShipmentService.ActiveFlag);
    public int HiddenCount => CanViewHidden ? source.Count(x => x.ManagementFlag == ShipmentService.HiddenFlag) : 0;
    public string CountText => $"{Rows.Count:N0} 件表示 / 全 {CurrentTabCount:N0} 件";
    public string AllTabText => $"すべて　{TotalCount:N0}";
    public string VisibleTabText => $"表示中　{VisibleCount:N0}";
    public string HiddenTabText => $"非表示　{HiddenCount:N0}";
    public string SummaryTotalText => $"{summary.Total:N0} 件";
    public string SummaryUnconfirmedText => $"{summary.Unconfirmed:N0} 件";
    public string SummaryConfirmedText => $"{summary.Confirmed:N0} 件";
    public string SummaryScope => "全期間・表示中出荷（許可された営業所の範囲）";
    public string EmptyTitle => ShowSearchError ? "検索条件を確認してください" : "出荷データがありません";
    public string EmptyDescription => ShowSearchError ? SearchError : "検索条件を変更するか、条件をクリアしてください。";
    public bool IsEmpty => Rows.Count == 0;
    public bool ShowEmptyState => !IsLoading && IsEmpty;
    public bool HasConditions => !string.IsNullOrWhiteSpace(Keyword) || StartDate.HasValue || EndDate.HasValue
        || SelectedState != ShipmentStateFilter.All;
    private int CurrentTabCount => ActiveTab switch
    {
        ShipmentManagementTab.Visible => VisibleCount,
        ShipmentManagementTab.Hidden => HiddenCount,
        _ => TotalCount
    };

    public ShipmentManagementViewModel()
    {
        debounce.Tick += async (_, _) => { debounce.Stop(); await ApplyFiltersAsync(); };
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (IsLoading || isDisposed) return;
        IsLoading = true;
        Feedback = "出荷一覧を読み込んでいます…";
        try
        {
            var directory = await Task.Run(ShipmentService.Load);
            if (isDisposed) return;
            source = directory.Shipments;
            summary = directory.Summary;
            Notify(nameof(SummaryTotalText)); Notify(nameof(SummaryUnconfirmedText));
            Notify(nameof(SummaryConfirmedText)); Notify(nameof(SummaryScope));
            NotifyTabCounts();
            Feedback = source.Count == 0 ? "表示できる出荷はありません。" : "";
            await ApplyFiltersAsync();
        }
        catch (UnauthorizedAccessException ex)
        {
            source = []; Rows.Clear(); Feedback = ex.Message; NotifyTabCounts(); NotifyEmptyState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Shipment load failed: {ex}");
            source = []; Rows.Clear();
            Feedback = "出荷一覧を読み込めませんでした。LocalDB の起動状態と接続を確認してください。";
            NotifyTabCounts(); NotifyEmptyState();
        }
        finally { IsLoading = false; NotifyEmptyState(); }
    }

    public void SelectTab(ShipmentManagementTab tab)
    {
        if (tab == ShipmentManagementTab.Hidden && !CanViewHidden) tab = ShipmentManagementTab.Visible;
        ActiveTab = tab;
        _ = ApplyFiltersAsync();
    }

    public void ToggleFilters() => IsFiltersExpanded = !IsFiltersExpanded;

    public void ClearConditions()
    {
        Keyword = ""; StartDate = null; EndDate = null; SelectedState = ShipmentStateFilter.All;
        debounce.Stop();
        _ = ApplyFiltersAsync();
    }

    private void ScheduleSearch()
    {
        if (isDisposed) return;
        Notify(nameof(HasConditions));
        debounce.Stop(); debounce.Start();
    }

    private async Task ApplyFiltersAsync()
    {
        var revision = Interlocked.Increment(ref searchRevision);
        var criteria = new ShipmentSearchCriteria(Keyword, StartDate, EndDate, SelectedState);
        SearchError = ShipmentFilter.Validate(criteria) ?? "";
        if (SearchError.Length > 0)
        {
            if (revision == Volatile.Read(ref searchRevision) && !isDisposed) Rows.Clear();
            NotifyCounts(); NotifyEmptyState(); return;
        }
        await Task.Yield();
        if (revision != Volatile.Read(ref searchRevision) || isDisposed) return;
        var filtered = source.Where(x => ShipmentFilter.Matches(x, criteria, ActiveTab, CanViewHidden)).ToArray();
        Rows.Clear();
        foreach (var shipment in filtered) Rows.Add(new ShipmentRow(shipment));
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
    public void Dispose() { isDisposed = true; debounce.Stop(); }
}

public sealed class ShipmentDetailViewModel
{
    public ShipmentRecord Shipment { get; }
    public IReadOnlyList<ShipmentLineRecord> Lines { get; }
    public bool CanConfirm => ShipmentService.CanConfirm && Shipment.IsUnconfirmed
        && Shipment.ManagementFlag == ShipmentService.ActiveFlag;
    public bool CanHide => ShipmentService.CanHide && (Shipment.IsConfirmed || Shipment.IsUnconfirmed)
        && Shipment.ManagementFlag == ShipmentService.ActiveFlag;
    public bool ShowHideAction => ShipmentService.CanHide && Shipment.ManagementFlag == ShipmentService.ActiveFlag;
    public string HideUnavailableReason => Shipment.IsConfirmed || Shipment.IsUnconfirmed ? "" : "業務状態を確認してください。";
    public bool HasHiddenReason => Shipment.ManagementFlag == ShipmentService.HiddenFlag
        && !string.IsNullOrWhiteSpace(Shipment.HiddenReason);
    public string QuantityText => Shipment.Quantity.ToString("N0");
    public string LineCountText => $"{Shipment.LineCount:N0} 行";
    public string StateLabel => Shipment.StateLabel;
    public string ManagementLabel => Shipment.ManagementLabel;
    public string ArrivalReferenceNote => "入荷IDは出荷テーブルに保存されていません。注文・顧客・営業所と商品数量での一意照合が確定条件です。";
    public Brush StateBackground => Shipment.State switch
    {
        ShipmentService.ConfirmedState => AppTheme.SuccessSurface,
        ShipmentService.UnconfirmedState => AppTheme.WarningSurface,
        _ => AppTheme.DangerSurface
    };
    public Brush StateForeground => Shipment.State switch
    {
        ShipmentService.ConfirmedState => AppTheme.SuccessText,
        ShipmentService.UnconfirmedState => AppTheme.WarningText,
        _ => AppTheme.DangerText
    };

    public ShipmentDetailViewModel(ShipmentDetail detail) { Shipment = detail.Shipment; Lines = detail.Lines; }
}

public sealed class ShipmentConfirmDialogViewModel : ViewModelBase
{
    private bool isProcessing;
    public ShipmentRecord Shipment { get; }
    public string QuantityText => Shipment.Quantity.ToString("N0");
    public string LineCountText => $"{Shipment.LineCount:N0} 行";
    public bool IsProcessing { get => isProcessing; set { if (Set(ref isProcessing, value)) { Notify(nameof(CanConfirm)); Notify(nameof(ProgressText)); } } }
    public bool CanConfirm => !IsProcessing;
    public string ProgressText => IsProcessing ? "出荷状態と売上連携を保存しています…" : "";
    public ShipmentConfirmDialogViewModel(ShipmentRecord shipment) => Shipment = shipment;
}

public sealed class ShipmentHideDialogViewModel : ViewModelBase
{
    private string reason = "";
    public ShipmentRecord Shipment { get; }
    public string Reason { get => reason; set { if (Set(ref reason, value ?? "")) { Notify(nameof(CanConfirm)); Notify(nameof(ValidationMessage)); } } }
    public string TargetText => $"{Shipment.Code}　注文 {Shipment.OrderCode}\n{Shipment.OfficeName}　{Shipment.CustomerName}　出荷数量 {Shipment.Quantity:N0}";
    public bool CanConfirm => ShipmentOperationRules.ValidateHideReason(Reason) == null;
    public string ValidationMessage => ShipmentOperationRules.ValidateHideReason(Reason) ?? "";
    public ShipmentHideDialogViewModel(ShipmentRecord shipment) => Shipment = shipment;
}
