using System.Collections.ObjectModel;
using System.Globalization;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Threading;

namespace SalesManagement_SysDev;

public sealed class PurchaseOrderRow : ViewModelBase
{
    private bool isSelected;
    public PurchaseOrderRecord Data { get; }
    public string Code => Data.Code;
    public string OrderDateText => Data.OrderDate.ToString("yyyy/MM/dd");
    public string MakerName => Data.MakerName;
    public string EmployeeName => Data.EmployeeName;
    public string ProductLineCountText => $"{Data.ProductLineCount:N0} 行";
    public string OrderedQuantityText => Data.OrderedQuantity.ToString("N0", CultureInfo.CurrentCulture);
    public string ReceivedQuantityText => Data.ReceivedQuantity.ToString("N0", CultureInfo.CurrentCulture);
    public string RemainingQuantityText => Data.RemainingQuantity.ToString("N0", CultureInfo.CurrentCulture);
    public string ProgressLabel => Data.ProgressLabel;
    public string ManagementLabel => Data.ManagementLabel;
    public Brush ProgressBackground => ProgressLabel switch
    {
        "入庫済" => AppTheme.SuccessSurface,
        "一部入庫" or "入庫待ち" or "棚入れ待ち" => AppTheme.WarningSurface,
        "数量超過" or "数量要確認" => AppTheme.DangerSurface,
        _ => AppTheme.Subtle
    };
    public Brush ProgressForeground => ProgressLabel switch
    {
        "入庫済" => AppTheme.SuccessText,
        "一部入庫" or "入庫待ち" or "棚入れ待ち" => AppTheme.WarningText,
        "数量超過" or "数量要確認" => AppTheme.DangerText,
        _ => AppTheme.SecondaryText
    };
    public Brush ManagementBackground => ManagementLabel switch
    {
        "表示中" => AppTheme.SuccessSurface,
        "非表示" => AppTheme.DangerSurface,
        _ => AppTheme.WarningSurface
    };
    public Brush ManagementForeground => ManagementLabel switch
    {
        "表示中" => AppTheme.SuccessText,
        "非表示" => AppTheme.DangerText,
        _ => AppTheme.WarningText
    };
    public bool IsSelectable => PurchaseOrderService.CanHide && Data.ManagementFlag == PurchaseOrderService.ActiveFlag;
    public bool IsSelected
    {
        get => isSelected;
        set { if (Set(ref isSelected, value)) SelectionChanged?.Invoke(this, value); }
    }

    internal Action<PurchaseOrderRow, bool>? SelectionChanged { get; set; }
    internal void SetSelectedFromViewModel(bool value) => Set(ref isSelected, value, nameof(IsSelected));
    public PurchaseOrderRow(PurchaseOrderRecord data) => Data = data;
}

public sealed class PurchaseOrderManagementViewModel : ViewModelBase, IDisposable
{
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private IReadOnlyList<PurchaseOrderRecord> source = [];
    private IReadOnlyList<PurchaseOrderRecord> matches = [];
    private IReadOnlyList<PurchaseMakerOption> makers = [];
    private IReadOnlyList<PurchaseEmployeeOption> employees = [];
    private PurchaseOrderSummary summary = new(0, 0, 0);
    private PurchaseManagementTab activeTab = PurchaseOrderService.CanViewHidden ? PurchaseManagementTab.Visible : PurchaseManagementTab.All;
    private PurchaseProgressFilter selectedProgress;
    private PurchaseOrderRow? selectedRow;
    private string keyword = "", orderCode = "", productFilter = "", feedback = "", searchError = "", dateInputError = "";
    private PurchaseProgressFilter progressTab;
    private int page = 1, pageSize = 20;
    private Func<PurchaseOrderRecord, IComparable>? sortKey;
    private bool sortDescending, searchPending;
    private DateTime? startDate, endDate;
    private int makerSelection, employeeSelection, searchRevision;
    private bool filtersExpanded = true, isLoading, isBusy, isDisposed;

    public ObservableCollection<PurchaseOrderRow> Rows { get; } = [];
    public IReadOnlyList<PurchaseMakerOption> MakerOptions => [new(0, "すべて"), .. makers];
    public IReadOnlyList<PurchaseEmployeeOption> EmployeeOptions => [new(0, "すべて"), .. employees];
    public IReadOnlyList<PurchaseProgressOption> ProgressOptions { get; } =
    [
        new(PurchaseProgressFilter.All, "すべて"),
        new(PurchaseProgressFilter.Waiting, "入庫待ち"),
        new(PurchaseProgressFilter.Partial, "一部入庫"),
        new(PurchaseProgressFilter.Received, "入庫済")
    ];
    public string Keyword { get => keyword; set { if (Set(ref keyword, value ?? "")) ScheduleSearch(); } }
    public string OrderCode { get => orderCode; set { if (Set(ref orderCode, value)) ScheduleSearch(); } }
    public string ProductFilter { get => productFilter; set { if (Set(ref productFilter, value)) ScheduleSearch(); } }
    public DateTime? StartDate { get => startDate; set { if (Set(ref startDate, value?.Date)) ScheduleSearch(); } }
    public DateTime? EndDate { get => endDate; set { if (Set(ref endDate, value?.Date)) ScheduleSearch(); } }
    public int MakerSelection { get => makerSelection; set { if (Set(ref makerSelection, value)) ScheduleSearch(); } }
    public int EmployeeSelection { get => employeeSelection; set { if (Set(ref employeeSelection, value)) ScheduleSearch(); } }
    public PurchaseProgressFilter SelectedProgress { get => selectedProgress; set { if (Set(ref selectedProgress, value)) ScheduleSearch(); } }
    public string Feedback { get => feedback; private set => Set(ref feedback, value); }
    public string SearchError { get => searchError; private set { if (Set(ref searchError, value)) Notify(nameof(ShowSearchError)); } }
    public bool ShowSearchError => !string.IsNullOrWhiteSpace(SearchError);
    public PurchaseManagementTab ActiveTab { get => activeTab; private set { if (Set(ref activeTab, value)) NotifyTabCounts(); } }
    public PurchaseOrderRow? SelectedRow { get => selectedRow; private set { if (Set(ref selectedRow, value)) NotifySelection(); } }
    public bool IsFiltersExpanded { get => filtersExpanded; set { if (Set(ref filtersExpanded, value)) Notify(nameof(FiltersToggleLabel)); } }
    public string FiltersToggleLabel => IsFiltersExpanded ? "詳細条件を折りたたむ" : "詳細条件を展開";
    public bool IsLoading { get => isLoading; private set { if (Set(ref isLoading, value)) NotifyActions(); } }
    public bool IsBusy { get => isBusy; private set { if (Set(ref isBusy, value)) NotifyActions(); } }
    public bool CanReload => !IsLoading && !IsBusy;
    public bool CanRegister => PurchaseOrderService.CanRegister && !IsLoading && !IsBusy;
    public bool CanAssignOffice => PurchaseOrderService.CanAssignOffice;
    public bool CanHide => PurchaseOrderService.CanHide && !IsLoading && !IsBusy
        && !searchPending && ActiveTab != PurchaseManagementTab.Hidden && SelectedRow is { IsSelectable: true };
    public bool CanClearSelection => SelectedRow != null;
    public string SelectionText => SelectedRow == null ? "選択なし" : "選択 1 件";
    public bool CanViewHidden => PurchaseOrderService.CanViewHidden;
    public int TotalCount => source.Count;
    public int VisibleCount => source.Count(x => x.ManagementFlag == PurchaseOrderService.ActiveFlag);
    public int HiddenCount => CanViewHidden ? source.Count(x => x.ManagementFlag == PurchaseOrderService.HiddenFlag) : 0;
    public int WaitingCount => source.Count(x => x.ManagementFlag == PurchaseOrderService.ActiveFlag && x.IsOutstanding);
    public int PartialCount => CountProgress(PurchaseReceiptStatus.Partial);
    public int ReceivedCount => CountProgress(PurchaseReceiptStatus.Received);
    public string CountText => $"{matches.Count:N0} 件 / 全 {CurrentTabCount:N0} 件";
    public string PageText => matches.Count == 0 ? "0 件" : $"{(Page - 1) * PageSize + 1:N0}～{(Page - 1) * PageSize + Rows.Count:N0} 件 / 全 {matches.Count:N0} 件";
    public IReadOnlyList<int> PageSizes { get; } = [20, 50, 100];
    public int PageSize { get => pageSize; set { if (PageSizes.Contains(value) && Set(ref pageSize, value)) ShowPage(1); } }
    public int Page => page;
    public int PageCount => matches.Count == 0 ? 1 : (matches.Count - 1) / PageSize + 1;
    public bool CanPrevious => Page > 1 && !searchPending && !IsLoading;
    public bool CanNext => Page < PageCount && !searchPending && !IsLoading;
    public bool CanExport => matches.Count > 0 && !searchPending && !IsLoading && !IsBusy && PurchaseOrderService.CanView;
    public IReadOnlyList<PurchaseOrderRecord> ExportRows => CanExport ? matches : [];
    public string AllTabText => $"すべて　{TotalCount:N0}";
    public string VisibleTabText => $"表示中　{VisibleCount:N0}";
    public string HiddenTabText => $"非表示　{HiddenCount:N0}";
    public string SummaryScope => "全期間・表示中発注（管理者／物流担当の全社範囲）";
    public string TotalSummaryText => $"{summary.Total:N0} 件";
    public string OutstandingSummaryText => $"{summary.Outstanding:N0} 件";
    public string FullyReceivedSummaryText => $"{summary.FullyReceived:N0} 件";
    public string EmptyTitle => ShowSearchError ? "検索条件を確認してください" : "発注データがありません";
    public string EmptyDescription => ShowSearchError ? SearchError : "発注登録を行うか、検索条件を変更してください。";
    public bool IsEmpty => Rows.Count == 0;
    public bool ShowEmptyState => !IsLoading && IsEmpty;
    public bool HasConditions => !string.IsNullOrWhiteSpace(Keyword) || StartDate.HasValue || EndDate.HasValue
        || MakerSelection > 0 || EmployeeSelection > 0 || SelectedProgress != PurchaseProgressFilter.All
        || !string.IsNullOrWhiteSpace(OrderCode) || !string.IsNullOrWhiteSpace(ProductFilter) || dateInputError.Length > 0;
    private int CurrentTabCount => ActiveTab switch
    {
        PurchaseManagementTab.Visible => progressTab switch
        {
            PurchaseProgressFilter.Waiting => WaitingCount,
            PurchaseProgressFilter.Partial => PartialCount,
            PurchaseProgressFilter.Received => ReceivedCount,
            _ => VisibleCount
        },
        PurchaseManagementTab.Hidden => HiddenCount,
        _ => TotalCount
    };

    public PurchaseOrderManagementViewModel()
        : this(loadOnCreate: true)
    {
    }

    internal PurchaseOrderManagementViewModel(bool loadOnCreate)
    {
        debounce.Tick += async (_, _) => { debounce.Stop(); await ApplyFiltersAsync(); };
        if (loadOnCreate) _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (IsLoading || IsBusy || isDisposed) return;
        IsLoading = true;
        Feedback = "発注一覧を読み込んでいます…";
        try
        {
            var directory = await Task.Run(PurchaseOrderService.Load);
            source = directory.Orders;
            makers = directory.Makers;
            employees = directory.Employees;
            summary = directory.Summary;
            Notify(nameof(MakerOptions)); Notify(nameof(EmployeeOptions));
            Notify(nameof(TotalSummaryText)); Notify(nameof(OutstandingSummaryText));
            Notify(nameof(FullyReceivedSummaryText)); Notify(nameof(SummaryScope));
            NotifyTabCounts();
            Feedback = source.Count == 0 ? "表示できる発注はありません。" : "";
            await ApplyFiltersAsync();
        }
        catch (UnauthorizedAccessException ex)
        {
            source = []; ClearResults(); Feedback = ex.Message; NotifyTabCounts();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Purchase order load failed: {ex}");
            source = []; ClearResults(); Feedback = "発注一覧を読み込めませんでした。LocalDB の起動状態とデータベース接続を確認してください。";
            NotifyTabCounts();
        }
        finally { IsLoading = false; NotifyEmptyState(); }
    }

    public void SelectTab(PurchaseManagementTab tab, PurchaseProgressFilter progress = PurchaseProgressFilter.All)
    {
        if (tab == PurchaseManagementTab.Hidden && !CanViewHidden) return;
        progressTab = progress;
        ActiveTab = tab;
        _ = ApplyFiltersAsync();
    }

    public void ToggleFilters() => IsFiltersExpanded = !IsFiltersExpanded;

    public void ClearConditions()
    {
        debounce.Stop();
        Keyword = ""; StartDate = null; EndDate = null; MakerSelection = 0;
        EmployeeSelection = 0; SelectedProgress = PurchaseProgressFilter.All; OrderCode = ""; ProductFilter = "";
        dateInputError = "";
        debounce.Stop();
        _ = ApplyFiltersAsync();
    }

    public void SetDateInputError(string message)
    {
        dateInputError = message;
        ScheduleSearch();
    }

    public void ClearSelection()
    {
        if (SelectedRow != null) SelectedRow.SetSelectedFromViewModel(false);
        SelectedRow = null;
    }

    public void SelectOnly(PurchaseOrderRow row)
    {
        if (!row.IsSelectable) return;
        foreach (var other in Rows.Where(x => !ReferenceEquals(x, row) && x.IsSelected))
            other.SetSelectedFromViewModel(false);
        row.SetSelectedFromViewModel(true);
        SelectedRow = row;
    }

    public async Task<PurchaseOperationResult> HideAsync(PurchaseOrderRow row, string reason)
    {
        if (!CanHide || !ReferenceEquals(SelectedRow, row))
            return new(false, null, "非表示にできる発注を1件選択してください。");
        IsBusy = true;
        try
        {
            var result = await Task.Run(() => PurchaseOrderService.Hide(AppSession.Employee!.EmId, row.Data.Id, reason));
            if (result.Succeeded)
            {
                ClearSelection();
                await ReloadCoreAsync();
            }
            return result;
        }
        finally { IsBusy = false; }
    }

    private void ScheduleSearch()
    {
        Interlocked.Increment(ref searchRevision);
        searchPending = true;
        Notify(nameof(HasConditions));
        NotifyPaging();
        debounce.Stop();
        if (!isDisposed) debounce.Start();
    }

    private async Task ApplyFiltersAsync()
    {
        debounce.Stop();
        var revision = Interlocked.Increment(ref searchRevision);
        searchPending = true;
        NotifyPaging();
        var criteria = new PurchaseOrderSearchCriteria(Keyword, StartDate, EndDate,
            MakerSelection > 0 ? MakerSelection : null,
            EmployeeSelection > 0 ? EmployeeSelection : null, SelectedProgress)
            { OrderCode = OrderCode, Product = ProductFilter };
        var tab = ActiveTab;
        var tabProgress = progressTab;
        SearchError = dateInputError.Length > 0 ? dateInputError : PurchaseOrderFilter.Validate(criteria) ?? "";
        if (SearchError.Length > 0)
        {
            if (revision == Volatile.Read(ref searchRevision))
            {
                searchPending = false;
                ClearResults();
            }
            NotifyCounts(); NotifyEmptyState();
            return;
        }

        try
        {
            IReadOnlySet<int>? productOrderIds = null;
            IReadOnlySet<int>? advancedProductOrders = null;
            if (!string.IsNullOrWhiteSpace(criteria.Keyword))
                productOrderIds = await Task.Run(() => PurchaseOrderService.FindProductOrderIds(criteria.Keyword));
            if (!string.IsNullOrWhiteSpace(criteria.Product))
                advancedProductOrders = string.Equals(criteria.Product.Trim(), criteria.Keyword.Trim(), StringComparison.OrdinalIgnoreCase)
                    ? productOrderIds : await Task.Run(() => PurchaseOrderService.FindProductOrderIds(criteria.Product));
            if (revision != Volatile.Read(ref searchRevision) || isDisposed) return;
            var filtered = source.Where(x => PurchaseOrderFilter.Matches(x, criteria, tab,
                CanViewHidden, productOrderIds, advancedProductOrders)
                && PurchaseOrderFilter.MatchesProgress(x, tabProgress));
            matches = (sortKey == null ? filtered : sortDescending ? filtered.OrderByDescending(sortKey) : filtered.OrderBy(sortKey)).ToArray();
            SearchError = "";
            ShowPage(1);
        }
        catch (Exception ex)
        {
            if (revision != Volatile.Read(ref searchRevision) || isDisposed) return;
            System.Diagnostics.Debug.WriteLine($"Purchase order search failed: {ex}");
            SearchError = ex is UnauthorizedAccessException ? ex.Message : "発注を検索できませんでした。データベースの接続状態を確認してください。";
            ClearResults();
        }
        finally
        {
            if (revision == Volatile.Read(ref searchRevision)) { searchPending = false; NotifyPaging(); }
        }
    }

    private int CountProgress(PurchaseReceiptStatus status) => source.Count(x =>
        x.ManagementFlag == PurchaseOrderService.ActiveFlag && x.ReceiptStatus == status);

    public void GoToPage(int requested)
    {
        if (!searchPending && !IsLoading && requested >= 1 && requested <= PageCount && requested != Page)
            ShowPage(requested);
    }

    public void Sort(Func<PurchaseOrderRecord, IComparable> key, ListSortDirection direction)
    {
        sortKey = key;
        sortDescending = direction == ListSortDirection.Descending;
        _ = ApplyFiltersAsync();
    }

    private void ShowPage(int requested)
    {
        var selectedId = SelectedRow?.Data.Id;
        ClearSelection();
        page = Math.Clamp(requested, 1, PageCount);
        Rows.Clear();
        foreach (var record in matches.Skip((page - 1) * PageSize).Take(PageSize))
        {
            var row = new PurchaseOrderRow(record) { SelectionChanged = OnSelectionChanged };
            Rows.Add(row);
            if (record.Id == selectedId && row.IsSelectable) SelectOnly(row);
        }
        NotifyCounts(); NotifyEmptyState(); NotifyPaging();
    }

    private void ClearResults()
    {
        matches = [];
        ShowPage(1);
    }

    private void NotifyPaging()
    {
        Notify(nameof(Page)); Notify(nameof(PageCount)); Notify(nameof(PageText));
        Notify(nameof(CanNext)); Notify(nameof(CanPrevious)); Notify(nameof(CanExport));
        Notify(nameof(CanHide));
    }

    private async Task ReloadCoreAsync()
    {
        IsLoading = true;
        Feedback = "発注一覧を更新しています…";
        try
        {
            var directory = await Task.Run(PurchaseOrderService.Load);
            source = directory.Orders;
            makers = directory.Makers; employees = directory.Employees; summary = directory.Summary;
            Notify(nameof(MakerOptions)); Notify(nameof(EmployeeOptions));
            Notify(nameof(TotalSummaryText)); Notify(nameof(OutstandingSummaryText));
            Notify(nameof(FullyReceivedSummaryText)); Notify(nameof(SummaryScope));
            NotifyTabCounts(); Feedback = "";
            await ApplyFiltersAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Purchase order refresh failed: {ex}");
            Feedback = "発注一覧を更新できませんでした。データベースの接続状態を確認してください。";
        }
        finally { IsLoading = false; NotifyEmptyState(); }
    }

    private void OnSelectionChanged(PurchaseOrderRow row, bool selected)
    {
        if (selected)
        {
            if (!row.IsSelectable) { row.SetSelectedFromViewModel(false); return; }
            foreach (var other in Rows.Where(x => !ReferenceEquals(x, row) && x.IsSelected))
                other.SetSelectedFromViewModel(false);
            SelectedRow = row;
        }
        else if (ReferenceEquals(SelectedRow, row)) SelectedRow = null;
        NotifySelection();
    }

    private void NotifyTabCounts()
    {
        Notify(nameof(TotalCount)); Notify(nameof(VisibleCount)); Notify(nameof(HiddenCount));
        Notify(nameof(WaitingCount)); Notify(nameof(PartialCount)); Notify(nameof(ReceivedCount));
        Notify(nameof(AllTabText)); Notify(nameof(VisibleTabText)); Notify(nameof(HiddenTabText));
        Notify(nameof(CountText)); Notify(nameof(CanHide)); NotifyEmptyState();
    }

    private void NotifyCounts() { Notify(nameof(CountText)); Notify(nameof(IsEmpty)); }
    private void NotifyEmptyState() { Notify(nameof(EmptyTitle)); Notify(nameof(EmptyDescription)); Notify(nameof(ShowEmptyState)); }
    private void NotifySelection()
    {
        Notify(nameof(SelectedRow)); Notify(nameof(CanHide)); Notify(nameof(CanClearSelection)); Notify(nameof(SelectionText));
    }
    private void NotifyActions()
    {
        Notify(nameof(CanReload)); Notify(nameof(CanRegister)); Notify(nameof(CanHide));
        NotifyPaging();
    }

    public void Dispose()
    {
        isDisposed = true;
        debounce.Stop();
    }
}

public sealed record PurchaseProgressOption(PurchaseProgressFilter Value, string Name);

public sealed class PurchaseOrderDetailViewModel
{
    public PurchaseOrderRecord Order { get; }
    public IReadOnlyList<PurchaseOrderLineRecord> Lines { get; }
    public IReadOnlyList<PurchaseReceiptRecord> Receipts { get; }
    public string OfficeName { get; }
    public bool CanHide { get; }
    public bool IsHidden => Order.ManagementFlag == PurchaseOrderService.HiddenFlag;
    public bool HasHiddenReason => IsHidden && !string.IsNullOrWhiteSpace(Order.HiddenReason);
    public string OrderedQuantityText => $"{Order.OrderedQuantity:N0}";
    public string ReceivedQuantityText => $"{Order.ReceivedQuantity:N0}";
    public string RemainingQuantityText => $"{Order.RemainingQuantity:N0}";

    public PurchaseOrderDetailViewModel(PurchaseOrderDetail detail, bool canHide)
    {
        Order = detail.Order;
        Lines = detail.Lines;
        Receipts = detail.Receipts;
        OfficeName = string.IsNullOrWhiteSpace(detail.OfficeName) ? "未記録" : detail.OfficeName;
        CanHide = canHide && !IsHidden;
    }
}

public sealed class PurchaseOrderDraftLine : ViewModelBase
{
    public PurchaseProductOption Product { get; }
    private string quantityText;
    public string QuantityText
    {
        get => quantityText;
        set { if (Set(ref quantityText, value)) Notify(nameof(Quantity)); }
    }
    public int Quantity => int.TryParse(QuantityText, out var quantity) && quantity > 0 ? quantity : 0;
    public string Code => Product.Code.Length == 0 ? $"P-{Product.Id:0000}" : Product.Code;
    public string Name => Product.Name;
    public string MakerName => Product.MakerName;
    public int StockQuantity => Product.StockQuantity;
    public int SafetyStock => Product.SafetyStock;
    public int OutstandingPurchaseQuantity => Product.OutstandingPurchaseQuantity;
    public PurchaseOrderDraftLine(PurchaseProductOption product, int quantity)
    {
        Product = product;
        quantityText = quantity.ToString(CultureInfo.CurrentCulture);
    }
}

public sealed class PurchaseOrderRegisterViewModel : ViewModelBase
{
    private string quantityText = "1", feedback = "";
    private IReadOnlyList<PurchaseMakerOption> makers = [];
    private IReadOnlyList<PurchaseProductOption> products = [];
    private PurchaseMakerOption? selectedMaker;
    private PurchaseProductOption? selectedProduct;
    private (PurchaseOrderDraftLine Line, int Index)? removedLine;
    private DateTime? orderDate = DateTime.Today;
    private bool isLoading = true, isSaving;
    private bool hasDateInputError;

    public ObservableCollection<PurchaseOrderDraftLine> Lines { get; } = [];
    public IReadOnlyList<PurchaseMakerOption> Makers => makers;
    public IReadOnlyList<PurchaseProductOption> AvailableProducts => selectedMaker == null
        ? [] : products.Where(x => x.MakerId == selectedMaker.Id).ToArray();
    public PurchaseMakerOption? SelectedMaker
    {
        get => selectedMaker;
        set
        {
            if (!Set(ref selectedMaker, value)) return;
            if (SelectedProduct?.MakerId != value?.Id) SelectedProduct = null;
            Notify(nameof(AvailableProducts)); NotifyRegistrationState();
        }
    }
    public PurchaseProductOption? SelectedProduct
    {
        get => selectedProduct;
        set
        {
            if (!Set(ref selectedProduct, value)) return;
            Notify(nameof(InventoryReferenceText)); Notify(nameof(InventoryWarning));
            Notify(nameof(HasInventoryReference)); NotifyRegistrationState();
        }
    }
    public DateTime? OrderDate { get => orderDate; set { if (Set(ref orderDate, value?.Date)) NotifyRegistrationState(); } }
    public bool HasDateInputError
    {
        get => hasDateInputError;
        set { if (Set(ref hasDateInputError, value)) NotifyRegistrationState(); }
    }
    public string QuantityText { get => quantityText; set { if (Set(ref quantityText, value ?? "")) NotifyRegistrationState(); } }
    public string Feedback { get => feedback; private set => Set(ref feedback, value); }
    public bool IsLoading { get => isLoading; private set { if (Set(ref isLoading, value)) NotifyRegistrationState(); } }
    public bool IsSaving { get => isSaving; private set { if (Set(ref isSaving, value)) NotifyRegistrationState(); } }
    public bool CanCancel => !IsSaving;
    public bool HasInventoryReference => SelectedProduct != null;
    public string InventoryReferenceText => SelectedProduct == null ? "商品を選択すると在庫情報を表示します。"
        : $"現在庫 {SelectedProduct.StockQuantity:N0}　発注点 {SelectedProduct.SafetyStock:N0}　未入庫発注数量 {SelectedProduct.OutstandingPurchaseQuantity:N0}";
    public string InventoryWarning => SelectedProduct is { } product && product.StockQuantity <= product.SafetyStock
        ? product.OutstandingPurchaseQuantity > 0
            ? $"発注点以下です。既存の未入庫発注数量は {product.OutstandingPurchaseQuantity:N0} です。追加発注前に確認してください。標準発注量は商品マスタにありません。"
            : "発注点以下のため補充を検討してください。標準発注量は商品マスタにありません。"
        : SelectedProduct is { OutstandingPurchaseQuantity: > 0 } pending
            ? $"同じ商品に未入庫発注数量 {pending.OutstandingPurchaseQuantity:N0} があります。登録前に重複がないか確認してください。"
            : "在庫数・発注点は参照情報です。発注登録では在庫を変更しません。";
    public bool IsBelowReorderPoint => SelectedProduct is { } product && product.StockQuantity <= product.SafetyStock;
    public int TotalLineCount => Lines.Count;
    public long TotalQuantity => Lines.Sum(x => (long)x.Quantity);
    public string EmployeeName => AppSession.Employee?.EmName ?? "ログイン情報を確認できません";
    public string OfficeName => AppSession.Employee?.So?.SoName ?? "未記録";
    public string OrderNumberNote => "発注番号は登録時にデータベースが採番します。";
    public string AmountNote => "仕入単価・発注金額は未記録";
    public string ValidationMessage => IsLoading ? "商品・メーカーを読み込んでいます…"
        : IsSaving ? "発注を登録しています…"
        : SelectedMaker == null ? "メーカーを選択してください。"
        : HasDateInputError || OrderDate == null ? "発注日を正しく入力してください。"
        : Lines.Count == 0 ? "商品と正の発注数量を入力して明細に追加してください。"
        : Lines.Any(x => x.Quantity <= 0) ? "各明細の発注数量を1以上の整数で入力してください。"
        : Lines.Any(x => x.Product.MakerId != SelectedMaker.Id) ? "仕入先と異なる商品を明細から削除してください。"
        : "登録内容を確認できます。";
    public bool CanAddLine => !IsLoading && !IsSaving && SelectedMaker != null && SelectedProduct != null
        && SelectedProduct.MakerId == SelectedMaker.Id && int.TryParse(QuantityText, out var quantity) && quantity > 0
        && Lines.All(x => x.Product.Id != SelectedProduct.Id);
    public bool CanRegister => !IsLoading && !IsSaving && PurchaseOrderService.CanRegister && SelectedMaker != null
        && !HasDateInputError && OrderDate != null && Lines.Count > 0 && Lines.All(x => x.Quantity > 0 && x.Product.MakerId == SelectedMaker.Id);
    public bool CanUndoRemove => !IsSaving && removedLine is { } removed && removed.Line.Product.MakerId == SelectedMaker?.Id
        && Lines.All(x => x.Product.Id != removed.Line.Product.Id);
    public RelayCommand AddLineCommand { get; }
    public RelayCommand RemoveLineCommand { get; }
    public RelayCommand UndoRemoveCommand { get; }

    public PurchaseOrderRegisterViewModel() : this(loadOnCreate: true) { }

    internal PurchaseOrderRegisterViewModel(bool loadOnCreate)
    {
        AddLineCommand = new RelayCommand(AddSelectedLine, () => CanAddLine);
        RemoveLineCommand = new RelayCommand(RemoveLine, parameter => !IsSaving && parameter is PurchaseOrderDraftLine);
        UndoRemoveCommand = new RelayCommand(UndoRemove, () => CanUndoRemove);
        Lines.CollectionChanged += (_, _) => { Notify(nameof(TotalLineCount)); Notify(nameof(TotalQuantity)); NotifyRegistrationState(); };
        if (loadOnCreate) _ = LoadCatalogAsync();
        else IsLoading = false;
    }

    public async Task LoadCatalogAsync()
    {
        IsLoading = true;
        try
        {
            var catalog = await Task.Run(PurchaseOrderService.LoadRegistrationCatalog);
            makers = catalog.Makers;
            products = catalog.Products;
            Notify(nameof(Makers)); Notify(nameof(AvailableProducts));
            if (SelectedMaker == null && makers.Count == 1) SelectedMaker = makers[0];
            Feedback = makers.Count == 0 ? "利用できるメーカーがありません。メーカーと商品マスタを確認してください。" : "";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Purchase registration catalog load failed: {ex}");
            Feedback = ex is UnauthorizedAccessException ? ex.Message
                : "メーカー・商品を読み込めませんでした。データベースの接続状態を確認してください。";
        }
        finally { IsLoading = false; }
    }

    public void ClearFeedback() => Feedback = "";

    public void AddSelectedLine()
    {
        if (!CanAddLine || SelectedProduct == null || !int.TryParse(QuantityText, out var quantity)) return;
        var line = new PurchaseOrderDraftLine(SelectedProduct, quantity);
        line.PropertyChanged += LineChanged;
        Lines.Add(line);
        Feedback = "";
        SelectedProduct = null;
        QuantityText = "1";
        NotifyRegistrationState();
    }

    private void RemoveLine(object? parameter)
    {
        if (parameter is not PurchaseOrderDraftLine line) return;
        var index = Lines.IndexOf(line);
        if (index < 0) return;
        removedLine = (line, index);
        line.PropertyChanged -= LineChanged;
        Lines.RemoveAt(index);
    }

    private void UndoRemove()
    {
        if (!CanUndoRemove || removedLine is not { } removed) return;
        removedLine = null;
        removed.Line.PropertyChanged += LineChanged;
        Lines.Insert(Math.Min(removed.Index, Lines.Count), removed.Line);
    }

    private void LineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PurchaseOrderDraftLine.Quantity)) return;
        Notify(nameof(TotalQuantity));
        NotifyRegistrationState();
    }

    public async Task<PurchaseOperationResult> RegisterAsync()
    {
        if (!CanRegister || SelectedMaker == null)
            return new(false, null, ValidationMessage);
        var input = Lines.Select(x => new PurchaseLineInput(x.Product.Id, x.Quantity)).ToArray();
        if (PurchaseOrderService.ValidateRegistration(SelectedMaker.Id, OrderDate, input) is { } validation)
            return new(false, null, validation);
        IsSaving = true;
        Feedback = "発注を登録しています…";
        try
        {
            var result = await Task.Run(() => PurchaseOrderService.Register(
                AppSession.Employee!.EmId, SelectedMaker.Id, OrderDate, input));
            Feedback = result.Message;
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Purchase registration failed: {ex}");
            var result = new PurchaseOperationResult(false, null,
                "発注を登録できませんでした。接続を確認してから、一覧を再読込してください。");
            Feedback = result.Message;
            return result;
        }
        finally { IsSaving = false; }
    }

    private void NotifyRegistrationState()
    {
        Notify(nameof(ValidationMessage)); Notify(nameof(CanAddLine)); Notify(nameof(CanRegister)); Notify(nameof(CanCancel));
        AddLineCommand?.Refresh(); RemoveLineCommand?.Refresh(); UndoRemoveCommand?.Refresh();
    }
}

public sealed class PurchaseOrderHideDialogViewModel : ViewModelBase
{
    private string reason = "";
    public PurchaseOrderRecord Order { get; }
    public string Reason
    {
        get => reason;
        set
        {
            if (!Set(ref reason, value ?? "")) return;
            Notify(nameof(ValidationMessage)); Notify(nameof(CanConfirm)); ConfirmCommand.Refresh();
        }
    }
    public string ValidationMessage => PurchaseOrderService.ValidateHideReason(Reason) ?? "";
    public bool CanConfirm => ValidationMessage.Length == 0;
    public RelayCommand ConfirmCommand { get; }
    public PurchaseOrderHideDialogViewModel(PurchaseOrderRecord order, Action confirm)
    {
        Order = order;
        ConfirmCommand = new RelayCommand(confirm, () => CanConfirm);
    }
}
