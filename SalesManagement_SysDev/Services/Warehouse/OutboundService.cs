using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public static partial class OutboundService
{
    public const int ActiveFlag = 0;
    public const int HiddenFlag = 2;
    public const int UnconfirmedState = 0;
    public const int ConfirmedState = 1;

    public static bool CanView => WarehouseActorResolver.CanUse;
    public static bool CanConfirm => CanView;
    public static bool CanViewHidden => WarehouseActorResolver.CanAdmin;
    public static bool CanHide => CanView;

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
                && x.ClId == outbound.ClId && x.SoId == outbound.SoId && x.OrStateFlag == 1);
            if (!officeIsActive || !customerIsActive || !orderIsActive)
                return Denied("注文・顧客・営業所の有効状態または関連を確認できません。");

            var sourceOrders = db.TChumons.AsNoTracking().Include(x => x.TChumonDetails)
                .Where(x => x.OrId == outbound.OrId && x.ClId == outbound.ClId && x.SoId == outbound.SoId
                    && (x.ChFlag == ActiveFlag || x.ChFlag == HiddenFlag) && x.ChStateFlag == ConfirmedState).ToList();
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
            return Denied("出庫を非表示にする権限がありません。管理者または物流担当としてログインしてください。");
        if (ValidateHideReason(reason) is { } validation) return Denied(validation);
        using var db = new SalesManagementContext();
        try
        {
            using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
            var actor = WarehouseActorResolver.Resolve(db, actorId);
            if (actor == null) return Denied("出庫を非表示にする権限がありません。");
            var outbound = db.TSyukkos.FromSqlInterpolated(
                $"SELECT * FROM dbo.T_Syukko WITH (UPDLOCK, HOLDLOCK) WHERE SyID = {syukkoId}")
                .SingleOrDefault();
            if (outbound == null) return Denied("対象の出庫が見つかりません。");
            if (!actor.IsAdministrator && outbound.SoId != actor.OfficeId)
                return Denied("自営業所の出庫だけ非表示にできます。");
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
