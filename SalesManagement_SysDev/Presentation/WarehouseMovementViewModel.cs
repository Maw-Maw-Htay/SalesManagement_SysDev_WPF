using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;

namespace SalesManagement_SysDev;

public sealed class WarehouseMovementRow : ViewModelBase
{
    private bool selectedForConfirmation;
    public WarehouseMovementRecord Data { get; }
    public string Code => Data.Code;
    public string ReferenceCode => Data.ReferenceCode;
    public string MovementDateText => Data.MovementDateText;
    public string PartnerDisplay => Data.PartnerDisplay;
    public string LineCountText => $"{Data.LineCount:N0} 行";
    public string ProductSummary => Data.ProductSearchText.Length == 0
        ? $"{Data.LineCount:N0} 行"
        : $"{Data.ProductSearchText}（{Data.LineCount:N0}行）";
    public string QuantityText => Data.Quantity.ToString("N0");
    public string BusinessStatusLabel => Data.BusinessStatusLabel;
    public string ManagementLabel => Data.ManagementLabel;
    public bool CanSelectInbound => Data.Kind == WarehouseMovementKind.Inbound && WarehousingService.CanConfirm
        && WarehouseMovementRules.ValidateConfirmation(Data.ManagementFlag, Data.BusinessState) == null;
    public bool SelectedForConfirmation { get => selectedForConfirmation; internal set => Set(ref selectedForConfirmation, value); }
    public Brush BusinessStatusBackground => Data.BusinessState switch
    {
        WarehousingService.ConfirmedState => AppTheme.SuccessSurface,
        WarehousingService.UnconfirmedState => AppTheme.WarningSurface,
        _ => AppTheme.DangerSurface
    };
    public Brush BusinessStatusForeground => Data.BusinessState switch
    {
        WarehousingService.ConfirmedState => AppTheme.SuccessText,
        WarehousingService.UnconfirmedState => AppTheme.WarningText,
        _ => AppTheme.DangerText
    };
    public Brush ManagementBackground => Data.ManagementFlag switch
    {
        WarehousingService.ActiveFlag => AppTheme.SuccessSurface,
        WarehousingService.HiddenFlag => AppTheme.Subtle,
        _ => AppTheme.WarningSurface
    };
    public Brush ManagementForeground => Data.ManagementFlag switch
    {
        WarehousingService.ActiveFlag => AppTheme.SuccessText,
        WarehousingService.HiddenFlag => AppTheme.SecondaryText,
        _ => AppTheme.WarningText
    };

    public WarehouseMovementRow(WarehouseMovementRecord data) => Data = data;
}

public sealed class WarehouseMovementViewModel : ViewModelBase, IDisposable
{
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private readonly Func<WarehouseMovementDirectory> loadDirectory;
    private IReadOnlyList<WarehouseMovementRecord> source = [];
    private IReadOnlyList<WarehouseLookupOption> primaryOptions = [];
    private IReadOnlyList<WarehouseLookupOption> employees = [];
    private WarehouseMovementSummary summary = new(0, 0, 0);
    private WarehouseManagementTab activeTab = WarehouseManagementTab.Visible;
    private WarehouseBusinessStatusFilter selectedStatus;
    private string keyword = "", feedback = "", searchError = "";
    private DateTime? startDate, endDate;
    private int primarySelection, employeeSelection, searchRevision;
    private bool filtersExpanded = true, isLoading, isDisposed;
    private WarehouseBusinessStatusFilter tabStatus;
    private IReadOnlyList<WarehouseMovementRecord> filtered = [];
    private int page = 1, pageSize = 20;
    private string outboundCode = "", relatedCode = "", customer = "", product = "", dateError = "";
    private OutboundArrivalStatus? arrivalFilter;
    private bool showingOutboundHistory, showingInboundHistory;
    private string receiptNumber = "", purchaseNumber = "", supplier = "", productCode = "", productName = "";
    private DateTime? purchaseFrom, purchaseTo;
    private WarehouseMovementRow? selectedReceipt;
    private Func<WarehouseMovementRecord, IComparable?>? sortKey;
    private bool descending;

    public WarehouseMovementKind Kind { get; }
    public ObservableCollection<WarehouseMovementRow> Rows { get; } = [];
    public IReadOnlyList<WarehouseLookupOption> PrimaryOptions => [new(0, "すべて"), .. primaryOptions];
    public IReadOnlyList<WarehouseLookupOption> EmployeeOptions => [new(0, "すべて"), .. employees];
    public IReadOnlyList<WarehouseBusinessStatusOption> StatusOptions { get; } =
    [
        new(WarehouseBusinessStatusFilter.All, "すべて"),
        new(WarehouseBusinessStatusFilter.Unconfirmed, "未確定"),
        new(WarehouseBusinessStatusFilter.Confirmed, "確定済")
    ];
    public string Title => Kind == WarehouseMovementKind.Inbound ? "入庫管理" : "出庫管理";
    public string Subtitle => Kind == WarehouseMovementKind.Inbound
        ? "メーカーからの入庫情報を確認し、棚入れを確定します。"
        : "倉庫から営業所への出庫情報を確認・確定します。";
    public string PrimaryFilterLabel => Kind == WarehouseMovementKind.Inbound ? "メーカー" : "出庫先営業所";
    public string KeywordLabel => Kind == WarehouseMovementKind.Inbound
        ? "入庫番号・発注番号・メーカー・商品"
        : "出庫番号・注文番号・営業所・顧客・商品";
    public string ReferenceColumnHeader => Kind == WarehouseMovementKind.Inbound ? "発注番号" : "注文番号";
    public string DateColumnHeader => Kind == WarehouseMovementKind.Inbound ? "入庫日" : "出庫日";
    public string PartnerColumnHeader => Kind == WarehouseMovementKind.Inbound ? "メーカー" : "出庫先営業所";
    public string QuantityColumnHeader => Kind == WarehouseMovementKind.Inbound ? "入庫数量" : "出庫数量";
    public string StatusColumnHeader => Kind == WarehouseMovementKind.Inbound ? "棚入れ状況" : "出庫状況";
    public string SummaryTitle => Kind == WarehouseMovementKind.Inbound ? "入庫件数" : "出庫件数";
    public string SummaryPendingTitle => Kind == WarehouseMovementKind.Inbound ? "未確定" : "未確定";
    public string SummaryConfirmedTitle => Kind == WarehouseMovementKind.Inbound ? "確定済" : "確定済";
    public string SummaryScope => "全期間・表示中データ（許可範囲内）";
    public string Keyword { get => keyword; set { if (Set(ref keyword, value ?? "")) ScheduleSearch(); } }
    public string ReceiptNumber { get => receiptNumber; set { if (Set(ref receiptNumber, value ?? "")) ScheduleSearch(); } }
    public string PurchaseNumber { get => purchaseNumber; set { if (Set(ref purchaseNumber, value ?? "")) ScheduleSearch(); } }
    public string Supplier { get => supplier; set { if (Set(ref supplier, value ?? "")) ScheduleSearch(); } }
    public string ReceiptProductCode { get => productCode; set { if (Set(ref productCode, value ?? "")) ScheduleSearch(); } }
    public string ReceiptProductName { get => productName; set { if (Set(ref productName, value ?? "")) ScheduleSearch(); } }
    public DateTime? PurchaseFrom { get => purchaseFrom; set { if (Set(ref purchaseFrom, value?.Date)) ScheduleSearch(); } }
    public DateTime? PurchaseTo { get => purchaseTo; set { if (Set(ref purchaseTo, value?.Date)) ScheduleSearch(); } }
    public string OutboundCode { get => outboundCode; set { if (Set(ref outboundCode, value ?? "")) ScheduleSearch(); } }
    public string RelatedCode { get => relatedCode; set { if (Set(ref relatedCode, value ?? "")) ScheduleSearch(); } }
    public string Customer { get => customer; set { if (Set(ref customer, value ?? "")) ScheduleSearch(); } }
    public string Product { get => product; set { if (Set(ref product, value ?? "")) ScheduleSearch(); } }
    public int ArrivalSelection
    {
        get => arrivalFilter.HasValue ? (int)arrivalFilter.Value : -1;
        set { if (Set(ref arrivalFilter, value < 0 ? null : (OutboundArrivalStatus?)value)) ScheduleSearch(); }
    }
    public IReadOnlyList<WarehouseLookupOption> ArrivalOptions { get; } =
        [new(-1, "すべて"), new(0, "未連携"), new(1, "入荷未確定"), new(2, "入荷済み"), new(3, "要確認")];
    public DateTime? StartDate { get => startDate; set { if (Set(ref startDate, value?.Date)) ScheduleSearch(); } }
    public DateTime? EndDate { get => endDate; set { if (Set(ref endDate, value?.Date)) ScheduleSearch(); } }
    public int PrimarySelection { get => primarySelection; set { if (Set(ref primarySelection, value)) ScheduleSearch(); } }
    public int EmployeeSelection { get => employeeSelection; set { if (Set(ref employeeSelection, value)) ScheduleSearch(); } }
    public WarehouseBusinessStatusFilter SelectedStatus { get => selectedStatus; set { if (Set(ref selectedStatus, value)) ScheduleSearch(); } }
    public string Feedback { get => feedback; private set => Set(ref feedback, value); }
    public string SearchError { get => searchError; private set { if (Set(ref searchError, value)) NotifyEmpty(); } }
    public WarehouseManagementTab ActiveTab { get => activeTab; private set { if (Set(ref activeTab, value)) NotifyTabCounts(); } }
    public bool IsFiltersExpanded { get => filtersExpanded; set { if (Set(ref filtersExpanded, value)) Notify(nameof(FiltersToggleLabel)); } }
    public string FiltersToggleLabel => IsFiltersExpanded ? "詳細条件を折りたたむ" : "詳細条件を展開";
    public bool IsLoading { get => isLoading; private set { if (Set(ref isLoading, value)) { Notify(nameof(CanReload)); Notify(nameof(CanConfirmReceipt)); NotifyPages(); } } }
    public bool CanReload => !IsLoading;
    public bool CanViewHidden => Kind == WarehouseMovementKind.Inbound
        ? WarehousingService.CanViewHidden : OutboundService.CanViewHidden;
    public bool HasConditions => !string.IsNullOrWhiteSpace(Keyword) || StartDate.HasValue || EndDate.HasValue
        || PrimarySelection > 0 || EmployeeSelection > 0 || SelectedStatus != WarehouseBusinessStatusFilter.All
        || OutboundCode.Length > 0 || RelatedCode.Length > 0 || Customer.Length > 0 || Product.Length > 0
        || arrivalFilter.HasValue || dateError.Length > 0 || InboundAdvancedCount > 0;
    public int InboundAdvancedCount => new[] { ReceiptNumber, PurchaseNumber, Supplier, ReceiptProductCode, ReceiptProductName }
        .Count(x => !string.IsNullOrWhiteSpace(x)) + (StartDate.HasValue ? 1 : 0) + (EndDate.HasValue ? 1 : 0)
        + (PurchaseFrom.HasValue ? 1 : 0) + (PurchaseTo.HasValue ? 1 : 0) + (PrimarySelection > 0 ? 1 : 0)
        + (EmployeeSelection > 0 ? 1 : 0) + (SelectedStatus != WarehouseBusinessStatusFilter.All ? 1 : 0);
    public bool HasInboundAdvanced => InboundAdvancedCount > 0;
    public int TotalCount => source.Count;
    public int VisibleCount => source.Count(x => x.ManagementFlag == WarehousingService.ActiveFlag);
    public int HiddenCount => CanViewHidden ? source.Count(x => x.ManagementFlag == WarehousingService.HiddenFlag) : 0;
    public string CountText => $"{filtered.Count:N0} 件表示 / 全 {CurrentTabCount:N0} 件";
    public int PendingCount => source.Count(x => x.ManagementFlag == OutboundService.ActiveFlag && x.IsUnconfirmed);
    public int ConfirmedCount => source.Count(x => x.ManagementFlag == OutboundService.ActiveFlag && x.IsConfirmed);
    public int ArrivalPendingCount => source.Count(x => x.ManagementFlag == OutboundService.ActiveFlag && x.ArrivalStatus == OutboundArrivalStatus.Pending);
    public bool ShowingOutboundHistory
    {
        get => showingOutboundHistory;
        set { if (Set(ref showingOutboundHistory, value)) { Notify(nameof(ListTitle)); _ = ApplyFiltersAsync(); } }
    }
    public string ListTitle => ShowingOutboundHistory ? "出庫履歴 · 確定済み記録" : "出庫一覧";
    public bool ShowingInboundHistory => showingInboundHistory;
    public string InboundListTitle => ShowingInboundHistory ? "入庫履歴" : "入庫一覧";
    public string InboundHistoryButtonText => ShowingInboundHistory ? "入庫一覧に戻る" : "入庫履歴";
    public WarehouseMovementRow? SelectedReceipt => selectedReceipt;
    public bool CanConfirmReceipt => !IsLoading && !ShowingInboundHistory && selectedReceipt is { CanSelectInbound: true };
    public string ReceiptSelectionText => selectedReceipt == null ? "選択 0 件" : "選択 1 件";
    public IReadOnlyList<int> PageSizes { get; }
    public int PageSize { get => pageSize; set { if (PageSizes.Contains(value) && Set(ref pageSize, value)) SetPage(1); } }
    public int Page => page;
    public int InboundPageSelection { get => Page; set { if (value > 0 && value <= PageCount && value != Page) SetPage(value); } }
    public IReadOnlyList<int> InboundPages { get; private set; } = [1];
    public int PageCount => filtered.Count == 0 ? 1 : (filtered.Count - 1) / PageSize + 1;
    public string PageText => filtered.Count == 0 ? "0 件" : $"{(Page - 1) * PageSize + 1:N0}～{(Page - 1) * PageSize + Rows.Count:N0} 件 / 全 {filtered.Count:N0} 件";
    public bool CanPrevious => !IsLoading && Page > 1;
    public bool CanNext => !IsLoading && Page < PageCount;
    public string AllTabText => $"すべて　{TotalCount:N0}";
    public string VisibleTabText => $"表示中　{VisibleCount:N0}";
    public string HiddenTabText => $"非表示　{HiddenCount:N0}";
    public string SummaryTotalText => $"{summary.Total:N0} 件";
    public string SummaryUnconfirmedText => $"{summary.Unconfirmed:N0} 件";
    public string SummaryConfirmedText => $"{summary.Confirmed:N0} 件";
    public bool IsEmpty => Rows.Count == 0;
    public bool ShowEmptyState => !IsLoading && IsEmpty;
    public string EmptyTitle => string.IsNullOrWhiteSpace(SearchError) ? $"該当する{(Kind == WarehouseMovementKind.Inbound ? "入庫" : "出庫")}がありません" : "検索条件を確認してください";
    public string EmptyDescription => string.IsNullOrWhiteSpace(SearchError) ? "条件を変更するか、再読込してください。" : SearchError;
    private int CurrentTabCount => ShowingOutboundHistory || ShowingInboundHistory ? source.Count(x => x.IsConfirmed)
        : ActiveTab switch
    {
        WarehouseManagementTab.Visible => tabStatus switch
        {
            WarehouseBusinessStatusFilter.Unconfirmed => PendingCount,
            WarehouseBusinessStatusFilter.Confirmed => ConfirmedCount,
            _ => VisibleCount
        },
        WarehouseManagementTab.Hidden => HiddenCount,
        _ => TotalCount
    };

    public WarehouseMovementViewModel(WarehouseMovementKind kind) : this(kind, loadOnCreate: true) { }

    internal WarehouseMovementViewModel(WarehouseMovementKind kind, bool loadOnCreate)
        : this(kind, loadOnCreate, null) { }

    internal WarehouseMovementViewModel(WarehouseMovementKind kind, Func<WarehouseMovementDirectory> loadDirectory)
        : this(kind, true, loadDirectory) { }

    private WarehouseMovementViewModel(WarehouseMovementKind kind, bool loadOnCreate, Func<WarehouseMovementDirectory>? loadDirectory)
    {
        Kind = kind;
        PageSizes = kind == WarehouseMovementKind.Inbound ? [5, 10, 20, 50, 100] : [20, 50, 100];
        this.loadDirectory = loadDirectory ?? (kind == WarehouseMovementKind.Inbound ? WarehousingService.Load : OutboundService.Load);
        debounce.Tick += async (_, _) => { debounce.Stop(); await ApplyFiltersAsync(); };
        if (loadOnCreate) _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (IsLoading || isDisposed) return;
        IsLoading = true;
        Feedback = "一覧を読み込んでいます…";
        try
        {
            var directory = await Task.Run(loadDirectory);
            if (isDisposed) return;
            source = directory.Records;
            primaryOptions = directory.PrimaryOptions;
            employees = directory.Employees;
            summary = directory.Summary;
            Notify(nameof(PrimaryOptions)); Notify(nameof(EmployeeOptions));
            Notify(nameof(SummaryTotalText)); Notify(nameof(SummaryUnconfirmedText));
            Notify(nameof(SummaryConfirmedText)); Notify(nameof(SummaryScope));
            Notify(nameof(HasConditions)); NotifyTabCounts();
            Feedback = source.Count == 0 ? "表示できるデータはありません。" : "";
            await ApplyFiltersAsync();
        }
        catch (UnauthorizedAccessException ex)
        {
            source = []; filtered = []; SetPage(1); Feedback = ex.Message; NotifyTabCounts(); NotifyEmpty();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"{Kind} load failed: {ex}");
            source = []; filtered = []; SetPage(1);
            Feedback = "一覧を読み込めませんでした。LocalDB の起動状態と接続を確認してください。";
            NotifyTabCounts(); NotifyEmpty();
        }
        finally { IsLoading = false; NotifyEmpty(); }
    }

    public void SelectTab(WarehouseManagementTab tab, WarehouseBusinessStatusFilter status = WarehouseBusinessStatusFilter.All)
    {
        if (tab == WarehouseManagementTab.Hidden && !CanViewHidden) tab = WarehouseManagementTab.Visible;
        tabStatus = status;
        ActiveTab = tab;
        _ = ApplyFiltersAsync();
    }

    public void ToggleFilters() => IsFiltersExpanded = !IsFiltersExpanded;

    public void ToggleInboundHistory()
    {
        if (Kind != WarehouseMovementKind.Inbound) return;
        showingInboundHistory = !showingInboundHistory;
        if (ShowingInboundHistory) SelectedStatus = WarehouseBusinessStatusFilter.All;
        Notify(nameof(ShowingInboundHistory)); Notify(nameof(InboundListTitle)); Notify(nameof(InboundHistoryButtonText));
        Notify(nameof(CanConfirmReceipt));
        _ = ApplyFiltersAsync();
    }

    public void SelectReceipt(WarehouseMovementRow? row)
    {
        if (row != null && (ShowingInboundHistory || IsLoading || !row.CanSelectInbound || !Rows.Contains(row))) return;
        var previous = selectedReceipt;
        selectedReceipt = ReferenceEquals(previous, row) ? null : row;
        if (previous != null) previous.SelectedForConfirmation = false;
        if (selectedReceipt != null) selectedReceipt.SelectedForConfirmation = true;
        Notify(nameof(SelectedReceipt)); Notify(nameof(CanConfirmReceipt)); Notify(nameof(ReceiptSelectionText));
    }

    public void ClearConditions()
    {
        debounce.Stop();
        Keyword = ""; StartDate = null; EndDate = null; PrimarySelection = 0; EmployeeSelection = 0;
        SelectedStatus = WarehouseBusinessStatusFilter.All;
        OutboundCode = RelatedCode = Customer = Product = ""; ArrivalSelection = -1; dateError = "";
        ReceiptNumber = PurchaseNumber = Supplier = ReceiptProductCode = ReceiptProductName = "";
        PurchaseFrom = PurchaseTo = null;
        debounce.Stop();
        Notify(nameof(HasConditions)); Notify(nameof(InboundAdvancedCount)); Notify(nameof(HasInboundAdvanced));
        _ = ApplyFiltersAsync();
    }

    private void ScheduleSearch()
    {
        if (isDisposed) return;
        Interlocked.Increment(ref searchRevision);
        Notify(nameof(HasConditions)); Notify(nameof(InboundAdvancedCount)); Notify(nameof(HasInboundAdvanced));
        debounce.Stop();
        debounce.Start();
    }

    private async Task ApplyFiltersAsync()
    {
        debounce.Stop();
        var revision = Interlocked.Increment(ref searchRevision);
        var criteria = new WarehouseMovementSearchCriteria(Keyword, StartDate, EndDate,
            PrimarySelection > 0 ? PrimarySelection : null,
            EmployeeSelection > 0 ? EmployeeSelection : null, SelectedStatus)
        {
            ReceiptNumber = ReceiptNumber, PurchaseNumber = PurchaseNumber, Supplier = Supplier,
            ProductCode = ReceiptProductCode, ProductName = ReceiptProductName, PurchaseFrom = PurchaseFrom, PurchaseTo = PurchaseTo
        };
        SearchError = dateError.Length > 0 ? dateError : (Kind == WarehouseMovementKind.Inbound
            ? WarehouseMovementRules.ValidateReceiptSearch(criteria) : WarehouseMovementRules.ValidateDateRange(criteria)) ?? "";
        if (SearchError.Length > 0)
        {
            if (revision == Volatile.Read(ref searchRevision) && !isDisposed) { filtered = []; SetPage(1); }
            NotifyCounts(); NotifyEmpty();
            return;
        }
        await Task.Yield();
        if (revision != Volatile.Read(ref searchRevision) || isDisposed) return;
        var tab = ShowingOutboundHistory || ShowingInboundHistory ? WarehouseManagementTab.All : ActiveTab;
        var results = source.Where(x => WarehouseMovementRules.Matches(x, criteria, tab, CanViewHidden));
        if (Kind == WarehouseMovementKind.Inbound)
            results = results.Where(x => ShowingInboundHistory ? x.IsConfirmed : MatchesTabStatus(x));
        else
        {
            results = results.Where(x => (ShowingOutboundHistory ? x.IsConfirmed : MatchesTabStatus(x))
                && Contains(x.Code, OutboundCode) && (Contains(x.ReferenceCode, RelatedCode)
                    || Contains($"ORD-{x.ReferenceId:000000}", RelatedCode))
                && Contains(x.PartnerName, Customer) && Contains(x.ProductSearchText, Product)
                && (!arrivalFilter.HasValue || x.ArrivalStatus == arrivalFilter));
        }
        if (ShowingInboundHistory)
            results = results.OrderByDescending(x => x.MovementDate).ThenByDescending(x => x.Id);
        else if (sortKey is { } key)
            results = descending ? results.OrderByDescending(key) : results.OrderBy(key);
        filtered = results.ToArray();
        SetPage(1);
        SearchError = "";
        NotifyCounts(); NotifyEmpty();
    }

    public void SetDateError(string error)
    {
        dateError = error;
        _ = ApplyFiltersAsync();
    }

    public void Sort(Func<WarehouseMovementRecord, IComparable?> key, bool reverse)
    {
        sortKey = key;
        descending = reverse;
        _ = ApplyFiltersAsync();
    }

    private static bool Contains(string value, string filter) => value.Contains(filter.Trim(), StringComparison.CurrentCultureIgnoreCase);
    private bool MatchesTabStatus(WarehouseMovementRecord row) => tabStatus switch
    {
        WarehouseBusinessStatusFilter.Unconfirmed => row.IsUnconfirmed,
        WarehouseBusinessStatusFilter.Confirmed => row.IsConfirmed,
        _ => true
    };

    public void SetPage(int requested)
    {
        page = Math.Clamp(requested, 1, PageCount);
        if (selectedReceipt != null) SelectReceipt(null);
        Rows.Clear();
        foreach (var record in filtered.Skip((page - 1) * PageSize).Take(PageSize)) Rows.Add(new WarehouseMovementRow(record));
        if (Kind == WarehouseMovementKind.Inbound)
        {
            var first = Math.Clamp(Page - 2, 1, Math.Max(1, PageCount - 4));
            var pages = Enumerable.Range(first, Math.Min(5, PageCount)).ToArray();
            if (!InboundPages.SequenceEqual(pages)) { InboundPages = pages; Notify(nameof(InboundPages)); }
            Notify(nameof(InboundPageSelection));
        }
        NotifyPages();
    }

    private void NotifyPages()
    {
        Notify(nameof(Page)); Notify(nameof(PageCount)); Notify(nameof(PageText)); Notify(nameof(CanPrevious)); Notify(nameof(CanNext));
    }

    private void NotifyTabCounts()
    {
        Notify(nameof(TotalCount)); Notify(nameof(VisibleCount)); Notify(nameof(HiddenCount));
        Notify(nameof(PendingCount)); Notify(nameof(ConfirmedCount)); Notify(nameof(ArrivalPendingCount));
        Notify(nameof(AllTabText)); Notify(nameof(VisibleTabText)); Notify(nameof(HiddenTabText));
        Notify(nameof(CountText)); Notify(nameof(CanViewHidden)); NotifyEmpty();
    }
    private void NotifyCounts() { Notify(nameof(CountText)); Notify(nameof(IsEmpty)); }
    private void NotifyEmpty()
    {
        Notify(nameof(EmptyTitle)); Notify(nameof(EmptyDescription)); Notify(nameof(ShowEmptyState));
    }
    public void Dispose() { isDisposed = true; debounce.Stop(); }
}

public sealed record WarehouseBusinessStatusOption(WarehouseBusinessStatusFilter Value, string Name);

public sealed class WarehouseMovementDetailViewModel
{
    private readonly bool readOnly;
    public WarehouseMovementRecord Record { get; }
    public IReadOnlyList<WarehouseMovementLine> Lines { get; }
    public PurchaseOrderDetail? RelatedPurchase { get; }
    public bool HasRelatedPurchase => RelatedPurchase != null;
    public IReadOnlyList<WarehouseReceiptDetailLine> ReceiptLines { get; }
    public IReadOnlyList<WarehouseMovementLine> StockLines { get; }
    public IReadOnlyList<WarehouseDetailField> LeftFields { get; }
    public IReadOnlyList<WarehouseDetailField> RightFields { get; }
    public IReadOnlyList<WarehouseDetailField> PurchaseFields { get; }
    public string ReceiptTotals => $"入庫合計 {Record.Quantity:N0}　／　明細 {Record.LineCount:N0} 件"
        + (RelatedPurchase is { } purchase ? $"　／　関連発注合計 {purchase.Order.OrderedQuantity:N0}" : "");
    public bool CanRestore => !readOnly && IsInbound && WarehousingService.CanRestore && WarehousingService.IsRestorable(Record);
    public bool ShowRestoreRestriction => !readOnly && IsInbound && WarehousingService.CanRestore
        && Record.ManagementFlag == WarehousingService.HiddenFlag && !WarehousingService.IsRestorable(Record);
    public string Title => Record.Kind == WarehouseMovementKind.Inbound ? "入庫詳細" : "出庫詳細";
    public string StateLabel => Record.BusinessStatusLabel;
    public string KindLabel => Record.Kind == WarehouseMovementKind.Inbound ? "入庫情報" : "出庫情報";
    public string ReferenceLabel => Record.Kind == WarehouseMovementKind.Inbound ? "発注番号" : "注文番号";
    public string PartnerLabel => Record.Kind == WarehouseMovementKind.Inbound ? "メーカー" : "顧客";
    public string OfficeLabel => Record.Kind == WarehouseMovementKind.Inbound ? "倉庫" : "出庫先営業所";
    public string OfficeText => Record.Kind == WarehouseMovementKind.Inbound ? "既存データに倉庫項目なし" : Record.OfficeName;
    public string DateLabel => Record.Kind == WarehouseMovementKind.Inbound ? "入庫日" : "出庫日";
    public string EmployeeLabel => Record.Kind == WarehouseMovementKind.Inbound ? "入庫担当者" : "出庫担当者";
    public string ConfirmationEmployeeText => Record.Kind == WarehouseMovementKind.Inbound
        ? "既存DBに確定担当者専用項目なし" : Record.EmployeeName;
    public string QuantityLabel => Record.Kind == WarehouseMovementKind.Inbound ? "入庫数量" : "出庫数量";
    public string QuantityText => Record.Quantity.ToString("N0");
    public string LineCountText => $"{Record.LineCount:N0} 行";
    public string InventoryNote => Record.Kind == WarehouseMovementKind.Inbound
        ? "棚入れ確定時に既存の商品単位在庫を更新します。倉庫別在庫と確定日時を保持する列はありません。"
        : "注文確定時に在庫を減算済みです。出庫確定では再減算せず、未確定の入荷情報だけを作成します。";
    public bool IsInbound => Record.Kind == WarehouseMovementKind.Inbound;
    public bool HasHiddenReason => Record.ManagementFlag == WarehousingService.HiddenFlag
        && !string.IsNullOrWhiteSpace(Record.HiddenReason);
    public string ManagementLabel => Record.ManagementLabel;
    public bool CanConfirm => !readOnly && (IsInbound
        ? WarehousingService.CanConfirm && Record.IsUnconfirmed && Record.ManagementFlag == WarehousingService.ActiveFlag
        : OutboundService.CanConfirm && Record.IsUnconfirmed && Record.ManagementFlag == OutboundService.ActiveFlag);
    public string ConfirmActionLabel => IsInbound ? "入庫確定" : "出庫を確定";
    public bool CanHide => !readOnly && (IsInbound
        ? WarehousingService.CanHide && Record.IsConfirmed && Record.ManagementFlag == WarehousingService.ActiveFlag
        : OutboundService.CanHide && Record.IsConfirmed && Record.ManagementFlag == OutboundService.ActiveFlag);
    public bool CanShowHideRestriction => !readOnly && (IsInbound ? WarehousingService.CanHide : OutboundService.CanHide)
        && Record.ManagementFlag == WarehousingService.ActiveFlag && !Record.IsConfirmed;
    public string SourceSalesOrderCode => $"ORD-{Record.ReferenceId:000000}";
    public bool CanOpenRelatedOrder => !IsInbound && AppSession.CanManageOrders && Record.RelatedOrderId.HasValue;
    public bool CanOpenRelatedArrival => !IsInbound && ArrivalService.CanView && Record.RelatedArrival != null;
    public string RelatedArrivalMessage => !ArrivalService.CanView ? "入荷詳細を参照する権限がありません。"
        : Record.RelatedArrival is { } arrival ? $"{arrival.Code}　{arrival.ArrivalStateLabel}"
        : Record.ArrivalStatus == OutboundArrivalStatus.Review ? "関連する入荷を一意に特定できません。"
        : "関連する入荷データはありません。";
    public IReadOnlyList<OutboundDetailLine> OutboundLines { get; }
    public string HideRestrictionText => CanShowHideRestriction
        ? "未確定のデータは非表示にできません。先に業務確定を行ってください。" : "";
    public Brush StateBackground => Record.BusinessState == WarehousingService.ConfirmedState
        ? AppTheme.SuccessSurface : Record.BusinessState == WarehousingService.UnconfirmedState
            ? AppTheme.WarningSurface : AppTheme.DangerSurface;
    public Brush StateForeground => Record.BusinessState == WarehousingService.ConfirmedState
        ? AppTheme.SuccessText : Record.BusinessState == WarehousingService.UnconfirmedState
            ? AppTheme.WarningText : AppTheme.DangerText;

    public WarehouseMovementDetailViewModel(WarehouseMovementDetail detail, bool readOnly = false)
    {
        this.readOnly = readOnly;
        Record = detail.Record;
        Lines = detail.Lines;
        RelatedPurchase = IsInbound ? detail.RelatedPurchase : null;
        OutboundLines = IsInbound ? [] : Lines.Select((line, index) => new OutboundDetailLine(index + 1, line)).ToArray();
        var ordered = RelatedPurchase?.Lines.ToDictionary(x => x.ProductId, x => x.OrderedQuantity);
        var distinctProducts = Lines.GroupBy(x => x.ProductId).ToDictionary(x => x.Key, x => x.Count());
        ReceiptLines = Lines.Select((line, index) => new WarehouseReceiptDetailLine(index + 1, line,
            distinctProducts[line.ProductId] == 1 && ordered?.TryGetValue(line.ProductId, out var quantity) == true && quantity > 0
                ? quantity : null)).ToArray();
        StockLines = IsInbound ? Lines.DistinctBy(x => x.ProductId).ToArray() : [];
        LeftFields = [new(IsInbound ? "入庫番号" : "出庫番号", Record.Code), new(ReferenceLabel, Record.ReferenceCode),
            new(IsInbound ? "仕入先名" : PartnerLabel, Record.PartnerName), new(DateLabel, Record.MovementDateText)];
        var right = new List<WarehouseDetailField> { new(EmployeeLabel, Record.EmployeeName), new("管理状態", ManagementLabel) };
        if (Record.PurchaseDate is { } purchaseDate) right.Add(new("発注日", purchaseDate.ToString("yyyy/MM/dd")));
        if (!IsInbound) right.Add(new(OfficeLabel, OfficeText));
        if (HasHiddenReason) right.Add(new("非表示理由", Record.HiddenReason!));
        RightFields = right;
        PurchaseFields = RelatedPurchase is { } related
            ? [new("発注番号", related.Order.Code), new("仕入先名", related.Order.MakerName),
                new("発注日", related.Order.OrderDate.ToString("yyyy/MM/dd")), new("発注担当者", related.Order.EmployeeName),
                new("発注担当者の現所属", string.IsNullOrWhiteSpace(related.OfficeName) ? "未記録" : related.OfficeName),
                new("入庫状況", related.Order.ProgressLabel), new("発注の管理状態", related.Order.ManagementLabel)]
            : [];
    }
}

public sealed record WarehouseDetailField(string Label, string Value);
public sealed record WarehouseReceiptDetailLine(int Number, WarehouseMovementLine Line, int? OrderedQuantity)
{
    public string OrderedText => OrderedQuantity?.ToString("N0") ?? "—";
    public string DifferenceText => OrderedQuantity is { } quantity ? ((long)Line.Quantity - quantity).ToString("+#,##0;-#,##0;0") : "—";
}

public sealed record OutboundDetailLine(int Number, WarehouseMovementLine Line);

public sealed class WarehouseMovementConfirmViewModel : ViewModelBase
{
    private bool isProcessing;
    public WarehouseMovementRecord Record { get; }
    public IReadOnlyList<WarehouseMovementLine> Lines { get; }
    public bool IsInbound => Record.Kind == WarehouseMovementKind.Inbound;
    public string Title => IsInbound ? "入庫を確定しますか？" : "出庫確定の確認";
    public string ActionLabel => IsInbound ? "入庫を確定" : "出庫を確定";
    public string ConfirmationPrompt => IsInbound
        ? "以下の入庫内容を確認し、確定します。よろしいですか？"
        : "以下の出庫データを確定します。よろしいですか？";
    public string ImpactText => IsInbound
        ? "入庫確定後、在庫数量が更新されます。確定した入庫は再度確定できません。"
        : "出庫確定後、未確定の入荷データを作成します。\n在庫数量は注文確定時に減算済みです。この操作で再度減算することはありません。";
    public string TargetText => IsInbound
        ? $"入庫番号：{Record.Code}\n関連発注番号：{Record.ReferenceCode}\n仕入先名：{Record.PartnerName}\n入庫数量合計：{Record.Quantity:N0}（{Record.LineCount:N0} 明細）"
        : $"{Record.Code}　{Record.ReferenceCode}\n{Record.PartnerName}　{Record.OfficeName}　数量 {Record.Quantity:N0}　明細 {Record.LineCount:N0} 行";
    public IReadOnlyList<WarehouseMovementLineInput> ExpectedLines { get; }
    public string LineError => IsInbound ? WarehouseMovementRules.ValidateLines(ExpectedLines) ?? "" : "";
    public bool IsProcessing { get => isProcessing; set { if (Set(ref isProcessing, value)) { Notify(nameof(CanConfirm)); Notify(nameof(ProgressText)); } } }
    public bool CanConfirm => !IsProcessing && (IsInbound ? WarehousingService.CanConfirm : OutboundService.CanConfirm)
        && WarehouseMovementRules.ValidateConfirmation(Record.ManagementFlag, Record.BusinessState) == null && LineError.Length == 0;
    public string ProgressText => IsProcessing ? "データを確認して保存しています…" : "";

    public WarehouseMovementConfirmViewModel(WarehouseMovementDetail detail)
    {
        Record = detail.Record;
        Lines = detail.Lines;
        ExpectedLines = Lines.Select(line => new WarehouseMovementLineInput(line.ProductId, line.Quantity)).ToArray();
    }
}

public sealed class WarehouseMovementHideViewModel : ViewModelBase
{
    private string reason = "";
    private bool isProcessing;
    public WarehouseMovementRecord Record { get; }
    public IReadOnlyList<WarehouseMovementLine> Lines { get; }
    public string Title => Record.Kind == WarehouseMovementKind.Inbound ? "入庫を非表示にしますか？" : "出庫を非表示にする";
    public string Reason { get => reason; set { if (Set(ref reason, value ?? "")) { Notify(nameof(CanConfirm)); Notify(nameof(ValidationMessage)); } } }
    public string TargetText => Record.Kind == WarehouseMovementKind.Inbound
        ? $"入庫番号：{Record.Code}\n関連発注番号：{Record.ReferenceCode}\n入庫状態：{Record.BusinessStatusLabel}　管理状態：{Record.ManagementLabel}"
        : $"{Record.Code}　{Record.ReferenceCode}\n{Record.PartnerName}　{Record.OfficeName}　{Record.BusinessStatusLabel}　数量 {Record.Quantity:N0}";
    public bool IsProcessing { get => isProcessing; set { if (Set(ref isProcessing, value)) { Notify(nameof(CanConfirm)); Notify(nameof(CanEditReason)); Notify(nameof(ProgressText)); } } }
    public bool CanEditReason => !IsProcessing;
    public string ProgressText => IsProcessing ? "非表示理由を保存しています…" : "";
    public bool CanConfirm => !IsProcessing && (Record.Kind == WarehouseMovementKind.Inbound
        ? WarehousingService.ValidateHideReason(Reason) == null
        : OutboundService.ValidateHideReason(Reason) == null);
    public string ValidationMessage => Record.Kind == WarehouseMovementKind.Inbound
        ? WarehousingService.ValidateHideReason(Reason) ?? ""
        : OutboundService.ValidateHideReason(Reason) ?? "";
    public string VisibilityWarning => Record.Kind == WarehouseMovementKind.Inbound
        ? "通常の入庫一覧には表示されなくなります。在庫数量と入庫の確定状態は変わりません。"
        : "在庫数量、注文、発注、入荷情報は変更しません。";

    public WarehouseMovementHideViewModel(WarehouseMovementRecord record, IReadOnlyList<WarehouseMovementLine> lines)
    {
        Record = record;
        Lines = lines;
    }
}
