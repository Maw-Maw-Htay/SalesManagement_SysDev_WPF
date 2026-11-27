using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;
using System.Windows.Threading;

namespace SalesManagement_SysDev;

public enum OrderStatusTab { All, Pending, Confirmed, Hidden, History, Draft }

public sealed record OrderLineView(int ProductId, string ProductCode, string ProductName, int Quantity, decimal? UnitPrice,
    decimal? LineTotal, InventoryStockLookup Stock, long RequiredStockQuantity, int Number)
{
    public string ProductLabel => $"{ProductCode} / {ProductName}";
    public string CurrentStockText => Stock.State switch
    {
        InventoryStockState.Available => Stock.Quantity?.ToString("N0") ?? "—",
        InventoryStockState.MissingRecord => "未登録",
        InventoryStockState.QueryFailed => "照会失敗",
        _ => "—"
    };

    public string Availability => Stock.State switch
    {
        InventoryStockState.Available when Stock.Quantity == 0 => "在庫0",
        InventoryStockState.Available when Stock.Quantity > 0 => Stock.Quantity < RequiredStockQuantity ? "在庫不足" : "在庫あり",
        InventoryStockState.MissingRecord => "在庫未登録",
        InventoryStockState.InactiveRecord => "有効在庫なし",
        InventoryStockState.DuplicateActiveRecords => "在庫行重複",
        InventoryStockState.InvalidQuantity => "在庫数不正",
        InventoryStockState.QueryFailed => "照会失敗",
        _ => "在庫未確認"
    };
}

public sealed record OrderDetailView(int OrderId, string CustomerName, string CustomerPhone, string CustomerAddress,
    string OfficeName, DateTime? OrderDate, DateTime? DeliveryDate, string State, string ManagementState,
    int RelatedOrderId, decimal? Total, IReadOnlyList<OrderLineView> Lines);

public sealed record ChumonFilterOption(int Value, string Label);

public sealed class OrderManagementRow : ViewModelBase
{
    private bool isSelected;
    public int OrderId { get; init; }
    public string OrderNumber => $"CH-{OrderId:000000}";
    public int CustomerId { get; init; }
    public string Customer { get; init; } = "";
    public int OfficeId { get; init; }
    public string Office { get; init; } = "";
    public DateTime? OrderDate { get; init; }
    public DateTime? DeliveryDate { get; init; }
    public string State { get; init; } = "未確定";
    public int RelatedOrderId { get; init; }
    public int LineCount { get; init; }
    public int TotalQuantity { get; init; }
    public decimal? Total { get; init; }
    public int ManagementFlag { get; init; }
    public string ManagementState => ManagementFlag == 2 ? "非表示" : "通常表示";
    public bool IsEligible => ManagementFlag == 0 && State == "未確定";
    public bool IsSelected { get => isSelected; set { if (Set(ref isSelected, value)) SelectionChanged?.Invoke(); } }
    internal Action? SelectionChanged { get; set; }
}

public sealed class ChumonManagementViewModel : ViewModelBase
{
    private const int NoFilter = -1;
    private static readonly ChumonFilterOption Any = new(NoFilter, "すべて");
    private readonly List<OrderManagementRow> allRows = [];
    private string quickSearchText = "";
    private string orderFilter = "";
    private string customerFilter = "";
    private DateTime? fromDate;
    private DateTime? toDate;
    private DateTime? deliveryFrom;
    private DateTime? deliveryTo;
    private int officeFilter = NoFilter;
    private int stateFilter = NoFilter;
    private int managementFilter = NoFilter;
    private int activeTab;
    private string feedback = "";
    private bool isFiltersExpanded;
    private bool isBusy;
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };

    public ObservableCollection<OrderManagementRow> Rows { get; } = [];
    public string QuickSearchText { get => quickSearchText; set { if (Set(ref quickSearchText, value)) ScheduleTextSearch(); } }
    public string OrderFilter { get => orderFilter; set { if (Set(ref orderFilter, value)) ScheduleTextSearch(); } }
    public string CustomerFilter { get => customerFilter; set { if (Set(ref customerFilter, value)) ScheduleTextSearch(); } }
    public DateTime? FromDate { get => fromDate; set { if (Set(ref fromDate, value)) Refresh(); } }
    public DateTime? ToDate { get => toDate; set { if (Set(ref toDate, value)) Refresh(); } }
    public DateTime? DeliveryFrom { get => deliveryFrom; set { if (Set(ref deliveryFrom, value)) Refresh(); } }
    public DateTime? DeliveryTo { get => deliveryTo; set { if (Set(ref deliveryTo, value)) Refresh(); } }
    public int OfficeFilter { get => officeFilter; set { if (Set(ref officeFilter, value)) Refresh(); } }
    public int StateFilter { get => stateFilter; set { if (Set(ref stateFilter, value)) Refresh(); } }
    public int ManagementFilter { get => managementFilter; set { if (Set(ref managementFilter, value)) Refresh(); } }
    public IReadOnlyList<ChumonFilterOption> OfficeOptions { get; private set; } = [Any];
    public IReadOnlyList<ChumonFilterOption> StateOptions { get; } = [Any, new(0, "未確定"), new(1, "確定済み")];
    public IReadOnlyList<ChumonFilterOption> ManagementOptions { get; } = [Any, new(0, "通常表示"), new(2, "非表示")];
    public string FilterValidationMessage => (IsReversed(FromDate, ToDate), IsReversed(DeliveryFrom, DeliveryTo)) switch
    {
        (true, true) => "注文日と納期の開始日は、それぞれ終了日以前の日付を指定してください。",
        (true, false) => "注文日の開始日は、終了日以前の日付を指定してください。",
        (false, true) => "納期の開始日は、終了日以前の日付を指定してください。",
        _ => ""
    };
    public string Feedback { get => feedback; private set => Set(ref feedback, value); }
    public int ActiveTab { get => activeTab; private set => Set(ref activeTab, value); }
    public bool IsFiltersExpanded
    {
        get => isFiltersExpanded;
        set { if (Set(ref isFiltersExpanded, value)) Notify(nameof(FiltersToggleLabel)); }
    }
    public string FiltersToggleLabel => IsFiltersExpanded ? "詳細条件を折りたたむ" : "詳細条件を展開";
    public bool IsBusy { get => isBusy; private set { if (Set(ref isBusy, value)) NotifyCanOperate(); } }
    public int ResultCount => Rows.Count;
    public int AllCount => allRows.Count(x => x.ManagementFlag == 0);
    public int PendingCount => allRows.Count(x => x.ManagementFlag == 0 && x.State == "未確定");
    public int ConfirmedCount => allRows.Count(x => x.ManagementFlag == 0 && x.State == "確定済み");
    public int HiddenCount => allRows.Count(x => x.ManagementFlag == 2);
    public string AllTabText => $"すべて　{AllCount:N0}";
    public string PendingTabText => $"未確定　{PendingCount:N0}";
    public string ConfirmedTabText => $"確定済み　{ConfirmedCount:N0}";
    public string HiddenTabText => $"非表示　{HiddenCount:N0}";
    public int SelectedCount => Rows.Count(x => x.IsSelected);
    public bool CanConfirm => !IsBusy && Rows.Any(x => x.IsSelected && x.IsEligible);
    public bool CanHide => !IsBusy && Rows.Any(x => x.IsSelected && x.ManagementFlag == 0);
    public bool HasConditions => !string.IsNullOrWhiteSpace(QuickSearchText) || !string.IsNullOrWhiteSpace(OrderFilter)
        || !string.IsNullOrWhiteSpace(CustomerFilter) || FromDate.HasValue || ToDate.HasValue
        || DeliveryFrom.HasValue || DeliveryTo.HasValue || OfficeFilter != NoFilter
        || StateFilter != NoFilter || ManagementFilter != NoFilter;
    public bool CanSelectAll => !IsBusy && Rows.Any(x => x.IsEligible);
    public bool AllEligibleSelected => Rows.Any(x => x.IsEligible) && Rows.All(x => !x.IsEligible || x.IsSelected);
    public string CountText => $"{ResultCount:N0} 件表示 / 全 {TotalForTab:N0} 件";
    private int TotalForTab => ActiveTab switch { 1 => PendingCount, 2 => ConfirmedCount, 3 => HiddenCount, _ => AllCount };

    public ChumonManagementViewModel() : this(loadOnCreate: true) { }

    internal ChumonManagementViewModel(bool loadOnCreate)
    {
        searchTimer.Tick += (_, _) => { searchTimer.Stop(); Refresh(); };
        if (loadOnCreate) _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        Feedback = "";
        if (AppSession.Employee is not { } employee)
        {
            ApplyLoadedRows([]);
            return;
        }
        var isAdmin = AppSession.IsAdmin;
        var officeId = employee.SoId;
        try
        {
            var loaded = await Task.Run(async () =>
            {
                using var db = new SalesManagementContext();
                var orders = db.TChumons.AsNoTracking();
                if (!isAdmin) orders = orders.Where(order => order.SoId == officeId);

                var details = db.TChumonDetails.AsQueryable();
                if (!isAdmin) details = details.Where(line => line.Ch.SoId == officeId);
                var quantities = details.GroupBy(line => line.ChId)
                    .Select(group => new
                    {
                        OrderId = group.Key, LineCount = (int?)group.Count(), Quantity = (int?)group.Sum(line => line.ChQuantity)
                    });
                var savedTotals = db.TOrderDetails
                    .Where(line => (line.OrQuantity != 0 || line.OrTotalPrice != 0m) && orders.Any(order => order.OrId == line.OrId))
                    .GroupBy(line => line.OrId)
                    .Select(group => new { OrderId = group.Key, Amount = (decimal?)group.Sum(line => line.OrTotalPrice) });
                return await (from order in orders
                              join quantity in quantities on order.ChId equals quantity.OrderId into orderQuantities
                              from quantity in orderQuantities.DefaultIfEmpty()
                              join total in savedTotals on order.OrId equals total.OrderId into orderTotals
                              from total in orderTotals.DefaultIfEmpty()
                              select new OrderManagementRow
                              {
                                  OrderId = order.ChId, CustomerId = order.ClId, Customer = order.Cl.ClName,
                                  OfficeId = order.SoId, Office = order.So.SoName,
                                  OrderDate = order.ChDate, DeliveryDate = order.Or.OrDeliveryDate,
                                  State = (order.ChStateFlag ?? 0) == 1 ? "確定済み" : "未確定",
                                  RelatedOrderId = order.OrId, LineCount = quantity.LineCount ?? 0,
                                  TotalQuantity = quantity.Quantity ?? 0, Total = total.Amount,
                                  ManagementFlag = order.ChFlag
                              }).ToListAsync();
            });
            ApplyLoadedRows(loaded);
        }
        catch
        {
            ApplyLoadedRows([]);
            Feedback = "注文一覧を読み込めませんでした。データベースの接続状態を確認してください。";
        }
    }

    private void ApplyLoadedRows(IEnumerable<OrderManagementRow> loaded)
    {
        allRows.Clear();
        allRows.AddRange(loaded);
        // Use the real branches attached to orders that are already in the user's scope.
        var options = allRows.DistinctBy(x => x.OfficeId).OrderBy(x => x.Office)
            .Select(x => new ChumonFilterOption(x.OfficeId, x.Office));
        IReadOnlyList<ChumonFilterOption> updatedOptions = [Any, .. options];
        if (!OfficeOptions.SequenceEqual(updatedOptions))
        {
            if (!updatedOptions.Any(x => x.Value == officeFilter)) officeFilter = NoFilter;
            OfficeOptions = updatedOptions;
            Notify(nameof(OfficeOptions));
            Notify(nameof(OfficeFilter));
        }
        Refresh();
        NotifyTabCounts();
    }

    public void SelectTab(int tab)
    {
        foreach (var row in allRows) row.IsSelected = false;
        ActiveTab = tab; Refresh();
    }

    public void ToggleSelectVisible(bool selected)
    {
        foreach (var row in Rows) row.IsSelected = selected && row.IsEligible;
        NotifySelection();
    }

    public void ClearConditions()
    {
        searchTimer.Stop();
        Set(ref quickSearchText, "", nameof(QuickSearchText));
        Set(ref orderFilter, "", nameof(OrderFilter));
        Set(ref customerFilter, "", nameof(CustomerFilter));
        Set(ref fromDate, null, nameof(FromDate));
        Set(ref toDate, null, nameof(ToDate));
        Set(ref deliveryFrom, null, nameof(DeliveryFrom));
        Set(ref deliveryTo, null, nameof(DeliveryTo));
        Set(ref officeFilter, NoFilter, nameof(OfficeFilter));
        Set(ref stateFilter, NoFilter, nameof(StateFilter));
        Set(ref managementFilter, NoFilter, nameof(ManagementFilter));
        Refresh();
    }

    public async Task<OrderDetailView?> GetDetailsAsync(int id)
    {
        try
        {
            return await Task.Run(() =>
            {
                using var db = new SalesManagementContext();
                var c = db.TChumons.AsNoTracking().Include(x => x.Cl).Include(x => x.So).Include(x => x.Or)
                    .Include(x => x.TChumonDetails).ThenInclude(x => x.Pr)
                    .FirstOrDefault(x => x.ChId == id && (AppSession.IsAdmin || (AppSession.Employee != null && x.SoId == AppSession.Employee.SoId)));
                if (c == null) return null;
                var products = c.TChumonDetails.Select(x => x.PrId).ToList();
                var baseLines = db.TOrderDetails.AsNoTracking()
                    .Where(x => x.OrId == c.OrId && products.Contains(x.PrId) && (x.OrQuantity != 0 || x.OrTotalPrice != 0m)).ToList()
                    .GroupBy(x => x.PrId).ToDictionary(g => g.Key, g => g.ToList());
                var stocks = InventoryService.ReadCurrentStock(db, products);
                var requestedByProduct = c.TChumonDetails.GroupBy(line => line.PrId)
                    .ToDictionary(group => group.Key, group => new
                    {
                        LineCount = group.Count(),
                        Quantity = group.Sum(line => (long)line.ChQuantity)
                    });
                var lines = c.TChumonDetails.Select((line, index) =>
                {
                    var historical = baseLines.GetValueOrDefault(line.PrId);
                    var historicalQuantity = historical?.Sum(x => x.OrQuantity) ?? 0;
                    var historicalTotal = historical?.Sum(x => x.OrTotalPrice) ?? 0;
                    var requested = requestedByProduct[line.PrId];
                    var canMatchHistoricalAmount = requested.LineCount == 1 && historicalQuantity == line.ChQuantity;
                    decimal? unit = canMatchHistoricalAmount && historicalQuantity > 0 ? historicalTotal / historicalQuantity : null;
                    decimal? total = canMatchHistoricalAmount ? historicalTotal : null;
                    return new OrderLineView(line.PrId, line.Pr.PrJcode ?? $"P-{line.PrId:000000}", line.Pr.PrName,
                        line.ChQuantity, unit, total, stocks[line.PrId], requested.Quantity, index + 1);
                }).ToList();
                var orderTotal = baseLines.Values.SelectMany(x => x).Sum(x => x.OrTotalPrice);
                return new OrderDetailView(c.ChId, c.Cl.ClName, c.Cl.ClPhone, c.Cl.ClAddress, c.So.SoName,
                    c.ChDate, c.Or.OrDeliveryDate, (c.ChStateFlag ?? 0) == 1 ? "確定済み" : "未確定",
                    c.ChFlag == 2 ? "非表示" : "通常表示", c.OrId, baseLines.Count > 0 ? orderTotal : null, lines);
            });
        }
        catch { Feedback = "注文詳細を取得できませんでした。"; return null; }
    }

    public async Task<(int Success, int Failure, string Result)> ConfirmSelectedAsync()
    {
        var selected = Rows.Where(x => x.IsSelected && x.IsEligible).ToList();
        return await ConfirmRowsAsync(selected);
    }

    public Task<(int Success, int Failure, string Result)> ConfirmSingleAsync(OrderManagementRow row) => ConfirmRowsAsync([row]);

    private async Task<(int Success, int Failure, string Result)> ConfirmRowsAsync(IReadOnlyCollection<OrderManagementRow> selected)
    {
        if (IsBusy) return (0, 1, "別の注文操作を処理中です。");
        if (selected.Count == 0) return (0, 0, "確定可能な注文を選択してください。");
        IsBusy = true;
        var succeeded = 0; var failed = new List<string>(); var outboundIds = new List<string>();
        try
        {
            foreach (var row in selected)
            {
                var result = await Task.Run(() =>
                {
                    var ok = BusinessService.ConfirmChumon(row.OrderId, AppSession.Employee!.EmId, out var message, out var outboundId);
                    return (ok, message, outboundId);
                });
                if (result.ok) { succeeded++; if (result.outboundId.HasValue) outboundIds.Add($"{row.OrderNumber} → 出庫 {result.outboundId.Value}"); }
                else failed.Add($"{row.OrderNumber}\n{result.message}");
            }
        }
        finally { await LoadAsync(); IsBusy = false; }
        var summary = $"成功　{succeeded:N0} 件\n失敗　{failed.Count:N0} 件\n処理日時　{DateTime.Now:yyyy/MM/dd HH:mm}";
        if (outboundIds.Count > 0) summary += "\n\n" + string.Join("\n", outboundIds.Take(5));
        if (failed.Count > 0) summary += "\n\n" + string.Join("\n\n", failed.Take(5));
        return (succeeded, failed.Count, summary);
    }

    public async Task<(int Success, int Failure, string Result)> HideSelectedAsync(string? reason)
    {
        var selected = Rows.Where(x => x.IsSelected && x.ManagementFlag == 0).ToList();
        return await HideRowsAsync(selected, reason);
    }

    public Task<(int Success, int Failure, string Result)> HideSingleAsync(OrderManagementRow row, string? reason) => HideRowsAsync([row], reason);

    private async Task<(int Success, int Failure, string Result)> HideRowsAsync(IReadOnlyCollection<OrderManagementRow> selected, string? reason)
    {
        if (IsBusy) return (0, 1, "別の注文操作を処理中です。");
        if (selected.Count == 0) return (0, 0, "非表示にできる注文を選択してください。");
        IsBusy = true;
        var succeeded = 0; var failed = new List<string>();
        try
        {
            foreach (var row in selected)
            {
                var result = await Task.Run(() => BusinessService.HideChumon(row.OrderId, AppSession.Employee!.EmId, reason, out var message)
                    ? (ok: true, message: "") : (ok: false, message));
                if (result.ok) succeeded++;
                else failed.Add($"{row.OrderNumber}\n{result.message}");
            }
        }
        finally { await LoadAsync(); IsBusy = false; }
        var summary = $"成功　{succeeded:N0} 件\n失敗　{failed.Count:N0} 件";
        if (failed.Count > 0) summary += "\n\n" + string.Join("\n\n", failed.Take(5));
        return (succeeded, failed.Count, summary);
    }

    private void Refresh()
    {
        searchTimer.Stop();
        IEnumerable<OrderManagementRow> query = allRows;
        query = ActiveTab switch
        {
            1 => query.Where(x => x.ManagementFlag == 0 && x.State == "未確定"),
            2 => query.Where(x => x.ManagementFlag == 0 && x.State == "確定済み"),
            3 => query.Where(x => x.ManagementFlag == 2),
            _ => query.Where(x => x.ManagementFlag == 0)
        };
        var quickSearch = QuickSearchText.Trim();
        if (quickSearch.Length > 0)
            query = query.Where(x =>
                MatchesOrderNumber(x, quickSearch)
                || x.CustomerId.ToString().Contains(quickSearch, StringComparison.OrdinalIgnoreCase)
                || x.Customer.Contains(quickSearch, StringComparison.CurrentCultureIgnoreCase));
        var orderText = OrderFilter.Trim();
        if (orderText.Length > 0) query = query.Where(x => MatchesOrderNumber(x, orderText));
        var customer = CustomerFilter.Trim();
        if (customer.Length > 0) query = query.Where(x => x.Customer.Contains(customer, StringComparison.CurrentCultureIgnoreCase));
        if (FilterValidationMessage.Length > 0) query = [];
        else
        {
            if (FromDate.HasValue) query = query.Where(x => x.OrderDate?.Date >= FromDate.Value.Date);
            if (ToDate.HasValue) query = query.Where(x => x.OrderDate?.Date <= ToDate.Value.Date);
            if (DeliveryFrom.HasValue) query = query.Where(x => x.DeliveryDate?.Date >= DeliveryFrom.Value.Date);
            if (DeliveryTo.HasValue) query = query.Where(x => x.DeliveryDate?.Date <= DeliveryTo.Value.Date);
            if (OfficeFilter != NoFilter) query = query.Where(x => x.OfficeId == OfficeFilter);
            if (StateFilter != NoFilter)
                query = query.Where(x => x.State == (StateFilter == 1 ? "確定済み" : "未確定"));
            if (ManagementFilter != NoFilter) query = query.Where(x => x.ManagementFlag == ManagementFilter);
        }
        var visibleRows = query.OrderByDescending(x => x.OrderDate).ThenByDescending(x => x.OrderId).ToList();
        foreach (var row in allRows) row.SelectionChanged = NotifySelection;
        Rows.Clear(); foreach (var row in visibleRows) Rows.Add(row);
        Notify(nameof(ResultCount)); Notify(nameof(CountText)); Notify(nameof(HasConditions));
        Notify(nameof(FilterValidationMessage)); NotifySelection();
    }

    private static bool IsReversed(DateTime? start, DateTime? end) => start?.Date > end?.Date;

    private static bool MatchesOrderNumber(OrderManagementRow row, string value)
    {
        var digits = value.Replace("CH-", "", StringComparison.OrdinalIgnoreCase).TrimStart('0');
        return row.OrderNumber.Contains(value, StringComparison.OrdinalIgnoreCase)
            || (digits.Length > 0 && row.OrderId.ToString().Contains(digits, StringComparison.Ordinal));
    }

    private void NotifySelection()
    {
        Notify(nameof(SelectedCount)); Notify(nameof(CanConfirm)); Notify(nameof(CanHide)); Notify(nameof(CanSelectAll));
        Notify(nameof(AllEligibleSelected));
    }

    private void ScheduleTextSearch()
    {
        Notify(nameof(HasConditions));
        searchTimer.Stop(); searchTimer.Start();
    }

    private void NotifyCanOperate()
    {
        Notify(nameof(CanConfirm)); Notify(nameof(CanHide)); Notify(nameof(CanSelectAll));
    }

    private void NotifyTabCounts()
    {
        Notify(nameof(AllCount)); Notify(nameof(PendingCount)); Notify(nameof(ConfirmedCount)); Notify(nameof(HiddenCount));
        Notify(nameof(AllTabText)); Notify(nameof(PendingTabText)); Notify(nameof(ConfirmedTabText)); Notify(nameof(HiddenTabText));
    }
}
