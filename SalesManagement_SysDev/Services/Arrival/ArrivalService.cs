using System.Data;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public enum ArrivalManagementTab { All, Visible, Hidden }
public enum ArrivalStateFilter { All, Unconfirmed, Confirmed }

public sealed record ArrivalOfficeOption(int Id, string Name);
public sealed record ArrivalEmployeeOption(int Id, string Name);
public sealed record ArrivalSearchCriteria(string Keyword, DateTime? StartDate, DateTime? EndDate,
    int? OfficeId, int? EmployeeId, ArrivalStateFilter State);
public sealed record ArrivalSummary(int Total, int Unconfirmed, int Confirmed);
public sealed record ArrivalDirectory(IReadOnlyList<ArrivalRecord> Arrivals,
    IReadOnlyList<ArrivalOfficeOption> Offices, IReadOnlyList<ArrivalEmployeeOption> Employees,
    ArrivalSummary Summary);
public sealed record ArrivalLineRecord(int? ProductId, string ProductCode, string ProductName, int? Quantity)
{
    public string QuantityText => Quantity?.ToString("N0") ?? "未登録";
}
public sealed record ArrivalRecord(int Id, int OrderId, int CustomerId, string CustomerName, int OfficeId,
    string OfficeName, int? EmployeeId, string EmployeeName, DateTime? ArrivalDate, int? ArrivalState,
    int ManagementFlag, string? HiddenReason, int LineCount, int Quantity, string ProductSearchText = "")
{
    public string Code => $"AR-{Id:000000}";
    public string OrderCode => $"CH-{OrderId:000000}";
    public string ArrivalDateText => ArrivalDate?.ToString("yyyy/MM/dd") ?? "未設定";
    public string ArrivalStateLabel => ArrivalState switch
    {
        ArrivalService.UnconfirmedState => "未確定",
        ArrivalService.ConfirmedState => "確定済",
        _ => "要確認"
    };
    public string ManagementLabel => ManagementFlag switch
    {
        ArrivalService.ActiveFlag => "表示中",
        ArrivalService.HiddenFlag => "非表示",
        _ => "要確認"
    };
    public bool IsConfirmed => ArrivalState == ArrivalService.ConfirmedState;
    public bool IsUnconfirmed => ArrivalState == ArrivalService.UnconfirmedState;
}
public sealed record ArrivalDetail(ArrivalRecord Arrival, IReadOnlyList<ArrivalLineRecord> Lines);
public sealed record ArrivalQuantityInput(int? ProductId, int? Quantity);
public sealed record ArrivalOperationResult(bool Succeeded, string Message, int? ArrivalId = null,
    int? ShipmentId = null);

public static class ArrivalOperationRules
{
    public static string? ValidateConfirmState(int managementFlag, int? state, bool isAdministrator)
    {
        if (managementFlag != ArrivalService.ActiveFlag
            && !(isAdministrator && managementFlag == ArrivalService.HiddenFlag))
            return "対象の入荷は通常の状態ではありません。";
        if (state == ArrivalService.ConfirmedState)
            return "この入荷はすでに確定済みです。出荷情報は重複作成していません。";
        if (state != ArrivalService.UnconfirmedState)
            return "入荷状態を確認できないため確定できません。";
        return null;
    }

    public static string? ValidateHideState(int managementFlag, int? state)
    {
        if (managementFlag != ArrivalService.ActiveFlag) return "通常表示の入荷だけを非表示にできます。";
        return state != ArrivalService.ConfirmedState
            ? "未確定の入荷は非表示にできません。入荷確定の処理を続けてください。" : null;
    }

    public static string? ValidateLines(IReadOnlyCollection<ArrivalQuantityInput> lines) =>
        lines.Count == 0 || lines.Any(x => !x.ProductId.HasValue || !x.Quantity.HasValue || x.Quantity <= 0)
            ? "有効な商品と1以上の数量を持つ入荷明細が必要です。" : null;

    public static bool LinesMatch(IReadOnlyCollection<ArrivalQuantityInput> arrivalLines,
        IReadOnlyCollection<ArrivalQuantityInput> outboundLines)
    {
        if (ValidateLines(arrivalLines) != null || ValidateLines(outboundLines) != null) return false;
        var arrivalQuantities = arrivalLines.GroupBy(x => x.ProductId!.Value)
            .ToDictionary(x => x.Key, x => x.Sum(line => (long)line.Quantity!.Value));
        var outboundQuantities = outboundLines.GroupBy(x => x.ProductId!.Value)
            .ToDictionary(x => x.Key, x => x.Sum(line => (long)line.Quantity!.Value));
        return arrivalQuantities.Count == outboundQuantities.Count
            && arrivalQuantities.All(x => outboundQuantities.TryGetValue(x.Key, out var quantity)
                && quantity == x.Value);
    }

    public static string? ValidateOutboundMatchCount(int matchCount) => matchCount switch
    {
        1 => null,
        0 => "同じ注文・顧客・営業所と商品数量が一致する確定済み出庫を確認できません。",
        _ => "入荷元の出庫を一意に特定できません。データを確認してください。"
    };
}

public static class ArrivalFilter
{
    public static string? Validate(ArrivalSearchCriteria criteria) =>
        criteria.StartDate.HasValue && criteria.EndDate.HasValue
        && criteria.StartDate.Value.Date > criteria.EndDate.Value.Date
            ? "入荷日の開始日は終了日以前の日付にしてください。" : null;

    public static bool Matches(ArrivalRecord arrival, ArrivalSearchCriteria criteria,
        ArrivalManagementTab tab, bool canViewHidden)
    {
        if (Validate(criteria) != null) return false;
        if (tab == ArrivalManagementTab.Hidden
            && (!canViewHidden || arrival.ManagementFlag != ArrivalService.HiddenFlag)) return false;
        if (tab == ArrivalManagementTab.Visible && arrival.ManagementFlag != ArrivalService.ActiveFlag) return false;
        if (tab == ArrivalManagementTab.All && !canViewHidden
            && arrival.ManagementFlag != ArrivalService.ActiveFlag) return false;
        if (criteria.StartDate.HasValue
            && (!arrival.ArrivalDate.HasValue || arrival.ArrivalDate.Value.Date < criteria.StartDate.Value.Date)) return false;
        if (criteria.EndDate.HasValue
            && (!arrival.ArrivalDate.HasValue || arrival.ArrivalDate.Value.Date > criteria.EndDate.Value.Date)) return false;
        if (criteria.OfficeId.HasValue && arrival.OfficeId != criteria.OfficeId.Value) return false;
        if (criteria.EmployeeId.HasValue && arrival.EmployeeId != criteria.EmployeeId.Value) return false;
        if (criteria.State == ArrivalStateFilter.Unconfirmed && !arrival.IsUnconfirmed) return false;
        if (criteria.State == ArrivalStateFilter.Confirmed && !arrival.IsConfirmed) return false;

        var keyword = criteria.Keyword.Trim();
        if (keyword.Length > 0
            && !Contains(arrival.Code, keyword)
            && !Contains(arrival.Id.ToString(), keyword)
            && !Contains(arrival.OrderCode, keyword)
            && !Contains(arrival.OrderId.ToString(), keyword)
            && !Contains(arrival.CustomerName, keyword)
            && !Contains(arrival.OfficeName, keyword)
            && !Contains(arrival.EmployeeName, keyword)
            && !Contains(arrival.ProductSearchText, keyword)) return false;
        return true;
    }

    private static bool Contains(string value, string query) =>
        value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}

/// <summary>Reads existing arrival records and owns their confirmation and logical visibility transitions.</summary>
public static class ArrivalService
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
    public static bool CanHide => CanViewHidden;

    public static ArrivalDirectory Load()
    {
        EnsureCanView();
        using var db = new SalesManagementContext();
        var actor = ResolveActor(db, AppSession.Employee!.EmId);
        EnsureCanView(actor);

        var query = db.TArrivals.AsNoTracking();
        if (!actor!.IsAdministrator)
            query = query.Where(x => x.SoId == actor.OfficeId && x.ArFlag == ActiveFlag);

        var rows = query.OrderByDescending(x => x.ArDate).ThenByDescending(x => x.ArId)
            .Select(x => new ArrivalRecord(x.ArId, x.OrId, x.ClId, x.Cl.ClName, x.SoId, x.So.SoName,
                x.EmId, x.Em == null ? "未設定" : x.Em.EmName, x.ArDate, x.ArStateFlag, x.ArFlag, x.ArHidden,
                x.TArrivalDetails.Count(), x.TArrivalDetails.Select(d => d.ArQuantity).Sum() ?? 0, ""))
            .ToList();

        var ids = rows.Select(x => x.Id).ToArray();
        var termsByArrival = ids.Length == 0
            ? new Dictionary<int, string>()
            : db.TArrivalDetails.AsNoTracking().Where(x => x.ArId.HasValue && ids.Contains(x.ArId.Value))
                .Select(x => new
                {
                    ArrivalId = x.ArId!.Value,
                    ProductCode = x.Pr == null ? "" : x.Pr.PrJcode ?? "",
                    ProductName = x.Pr == null ? "" : x.Pr.PrName
                })
                .ToList()
                .GroupBy(x => x.ArrivalId)
                .ToDictionary(x => x.Key, x => string.Join(" ", x.Select(y => $"{y.ProductCode} {y.ProductName}")));
        rows = rows.Select(x => x with { ProductSearchText = termsByArrival.GetValueOrDefault(x.Id, "") }).ToList();

        var officesQuery = db.MSalesOffices.AsNoTracking().Where(x => x.SoFlag == ActiveFlag);
        var employeesQuery = db.MEmployees.AsNoTracking().Where(x => x.EmFlag == ActiveFlag && x.Po.PoFlag == ActiveFlag);
        if (!actor.IsAdministrator)
        {
            officesQuery = officesQuery.Where(x => x.SoId == actor.OfficeId);
            employeesQuery = employeesQuery.Where(x => x.SoId == actor.OfficeId);
        }
        var offices = officesQuery.OrderBy(x => x.SoName)
            .Select(x => new ArrivalOfficeOption(x.SoId, x.SoName)).ToList();
        var employees = employeesQuery.OrderBy(x => x.EmName)
            .Select(x => new ArrivalEmployeeOption(x.EmId, x.EmName)).ToList();
        var active = rows.Where(x => x.ManagementFlag == ActiveFlag).ToArray();
        var summary = new ArrivalSummary(active.Length, active.Count(x => x.IsUnconfirmed),
            active.Count(x => x.IsConfirmed));
        return new(rows, offices, employees, summary);
    }

    public static ArrivalDetail? LoadDetail(int arrivalId)
    {
        if (arrivalId <= 0) return null;
        EnsureCanView();
        using var db = new SalesManagementContext();
        var actor = ResolveActor(db, AppSession.Employee!.EmId);
        EnsureCanView(actor);
        var arrivalQuery = db.TArrivals.AsNoTracking().Where(x => x.ArId == arrivalId);
        if (!actor!.IsAdministrator)
            arrivalQuery = arrivalQuery.Where(x => x.SoId == actor.OfficeId && x.ArFlag == ActiveFlag);
        var header = arrivalQuery.Select(x => new ArrivalRecord(x.ArId, x.OrId, x.ClId, x.Cl.ClName,
            x.SoId, x.So.SoName, x.EmId, x.Em == null ? "未設定" : x.Em.EmName, x.ArDate,
            x.ArStateFlag, x.ArFlag, x.ArHidden, x.TArrivalDetails.Count(),
            x.TArrivalDetails.Select(d => d.ArQuantity).Sum() ?? 0, "")).SingleOrDefault();
        if (header == null) return null;

        var storedLines = db.TArrivalDetails.AsNoTracking().Where(x => x.ArId == arrivalId)
            .OrderBy(x => x.ArDetailId)
            .Select(x => new
            {
                x.PrId,
                ProductCode = x.Pr == null ? null : x.Pr.PrJcode,
                ProductName = x.Pr == null ? null : x.Pr.PrName,
                x.ArQuantity
            })
            .ToList();
        var lines = storedLines.Select(x => new ArrivalLineRecord(x.PrId,
            string.IsNullOrWhiteSpace(x.ProductCode) ? x.PrId.HasValue ? $"P-{x.PrId.Value:0000}" : ""
                : x.ProductCode,
            string.IsNullOrWhiteSpace(x.ProductName) ? "商品情報なし" : x.ProductName, x.ArQuantity)).ToArray();
        return new(header, lines);
    }

    public static ArrivalOperationResult Confirm(int actorId, int arrivalId)
    {
        if (arrivalId <= 0) return Denied("対象の入荷を確認してください。");
        if (!CanConfirm || AppSession.Employee?.EmId != actorId)
            return Denied("入荷を確定する権限がありません。管理者または営業担当者としてログインしてください。");

        using var db = new SalesManagementContext();
        try
        {
            using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
            var actor = ResolveActor(db, actorId);
            EnsureCanView(actor);

            // Lock this arrival until commit. A retry or concurrent confirmer will observe its committed state.
            var arrival = db.TArrivals.FromSqlInterpolated(
                $"SELECT * FROM dbo.T_Arrival WITH (UPDLOCK, HOLDLOCK) WHERE ArID = {arrivalId}")
                .SingleOrDefault();
            if (arrival == null) return Denied("対象の入荷が見つかりません。");
            if (!actor!.IsAdministrator && arrival.SoId != actor.OfficeId)
                return Denied("所属営業所以外の入荷は確定できません。");
            if (ArrivalOperationRules.ValidateConfirmState(arrival.ArFlag, arrival.ArStateFlag,
                actor!.IsAdministrator) is { } stateError) return Denied(stateError);

            var orderMatches = db.TOrders.AsNoTracking().Any(x => x.OrId == arrival.OrId
                && x.ClId == arrival.ClId && x.SoId == arrival.SoId);
            if (!orderMatches) return Denied("入荷に関連する注文・顧客・営業所の関係が一致しません。");

            var lines = db.TArrivalDetails.AsNoTracking().Where(x => x.ArId == arrivalId)
                .OrderBy(x => x.ArDetailId)
                .Select(x => new ArrivalQuantityInput(x.PrId, x.ArQuantity)).ToList();
            if (ArrivalOperationRules.ValidateLines(lines) is { } lineError) return Denied(lineError);

            // T_Arrival has no SyID. Resolve the source only when a unique confirmed outbound with the
            // same order/customer/office and the exact product quantities exists; never guess among candidates.
            var confirmedOutbounds = db.TSyukkos.AsNoTracking()
                .Where(x => x.OrId == arrival.OrId && x.ClId == arrival.ClId && x.SoId == arrival.SoId
                    && x.SyStateFlag == ConfirmedState)
                .Include(x => x.TSyukkoDetails)
                .ToList();
            var matchingOutbounds = confirmedOutbounds.Where(outbound => ArrivalOperationRules.LinesMatch(lines,
                outbound.TSyukkoDetails.Select(x => new ArrivalQuantityInput(x.PrId, x.SyQuantity)).ToArray())).ToArray();
            if (ArrivalOperationRules.ValidateOutboundMatchCount(matchingOutbounds.Length) is { } outboundError)
                return Denied(outboundError);

            var productIds = lines.Select(x => x.ProductId!.Value).Distinct().ToArray();
            if (db.MProducts.Count(x => productIds.Contains(x.PrId)) != productIds.Length)
                return Denied("入荷明細の商品情報を確認できません。");

            // Shipment has no ArrivalID either. At this schema level one visible order/customer/office
            // can have only one generated shipment; protect that key range in the same transaction.
            var existingShipment = db.TShipments.FromSqlInterpolated(
                    $"SELECT * FROM dbo.T_Shipment WITH (UPDLOCK, HOLDLOCK) WHERE OrID = {arrival.OrId} AND ClID = {arrival.ClId} AND SoID = {arrival.SoId}")
                .AsNoTracking().Any();
            if (existingShipment)
                return Denied("この注文・顧客・営業所の出荷情報はすでに存在します。重複作成を防ぐため確定できません。");

            arrival.ArStateFlag = ConfirmedState;
            arrival.ArDate = DateTime.Today;
            arrival.EmId = actor.EmployeeId;
            var shipment = new TShipment
            {
                ClId = arrival.ClId,
                SoId = arrival.SoId,
                OrId = arrival.OrId,
                ShStateFlag = 0,
                ShFlag = ActiveFlag,
                TShipmentDetails = lines.Select(x => new TShipmentDetail
                {
                    PrId = x.ProductId!.Value,
                    ShQuantity = x.Quantity!.Value
                }).ToList()
            };
            db.TShipments.Add(shipment);
            db.SaveChanges();
            transaction.Commit();
            return new(true, "入荷を確定し、未確定の出荷情報を作成しました。出荷確定は別操作です。",
                arrival.ArId, shipment.ShId);
        }
        catch (UnauthorizedAccessException ex) { return Denied(ex.Message); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Arrival confirmation failed: {ex}");
            return Denied("入荷確定と出荷情報の作成に失敗しました。データベースの状態を確認してください。");
        }
    }

    public static string? ValidateHideReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "非表示理由を入力してください。";
        return reason.Trim().Length > MaximumHideReasonLength
            ? $"非表示理由は{MaximumHideReasonLength}文字以内で入力してください。" : null;
    }

    public static ArrivalOperationResult Hide(int actorId, int arrivalId, string? reason)
    {
        if (arrivalId <= 0) return Denied("対象の入荷を確認してください。");
        if (!CanHide || AppSession.Employee?.EmId != actorId)
            return Denied("入荷を非表示にする権限がありません。管理者としてログインしてください。");
        if (ValidateHideReason(reason) is { } validation) return Denied(validation);

        using var db = new SalesManagementContext();
        try
        {
            using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
            var actor = ResolveActor(db, actorId);
            if (actor is not { IsAdministrator: true }) return Denied("入荷を非表示にする権限がありません。");
            var arrival = db.TArrivals.FromSqlInterpolated(
                $"SELECT * FROM dbo.T_Arrival WITH (UPDLOCK, HOLDLOCK) WHERE ArID = {arrivalId}")
                .SingleOrDefault();
            if (arrival == null) return Denied("対象の入荷が見つかりません。");
            if (ArrivalOperationRules.ValidateHideState(arrival.ArFlag, arrival.ArStateFlag) is { } stateError)
                return Denied(stateError);

            arrival.ArFlag = HiddenFlag;
            arrival.ArHidden = reason!.Trim();
            db.SaveChanges();
            transaction.Commit();
            return new(true, "入荷を非表示にしました。入荷・出荷・注文・在庫データは削除・変更していません。", arrivalId);
        }
        catch (UnauthorizedAccessException ex) { return Denied(ex.Message); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Arrival hide failed: {ex}");
            return Denied("入荷を非表示にできませんでした。一覧を再読込してからお試しください。");
        }
    }

    private static ArrivalActor? ResolveActor(SalesManagementContext db, int employeeId)
    {
        var session = AppSession.Employee;
        if (session is not { EmFlag: ActiveFlag } || session.EmId != employeeId) return null;
        var actor = db.MEmployees.AsNoTracking()
            .Where(x => x.EmId == employeeId && x.EmFlag == ActiveFlag && x.Po.PoFlag == ActiveFlag)
            .Select(x => new ArrivalActor(x.EmId, x.SoId, x.Po.PoName))
            .SingleOrDefault();
        if (actor == null || actor.OfficeId != session.SoId
            || !string.Equals(actor.RoleName, AppSession.Role, StringComparison.Ordinal)
            || !actor.IsAllowed) return null;
        return actor;
    }

    private static void EnsureCanView()
    {
        if (!CanView) throw new UnauthorizedAccessException("入荷情報を表示する権限がありません。");
    }

    private static void EnsureCanView(ArrivalActor? actor)
    {
        if (actor is not { IsAllowed: true })
            throw new UnauthorizedAccessException("入荷情報を表示する権限がありません。ログイン状態を確認してください。");
    }

    private static ArrivalOperationResult Denied(string message) => new(false, message);

    private sealed record ArrivalActor(int EmployeeId, int OfficeId, string RoleName)
    {
        public bool IsAdministrator => RoleName == "管理者";
        public bool IsAllowed => IsAdministrator || RoleName == "営業";
    }
}
