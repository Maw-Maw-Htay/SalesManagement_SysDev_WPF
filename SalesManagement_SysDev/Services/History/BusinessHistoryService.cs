using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public sealed record BusinessHistoryRow(string Module, int Id, string Number, DateTime? Date, string Target,
    string RelatedNumber, string State, string Management, string Office)
{
    public string DateText => Date?.ToString("yyyy/MM/dd") ?? "未記録";
}

/// <summary>Current completed business records, kept separate from recorded audit events.</summary>
public static class BusinessHistoryService
{
    public static IReadOnlyList<string> GetModules(string role) => role switch
    {
        "管理者" => ["受注管理", "注文管理", "発注管理", "入庫管理", "出庫管理", "入荷管理", "出荷管理", "売上管理", "在庫管理"],
        "営業" => ["受注管理", "注文管理", "入荷管理", "出荷管理", "売上管理"],
        "物流" => ["発注管理", "入庫管理", "出庫管理", "在庫管理"],
        _ => []
    };

    public static async Task<IReadOnlyList<BusinessHistoryRow>> ReadAsync(string module, CancellationToken token = default)
    {
        using var db = new SalesManagementContext();
        var viewer = await AuthorizeAsync(db, module, token);
        var isAdmin = viewer.Po.PoName == "管理者";
        var office = viewer.SoId;
        if (module == "受注管理")
        {
            var records = await db.TOrders.AsNoTracking().Where(x => x.OrStateFlag == 1 && (isAdmin || x.SoId == office && x.OrFlag == 0))
                .Select(x => new { x.OrId, x.OrDate, x.Cl.ClName, x.OrFlag, x.So.SoName }).ToListAsync(token);
            return records.Select(x => new BusinessHistoryRow(module, x.OrId, $"ORD-{x.OrId:000000}", x.OrDate,
                x.ClName, "—", "確定済み", Management(x.OrFlag), x.SoName)).ToArray();
        }
        if (module == "注文管理")
        {
            var records = await db.TChumons.AsNoTracking().Where(x => x.ChStateFlag == 1 && (isAdmin || x.SoId == office && x.ChFlag == 0))
                .Select(x => new { x.ChId, x.ChDate, x.Cl.ClName, x.OrId, x.ChFlag, x.So.SoName }).ToListAsync(token);
            return records.Select(x => new BusinessHistoryRow(module, x.ChId, $"CH-{x.ChId:000000}", x.ChDate,
                x.ClName, $"ORD-{x.OrId:000000}", "確定済み", Management(x.ChFlag), x.SoName)).ToArray();
        }

        return await Task.Run<IReadOnlyList<BusinessHistoryRow>>(() => module switch
        {
            "発注管理" => PurchaseOrderService.Load().Orders.Where(x => x.IsFullyReceived).Select(x =>
                new BusinessHistoryRow(module, x.Id, x.Code, x.OrderDate, x.MakerName, "—", x.ProgressLabel, x.ManagementLabel, "未記録")).ToArray(),
            "入庫管理" => WarehousingService.Load().Records.Where(x => x.IsConfirmed).Select(WarehouseRow).ToArray(),
            "出庫管理" => OutboundService.Load().Records.Where(x => x.IsConfirmed).Select(WarehouseRow).ToArray(),
            "入荷管理" => ArrivalService.Load().Arrivals.Where(x => x.IsConfirmed).Select(x =>
                new BusinessHistoryRow(module, x.Id, x.Code, x.ArrivalDate, x.CustomerName, x.OrderCode, x.ArrivalStateLabel, x.ManagementLabel, x.OfficeName)).ToArray(),
            "出荷管理" => ShipmentService.Load().Shipments.Where(x => x.IsConfirmed).Select(x =>
                new BusinessHistoryRow(module, x.Id, x.Code, x.ConfirmedDate, x.CustomerName, x.OrderCode, x.StateLabel, x.ManagementLabel, x.OfficeName)).ToArray(),
            "売上管理" => SalesService.Load().Sales.Select(x =>
                new BusinessHistoryRow(module, x.Id, x.Code, x.SaleDate, x.CustomerName, x.OrderCode, "売上記録", x.ManagementLabel, x.OfficeName)).ToArray(),
            _ => []
        }, token);
    }

    internal static async Task<MEmployee> AuthorizeAsync(SalesManagementContext db, string module, CancellationToken token = default)
    {
        var viewer = await HistoryManagementService.GetAuthorizedViewerAsync(db, token, allowLogistics: true);
        if (!GetModules(viewer.Po.PoName).Contains(module)) throw new UnauthorizedAccessException("この業務履歴を参照する権限がありません。");
        return viewer;
    }

    private static string Management(int flag) => flag switch { 0 => "表示中", 2 => "非表示", _ => "要確認" };

    private static BusinessHistoryRow WarehouseRow(WarehouseMovementRecord x) =>
        new(x.Kind == WarehouseMovementKind.Inbound ? "入庫管理" : "出庫管理", x.Id, x.Code, x.MovementDate,
            x.PartnerDisplay, x.ReferenceCode, x.BusinessStatusLabel, x.ManagementLabel, string.IsNullOrWhiteSpace(x.OfficeName) ? "未記録" : x.OfficeName);

}
