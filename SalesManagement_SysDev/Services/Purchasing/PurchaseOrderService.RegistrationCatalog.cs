using System.Data;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public static partial class PurchaseOrderService
{
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
}
