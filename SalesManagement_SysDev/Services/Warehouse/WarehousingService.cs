using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public static class WarehousingService
{
    public const int ActiveFlag = 0;
    public const int HiddenFlag = 2;
    public const int UnconfirmedState = 0;
    public const int ConfirmedState = 1;

    public static bool CanView => WarehouseActorResolver.CanUse;
    public static bool CanConfirm => CanView;
    public static bool CanViewHidden => WarehouseActorResolver.CanAdmin;
    public static bool CanHide => CanView;
    public static bool CanRestore => CanViewHidden;
    public static bool IsRestorable(WarehouseMovementRecord record) =>
        record.Kind == WarehouseMovementKind.Inbound && record.ManagementFlag == HiddenFlag && (record.IsConfirmed || record.IsUnconfirmed)
        && !string.IsNullOrWhiteSpace(record.HiddenReason);

    public static WarehouseMovementDirectory Load()
    {
        WarehouseActorResolver.EnsureCanUse();
        using var db = new SalesManagementContext();
        var actor = WarehouseActorResolver.Resolve(db, AppSession.Employee!.EmId);
        WarehouseActorResolver.EnsureCanUse(actor);

        var query = db.TWarehousings.AsNoTracking();
        if (!actor!.IsAdministrator) query = query.Where(x => x.WaFlag == ActiveFlag && x.Ha.RegisteredOfficeId == actor.OfficeId);
        var headers = query.OrderByDescending(x => x.WaDate).ThenByDescending(x => x.WaId)
            .Select(x => new
            {
                x.WaId, x.HaId, MakerId = x.Ha.MaId, x.WaDate, x.WaShelfFlag, x.WaFlag, x.WaHidden, x.EmId,
                EmployeeName = x.Em == null ? "未設定" : x.Em.EmName,
                MakerName = x.Ha.Ma.MaName,
                PurchaseDate = (DateTime?)(actor.IsAdministrator || x.Ha.HaFlag == ActiveFlag ? x.Ha.HaDate : null),
                LineCount = x.TWarehousingDetails.Count(),
                Quantity = x.TWarehousingDetails.Sum(d => (int?)d.WaQuantity) ?? 0
            }).ToList();

        var ids = headers.Select(x => x.WaId).ToArray();
        var productsByReceipt = ids.Length == 0 ? new Dictionary<int, ReceiptProductSearch[]>()
            : db.TWarehousingDetails.AsNoTracking().Where(x => ids.Contains(x.WaId))
                .Select(x => new { x.WaId, x.PrId, Code = x.Pr.PrJcode ?? "", x.Pr.PrName })
                .ToList().GroupBy(x => x.WaId)
                .ToDictionary(x => x.Key, x => x.Select(y => new ReceiptProductSearch(y.PrId, y.Code, y.PrName)).ToArray());
        var records = headers.Select(x =>
        {
            var products = productsByReceipt.GetValueOrDefault(x.WaId) ?? [];
            return new WarehouseMovementRecord(WarehouseMovementKind.Inbound,
                x.WaId, x.HaId, x.MakerId, x.MakerName, "倉庫情報なし", null, x.EmId, x.EmployeeName,
                x.WaDate, x.WaShelfFlag, x.WaFlag, x.WaHidden, x.LineCount, x.Quantity,
                string.Join(" ", products.Select(p => $"P-{p.Id:0000} {p.Code} {p.Name}")))
            { PurchaseDate = x.PurchaseDate, ReceiptProducts = products };
        }).ToArray();
        var makers = db.MMakers.AsNoTracking().Where(x => x.MaFlag == ActiveFlag).OrderBy(x => x.MaName)
            .Select(x => new WarehouseLookupOption(x.MaId, x.MaName)).ToList();
        var employees = db.MEmployees.AsNoTracking().Where(x => x.EmFlag == ActiveFlag && x.Po.PoFlag == ActiveFlag)
            .OrderBy(x => x.EmName).Select(x => new WarehouseLookupOption(x.EmId, x.EmName)).ToList();
        return new(records, makers, employees, Summarize(records));
    }

    public static WarehouseMovementDetail? LoadDetail(int id) => LoadDetail(id, includePurchase: true);

    internal static WarehouseMovementDetail? LoadDetail(int id, bool includePurchase)
    {
        if (id <= 0) return null;
        WarehouseActorResolver.EnsureCanUse();
        using var db = new SalesManagementContext();
        var actor = WarehouseActorResolver.Resolve(db, AppSession.Employee!.EmId);
        WarehouseActorResolver.EnsureCanUse(actor);
        var query = db.TWarehousings.AsNoTracking().Where(x => x.WaId == id);
        if (!actor!.IsAdministrator) query = query.Where(x => x.WaFlag == ActiveFlag && x.Ha.RegisteredOfficeId == actor.OfficeId);
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

    internal static WarehouseMovementOperationResult ConfirmShelfInContext(SalesManagementContext db, int actorId, int warehousingId,
        IReadOnlyCollection<WarehouseMovementLineInput>? expected) =>
        ConfirmShelfCore(db, actorId, warehousingId, expected, DateTime.Today);

    public static WarehouseMovementOperationResult ConfirmDemoShelf(int actorId, int warehousingId,
        IReadOnlyCollection<WarehouseMovementLineInput> expected, DateTime receiptDate)
    {
        if (!SalesManagementContext.IsDemoMode)
            return Denied("デモ用の入庫日を設定できるのはデモDBだけです。");
        using var db = new SalesManagementContext();
        return ConfirmShelfCore(db, actorId, warehousingId, expected, receiptDate.Date);
    }

    private static WarehouseMovementOperationResult ConfirmShelfCore(SalesManagementContext db, int actorId, int warehousingId,
        IReadOnlyCollection<WarehouseMovementLineInput>? expected, DateTime receiptDate)
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
                .SingleOrDefault(x => x.HaId == receipt.HaId && (x.HaFlag == ActiveFlag || x.HaFlag == HiddenFlag)
                    && (actor!.IsAdministrator || x.RegisteredOfficeId == actor.OfficeId));
            if (purchase == null) return Denied("関連する発注情報を確認できません。");
            var incoming = db.TWarehousingDetails.AsNoTracking().Where(x => x.WaId == warehousingId)
                .Select(x => new WarehouseMovementLineInput(x.PrId, x.WaQuantity)).ToList();
            if (WarehouseMovementRules.ValidateLines(incoming) is { } linesError) return Denied(linesError);
            if (expected != null && (expected.Count != incoming.Count || !WarehouseMovementRules.LinesMatch(expected, incoming)))
                return Denied("明細が更新されました。詳細を開き直して数量を確認してください。");
            var orderedProductIds = purchase.THattyuDetails.Select(x => x.PrId).ToHashSet();
            if (incoming.Any(x => !orderedProductIds.Contains(x.ProductId)))
                return Denied("入庫明細に発注明細にない商品が含まれています。");

            var productIds = incoming.Select(x => x.ProductId).Distinct().Order().ToArray();
            var products = db.MProducts.Where(x => productIds.Contains(x.PrId) && x.PrFlag == ActiveFlag
                && x.MaId == purchase.MaId && x.Ma.MaFlag == ActiveFlag).ToDictionary(x => x.PrId);
            if (products.Count != productIds.Length)
                return Denied("商品またはメーカー情報を確認できません。");

            var quantities = incoming.GroupBy(x => x.ProductId)
                .ToDictionary(x => x.Key, x => x.Sum(line => (long)line.Quantity));
            foreach (var productId in productIds)
            {
                var stockRows = db.TStocks.FromSqlInterpolated(
                    $"SELECT * FROM dbo.T_Stock WITH (UPDLOCK, HOLDLOCK) WHERE PrID = {productId}").ToList();
                var activeStocks = stockRows.Where(x => x.StFlag == ActiveFlag).ToArray();
                if (activeStocks.Length == 0 && stockRows.Count == 0)
                {
                    BusinessService.EnsureStock(products[productId], db);
                    db.SaveChanges();
                    stockRows = db.TStocks.FromSqlInterpolated(
                        $"SELECT * FROM dbo.T_Stock WITH (UPDLOCK, HOLDLOCK) WHERE PrID = {productId}").ToList();
                    activeStocks = stockRows.Where(x => x.StFlag == ActiveFlag).ToArray();
                }
                if (activeStocks.Length != 1)
                    return Denied("商品在庫が未登録または重複しています。データを確認してください。");
                var stock = activeStocks[0];
                var nextQuantity = (long)stock.StQuantity + quantities[productId];
                if (stock.StQuantity < 0 || nextQuantity > int.MaxValue)
                    return Denied("在庫数量が保存可能な範囲を超えるため棚入れできません。");
                stock.StQuantity = (int)nextQuantity;
            }

            receipt.WaShelfFlag = ConfirmedState;
            receipt.WaDate = receiptDate;
            receipt.EmId = actor!.EmployeeId;
            db.SaveChanges();
            transaction.Commit();
            return new(true, "棚入れを確定し、商品在庫を更新しました。入庫日と確定担当者を記録しました。", receipt.WaId);
        }
        catch (UnauthorizedAccessException ex) { return Denied(ex.Message); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Warehousing confirmation failed: {ex}");
            return Denied("棚入れと在庫更新を保存できませんでした。データベースの状態を確認してください。");
        }
    }

    public static WarehouseMovementOperationResult Hide(int actorId, int warehousingId, string? reason)
    {
        using var db = new SalesManagementContext();
        return HideInContext(db, actorId, warehousingId, reason);
    }

    internal static WarehouseMovementOperationResult HideInContext(SalesManagementContext db, int actorId, int warehousingId, string? reason)
    {
        if (!CanHide || AppSession.Employee?.EmId != actorId)
            return Denied("入庫を非表示にする権限がありません。管理者または物流担当としてログインしてください。");
        if (ValidateHideReason(reason) is { } validation) return Denied(validation);
        try
        {
            using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
            var actor = WarehouseActorResolver.Resolve(db, actorId);
            if (actor == null) return Denied("入庫を非表示にする権限がありません。");
            var receipt = db.TWarehousings.FromSqlInterpolated(
                $"SELECT * FROM dbo.T_Warehousing WITH (UPDLOCK, HOLDLOCK) WHERE WaID = {warehousingId}")
                .SingleOrDefault();
            if (receipt == null) return Denied("対象の入庫が見つかりません。");
            if (!actor.IsAdministrator && !db.THattyus.Any(x => x.HaId == receipt.HaId && x.RegisteredOfficeId == actor.OfficeId))
                return Denied("自営業所の発注に紐づく入庫だけ非表示にできます。");
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
        if (!IsRestorable(expected)) return Denied("非表示理由と有効な業務状態が記録された入庫だけ復元できます。");
        try
        {
            using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
            if (WarehouseActorResolver.Resolve(db, actorId) is not { IsAdministrator: true })
                return Denied("入庫を復元する権限がありません。");
            var receipt = db.TWarehousings.FromSqlInterpolated(
                $"SELECT * FROM dbo.T_Warehousing WITH (UPDLOCK, HOLDLOCK) WHERE WaID = {expected.Id}").SingleOrDefault();
            if (receipt == null || receipt.WaFlag != HiddenFlag || (receipt.WaShelfFlag is not (UnconfirmedState or ConfirmedState) || receipt.WaShelfFlag != expected.BusinessState)
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
