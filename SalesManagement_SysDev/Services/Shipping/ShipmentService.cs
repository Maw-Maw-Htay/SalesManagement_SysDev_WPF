using System.Data;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public enum ShipmentManagementTab { All, Visible, Hidden }
public enum ShipmentStateFilter { All, Unconfirmed, Confirmed }

public sealed record ShipmentSearchCriteria(string Keyword, DateTime? StartDate, DateTime? EndDate,
    ShipmentStateFilter State);
public sealed record ShipmentSummary(int Total, int Unconfirmed, int Confirmed);
public sealed record ShipmentLineRecord(int ProductId, string ProductCode, string ProductName, int Quantity)
{
    public string QuantityText => Quantity.ToString("N0");
}
public sealed record ShipmentRecord(int Id, int OrderId, int CustomerId, string CustomerName, int OfficeId,
    string OfficeName, int? EmployeeId, string EmployeeName, DateTime? ConfirmedDate, int? State,
    int ManagementFlag, string? HiddenReason, int LineCount, int Quantity, string ProductSearchText = "")
{
    public string Code => $"SH-{Id:000000}";
    public string OrderCode => $"CH-{OrderId:000000}";
    public string ConfirmedDateText => ConfirmedDate?.ToString("yyyy/MM/dd") ?? "未確定";
    public string StateLabel => State switch
    {
        ShipmentService.UnconfirmedState => "未確定",
        ShipmentService.ConfirmedState => "確定済",
        _ => "要確認"
    };
    public string ManagementLabel => ManagementFlag switch
    {
        ShipmentService.ActiveFlag => "表示中",
        ShipmentService.HiddenFlag => "非表示",
        _ => "要確認"
    };
    public bool IsConfirmed => State == ShipmentService.ConfirmedState;
    public bool IsUnconfirmed => State == ShipmentService.UnconfirmedState;
}
public sealed record ShipmentDetail(ShipmentRecord Shipment, IReadOnlyList<ShipmentLineRecord> Lines);
public sealed record ShipmentDirectory(IReadOnlyList<ShipmentRecord> Shipments, ShipmentSummary Summary);
public sealed record ShipmentOperationResult(bool Succeeded, string Message, bool SaleReused = false);
public sealed record ShipmentQuantityInput(int ProductId, int Quantity);

public static class ShipmentOperationRules
{
    public static string? ValidateConfirmState(int managementFlag, int? state)
    {
        if (managementFlag != ShipmentService.ActiveFlag) return "表示中の出荷だけを確定できます。";
        return state switch
        {
            ShipmentService.UnconfirmedState => null,
            ShipmentService.ConfirmedState => "この出荷はすでに確定済みです。二重確定は行いません。",
            _ => "出荷状況を確認できないため確定できません。"
        };
    }

    public static string? ValidateHideState(int managementFlag, int? state)
    {
        if (managementFlag != ShipmentService.ActiveFlag) return "表示中の出荷だけを非表示にできます。";
        return state is 0 or 1 ? null : "出荷状態を確認してください。";
    }

    // New sales use the saved order amount. Legacy sales keep their recorded amounts on reuse.
    public static TSaleDetail CreateSaleDetail(TSale sale, TOrderDetail orderLine) => new()
    {
        Sa = sale,
        PrId = orderLine.PrId,
        SaQuantity = orderLine.OrQuantity,
        SaPrTotalPrice = orderLine.OrTotalPrice
    };

    public static string? ValidateLines(IReadOnlyCollection<ShipmentQuantityInput> lines) =>
        lines.Count == 0 || lines.Any(x => x.ProductId <= 0 || x.Quantity <= 0)
            ? "有効な商品と1以上の数量を持つ出荷明細が必要です。" : null;

    public static bool LinesMatch(IReadOnlyCollection<ShipmentQuantityInput> shipment,
        IReadOnlyCollection<ShipmentQuantityInput> arrival)
    {
        if (ValidateLines(shipment) != null || ValidateLines(arrival) != null) return false;
        var left = shipment.GroupBy(x => x.ProductId).ToDictionary(x => x.Key, x => x.Sum(y => (long)y.Quantity));
        var right = arrival.GroupBy(x => x.ProductId).ToDictionary(x => x.Key, x => x.Sum(y => (long)y.Quantity));
        return left.Count == right.Count && left.All(x => right.TryGetValue(x.Key, out var quantity) && quantity == x.Value);
    }

    public static string? ValidateArrivalMatchCount(int count) => count switch
    {
        1 => null,
        0 => "商品数量が一致する確定済み入荷を確認できません。入荷情報を確認してください。",
        _ => "出荷元の入荷を一意に特定できません。データを確認してください。"
    };

    public static string? ValidateShipmentMatchCount(int count) => count switch
    {
        1 => null,
        0 => "対象の出荷が見つかりません。",
        _ => "同じ注文・顧客・営業所に複数の出荷があります。重複を確認してください。"
    };

    public static bool SaleAmountsMatch(IEnumerable<(int ProductId, decimal Amount)> order,
        IEnumerable<(int ProductId, decimal Amount)> sale)
    {
        var saleLines = sale.ToArray();
        if (saleLines.Any(line => line.Amount < 0)) return false;

        return order.GroupBy(line => line.ProductId).OrderBy(group => group.Key)
            .Select(group => (group.Key, group.Sum(line => line.Amount)))
            .SequenceEqual(saleLines.GroupBy(line => line.ProductId).OrderBy(group => group.Key)
                .Select(group => (group.Key, group.Sum(line => line.Amount))));
    }

    public static string? ValidateExistingSaleCount(int count) => count switch
    {
        0 or 1 => null,
        _ => "同じ注文に複数の売上が登録されています。重複のため出荷を確定できません。"
    };

    public static string? ValidateExistingSale(bool sameCustomerAndOffice,
        IReadOnlyCollection<ShipmentQuantityInput> shipment, IReadOnlyCollection<ShipmentQuantityInput> sale) =>
        !sameCustomerAndOffice
            ? "既存売上の顧客・営業所が出荷と一致しません。データを確認してください。"
            : LinesMatch(shipment, sale)
                ? null
                : "既存売上の商品・数量が出荷と一致しません。重複や数量差異を確認してください。";

    public static string? ValidateHideReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "非表示理由を入力してください。";
        return reason.Trim().Length > ShipmentService.MaximumHideReasonLength
            ? $"非表示理由は{ShipmentService.MaximumHideReasonLength}文字以内で入力してください。" : null;
    }
}

public static class ShipmentFilter
{
    public static string? Validate(ShipmentSearchCriteria criteria) =>
        criteria.StartDate.HasValue && criteria.EndDate.HasValue
        && criteria.StartDate.Value.Date > criteria.EndDate.Value.Date
            ? "確定日の開始日は終了日以前の日付にしてください。" : null;

    public static bool Matches(ShipmentRecord shipment, ShipmentSearchCriteria criteria,
        ShipmentManagementTab tab, bool canViewHidden)
    {
        if (Validate(criteria) != null) return false;
        if (tab == ShipmentManagementTab.Hidden
            && (!canViewHidden || shipment.ManagementFlag != ShipmentService.HiddenFlag)) return false;
        if (tab == ShipmentManagementTab.Visible && shipment.ManagementFlag != ShipmentService.ActiveFlag) return false;
        if (tab == ShipmentManagementTab.All && !canViewHidden
            && shipment.ManagementFlag != ShipmentService.ActiveFlag) return false;
        if (criteria.StartDate.HasValue && (!shipment.ConfirmedDate.HasValue
            || shipment.ConfirmedDate.Value.Date < criteria.StartDate.Value.Date)) return false;
        if (criteria.EndDate.HasValue && (!shipment.ConfirmedDate.HasValue
            || shipment.ConfirmedDate.Value.Date > criteria.EndDate.Value.Date)) return false;
        if (criteria.State == ShipmentStateFilter.Unconfirmed && !shipment.IsUnconfirmed) return false;
        if (criteria.State == ShipmentStateFilter.Confirmed && !shipment.IsConfirmed) return false;

        var keyword = criteria.Keyword.Trim();
        return keyword.Length == 0 || Contains(shipment.Code, keyword) || Contains(shipment.Id.ToString(), keyword)
            || Contains(shipment.OrderCode, keyword) || Contains(shipment.OrderId.ToString(), keyword)
            || Contains(shipment.CustomerId.ToString(), keyword) || Contains(shipment.CustomerName, keyword) || Contains(shipment.OfficeName, keyword)
            || Contains(shipment.EmployeeName, keyword) || Contains(shipment.ProductSearchText, keyword);
    }

    private static bool Contains(string value, string keyword) => value.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);
}

/// <summary>Shipment read and state-transition boundary for the existing shipment schema.</summary>
public static class ShipmentService
{
    public const int ActiveFlag = 0;
    public const int HiddenFlag = 2;
    public const int UnconfirmedState = 0;
    public const int ConfirmedState = 1;
    public const int MaximumHideReasonLength = 500;

    public static bool CanView => AppSession.Employee is { EmFlag: ActiveFlag }
        && (AppSession.IsAdmin || AppSession.IsSales);
    public static bool CanConfirm => CanView;
    public static bool CanViewHidden => AppSession.Employee is { EmFlag: ActiveFlag } && AppSession.IsAdmin;
    public static bool CanHide => CanView;

    public static ShipmentDirectory Load()
    {
        EnsureCanView();
        using var db = new SalesManagementContext();
        var actor = ResolveActor(db, AppSession.Employee!.EmId);
        EnsureCanView(actor);

        var query = db.TShipments.AsNoTracking();
        if (!actor!.IsAdministrator) query = query.Where(x => x.SoId == actor.OfficeId && x.ShFlag == ActiveFlag);
        var rows = query.OrderByDescending(x => x.ShFinishDate).ThenByDescending(x => x.ShId)
            .Select(x => new ShipmentRecord(x.ShId, x.OrId, x.ClId, x.Cl.ClName, x.SoId, x.So.SoName,
                x.EmId, x.Em == null ? "未設定" : x.Em.EmName, x.ShFinishDate, x.ShStateFlag, x.ShFlag,
                x.ShHidden, x.TShipmentDetails.Count(), x.TShipmentDetails.Select(d => (int?)d.ShQuantity).Sum() ?? 0, ""))
            .ToList();

        var ids = rows.Select(x => x.Id).ToArray();
        var productTerms = ids.Length == 0 ? new Dictionary<int, string>() : db.TShipmentDetails.AsNoTracking()
            .Where(x => ids.Contains(x.ShId))
            .Select(x => new { x.ShId, ProductCode = x.Pr.PrJcode ?? "", ProductName = x.Pr.PrName })
            .ToList().GroupBy(x => x.ShId)
            .ToDictionary(x => x.Key, x => string.Join(" ", x.Select(y => $"{y.ProductCode} {y.ProductName}")));
        rows = rows.Select(x => x with { ProductSearchText = productTerms.GetValueOrDefault(x.Id, "") }).ToList();
        var active = rows.Where(x => x.ManagementFlag == ActiveFlag).ToArray();
        return new(rows, new(active.Length, active.Count(x => x.IsUnconfirmed), active.Count(x => x.IsConfirmed)));
    }

    public static ShipmentDetail? LoadDetail(int shipmentId)
    {
        if (shipmentId <= 0) return null;
        EnsureCanView();
        using var db = new SalesManagementContext();
        var actor = ResolveActor(db, AppSession.Employee!.EmId);
        EnsureCanView(actor);
        var query = db.TShipments.AsNoTracking().Where(x => x.ShId == shipmentId);
        if (!actor!.IsAdministrator) query = query.Where(x => x.SoId == actor.OfficeId && x.ShFlag == ActiveFlag);
        var shipment = query.Select(x => new ShipmentRecord(x.ShId, x.OrId, x.ClId, x.Cl.ClName,
            x.SoId, x.So.SoName, x.EmId, x.Em == null ? "未設定" : x.Em.EmName, x.ShFinishDate,
            x.ShStateFlag, x.ShFlag, x.ShHidden, x.TShipmentDetails.Count(),
            x.TShipmentDetails.Select(d => (int?)d.ShQuantity).Sum() ?? 0, "")).SingleOrDefault();
        if (shipment == null) return null;
        var lines = db.TShipmentDetails.AsNoTracking().Where(x => x.ShId == shipmentId).OrderBy(x => x.ShDetailId)
            .Select(x => new ShipmentLineRecord(x.PrId, x.Pr.PrJcode ?? $"P-{x.PrId:0000}", x.Pr.PrName, x.ShQuantity))
            .ToArray();
        return new(shipment, lines);
    }

    public static ShipmentOperationResult Confirm(int actorId, int shipmentId)
    {
        if (shipmentId <= 0) return Denied("対象の出荷を確認してください。");
        if (!CanConfirm || AppSession.Employee?.EmId != actorId)
            return Denied("出荷を確定する権限がありません。管理者または営業担当者としてログインしてください。");

        using var db = new SalesManagementContext();
        try
        {
            using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
            var actor = ResolveActor(db, actorId);
            EnsureCanView(actor);
            var shipment = db.TShipments.FromSqlInterpolated(
                    $"SELECT * FROM dbo.T_Shipment WITH (UPDLOCK, HOLDLOCK) WHERE ShID = {shipmentId}")
                .SingleOrDefault();
            if (shipment == null) return Denied("対象の出荷が見つかりません。");
            EnsureShipmentScope(shipment, actor!);
            if (ShipmentOperationRules.ValidateConfirmState(shipment.ShFlag, shipment.ShStateFlag) is { } stateError)
                return Denied(stateError);

            var orderMatches = db.TOrders.AsNoTracking().Any(x => x.OrId == shipment.OrId
                && x.ClId == shipment.ClId && x.SoId == shipment.SoId);
            if (!orderMatches) return Denied("出荷に関連する注文・顧客・営業所の関係が一致しません。");

            var sameKeyShipments = db.TShipments.FromSqlInterpolated(
                    $"SELECT * FROM dbo.T_Shipment WITH (UPDLOCK, HOLDLOCK) WHERE OrID = {shipment.OrId} AND ClID = {shipment.ClId} AND SoID = {shipment.SoId}")
                .AsNoTracking().Count();
            if (ShipmentOperationRules.ValidateShipmentMatchCount(sameKeyShipments) is { } duplicateShipmentError)
                return Denied(duplicateShipmentError);

            var storedLines = db.TShipmentDetails.AsNoTracking().Where(x => x.ShId == shipmentId)
                .OrderBy(x => x.ShDetailId).Select(x => new ShipmentQuantityInput(x.PrId, x.ShQuantity)).ToList();
            if (ShipmentOperationRules.ValidateLines(storedLines) is { } lineError) return Denied(lineError);
            var productIds = storedLines.Select(x => x.ProductId).Distinct().ToArray();
            var products = db.MProducts.AsNoTracking().Where(x => productIds.Contains(x.PrId))
                .Select(x => new { x.PrId, x.Price }).ToDictionary(x => x.PrId);
            if (products.Count != productIds.Length) return Denied("出荷明細の商品情報を確認できません。");

            // T_Shipment has no ArID. Confirm only when one confirmed arrival can be resolved from
            // the existing order/customer/office keys and the exact product quantities.
            var arrivalCandidates = db.TArrivals.AsNoTracking().Where(x => x.OrId == shipment.OrId
                    && x.ClId == shipment.ClId && x.SoId == shipment.SoId && x.ArStateFlag == ConfirmedState)
                .Include(x => x.TArrivalDetails).ToList();
            var matchingArrivals = arrivalCandidates.Where(x => ShipmentOperationRules.LinesMatch(storedLines,
                x.TArrivalDetails.Select(d => new ShipmentQuantityInput(d.PrId ?? 0, d.ArQuantity ?? 0)).ToArray())).ToArray();
            if (ShipmentOperationRules.ValidateArrivalMatchCount(matchingArrivals.Length) is { } arrivalError)
                return Denied(arrivalError);

            var orderLines = db.TOrderDetails.AsNoTracking()
                .Where(x => x.OrId == shipment.OrId && (x.OrQuantity != 0 || x.OrTotalPrice != 0))
                .OrderBy(x => x.OrDetailId).ToList();
            var quantities = orderLines.Select(x => new ShipmentQuantityInput(x.PrId, x.OrQuantity)).ToArray();
            if (orderLines.Any(x => x.OrTotalPrice < 0) || !ShipmentOperationRules.LinesMatch(storedLines, quantities))
                return Denied("受注明細の金額または数量が出荷内容と一致しません。受注を確認してください。");

            // Preserve the approved existing sales flow but lock the order range and reuse a matching
            // sale if one already exists. This prevents retries or legacy seed data from duplicating sales.
            var existingSales = db.TSales.FromSqlInterpolated(
                    $"SELECT * FROM dbo.T_Sale WITH (UPDLOCK, HOLDLOCK) WHERE OrID = {shipment.OrId}")
                .ToList();
            TSale? saleToReuse = null;
            if (ShipmentOperationRules.ValidateExistingSaleCount(existingSales.Count) is { } salesCountError)
                return Denied(salesCountError);
            if (existingSales.Count == 1)
            {
                var existingSale = existingSales[0];
                if (existingSale.SaFlag != ActiveFlag)
                    return Denied("同じ受注に非表示または無効の売上があります。売上の状態を確認してください。");
                var saleLines = db.TSaleDetails.AsNoTracking().Where(x => x.SaId == existingSale.SaId)
                    .Select(x => new { x.PrId, x.SaQuantity, x.SaPrTotalPrice }).ToArray();
                if (ShipmentOperationRules.ValidateExistingSale(existingSale.ClId == shipment.ClId
                    && existingSale.SoId == shipment.SoId, storedLines, saleLines.Select(x => new ShipmentQuantityInput(x.PrId, x.SaQuantity)).ToArray()) is { } saleError)
                    return Denied(saleError);
                if (!ShipmentOperationRules.SaleAmountsMatch(
                    orderLines.Select(x => (x.PrId, x.OrTotalPrice)),
                    saleLines.Select(x => (x.PrId, x.SaPrTotalPrice))))
                    return Denied("既存売上の金額が受注時の金額と一致しません。売上明細を確認してください。");
                saleToReuse = existingSale;
            }

            shipment.ShStateFlag = ConfirmedState;
            var confirmedDate = DateTime.Today;
            shipment.ShFinishDate = confirmedDate;
            shipment.EmId = actor!.EmployeeId;
            if (saleToReuse == null)
            {
                var sale = new TSale
                {
                    ClId = shipment.ClId,
                    SoId = shipment.SoId,
                    OrId = shipment.OrId,
                    EmId = actor.EmployeeId,
                    SaDate = confirmedDate,
                    SaFlag = ActiveFlag
                };
                db.TSaleDetails.AddRange(orderLines.Select(line => ShipmentOperationRules.CreateSaleDetail(sale, line)));
                db.TSales.Add(sale);
            }
            else
            {
                saleToReuse.SaDate = confirmedDate;
                saleToReuse.EmId = actor.EmployeeId;
            }
            var reuseSale = saleToReuse != null;
            db.SaveChanges();
            transaction.Commit();
            return new(true, reuseSale
                ? "出荷を確定しました。数量と金額が一致する既存売上を再利用し、重複作成していません。"
                : "出荷を確定し、既存の売上連携を同一トランザクションで1件だけ登録しました。", reuseSale);
        }
        catch (UnauthorizedAccessException ex) { return Denied(ex.Message); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Shipment confirmation failed: {ex}");
            return Denied("出荷確定に失敗しました。データベースの状態を確認してください。");
        }
    }

    public static ShipmentOperationResult Hide(int actorId, int shipmentId, string? reason)
    {
        if (shipmentId <= 0) return Denied("対象の出荷を確認してください。");
        if (!CanHide || AppSession.Employee?.EmId != actorId)
            return Denied("出荷を非表示にする権限がありません。管理者または営業担当者としてログインしてください。");
        if (ShipmentOperationRules.ValidateHideReason(reason) is { } validation) return Denied(validation);
        using var db = new SalesManagementContext();
        try
        {
            using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
            var actor = ResolveActor(db, actorId);
            EnsureCanView(actor);
            var shipment = db.TShipments.FromSqlInterpolated(
                $"SELECT * FROM dbo.T_Shipment WITH (UPDLOCK, HOLDLOCK) WHERE ShID = {shipmentId}")
                .SingleOrDefault();
            if (shipment == null) return Denied("対象の出荷が見つかりません。");
            EnsureShipmentScope(shipment, actor!);
            if (ShipmentOperationRules.ValidateHideState(shipment.ShFlag, shipment.ShStateFlag) is { } stateError)
                return Denied(stateError);
            shipment.ShFlag = HiddenFlag;
            shipment.ShHidden = reason!.Trim();
            db.SaveChanges();
            transaction.Commit();
            return new(true, "出荷を非表示にしました。出荷・入荷・注文・売上・在庫データは削除・変更していません。");
        }
        catch (UnauthorizedAccessException ex) { return Denied(ex.Message); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Shipment hide failed: {ex}");
            return Denied("出荷を非表示にできませんでした。一覧を再読込してからお試しください。");
        }
    }

    private static ShipmentActor? ResolveActor(SalesManagementContext db, int employeeId)
    {
        var session = AppSession.Employee;
        if (session is not { EmFlag: ActiveFlag } || session.EmId != employeeId) return null;
        var actor = db.MEmployees.AsNoTracking().Where(x => x.EmId == employeeId && x.EmFlag == ActiveFlag
                && x.Po.PoFlag == ActiveFlag)
            .Select(x => new ShipmentActor(x.EmId, x.SoId, x.Po.PoName)).SingleOrDefault();
        if (actor == null || actor.OfficeId != session.SoId
            || !string.Equals(actor.RoleName, AppSession.Role, StringComparison.Ordinal) || !actor.IsAllowed) return null;
        return actor;
    }

    private static void EnsureShipmentScope(TShipment shipment, ShipmentActor actor)
    {
        if (!actor.IsAdministrator && (shipment.SoId != actor.OfficeId || shipment.ShFlag != ActiveFlag))
            throw new UnauthorizedAccessException("所属営業所以外、または非表示の出荷は操作できません。");
    }

    private static void EnsureCanView()
    {
        if (!CanView) throw new UnauthorizedAccessException("出荷情報を表示する権限がありません。");
    }

    private static void EnsureCanView(ShipmentActor? actor)
    {
        if (actor is not { IsAllowed: true }) throw new UnauthorizedAccessException("出荷情報を表示する権限がありません。ログイン状態を確認してください。");
    }

    private static ShipmentOperationResult Denied(string message) => new(false, message);

    private sealed record ShipmentActor(int EmployeeId, int OfficeId, string RoleName)
    {
        public bool IsAdministrator => RoleName == "管理者";
        public bool IsAllowed => IsAdministrator || RoleName == "営業";
    }
}
