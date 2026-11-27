using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public sealed record ProductOrderRow(int OrderId, DateTime OrderDate, string Customer, int Quantity, decimal Amount, int State, int ManagementFlag)
{
    public string Code => $"ORD-{OrderId:000000}";
    // T_OrderDetail stores quantity and line total, not a separate unit-price column.
    public decimal? RecordedUnitPrice => Quantity == 0 ? null : Amount / Quantity;
    public string UnitPriceText => RecordedUnitPrice is { } price ? $"¥{price:#,##0.##}" : "未記録";
    public string Status => OrderDetailHistoryRow.StateName(State) + (ManagementFlag == 2 ? "・非表示" : "");
}

public sealed record ProductOrderPage(IReadOnlyList<ProductOrderRow> Rows, int PageNumber, int PageCount, int Total);

public static class ProductOrderHistory
{
    public static ProductOrderPage ReadPage(int productId, int requestedPage, int pageSize = 20)
    {
        using var db = new SalesManagementContext();
        var lines = ScopedLines(db, productId);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = lines.Count();
        var pages = Math.Max(1, (total + pageSize - 1) / pageSize);
        var page = Math.Clamp(requestedPage, 1, pages);
        var rows = lines.OrderByDescending(x => x.Or.OrDate).ThenByDescending(x => x.OrId).ThenByDescending(x => x.OrDetailId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new ProductOrderRow(x.OrId, x.Or.OrDate, x.Or.Cl.ClName, x.OrQuantity, x.OrTotalPrice,
                x.Or.OrStateFlag ?? 0, x.Or.OrFlag)).ToList();
        return new(rows, page, pages, total);
    }

    public static OrderDetailSnapshot? ReadDetail(int productId, int orderId)
    {
        using var db = new SalesManagementContext();
        var lines = ScopedLines(db, productId);
        var orders = db.TOrders.AsNoTracking().Where(x => lines.Any(line => line.OrId == x.OrId));
        return RelatedOrderDetail.Read(orders, orderId, recordedUnitPricesOnly: true);
    }

    private static IQueryable<TOrderDetail> ScopedLines(SalesManagementContext db, int productId)
    {
        if (!ProductService.CanViewOrders) throw new UnauthorizedAccessException("受注を参照する権限がありません。");
        var actor = ProductService.GetActor(db);
        return ForProduct(db.TOrderDetails.AsNoTracking(), productId, actor.IsAdmin ? null : actor.OfficeId);
    }

    internal static IQueryable<TOrderDetail> ForProduct(IQueryable<TOrderDetail> lines, int productId, int? officeId)
    {
        var matching = lines.Where(line => line.PrId == productId && (line.OrQuantity != 0 || line.OrTotalPrice != 0m));
        return officeId is { } office ? matching.Where(line => line.Or.SoId == office) : matching;
    }
}
