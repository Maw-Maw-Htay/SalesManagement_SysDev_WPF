using System.Data;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public enum PurchaseManagementTab { All, Visible, Hidden }
public enum PurchaseProgressFilter { All, Waiting, Partial, Received }
public enum PurchaseReceiptStatus { Unknown, Waiting, Partial, Received, Excess }

public sealed record PurchaseMakerOption(int Id, string Name);
public sealed record PurchaseEmployeeOption(int Id, string Name);
public sealed record PurchaseProductOption(
    int Id, int MakerId, string Code, string Name, string MakerName, int StockQuantity, int SafetyStock,
    int OutstandingPurchaseQuantity)
{
    public long PendingShelvingQuantity { get; init; }
    public string DisplayCode => string.IsNullOrWhiteSpace(Code) ? $"P-{Id:0000}" : Code;
    public string SearchLabel => $"{DisplayCode}　{Name}";
}

public sealed record PurchaseOrderRecord(
    int Id, DateTime OrderDate, int MakerId, string MakerName, int EmployeeId, string EmployeeName,
    int ManagementFlag, string? HiddenReason, int ProductLineCount, int OrderedQuantity, int ReceivedQuantity)
{
    public string Code => $"PO-{Id:000000}";
    public int? WarehouseFlag { get; init; }
    public int? RegisteredOfficeId { get; init; }
    public PurchaseReceiptStatus? RecordedProgress { get; init; }
    public int? RecordedRemaining { get; init; }
    public PurchaseReceiptStatus ReceiptStatus => RecordedProgress ??
        PurchaseProgress.Classify([(OrderedQuantity, ReceivedQuantity)], ProductLineCount);
    public string ManagementLabel => ManagementFlag switch
    {
        PurchaseOrderService.ActiveFlag => "表示中",
        PurchaseOrderService.HiddenFlag => "非表示",
        _ => "要確認"
    };
    public int RemainingQuantity => RecordedRemaining ?? Math.Max(0, OrderedQuantity - ReceivedQuantity);
    public string ProgressLabel => ReceiptStatus switch
    {
        PurchaseReceiptStatus.Waiting => WarehouseFlag switch { 0 => "入庫待ち", 1 => "棚入れ待ち", _ => "要確認" },
        PurchaseReceiptStatus.Partial => "一部入庫",
        PurchaseReceiptStatus.Received => "入庫済",
        PurchaseReceiptStatus.Excess => "数量超過",
        _ => ProductLineCount == 0 ? "明細なし" : "数量要確認"
    };
    public bool IsOutstanding => WarehouseFlag == 0;
    public bool IsFullyReceived => ReceiptStatus == PurchaseReceiptStatus.Received;
}

public sealed record PurchaseOrderLineRecord(
    int DetailId, int ProductId, string ProductCode, string ProductName, int OrderedQuantity,
    int ReceivedQuantity, int CurrentStock, int SafetyStock)
{
    public int RemainingQuantity => Math.Max(0, OrderedQuantity - ReceivedQuantity);
}

public sealed record PurchaseReceiptRecord(int Id, int OrderId, DateTime? Date, string MakerName, string EmployeeName,
    int? ShelfFlag, int Flag, int Quantity)
{
    public string Code => $"IN-{Id:000000}";
    public string OrderCode => PurchaseOrderService.FormatCode(OrderId);
    public string Status => Flag != PurchaseOrderService.ActiveFlag ? "非表示／無効"
        : ShelfFlag == WarehousingService.ConfirmedState ? "棚入れ確定済み" : "棚入れ未確定";
}

public sealed record PurchaseOrderDetail(PurchaseOrderRecord Order, IReadOnlyList<PurchaseOrderLineRecord> Lines)
{
    public IReadOnlyList<PurchaseReceiptRecord> Receipts { get; init; } = [];
    public string SupplierPhone { get; init; } = "";
    public string OfficeName { get; init; } = "";
    public string OfficePhone { get; init; } = "";
}
public sealed record PurchaseOrderSummary(int Total, int Outstanding, int FullyReceived);
public sealed record PurchaseCatalog(IReadOnlyList<PurchaseMakerOption> Makers, IReadOnlyList<PurchaseProductOption> Products);
public sealed record PurchaseOrderDirectory(
    IReadOnlyList<PurchaseOrderRecord> Orders,
    IReadOnlyList<PurchaseMakerOption> Makers,
    IReadOnlyList<PurchaseEmployeeOption> Employees,
    PurchaseOrderSummary Summary);
public sealed record PurchaseOperationResult(bool Succeeded, int? OrderId, string Message);
public sealed record PurchaseLineInput(int ProductId, int Quantity);
public sealed record PurchaseOrderSearchCriteria(
    string Keyword, DateTime? StartDate, DateTime? EndDate, int? MakerId, int? EmployeeId,
    PurchaseProgressFilter Progress)
{
    public string OrderCode { get; init; } = "";
    public string Product { get; init; } = "";
}

public static class PurchaseProgress
{
    public static PurchaseReceiptStatus Classify(IReadOnlyList<(int Ordered, int Received)> quantities, int lineCount)
    {
        if (lineCount <= 0 || quantities.Count == 0 || quantities.Any(x => x.Ordered <= 0 || x.Received < 0))
            return PurchaseReceiptStatus.Unknown;
        if (quantities.Any(x => x.Received > x.Ordered)) return PurchaseReceiptStatus.Excess;
        if (quantities.All(x => x.Received == 0)) return PurchaseReceiptStatus.Waiting;
        return quantities.All(x => x.Received == x.Ordered) ? PurchaseReceiptStatus.Received : PurchaseReceiptStatus.Partial;
    }

    public static string GetLabel(int lineCount, int orderedQuantity, int receivedQuantity)
    {
        if (lineCount <= 0) return "明細なし";
        if (orderedQuantity <= 0) return "数量要確認";
        if (receivedQuantity <= 0) return "入庫待ち";
        if (receivedQuantity < orderedQuantity) return "一部入庫";
        if (receivedQuantity > orderedQuantity) return "数量超過";
        return "入庫済";
    }

    public static PurchaseOrderSummary Summarize(IEnumerable<PurchaseOrderRecord> orders)
    {
        var eligible = orders.Where(x => x.ManagementFlag == PurchaseOrderService.ActiveFlag).ToArray();
        return new(eligible.Length, eligible.Count(x => x.IsOutstanding), eligible.Count(x => x.IsFullyReceived));
    }
}

public static class PurchaseOrderFilter
{
    public static string? Validate(PurchaseOrderSearchCriteria criteria)
    {
        if (criteria.StartDate.HasValue && criteria.EndDate.HasValue
            && criteria.StartDate.Value.Date > criteria.EndDate.Value.Date)
            return "発注日の開始日は終了日以前の日付にしてください。";
        return null;
    }

    public static bool Matches(PurchaseOrderRecord order, PurchaseOrderSearchCriteria criteria,
        PurchaseManagementTab tab, bool canViewHidden, IReadOnlySet<int>? matchingProductOrders = null,
        IReadOnlySet<int>? advancedProductOrders = null)
    {
        if (Validate(criteria) != null) return false;
        if (tab == PurchaseManagementTab.Hidden && (!canViewHidden || order.ManagementFlag != PurchaseOrderService.HiddenFlag)) return false;
        if (tab == PurchaseManagementTab.Visible && order.ManagementFlag != PurchaseOrderService.ActiveFlag) return false;
        if (tab == PurchaseManagementTab.All && !canViewHidden && order.ManagementFlag != PurchaseOrderService.ActiveFlag) return false;
        if (criteria.StartDate.HasValue && order.OrderDate.Date < criteria.StartDate.Value.Date) return false;
        if (criteria.EndDate.HasValue && order.OrderDate.Date > criteria.EndDate.Value.Date) return false;
        if (criteria.MakerId.HasValue && order.MakerId != criteria.MakerId.Value) return false;
        if (criteria.EmployeeId.HasValue && order.EmployeeId != criteria.EmployeeId.Value) return false;
        var orderCode = criteria.OrderCode.Trim();
        if (orderCode.Length > 0 && !order.Code.Contains(orderCode, StringComparison.OrdinalIgnoreCase)
            && !order.Id.ToString().Contains(orderCode, StringComparison.Ordinal)) return false;
        if (!string.IsNullOrWhiteSpace(criteria.Product) && advancedProductOrders?.Contains(order.Id) != true) return false;

        var keyword = criteria.Keyword.Trim();
        if (keyword.Length > 0)
        {
            var localMatch = order.Code.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
                || order.Id.ToString().Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
                || order.MakerName.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
                || order.EmployeeName.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);
            if (!localMatch && (matchingProductOrders == null || !matchingProductOrders.Contains(order.Id))) return false;
        }

        return MatchesProgress(order, criteria.Progress);
    }

    public static bool MatchesProgress(PurchaseOrderRecord order, PurchaseProgressFilter progress)
    {
        return progress switch
        {
            PurchaseProgressFilter.Waiting => order.IsOutstanding,
            PurchaseProgressFilter.Partial => order.ReceiptStatus == PurchaseReceiptStatus.Partial,
            PurchaseProgressFilter.Received => order.IsFullyReceived,
            _ => true
        };
    }
}

/// <summary>Owns purchase-order registration and visibility while preserving receiving and stock records.</summary>
public static partial class PurchaseOrderService
{
    public const int ActiveFlag = 0;
    public const int HiddenFlag = 2;
    public const int MaximumHideReasonLength = 500;
    private static int registrationInProgress;

    public static bool CanView => AppSession.Employee is { EmFlag: ActiveFlag }
        && (AppSession.IsAdmin || AppSession.IsLogistics);
    public static bool CanRegister => CanView;
    public static bool CanViewHidden => AppSession.Employee is { EmFlag: ActiveFlag } && AppSession.IsAdmin;
    public static bool CanHide => CanView;

    public static PurchaseOrderDirectory Load()
    {
        EnsureCanView();
        using var db = new SalesManagementContext();
        var actor = ResolveActor(db, AppSession.Employee!.EmId);
        EnsureCanView(actor);

        var headerQuery = db.THattyus.AsNoTracking();
        if (!actor!.IsAdministrator) headerQuery = headerQuery.Where(x => x.HaFlag == ActiveFlag && x.RegisteredOfficeId == actor.OfficeId);
        var headers = headerQuery.Select(x => new HeaderRow(
            x.HaId, x.HaDate, x.MaId, x.Ma.MaName, x.EmId, x.Em.EmName, x.HaFlag, x.HaHidden,
            x.THattyuDetails.Count(), x.RegisteredOfficeId, x.WaWarehouseFlag)).ToList();
        var orderIds = headers.Select(x => x.Id).ToArray();

        var orderedLines = orderIds.Length == 0
            ? new List<LineAggregate>()
            : db.THattyuDetails.AsNoTracking().Where(x => orderIds.Contains(x.HaId))
                .GroupBy(x => new { x.HaId, x.PrId })
                .Select(g => new LineAggregate(g.Key.HaId, g.Key.PrId, g.Sum(x => x.HaQuantity)))
                .ToList();
        var receivedLines = orderIds.Length == 0
            ? new List<LineAggregate>()
            : db.TWarehousingDetails.AsNoTracking()
                .Where(x => orderIds.Contains(x.Wa.HaId) && (x.Wa.WaFlag == ActiveFlag || x.Wa.WaFlag == HiddenFlag)
                    && x.Wa.WaShelfFlag == WarehousingService.ConfirmedState)
                .GroupBy(x => new { x.Wa.HaId, x.PrId })
                .Select(g => new LineAggregate(g.Key.HaId, g.Key.PrId, g.Sum(x => x.WaQuantity)))
                .ToList();

        var receiptByOrder = receivedLines.ToLookup(x => x.OrderId);
        var orderedByOrder = orderedLines.GroupBy(x => x.OrderId).ToDictionary(x => x.Key, x => x.ToArray());
        var rows = headers.Select(header =>
        {
            var lines = orderedByOrder.GetValueOrDefault(header.Id) ?? [];
            var receipts = receiptByOrder[header.Id].ToDictionary(x => x.ProductId, x => x.Quantity);
            var quantities = lines.Select(x => (Ordered: x.Quantity, Received: receipts.GetValueOrDefault(x.ProductId))).ToArray();
            var hasUnexpectedProduct = receipts.Keys.Except(lines.Select(x => x.ProductId)).Any();
            return new PurchaseOrderRecord(header.Id, header.OrderDate, header.MakerId, header.MakerName,
                header.EmployeeId, header.EmployeeName, header.ManagementFlag, header.HiddenReason,
                header.ProductLineCount, quantities.Sum(x => x.Ordered), quantities.Sum(x => x.Received))
            {
                WarehouseFlag = header.WarehouseFlag,
                RegisteredOfficeId = header.RegisteredOfficeId,
                RecordedProgress = hasUnexpectedProduct ? PurchaseReceiptStatus.Unknown : PurchaseProgress.Classify(quantities, header.ProductLineCount),
                RecordedRemaining = quantities.Sum(x => Math.Max(0, x.Ordered - x.Received))
            };
        }).OrderByDescending(x => x.OrderDate).ThenByDescending(x => x.Id).ToArray();

        var makers = db.MMakers.AsNoTracking().Where(x => x.MaFlag == ActiveFlag)
            .OrderBy(x => x.MaName).Select(x => new PurchaseMakerOption(x.MaId, x.MaName)).ToList();
        var employees = db.MEmployees.AsNoTracking().Where(x => x.EmFlag == ActiveFlag)
            .OrderBy(x => x.EmId).Select(x => new PurchaseEmployeeOption(x.EmId, x.EmName)).ToList();
        return new(rows, makers, employees, PurchaseProgress.Summarize(rows));
    }

    public static IReadOnlySet<int> FindProductOrderIds(string keyword)
    {
        EnsureCanView();
        var term = keyword.Trim();
        if (term.Length == 0) return new HashSet<int>();
        var code = term.ToUpperInvariant();
        using var db = new SalesManagementContext();
        var actor = ResolveActor(db, AppSession.Employee!.EmId);
        EnsureCanView(actor);
        return db.THattyuDetails.AsNoTracking()
            .Where(x => (actor!.IsAdministrator || (x.Ha.HaFlag == ActiveFlag && x.Ha.RegisteredOfficeId == actor.OfficeId))
                && (x.Pr.PrName.Contains(term) || (x.Pr.PrJcode != null && x.Pr.PrJcode.Contains(term))
                    || ((x.PrId < 10 ? "P-000" : x.PrId < 100 ? "P-00" : x.PrId < 1000 ? "P-0" : "P-")
                        + x.PrId.ToString()).Contains(code)))
            .Select(x => x.HaId).Distinct().ToHashSet();
    }

    public static PurchaseCatalog LoadRegistrationCatalog()
    {
        EnsureCanView();
        using var db = new SalesManagementContext();
        using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
        var actor = ResolveActor(db, AppSession.Employee!.EmId);
        EnsureCanView(actor);
        var makers = db.MMakers.AsNoTracking().Where(x => x.MaFlag == ActiveFlag)
            .OrderBy(x => x.MaName).Select(x => new PurchaseMakerOption(x.MaId, x.MaName)).ToList();
        var products = LoadProductOptions(db, actor!);
        transaction.Commit();
        return new(makers, products);
    }

    public static PurchaseOrderDetail? LoadDetail(int orderId)
    {
        EnsureCanView();
        using var db = new SalesManagementContext();
        var actor = ResolveActor(db, AppSession.Employee!.EmId);
        EnsureCanView(actor);
        var header = db.THattyus.AsNoTracking()
            .Where(x => x.HaId == orderId && (actor!.IsAdministrator || (x.HaFlag == ActiveFlag && x.RegisteredOfficeId == actor.OfficeId)))
            .Select(x => new HeaderRow(x.HaId, x.HaDate, x.MaId, x.Ma.MaName, x.EmId, x.Em.EmName,
                x.HaFlag, x.HaHidden, x.THattyuDetails.Count(), x.RegisteredOfficeId, x.WaWarehouseFlag))
            .FirstOrDefault();
        if (header == null) return null;

        var details = db.THattyuDetails.AsNoTracking().Where(x => x.HaId == orderId)
            .OrderBy(x => x.HaDetailId)
            .Select(x => new DetailLineRow(x.HaDetailId, x.PrId, x.Pr.PrJcode ?? "", x.Pr.PrName,
                x.HaQuantity, x.Pr.TStocks.Where(s => s.StFlag == ActiveFlag).Sum(s => (int?)s.StQuantity) ?? 0,
                x.Pr.PrSafetyStock)).ToList();
        var receivedByProduct = db.TWarehousingDetails.AsNoTracking()
            .Where(x => x.Wa.HaId == orderId && (x.Wa.WaFlag == ActiveFlag || x.Wa.WaFlag == HiddenFlag)
                    && x.Wa.WaShelfFlag == WarehousingService.ConfirmedState)
            .GroupBy(x => x.PrId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.WaQuantity) })
            .ToDictionary(x => x.ProductId, x => x.Quantity);
        // Legacy duplicate lines have no line-level receipt relationship. Show a single total per product.
        var lines = details.GroupBy(x => x.ProductId).Select(group =>
        {
            var first = group.First();
            return new PurchaseOrderLineRecord(first.DetailId, first.ProductId,
                string.IsNullOrWhiteSpace(first.ProductCode) ? $"P-{first.ProductId:0000}" : first.ProductCode,
                first.ProductName, group.Sum(x => x.OrderedQuantity),
                receivedByProduct.GetValueOrDefault(first.ProductId), first.CurrentStock, first.SafetyStock);
        }).ToArray();
        var order = new PurchaseOrderRecord(header.Id, header.OrderDate, header.MakerId, header.MakerName,
            header.EmployeeId, header.EmployeeName, header.ManagementFlag, header.HiddenReason,
            header.ProductLineCount, lines.Sum(x => x.OrderedQuantity), lines.Sum(x => x.ReceivedQuantity))
        {
            WarehouseFlag = header.WarehouseFlag,
            RegisteredOfficeId = header.RegisteredOfficeId,
            RecordedProgress = receivedByProduct.Keys.Except(lines.Select(x => x.ProductId)).Any() ? PurchaseReceiptStatus.Unknown
                : PurchaseProgress.Classify(lines.Select(x => (x.OrderedQuantity, x.ReceivedQuantity)).ToArray(), header.ProductLineCount),
            RecordedRemaining = lines.Sum(x => x.RemainingQuantity)
        };
        var contact = db.THattyus.AsNoTracking().Where(x => x.HaId == orderId && (actor!.IsAdministrator || (x.HaFlag == ActiveFlag && x.RegisteredOfficeId == actor.OfficeId)))
            .Select(x => new
            {
                x.Ma.MaPhone,
                SoName = db.MSalesOffices.Where(office => office.SoId == x.RegisteredOfficeId)
                    .Select(office => office.SoName).FirstOrDefault(),
                SoPhone = db.MSalesOffices.Where(office => office.SoId == x.RegisteredOfficeId)
                    .Select(office => office.SoPhone).FirstOrDefault()
            }).FirstOrDefault();
        return new(order, lines)
        {
            Receipts = QueryReceipts(db, actor!.IsAdministrator, actor.OfficeId, orderId).ToArray(),
            SupplierPhone = contact?.MaPhone ?? "",
            OfficeName = contact?.SoName ?? "",
            OfficePhone = contact?.SoPhone ?? ""
        };
    }

    public static IReadOnlyList<PurchaseReceiptRecord> LoadReceipts()
    {
        EnsureCanView();
        using var db = new SalesManagementContext();
        var actor = ResolveActor(db, AppSession.Employee!.EmId);
        EnsureCanView(actor);
        return QueryReceipts(db, actor!.IsAdministrator, actor.OfficeId, null).ToArray();
    }

    private static IQueryable<PurchaseReceiptRecord> QueryReceipts(SalesManagementContext db, bool administrator, int officeId, int? orderId)
    {
        return db.TWarehousings.AsNoTracking()
            .Where(x => (administrator || (x.WaFlag == ActiveFlag && x.Ha.HaFlag == ActiveFlag && x.Ha.RegisteredOfficeId == officeId))
                && (!orderId.HasValue || x.HaId == orderId.Value))
            .OrderByDescending(x => x.WaDate).ThenByDescending(x => x.WaId)
            .Select(x => new PurchaseReceiptRecord(x.WaId, x.HaId, x.WaDate, x.Ha.Ma.MaName,
                x.Em == null ? "未記録" : x.Em.EmName, x.WaShelfFlag, x.WaFlag,
                x.TWarehousingDetails.Sum(d => (int?)d.WaQuantity) ?? 0));
    }

    public static string FormatCode(int id) => $"PO-{id:000000}";

    private static IReadOnlyList<PurchaseProductOption> LoadProductOptions(SalesManagementContext db, PurchaseActor actor)
    {
        var stocks = db.TStocks.AsNoTracking().Where(x => x.StFlag == ActiveFlag)
            .GroupBy(x => x.PrId).Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.StQuantity) })
            .ToDictionary(x => x.ProductId, x => x.Quantity);
        var scopedPurchases = db.THattyus.AsNoTracking()
            .Where(x => x.HaFlag == ActiveFlag || x.HaFlag == HiddenFlag);
        if (!actor.IsAdministrator)
            scopedPurchases = scopedPurchases.Where(x => x.RegisteredOfficeId == actor.OfficeId);

        var awaitingArrival = scopedPurchases.Where(x => x.WaWarehouseFlag == 0).Select(x => x.HaId);
        var awaitingShelving = scopedPurchases.Where(x => x.WaWarehouseFlag == 1).Select(x => x.HaId);
        var outstandingByProduct = db.THattyuDetails.AsNoTracking()
            .Where(x => awaitingArrival.Contains(x.HaId))
            .GroupBy(x => x.PrId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.HaQuantity) })
            .ToDictionary(x => x.ProductId, x => x.Quantity);
        var pendingShelvingByProduct = db.TWarehousingDetails.AsNoTracking()
            .Where(x => awaitingShelving.Contains(x.Wa.HaId)
                && (x.Wa.WaFlag == ActiveFlag || x.Wa.WaFlag == HiddenFlag)
                && x.Wa.WaShelfFlag == WarehousingService.UnconfirmedState
                && x.WaQuantity > 0)
            .GroupBy(x => x.PrId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => (long)x.WaQuantity) })
            .ToDictionary(x => x.ProductId, x => x.Quantity);
        return db.MProducts.AsNoTracking().Where(x => x.PrFlag == ActiveFlag && x.Ma.MaFlag == ActiveFlag)
            .OrderBy(x => x.Ma.MaName).ThenBy(x => x.PrName)
            .Select(x => new { x.PrId, x.MaId, Code = x.PrJcode ?? "", x.PrName, MakerName = x.Ma.MaName, x.PrSafetyStock })
            .ToList()
            .Select(x => new PurchaseProductOption(x.PrId, x.MaId, x.Code, x.PrName, x.MakerName,
                stocks.GetValueOrDefault(x.PrId), x.PrSafetyStock, outstandingByProduct.GetValueOrDefault(x.PrId))
            {
                PendingShelvingQuantity = pendingShelvingByProduct.GetValueOrDefault(x.PrId)
            }).ToArray();
    }

    private static PurchaseActor? ResolveActor(SalesManagementContext db, int actorId)
    {
        if (AppSession.Employee is not { EmFlag: ActiveFlag } session || session.EmId != actorId) return null;
        var role = db.MEmployees.AsNoTracking()
            .Where(x => x.EmId == actorId && x.EmFlag == ActiveFlag && x.Po.PoFlag == 0 && x.SoId == session.SoId)
            .Select(x => x.Po.PoName).FirstOrDefault();
        if (role == null || !string.Equals(role, AppSession.Role, StringComparison.Ordinal)) return null;
        var isAdmin = role == "管理者";
        var isLogistics = role == "物流";
        return new(actorId, session.SoId, isAdmin, isLogistics);
    }

    private static void EnsureCanView()
    {
        if (!CanView) throw new UnauthorizedAccessException("発注情報を表示する権限がありません。");
    }

    private static void EnsureCanView(PurchaseActor? actor)
    {
        if (actor is not { IsAllowedPurchaser: true })
            throw new UnauthorizedAccessException("発注情報を表示する権限がありません。管理者または物流担当としてログインしてください。");
    }

    private static PurchaseOperationResult Denied(string message) => new(false, null, message);

    private sealed record HeaderRow(int Id, DateTime OrderDate, int MakerId, string MakerName,
        int EmployeeId, string EmployeeName, int ManagementFlag, string? HiddenReason, int ProductLineCount, int? RegisteredOfficeId, int? WarehouseFlag);
    private sealed record LineAggregate(int OrderId, int ProductId, int Quantity);
    private sealed record DetailLineRow(int DetailId, int ProductId, string ProductCode, string ProductName,
        int OrderedQuantity, int CurrentStock, int SafetyStock);
    private sealed record PurchaseActor(int EmployeeId, int OfficeId, bool IsAdministrator, bool IsLogistics)
    {
        public bool IsAllowedPurchaser => IsAdministrator || IsLogistics;
    }
}
