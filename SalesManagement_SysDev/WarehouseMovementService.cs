using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public enum WarehouseMovementKind { Inbound, Outbound }
public enum WarehouseManagementTab { All, Visible, Hidden }
public enum WarehouseBusinessStatusFilter { All, Unconfirmed, Confirmed }

public sealed record WarehouseLookupOption(int Id, string Name);
public sealed record WarehouseMovementSearchCriteria(string Keyword, DateTime? StartDate, DateTime? EndDate,
    int? PrimaryId, int? EmployeeId, WarehouseBusinessStatusFilter Status)
{
    public string ReceiptNumber { get; init; } = "";
    public string PurchaseNumber { get; init; } = "";
    public string Supplier { get; init; } = "";
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public DateTime? PurchaseFrom { get; init; }
    public DateTime? PurchaseTo { get; init; }
}
public sealed record ReceiptProductSearch(int Id, string Code, string Name);
public sealed record WarehouseMovementSummary(int Total, int Unconfirmed, int Confirmed);
public sealed record WarehouseMovementDirectory(IReadOnlyList<WarehouseMovementRecord> Records,
    IReadOnlyList<WarehouseLookupOption> PrimaryOptions, IReadOnlyList<WarehouseLookupOption> Employees,
    WarehouseMovementSummary Summary);
public sealed record WarehouseMovementLineInput(int ProductId, int Quantity);
public sealed record WarehouseMovementLine(int ProductId, string ProductCode, string ProductName, int Quantity,
    int? CurrentStock)
{
    public bool IsAwaitingQuantity { get; init; }
    public int? SafetyStock { get; init; }
    public string SafetyStockText => SafetyStock is >= 0 ? SafetyStock.Value.ToString("N0") : "未記録";
    public string StockConditionText => new InventoryRowData(null, ProductId, ProductCode, ProductName, "", "", "", "",
        default, CurrentStock, SafetyStock, 0).ConditionLabel;
    public string QuantityText => IsAwaitingQuantity ? "未入力" : Quantity.ToString("N0");
    public string CurrentStockText => CurrentStock?.ToString("N0") ?? "要確認";
    public string InventoryImpactText => CurrentStock.HasValue
        ? $"{CurrentStock.Value:N0} → {(long)CurrentStock.Value + Quantity:N0}"
        : "在庫情報を確認";
}
public sealed record WarehouseMovementRecord(WarehouseMovementKind Kind, int Id, int ReferenceId, int PrimaryFilterId,
    string PartnerName, string OfficeName, int? OfficeId, int? EmployeeId, string EmployeeName,
    DateTime? MovementDate, int? BusinessState, int ManagementFlag, string? HiddenReason,
    int LineCount, int Quantity, string ProductSearchText)
{
    public DateTime? PurchaseDate { get; init; }
    public bool IsAwaitingReceipt { get; init; }
    public long? OrderedQuantity { get; init; }
    public IReadOnlyList<ReceiptProductSearch> ReceiptProducts { get; init; } = [];
    public int? RelatedOrderId { get; init; }
    public int? CustomerId { get; init; }
    public ArrivalRecord? RelatedArrival { get; init; }
    public OutboundArrivalStatus ArrivalStatus { get; init; }
    public string RelatedOrderCode => RelatedOrderId is int id ? $"CH-{id:000000}" : "未特定";
    public string ArrivalStatusLabel => ArrivalStatus switch
    {
        OutboundArrivalStatus.Pending => "入荷未確定",
        OutboundArrivalStatus.Completed => "入荷済み",
        OutboundArrivalStatus.Review => "要確認",
        _ => "未連携"
    };
    public string Code => IsAwaitingReceipt ? "未採番"
        : Kind == WarehouseMovementKind.Inbound ? $"IN-{Id:000000}" : $"OUT-{Id:000000}";
    public string ReferenceCode => Kind == WarehouseMovementKind.Inbound
        ? $"PO-{ReferenceId:000000}" : RelatedOrderId.HasValue ? RelatedOrderCode : $"ORD-{ReferenceId:000000}";
    public string PartnerColumnHeader => Kind == WarehouseMovementKind.Inbound ? "メーカー" : "出庫先営業所";
    public string PartnerDisplay => Kind == WarehouseMovementKind.Inbound ? PartnerName : OfficeName;
    public string MovementDateLabel => Kind == WarehouseMovementKind.Inbound ? "入庫日" : "出庫日";
    public string MovementDateText => MovementDate?.ToString("yyyy/MM/dd") ?? "未設定";
    public string QuantityLabel => Kind == WarehouseMovementKind.Inbound ? "入庫数量" : "出庫数量";
    public string BusinessStatusLabel => IsAwaitingReceipt ? "入庫待ち" : (Kind, BusinessState) switch
    {
        (WarehouseMovementKind.Inbound, WarehousingService.ConfirmedState) => "確定済み",
        (WarehouseMovementKind.Outbound, OutboundService.ConfirmedState) => "確定済み",
        (_, WarehousingService.UnconfirmedState) => "未確定",
        _ => "要確認"
    };
    public string ManagementLabel => ManagementFlag switch
    {
        WarehousingService.ActiveFlag => "表示中",
        WarehousingService.HiddenFlag => "非表示",
        _ => "要確認"
    };
    public bool IsConfirmed => BusinessState == WarehousingService.ConfirmedState;
    public bool IsUnconfirmed => BusinessState == WarehousingService.UnconfirmedState;
}
public sealed record WarehouseMovementDetail(WarehouseMovementRecord Record,
    IReadOnlyList<WarehouseMovementLine> Lines)
{
    public PurchaseOrderDetail? RelatedPurchase { get; init; }
}
public sealed record WarehouseMovementOperationResult(bool Succeeded, string Message,
    int? RecordId = null, int? RelatedRecordId = null);

public static class WarehouseMovementRules
{
    public static string? ValidateDateRange(WarehouseMovementSearchCriteria criteria) =>
        criteria.StartDate.HasValue && criteria.EndDate.HasValue
        && criteria.StartDate.Value.Date > criteria.EndDate.Value.Date
            ? "開始日は終了日以前の日付にしてください。" : null;

    public static string? ValidateReceiptSearch(WarehouseMovementSearchCriteria criteria)
    {
        if (criteria.PurchaseFrom?.Date > criteria.PurchaseTo?.Date) return "発注日の開始日は終了日以前にしてください。";
        if (criteria.ReceiptNumber.Trim().Length > 0 && ParseIdentifier(criteria.ReceiptNumber, "IN-", "RC-") == null)
            return "入庫番号は数字または IN-000001 形式で入力してください。";
        if (criteria.PurchaseNumber.Trim().Length > 0 && ParseIdentifier(criteria.PurchaseNumber, "PO-") == null)
            return "発注番号は数字または PO-000001 形式で入力してください。";
        return ValidateDateRange(criteria);
    }

    public static bool Matches(WarehouseMovementRecord record, WarehouseMovementSearchCriteria criteria,
        WarehouseManagementTab tab, bool canViewHidden)
    {
        if (ValidateDateRange(criteria) != null) return false;
        if (tab == WarehouseManagementTab.Visible && record.ManagementFlag != WarehousingService.ActiveFlag) return false;
        if (tab == WarehouseManagementTab.Hidden
            && (!canViewHidden || record.ManagementFlag != WarehousingService.HiddenFlag)) return false;
        if (tab == WarehouseManagementTab.All && !canViewHidden
            && record.ManagementFlag != WarehousingService.ActiveFlag) return false;
        if (criteria.StartDate.HasValue && (!record.MovementDate.HasValue
            || record.MovementDate.Value.Date < criteria.StartDate.Value.Date)) return false;
        if (criteria.EndDate.HasValue && (!record.MovementDate.HasValue
            || record.MovementDate.Value.Date > criteria.EndDate.Value.Date)) return false;
        if (criteria.PrimaryId.HasValue && record.PrimaryFilterId != criteria.PrimaryId.Value) return false;
        if (criteria.EmployeeId.HasValue && record.EmployeeId != criteria.EmployeeId.Value) return false;
        if (criteria.Status == WarehouseBusinessStatusFilter.Unconfirmed && !record.IsUnconfirmed) return false;
        if (criteria.Status == WarehouseBusinessStatusFilter.Confirmed && !record.IsConfirmed) return false;

        if (record.Kind == WarehouseMovementKind.Inbound)
        {
            if (ValidateReceiptSearch(criteria) != null) return false;
            if (ParseIdentifier(criteria.ReceiptNumber, "IN-", "RC-") is { } receiptId && receiptId != record.Id) return false;
            if (ParseIdentifier(criteria.PurchaseNumber, "PO-") is { } purchaseId && purchaseId != record.ReferenceId) return false;
            if (!Contains(record.PartnerName, criteria.Supplier.Trim())) return false;
            if (criteria.PurchaseFrom.HasValue && (!record.PurchaseDate.HasValue || record.PurchaseDate.Value.Date < criteria.PurchaseFrom.Value.Date)) return false;
            if (criteria.PurchaseTo.HasValue && (!record.PurchaseDate.HasValue || record.PurchaseDate.Value.Date > criteria.PurchaseTo.Value.Date)) return false;
            var code = criteria.ProductCode.Trim();
            var name = criteria.ProductName.Trim();
            if ((code.Length > 0 || name.Length > 0)
                && !record.ReceiptProducts.Any(p => (code.Length == 0 || ParseIdentifier(code, "P-") == p.Id
                    || Contains(p.Code, code) || Contains($"P-{p.Id:0000}", code)) && Contains(p.Name, name))) return false;
        }

        var keyword = criteria.Keyword.Trim();
        if (keyword.Length == 0) return true;
        if (record.Kind == WarehouseMovementKind.Inbound
            && (ParseIdentifier(keyword, "IN-", "RC-") == record.Id || ParseIdentifier(keyword, "PO-") == record.ReferenceId)) return true;
        return Contains(record.Code, keyword) || record.Id.ToString().Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
            || Contains(record.ReferenceCode, keyword) || record.ReferenceId.ToString().Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
            || Contains(record.PartnerName, keyword) || Contains(record.OfficeName, keyword)
            || Contains(record.EmployeeName, keyword) || Contains(record.ProductSearchText, keyword);
    }

    private static int? ParseIdentifier(string text, params string[] prefixes)
    {
        var value = text.Trim();
        foreach (var prefix in prefixes)
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { value = value[prefix.Length..]; break; }
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
    }

    public static string? ValidateConfirmation(int managementFlag, int? state)
    {
        if (managementFlag != WarehousingService.ActiveFlag) return "表示中のデータだけ確定できます。";
        if (state == WarehousingService.ConfirmedState) return "すでに確定済みです。重複処理は行いません。";
        return state == WarehousingService.UnconfirmedState ? null : "状態を確認できないため確定できません。";
    }

    public static string? ValidateHide(int managementFlag, int? state)
    {
        if (managementFlag != WarehousingService.ActiveFlag) return "通常表示のデータだけ非表示にできます。";
        return state == WarehousingService.ConfirmedState ? null : "未確定のデータは非表示にできません。先に確定してください。";
    }

    public static string? ValidateLines(IReadOnlyCollection<WarehouseMovementLineInput> lines) =>
        lines.Count == 0 || lines.Any(x => x.ProductId <= 0 || x.Quantity <= 0)
            ? "商品と1以上の数量を持つ明細が必要です。" : null;

    public static bool LinesMatch(IReadOnlyCollection<WarehouseMovementLineInput> expected,
        IReadOnlyCollection<WarehouseMovementLineInput> actual)
    {
        if (ValidateLines(expected) != null || ValidateLines(actual) != null) return false;
        var expectedByProduct = expected.GroupBy(x => x.ProductId)
            .ToDictionary(x => x.Key, x => x.Sum(line => (long)line.Quantity));
        var actualByProduct = actual.GroupBy(x => x.ProductId)
            .ToDictionary(x => x.Key, x => x.Sum(line => (long)line.Quantity));
        return expectedByProduct.Count == actualByProduct.Count
            && expectedByProduct.All(x => actualByProduct.TryGetValue(x.Key, out var quantity) && quantity == x.Value);
    }

    public static string? ValidateFullReceipt(DateTime? receivedOn, IReadOnlyCollection<WarehouseMovementLineInput> ordered,
        IReadOnlyCollection<WarehouseMovementLineInput> actual)
    {
        if (receivedOn is not { } date || date == default || date.Date > DateTime.Today)
            return "実際の入庫日を入力してください。";
        if (ValidateLines(ordered) != null) return "発注明細の数量を確認してください。";
        if (ValidateLines(actual) != null) return "全明細の実入庫数量を入力してください。";
        if (actual.Count != ordered.Count || actual.Select(line => line.ProductId).Distinct().Count() != actual.Count
            || !LinesMatch(ordered, actual))
            return "すべての商品の実入庫数量を発注数量と一致させてください。";
        return null;
    }

    public static string? ValidateUniqueSourceCount(int count) => count switch
    {
        1 => null,
        0 => "対応する注文・明細を確認できません。",
        _ => "対応する注文を一意に特定できません。データを確認してください。"
    };

    private static bool Contains(string value, string keyword) =>
        value.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);
}

public static class WarehousingService
{
    public const int ActiveFlag = 0;
    public const int HiddenFlag = 2;
    public const int UnconfirmedState = 0;
    public const int ConfirmedState = 1;

    public static bool CanView => WarehouseActorResolver.CanUse;
    public static bool CanConfirm => CanView;
    public static bool CanViewHidden => WarehouseActorResolver.CanAdmin;
    public static bool CanHide => CanViewHidden;
    public static bool CanRestore => CanHide;
    public static bool IsRestorable(WarehouseMovementRecord record) =>
        record.Kind == WarehouseMovementKind.Inbound && record.ManagementFlag == HiddenFlag && record.IsConfirmed
        && !string.IsNullOrWhiteSpace(record.HiddenReason);

    public static WarehouseMovementDirectory Load()
    {
        WarehouseActorResolver.EnsureCanUse();
        using var db = new SalesManagementContext();
        var actor = WarehouseActorResolver.Resolve(db, AppSession.Employee!.EmId);
        WarehouseActorResolver.EnsureCanUse(actor);

        var query = db.TWarehousings.AsNoTracking();
        if (!actor!.IsAdministrator) query = query.Where(x => x.WaFlag == ActiveFlag);
        var headers = query.OrderByDescending(x => x.WaDate).ThenByDescending(x => x.WaId)
            .Select(x => new
            {
                x.WaId, x.HaId, MakerId = x.Ha.MaId, x.WaDate, x.WaShelfFlag, x.WaFlag, x.WaHidden, x.EmId,
                EmployeeName = x.Em == null ? "未設定" : x.Em.EmName,
                MakerName = x.Ha.Ma.MaName,
                PurchaseDate = (DateTime?)(actor.IsAdministrator || x.Ha.HaFlag == ActiveFlag ? x.Ha.HaDate : null),
                OrderedQuantity = actor.IsAdministrator || x.Ha.HaFlag == ActiveFlag
                    ? x.Ha.THattyuDetails.Sum(d => (long?)d.HaQuantity) ?? 0L : (long?)null,
                LineCount = x.TWarehousingDetails.Count(),
                Quantity = x.TWarehousingDetails.Sum(d => (int?)d.WaQuantity) ?? 0
            }).ToList();

        var ids = headers.Select(x => x.WaId).ToArray();
        var productsByReceipt = ids.Length == 0 ? new Dictionary<int, ReceiptProductSearch[]>()
            : db.TWarehousingDetails.AsNoTracking().Where(x => ids.Contains(x.WaId))
                .Select(x => new { x.WaId, x.PrId, Code = x.Pr.PrJcode ?? "", x.Pr.PrName })
                .ToList().GroupBy(x => x.WaId)
                .ToDictionary(x => x.Key, x => x.Select(y => new ReceiptProductSearch(y.PrId, y.Code, y.PrName)).ToArray());
        var receipts = headers.Select(x =>
        {
            var products = productsByReceipt.GetValueOrDefault(x.WaId) ?? [];
            return new WarehouseMovementRecord(WarehouseMovementKind.Inbound,
                x.WaId, x.HaId, x.MakerId, x.MakerName, "倉庫情報なし", null, x.EmId, x.EmployeeName,
                x.WaDate, x.WaShelfFlag, x.WaFlag, x.WaHidden, x.LineCount, x.Quantity,
                string.Join(" ", products.Select(p => $"P-{p.Id:0000} {p.Code} {p.Name}")))
            { PurchaseDate = x.PurchaseDate, OrderedQuantity = x.OrderedQuantity, ReceiptProducts = products };
        });
        var awaiting = PurchaseOrderService.AwaitingReceipt(db)
            .OrderByDescending(order => order.HaDate).ThenByDescending(order => order.HaId)
            .Select(order => new
            {
                order.HaId, order.HaDate, order.MaId, order.Ma.MaName,
                OrderedQuantity = order.THattyuDetails.Sum(line => (long?)line.HaQuantity) ?? 0L,
                Products = order.THattyuDetails.OrderBy(line => line.HaDetailId)
                    .Select(line => new ReceiptProductSearch(line.PrId, line.Pr.PrJcode ?? "", line.Pr.PrName)).ToList()
            }).ToList();
        var records = awaiting.Select(order => AwaitingReceiptRecord(order.HaId, order.HaDate, order.MaId,
            order.MaName, order.OrderedQuantity, order.Products)).Concat(receipts).ToArray();
        var makers = db.MMakers.AsNoTracking().Where(x => x.MaFlag == ActiveFlag).OrderBy(x => x.MaName)
            .Select(x => new WarehouseLookupOption(x.MaId, x.MaName)).ToList();
        var employees = db.MEmployees.AsNoTracking().Where(x => x.EmFlag == ActiveFlag && x.Po.PoFlag == ActiveFlag)
            .OrderBy(x => x.EmName).Select(x => new WarehouseLookupOption(x.EmId, x.EmName)).ToList();
        return new(records, makers, employees, Summarize(records));
    }

    public static WarehouseMovementDetail? LoadDetail(int id) => LoadDetail(id, includePurchase: true);

    internal static WarehouseMovementDetail? LoadAwaitingReceipt(int orderId)
    {
        WarehouseActorResolver.EnsureCanUse();
        using var db = new SalesManagementContext();
        WarehouseActorResolver.EnsureCanUse(WarehouseActorResolver.Resolve(db, AppSession.Employee!.EmId));
        if (!PurchaseOrderService.AwaitingReceipt(db).Any(order => order.HaId == orderId)) return null;

        var purchase = PurchaseOrderService.LoadDetail(orderId);
        if (purchase is not { Order: { IsConfirmedForReceiving: true, ManagementFlag: ActiveFlag }, Receipts.Count: 0 })
            return null;

        var order = purchase.Order;
        var products = purchase.Lines.Select(line =>
            new ReceiptProductSearch(line.ProductId, line.ProductCode, line.ProductName)).ToArray();
        var record = AwaitingReceiptRecord(order.Id, order.OrderDate, order.MakerId, order.MakerName, order.OrderedQuantity, products);
        var lines = purchase.Lines.Select(line =>
            new WarehouseMovementLine(line.ProductId, line.ProductCode, line.ProductName, 0, null) { IsAwaitingQuantity = true }).ToArray();
        return new(record, lines) { RelatedPurchase = purchase };
    }

    private static WarehouseMovementRecord AwaitingReceiptRecord(int orderId, DateTime orderDate, int makerId,
        string makerName, long orderedQuantity, IReadOnlyList<ReceiptProductSearch> products) =>
        new(WarehouseMovementKind.Inbound, 0, orderId, makerId, makerName, "倉庫情報なし", null, null, "未設定",
            null, UnconfirmedState, ActiveFlag, null, products.Count, 0,
            string.Join(" ", products.Select(product => $"P-{product.Id:0000} {product.Code} {product.Name}")))
        {
            IsAwaitingReceipt = true, PurchaseDate = orderDate, OrderedQuantity = orderedQuantity, ReceiptProducts = products
        };

    internal static WarehouseMovementDetail? LoadDetail(int id, bool includePurchase)
    {
        if (id <= 0) return null;
        WarehouseActorResolver.EnsureCanUse();
        using var db = new SalesManagementContext();
        var actor = WarehouseActorResolver.Resolve(db, AppSession.Employee!.EmId);
        WarehouseActorResolver.EnsureCanUse(actor);
        var query = db.TWarehousings.AsNoTracking().Where(x => x.WaId == id);
        if (!actor!.IsAdministrator) query = query.Where(x => x.WaFlag == ActiveFlag);
        var header = query.Select(x => new
        {
            x.WaId, x.HaId, MakerId = x.Ha.MaId, x.WaDate, x.WaShelfFlag, x.WaFlag, x.WaHidden, x.EmId,
            EmployeeName = x.Em == null ? "未設定" : x.Em.EmName,
            MakerName = x.Ha.Ma.MaName,
            PurchaseDate = (DateTime?)(actor.IsAdministrator || x.Ha.HaFlag == ActiveFlag ? x.Ha.HaDate : null),
            LineCount = x.TWarehousingDetails.Count(),
            Quantity = x.TWarehousingDetails.Sum(d => (int?)d.WaQuantity) ?? 0
        }).SingleOrDefault();
        if (header == null) return null;

        var storedLines = db.TWarehousingDetails.AsNoTracking().Where(x => x.WaId == id).OrderBy(x => x.WaDetailId)
            .Select(x => new { x.PrId, Code = x.Pr.PrJcode ?? "", x.Pr.PrName, x.WaQuantity, x.Pr.PrSafetyStock }).ToList();
        var productIds = storedLines.Select(x => x.PrId).Distinct().ToArray();
        var stocks = productIds.Length == 0 ? new Dictionary<int, StockSummary>()
            : db.TStocks.AsNoTracking().Where(x => productIds.Contains(x.PrId))
                .GroupBy(x => x.PrId).Select(g => new StockSummary(g.Key, g.Count(x => x.StFlag == ActiveFlag),
                    g.Count(), g.Where(x => x.StFlag == ActiveFlag).Sum(x => (int?)x.StQuantity) ?? 0))
                .ToDictionary(x => x.ProductId);
        var lines = storedLines.Select(x =>
        {
            var stock = stocks.GetValueOrDefault(x.PrId);
            int? current = stock is { ActiveCount: 1 } ? stock.ActiveQuantity : null;
            return new WarehouseMovementLine(x.PrId, string.IsNullOrWhiteSpace(x.Code) ? $"P-{x.PrId:0000}" : x.Code,
                x.PrName, x.WaQuantity, current) { SafetyStock = x.PrSafetyStock };
        }).ToArray();
        var record = new WarehouseMovementRecord(WarehouseMovementKind.Inbound, header.WaId, header.HaId, header.MakerId,
            header.MakerName, "倉庫情報なし", null, header.EmId, header.EmployeeName, header.WaDate,
            header.WaShelfFlag, header.WaFlag, header.WaHidden, header.LineCount, header.Quantity,
            string.Join(" ", lines.Select(x => $"{x.ProductCode} {x.ProductName}"))) { PurchaseDate = header.PurchaseDate };
        return new(record, lines) { RelatedPurchase = includePurchase && header.PurchaseDate.HasValue
            ? PurchaseOrderService.LoadDetail(header.HaId) : null };
    }

    public static string? ValidateHideReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? "非表示理由を入力してください。" : null;

    public static WarehouseMovementOperationResult ConfirmShelf(int actorId, int warehousingId,
        IReadOnlyCollection<WarehouseMovementLineInput>? expected = null)
    {
        using var db = new SalesManagementContext();
        return ConfirmShelfInContext(db, actorId, warehousingId, expected);
    }

    public static WarehouseMovementOperationResult ConfirmPurchaseReceipt(int actorId, int orderId, DateTime? receivedOn,
        IReadOnlyCollection<WarehouseMovementLineInput> actual)
    {
        if (!CanConfirm || AppSession.Employee?.EmId != actorId)
            return Denied("入庫を確定する権限がありません。");
        if (orderId <= 0) return Denied("関連する発注を確認してください。");

        try
        {
            using var db = new SalesManagementContext();
            using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
            var actor = WarehouseActorResolver.Resolve(db, actorId);
            if (actor == null) return Denied("入庫を確定する権限を確認できません。");

            var purchase = PurchaseOrderService.FindForUpdate(db, orderId);
            if (purchase is not { HaFlag: ActiveFlag, WaWarehouseFlag: PurchaseOrderService.ConfirmedState })
                return Denied("表示中の確定済み発注だけを入庫できます。");
            if (db.TWarehousings.Any(receipt => receipt.HaId == orderId))
                return Denied("この発注にはすでに関連入庫があります。入庫一覧を再読込してください。");

            var ordered = db.THattyuDetails.AsNoTracking().Where(line => line.HaId == orderId)
                .Select(line => new WarehouseMovementLineInput(line.PrId, line.HaQuantity)).ToList();
            if (WarehouseMovementRules.ValidateFullReceipt(receivedOn, ordered, actual) is { } validation)
                return Denied(validation);
            if (IncreaseStock(db, purchase, actual) is { } stockError) return Denied(stockError);

            var receipt = new TWarehousing
            {
                HaId = purchase.HaId,
                EmId = actor.EmployeeId,
                WaDate = receivedOn!.Value.Date,
                WaShelfFlag = ConfirmedState,
                WaFlag = ActiveFlag,
                TWarehousingDetails = actual.Select(line =>
                    new TWarehousingDetail { PrId = line.ProductId, WaQuantity = line.Quantity }).ToList()
            };
            db.TWarehousings.Add(receipt);
            db.SaveChanges();
            transaction.Commit();
            return new(true, $"入庫 IN-{receipt.WaId:000000} を確定し、商品在庫を更新しました。", receipt.WaId, purchase.HaId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Purchase receipt confirmation failed: {ex}");
            return Denied("入庫と在庫更新を保存できませんでした。再読込してからお試しください。");
        }
    }

    internal static WarehouseMovementOperationResult ConfirmShelfInContext(SalesManagementContext db, int actorId, int warehousingId,
        IReadOnlyCollection<WarehouseMovementLineInput>? expected)
    {
        if (!CanConfirm || AppSession.Employee?.EmId != actorId)
            return Denied("棚入れを確定する権限がありません。管理者または物流担当としてログインしてください。");
        if (warehousingId <= 0) return Denied("対象の入庫を確認してください。");

        try
        {
            using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
            var actor = WarehouseActorResolver.Resolve(db, actorId);
            WarehouseActorResolver.EnsureCanUse(actor);
            var receipt = db.TWarehousings.FromSqlInterpolated(
                $"SELECT * FROM dbo.T_Warehousing WITH (UPDLOCK, HOLDLOCK) WHERE WaID = {warehousingId}")
                .SingleOrDefault();
            if (receipt == null) return Denied("対象の入庫が見つかりません。");
            if (WarehouseMovementRules.ValidateConfirmation(receipt.WaFlag, receipt.WaShelfFlag) is { } stateError)
                return Denied(stateError);

            var purchase = db.THattyus.Include(x => x.THattyuDetails)
                .SingleOrDefault(x => x.HaId == receipt.HaId && (x.HaFlag == ActiveFlag || x.HaFlag == HiddenFlag));
            if (purchase == null) return Denied("関連する発注情報を確認できません。");
            var incoming = db.TWarehousingDetails.AsNoTracking().Where(x => x.WaId == warehousingId)
                .Select(x => new WarehouseMovementLineInput(x.PrId, x.WaQuantity)).ToList();
            if (WarehouseMovementRules.ValidateLines(incoming) is { } linesError) return Denied(linesError);
            if (expected != null && (expected.Count != incoming.Count || !WarehouseMovementRules.LinesMatch(expected, incoming)))
                return Denied("明細が更新されました。詳細を開き直して数量を確認してください。");
            var orderedProductIds = purchase.THattyuDetails.Select(x => x.PrId).ToHashSet();
            if (incoming.Any(x => !orderedProductIds.Contains(x.ProductId)))
                return Denied("入庫明細に発注明細にない商品が含まれています。");

            if (IncreaseStock(db, purchase, incoming) is { } stockError) return Denied(stockError);

            receipt.WaShelfFlag = ConfirmedState;
            db.SaveChanges();
            transaction.Commit();
            return new(true, "棚入れを確定し、商品在庫を更新しました。入庫日と既存の担当者情報は保持しています。", receipt.WaId);
        }
        catch (UnauthorizedAccessException ex) { return Denied(ex.Message); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Warehousing confirmation failed: {ex}");
            return Denied("棚入れと在庫更新を保存できませんでした。データベースの状態を確認してください。");
        }
    }

    private static string? IncreaseStock(SalesManagementContext db, THattyu purchase,
        IReadOnlyCollection<WarehouseMovementLineInput> incoming)
    {
        var quantities = incoming.GroupBy(line => line.ProductId)
            .ToDictionary(group => group.Key, group => group.Sum(line => (long)line.Quantity));
        var productIds = quantities.Keys.Order().ToArray();
        var products = db.MProducts.AsNoTracking().Where(product => productIds.Contains(product.PrId))
            .ToDictionary(product => product.PrId);
        var makerIds = products.Values.Select(product => product.MaId).Append(purchase.MaId).Distinct().ToArray();
        var makers = db.MMakers.AsNoTracking().Where(maker => makerIds.Contains(maker.MaId))
            .Select(maker => new { maker.MaId, maker.MaName, maker.MaFlag }).ToDictionary(maker => maker.MaId);

        foreach (var productId in productIds)
        {
            var code = $"P-{productId:0000}";
            if (!products.TryGetValue(productId, out var product))
                return $"商品 {code} が商品マスタに見つかりません。入庫明細の商品を確認してください。";

            var productLabel = $"商品 {code}「{product.PrName}」";
            if (product.PrFlag != ActiveFlag)
                return $"{productLabel} は有効ではありません。商品マスタの状態を確認してください。";
            if (!makers.TryGetValue(product.MaId, out var maker))
                return $"{productLabel} のメーカー（ID {product.MaId}）がメーカー情報に見つかりません。";

            var orderCode = PurchaseOrderService.FormatCode(purchase.HaId);
            if (!makers.TryGetValue(purchase.MaId, out var supplier))
                return $"{productLabel} の関連発注 {orderCode} の仕入先（メーカーID {purchase.MaId}）が見つかりません。";
            if (product.MaId != purchase.MaId)
                return $"{productLabel} のメーカー {maker.MaName}（ID {maker.MaId}）が、発注 {orderCode} の仕入先 {supplier.MaName}（ID {supplier.MaId}）と一致しません。発注書と入庫明細を確認してください。";
            if (maker.MaFlag != ActiveFlag)
                return $"{productLabel} のメーカー {maker.MaName}（ID {maker.MaId}）は有効ではありません。メーカー情報の状態を確認してください。";
        }

        foreach (var productId in productIds)
        {
            var stocks = db.TStocks.FromSqlInterpolated(
                $"SELECT * FROM dbo.T_Stock WITH (UPDLOCK, HOLDLOCK) WHERE PrID = {productId}").ToList();
            if (stocks.Count == 0)
            {
                BusinessService.EnsureStock(products[productId], db);
                db.SaveChanges();
                stocks = db.TStocks.FromSqlInterpolated(
                    $"SELECT * FROM dbo.T_Stock WITH (UPDLOCK, HOLDLOCK) WHERE PrID = {productId}").ToList();
            }
            var activeStocks = stocks.Where(stock => stock.StFlag == ActiveFlag).ToArray();
            if (activeStocks.Length != 1)
                return "商品在庫が未登録または重複しています。データを確認してください。";
            var stock = activeStocks[0];
            var nextQuantity = (long)stock.StQuantity + quantities[productId];
            if (stock.StQuantity < 0 || nextQuantity > int.MaxValue)
                return "在庫数量が保存可能な範囲を超えるため棚入れできません。";
            stock.StQuantity = (int)nextQuantity;
        }
        return null;
    }

    public static WarehouseMovementOperationResult Hide(int actorId, int warehousingId, string? reason)
    {
        using var db = new SalesManagementContext();
        return HideInContext(db, actorId, warehousingId, reason);
    }

    internal static WarehouseMovementOperationResult HideInContext(SalesManagementContext db, int actorId, int warehousingId, string? reason)
    {
        if (!CanHide || AppSession.Employee?.EmId != actorId)
            return Denied("入庫を非表示にする権限がありません。管理者としてログインしてください。");
        if (ValidateHideReason(reason) is { } validation) return Denied(validation);
        try
        {
            using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
            var actor = WarehouseActorResolver.Resolve(db, actorId);
            if (actor is not { IsAdministrator: true }) return Denied("入庫を非表示にする権限がありません。");
            var receipt = db.TWarehousings.FromSqlInterpolated(
                $"SELECT * FROM dbo.T_Warehousing WITH (UPDLOCK, HOLDLOCK) WHERE WaID = {warehousingId}")
                .SingleOrDefault();
            if (receipt == null) return Denied("対象の入庫が見つかりません。");
            if (WarehouseMovementRules.ValidateHide(receipt.WaFlag, receipt.WaShelfFlag) is { } stateError)
                return Denied(stateError);
            receipt.WaFlag = HiddenFlag;
            receipt.WaHidden = reason!.Trim();
            db.SaveChanges();
            transaction.Commit();
            return new(true, "入庫を非表示にしました。在庫・発注・入庫明細は変更していません。", receipt.WaId);
        }
        catch (UnauthorizedAccessException ex) { return Denied(ex.Message); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Warehousing hide failed: {ex}");
            return Denied("入庫を非表示にできませんでした。一覧を再読込してからお試しください。");
        }
    }

    public static WarehouseMovementOperationResult Restore(int actorId, WarehouseMovementRecord expected)
    {
        using var db = new SalesManagementContext();
        return RestoreInContext(db, actorId, expected);
    }

    internal static WarehouseMovementOperationResult RestoreInContext(SalesManagementContext db, int actorId, WarehouseMovementRecord expected)
    {
        if (!CanRestore || AppSession.Employee?.EmId != actorId)
            return Denied("入庫を復元する権限がありません。");
        if (!IsRestorable(expected)) return Denied("非表示理由が記録された確定済みの入庫だけ復元できます。");
        try
        {
            using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
            if (WarehouseActorResolver.Resolve(db, actorId) is not { IsAdministrator: true })
                return Denied("入庫を復元する権限がありません。");
            var receipt = db.TWarehousings.FromSqlInterpolated(
                $"SELECT * FROM dbo.T_Warehousing WITH (UPDLOCK, HOLDLOCK) WHERE WaID = {expected.Id}").SingleOrDefault();
            if (receipt == null || receipt.WaFlag != HiddenFlag || receipt.WaShelfFlag != ConfirmedState
                || receipt.WaHidden != expected.HiddenReason || receipt.HaId != expected.ReferenceId)
                return Denied("入庫の状態が変更されました。再読込してからお試しください。");
            receipt.WaFlag = ActiveFlag;
            db.SaveChanges();
            transaction.Commit();
            return new(true, "入庫を表示に戻しました。在庫と確定状態は変更していません。", receipt.WaId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Warehousing restore failed: {ex}");
            return Denied("入庫を復元できませんでした。データベースの状態を確認してください。");
        }
    }

    private static WarehouseMovementSummary Summarize(IEnumerable<WarehouseMovementRecord> records)
    {
        var visible = records.Where(x => x.ManagementFlag == ActiveFlag).ToArray();
        return new(visible.Length, visible.Count(x => x.IsUnconfirmed), visible.Count(x => x.IsConfirmed));
    }

    private static WarehouseMovementOperationResult Denied(string message) => new(false, message);
    private sealed record StockSummary(int ProductId, int ActiveCount, int TotalCount, int ActiveQuantity);
}

public static partial class OutboundService
{
    public const int ActiveFlag = 0;
    public const int HiddenFlag = 2;
    public const int UnconfirmedState = 0;
    public const int ConfirmedState = 1;

    public static bool CanView => WarehouseActorResolver.CanUse;
    public static bool CanConfirm => CanView;
    public static bool CanViewHidden => WarehouseActorResolver.CanAdmin;
    public static bool CanHide => CanViewHidden;

    public static WarehouseMovementDirectory Load()
    {
        WarehouseActorResolver.EnsureCanUse();
        using var db = new SalesManagementContext();
        var actor = WarehouseActorResolver.Resolve(db, AppSession.Employee!.EmId);
        WarehouseActorResolver.EnsureCanUse(actor);
        var query = db.TSyukkos.AsNoTracking();
        if (!actor!.IsAdministrator) query = query.Where(x => x.SoId == actor.OfficeId && x.SyFlag == ActiveFlag);
        var headers = query.OrderByDescending(x => x.SyDate).ThenByDescending(x => x.SyId)
            .Select(x => new
            {
                x.SyId, x.OrId, x.ClId, CustomerName = x.Cl.ClName, x.SoId, OfficeName = x.So.SoName,
                x.SyDate, x.SyStateFlag, x.SyFlag, x.SyHidden, x.EmId,
                EmployeeName = x.Em == null ? "未設定" : x.Em.EmName,
                LineCount = x.TSyukkoDetails.Count(),
                Quantity = x.TSyukkoDetails.Sum(d => (int?)d.SyQuantity) ?? 0
            }).ToList();
        var links = LoadLinks(db, query, actor.IsAdministrator);
        var records = headers.Select(x => new WarehouseMovementRecord(WarehouseMovementKind.Outbound,
            x.SyId, x.OrId, x.SoId, x.CustomerName, x.OfficeName, x.SoId, x.EmId, x.EmployeeName,
            x.SyDate, x.SyStateFlag, x.SyFlag, x.SyHidden, x.LineCount, x.Quantity,
            "") { CustomerId = x.ClId }).Select(links.Enrich).ToArray();
        var officesQuery = db.MSalesOffices.AsNoTracking().Where(x => x.SoFlag == ActiveFlag);
        var employeesQuery = db.MEmployees.AsNoTracking().Where(x => x.EmFlag == ActiveFlag && x.Po.PoFlag == ActiveFlag);
        if (!actor.IsAdministrator)
        {
            officesQuery = officesQuery.Where(x => x.SoId == actor.OfficeId);
            employeesQuery = employeesQuery.Where(x => x.SoId == actor.OfficeId);
        }
        var offices = officesQuery.OrderBy(x => x.SoName)
            .Select(x => new WarehouseLookupOption(x.SoId, x.SoName)).ToList();
        var employees = employeesQuery.OrderBy(x => x.EmName)
            .Select(x => new WarehouseLookupOption(x.EmId, x.EmName)).ToList();
        return new(records, offices, employees, Summarize(records));
    }

    public static WarehouseMovementDetail? LoadDetail(int id)
    {
        if (id <= 0) return null;
        WarehouseActorResolver.EnsureCanUse();
        using var db = new SalesManagementContext();
        var actor = WarehouseActorResolver.Resolve(db, AppSession.Employee!.EmId);
        WarehouseActorResolver.EnsureCanUse(actor);
        var query = db.TSyukkos.AsNoTracking().Where(x => x.SyId == id);
        if (!actor!.IsAdministrator) query = query.Where(x => x.SoId == actor.OfficeId && x.SyFlag == ActiveFlag);
        var header = query.Select(x => new
        {
            x.SyId, x.OrId, x.ClId, CustomerName = x.Cl.ClName, x.SoId, OfficeName = x.So.SoName,
            x.SyDate, x.SyStateFlag, x.SyFlag, x.SyHidden, x.EmId,
            EmployeeName = x.Em == null ? "未設定" : x.Em.EmName,
            LineCount = x.TSyukkoDetails.Count(),
            Quantity = x.TSyukkoDetails.Sum(d => (int?)d.SyQuantity) ?? 0
        }).SingleOrDefault();
        if (header == null) return null;
        var lines = db.TSyukkoDetails.AsNoTracking().Where(x => x.SyId == id).OrderBy(x => x.SyDetailId)
            .Select(x => new WarehouseMovementLine(x.PrId, x.Pr.PrJcode ?? "", x.Pr.PrName,
                x.SyQuantity, null)).ToList()
            .Select(x => string.IsNullOrWhiteSpace(x.ProductCode) ? x with { ProductCode = $"P-{x.ProductId:0000}" } : x).ToArray();
        var record = new WarehouseMovementRecord(WarehouseMovementKind.Outbound, header.SyId,
            header.OrId, header.SoId, header.CustomerName, header.OfficeName, header.SoId, header.EmId,
            header.EmployeeName, header.SyDate, header.SyStateFlag, header.SyFlag, header.SyHidden,
            header.LineCount, header.Quantity, string.Join(" ", lines.Select(x => $"{x.ProductCode} {x.ProductName}")))
            { CustomerId = header.ClId };
        return new(LoadLinks(db, query, actor.IsAdministrator).Enrich(record), lines);
    }

    public static string? ValidateHideReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? "非表示理由を入力してください。" : null;

    public static WarehouseMovementOperationResult Confirm(int actorId, int syukkoId)
    {
        using var db = new SalesManagementContext();
        return ConfirmInContext(db, actorId, syukkoId);
    }

    internal static WarehouseMovementOperationResult ConfirmInContext(SalesManagementContext db, int actorId, int syukkoId)
    {
        if (!CanConfirm || AppSession.Employee?.EmId != actorId)
            return Denied("出庫を確定する権限がありません。管理者または物流担当としてログインしてください。");
        if (syukkoId <= 0) return Denied("対象の出庫を確認してください。");
        try
        {
            using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
            var actor = WarehouseActorResolver.Resolve(db, actorId);
            WarehouseActorResolver.EnsureCanUse(actor);
            var outbound = db.TSyukkos.FromSqlInterpolated(
                $"SELECT * FROM dbo.T_Syukko WITH (UPDLOCK, HOLDLOCK) WHERE SyID = {syukkoId}")
                .SingleOrDefault();
            if (outbound == null) return Denied("対象の出庫が見つかりません。");
            if (WarehouseMovementRules.ValidateConfirmation(outbound.SyFlag, outbound.SyStateFlag) is { } stateError)
                return Denied(stateError);
            if (!actor!.IsAdministrator && outbound.SoId != actor.OfficeId)
                return Denied("所属営業所以外の出庫は確定できません。");

            var officeIsActive = db.MSalesOffices.AsNoTracking().Any(x => x.SoId == outbound.SoId && x.SoFlag == ActiveFlag);
            var customerIsActive = db.MClients.AsNoTracking().Any(x => x.ClId == outbound.ClId
                && x.SoId == outbound.SoId && x.ClFlag == ActiveFlag);
            var orderIsActive = db.TOrders.AsNoTracking().Any(x => x.OrId == outbound.OrId
                && x.ClId == outbound.ClId && x.SoId == outbound.SoId && x.OrFlag == 0 && x.OrStateFlag == 1);
            if (!officeIsActive || !customerIsActive || !orderIsActive)
                return Denied("注文・顧客・営業所の有効状態または関連を確認できません。");

            var sourceOrders = db.TChumons.AsNoTracking().Include(x => x.TChumonDetails)
                .Where(x => x.OrId == outbound.OrId && x.ClId == outbound.ClId && x.SoId == outbound.SoId
                    && x.ChFlag == ActiveFlag && x.ChStateFlag == 1).ToList();
            var lines = db.TSyukkoDetails.AsNoTracking().Where(x => x.SyId == syukkoId)
                .Select(x => new WarehouseMovementLineInput(x.PrId, x.SyQuantity)).ToArray();
            if (WarehouseMovementRules.ValidateLines(lines) is { } linesError) return Denied(linesError);
            var matchingOrders = sourceOrders.Where(order => WarehouseMovementRules.LinesMatch(lines,
                order.TChumonDetails.Select(x => new WarehouseMovementLineInput(x.PrId, x.ChQuantity)).ToArray())).ToArray();
            if (WarehouseMovementRules.ValidateUniqueSourceCount(matchingOrders.Length) is { } sourceError)
                return Denied(sourceError);
            var productIds = lines.Select(x => x.ProductId).Distinct().ToArray();
            if (db.MProducts.Count(x => productIds.Contains(x.PrId) && x.PrFlag == ActiveFlag) != productIds.Length)
                return Denied("出庫明細に無効または未登録の商品が含まれています。");

            // T_Arrival has no SyID. Lock and check the existing order/customer/office key to make
            // repeated or concurrent outbound confirmations unable to generate duplicate arrivals.
            var existingArrival = db.TArrivals.FromSqlInterpolated(
                    $"SELECT * FROM dbo.T_Arrival WITH (UPDLOCK, HOLDLOCK) WHERE OrID = {outbound.OrId} AND ClID = {outbound.ClId} AND SoID = {outbound.SoId}")
                .AsNoTracking().Any();
            if (existingArrival) return Denied("対応する入荷情報がすでに存在します。重複作成を防ぐため確定できません。");

            outbound.SyStateFlag = ConfirmedState;
            outbound.SyDate = DateTime.Today;
            outbound.EmId = actor.EmployeeId;
            var arrival = new TArrival
            {
                SoId = outbound.SoId,
                ClId = outbound.ClId,
                OrId = outbound.OrId,
                ArDate = null,
                ArStateFlag = 0,
                ArFlag = WarehousingService.ActiveFlag,
                TArrivalDetails = lines.Select(x => new TArrivalDetail
                {
                    PrId = x.ProductId,
                    ArQuantity = x.Quantity
                }).ToList()
            };
            db.TArrivals.Add(arrival);
            db.SaveChanges();
            transaction.Commit();
            return new(true, "出庫を確定し、未確定の入荷情報を作成しました。在庫は再度減算していません。",
                outbound.SyId, arrival.ArId);
        }
        catch (UnauthorizedAccessException ex) { return Denied(ex.Message); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Outbound confirmation failed: {ex}");
            return Denied("出庫確定と入荷情報の作成に失敗しました。データベースの状態を確認してください。");
        }
    }

    public static WarehouseMovementOperationResult Hide(int actorId, int syukkoId, string? reason)
    {
        if (!CanHide || AppSession.Employee?.EmId != actorId)
            return Denied("出庫を非表示にする権限がありません。管理者としてログインしてください。");
        if (ValidateHideReason(reason) is { } validation) return Denied(validation);
        using var db = new SalesManagementContext();
        try
        {
            using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
            var actor = WarehouseActorResolver.Resolve(db, actorId);
            if (actor is not { IsAdministrator: true }) return Denied("出庫を非表示にする権限がありません。");
            var outbound = db.TSyukkos.FromSqlInterpolated(
                $"SELECT * FROM dbo.T_Syukko WITH (UPDLOCK, HOLDLOCK) WHERE SyID = {syukkoId}")
                .SingleOrDefault();
            if (outbound == null) return Denied("対象の出庫が見つかりません。");
            if (WarehouseMovementRules.ValidateHide(outbound.SyFlag, outbound.SyStateFlag) is { } stateError)
                return Denied(stateError);
            outbound.SyFlag = HiddenFlag;
            outbound.SyHidden = reason!.Trim();
            db.SaveChanges();
            transaction.Commit();
            return new(true, "出庫を非表示にしました。入荷情報・注文・在庫数量は変更していません。", syukkoId);
        }
        catch (UnauthorizedAccessException ex) { return Denied(ex.Message); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Outbound hide failed: {ex}");
            return Denied("出庫を非表示にできませんでした。一覧を再読込してからお試しください。");
        }
    }

    private static WarehouseMovementSummary Summarize(IEnumerable<WarehouseMovementRecord> records)
    {
        var visible = records.Where(x => x.ManagementFlag == ActiveFlag).ToArray();
        return new(visible.Length, visible.Count(x => x.IsUnconfirmed), visible.Count(x => x.IsConfirmed));
    }

    private static WarehouseMovementOperationResult Denied(string message) => new(false, message);
}

internal static class WarehouseActorResolver
{
    public static bool CanUse => AppSession.Employee is { EmFlag: 0 }
        && (AppSession.IsAdmin || AppSession.IsLogistics);
    public static bool CanAdmin => AppSession.Employee is { EmFlag: 0 } && AppSession.IsAdmin;

    public static WarehouseActor? Resolve(SalesManagementContext db, int employeeId)
    {
        var session = AppSession.Employee;
        if (session is not { EmFlag: 0 } || session.EmId != employeeId) return null;
        var actor = db.MEmployees.AsNoTracking()
            .Where(x => x.EmId == employeeId && x.EmFlag == 0 && x.Po.PoFlag == 0)
            .Select(x => new WarehouseActor(x.EmId, x.SoId, x.Po.PoName)).SingleOrDefault();
        if (actor == null || actor.OfficeId != session.SoId
            || !string.Equals(actor.RoleName, AppSession.Role, StringComparison.Ordinal) || !actor.IsAllowed) return null;
        return actor;
    }

    public static void EnsureCanUse() { if (!CanUse) throw new UnauthorizedAccessException("入庫・出庫管理を利用する権限がありません。"); }
    public static void EnsureCanUse(WarehouseActor? actor)
    {
        if (actor is not { IsAllowed: true })
            throw new UnauthorizedAccessException("入庫・出庫管理を利用する権限がありません。ログイン状態を確認してください。");
    }
}

internal sealed record WarehouseActor(int EmployeeId, int OfficeId, string RoleName)
{
    public bool IsAdministrator => RoleName == "管理者";
    public bool IsAllowed => IsAdministrator || RoleName == "物流";
}
