using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public sealed record CustomerOrderRow(int Id, DateTime OrderDate, int LineCount, decimal Amount, int State, int ManagementFlag)
{
    public string Code => $"ORD-{Id:000000}";
    public string Status => OrderDetailHistoryRow.StateName(State);
    public string VisibilityStatus => ManagementFlag == 2 ? "非表示" : "";
}

public sealed record CustomerOrderPage(IReadOnlyList<CustomerOrderRow> Rows, int PageNumber, int PageCount, int Total);

/// <summary>顧客に紐づく受注の読取専用照会。</summary>
public static class CustomerOrderHistory
{
    public static CustomerOrderPage ReadPage(int customerId, int requestedPage, int pageSize = 20)
    {
        using var db = new SalesManagementContext();
        var query = ScopedOrders(db, customerId);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = query.Count();
        var pages = Math.Max(1, (total + pageSize - 1) / pageSize);
        var page = Math.Clamp(requestedPage, 1, pages);
        var rows = query.OrderByDescending(x => x.OrDate).ThenByDescending(x => x.OrId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new CustomerOrderRow(x.OrId, x.OrDate,
                x.TOrderDetails.Count(line => line.OrQuantity != 0 || line.OrTotalPrice != 0m),
                x.TOrderDetails.Sum(line => (decimal?)line.OrTotalPrice) ?? 0m, x.OrStateFlag ?? 0, x.OrFlag))
            .ToList();
        return new(rows, page, pages, total);
    }

    public static OrderDetailSnapshot? ReadDetail(int customerId, int orderId)
    {
        using var db = new SalesManagementContext();
        return RelatedOrderDetail.Read(ScopedOrders(db, customerId), orderId);
    }

    private static IQueryable<TOrder> ScopedOrders(SalesManagementContext db, int customerId)
    {
        const string denied = "この顧客の受注を参照する権限がありません。";
        if (!CustomerService.CanViewOrders) throw new UnauthorizedAccessException(denied);
        var actor = CustomerService.GetActor(db);
        if (!actor.CanMaintain) throw new UnauthorizedAccessException(denied);
        // Check both the customer and the order office in the same SQL query.
        return ForCustomer(db.TOrders.AsNoTracking(), customerId, actor.IsAdmin ? null : actor.OfficeId);
    }

    internal static IQueryable<TOrder> ForCustomer(IQueryable<TOrder> orders, int customerId, int? scopeOfficeId)
    {
        var matchingCustomer = orders.Where(x => x.ClId == customerId);
        return scopeOfficeId is { } officeId
            ? matchingCustomer.Where(x => x.SoId == officeId && x.Cl.SoId == officeId)
            : matchingCustomer;
    }
}

internal static class RelatedOrderDetail
{
    internal static OrderDetailSnapshot? Read(IQueryable<TOrder> authorizedOrders, int orderId, bool recordedUnitPricesOnly = false)
    {
        var order = authorizedOrders
            .Include(x => x.Cl).Include(x => x.So).Include(x => x.Em)
            .Include(x => x.TOrderDetails).ThenInclude(x => x.Pr)
            .SingleOrDefault(x => x.OrId == orderId);
        if (order == null) return null;
        return new OrderDetailSnapshot
        {
            OrderId = order.OrId, OrderDate = order.OrDate, DeliveryDate = order.OrDeliveryDate,
            OfficeName = order.So.SoName, OfficeId = order.SoId, CustomerId = order.ClId,
            CustomerName = order.Cl.ClName, CustomerAddress = order.Cl.ClAddress, CustomerPhone = order.Cl.ClPhone,
            CustomerContact = order.ClCharge, EmployeeName = order.Em.EmName, EmployeeId = order.EmId,
            StateFlag = order.OrStateFlag ?? 0, ManagementFlag = order.OrFlag, HiddenReason = order.OrHidden,
            Lines = order.TOrderDetails.Where(x => !OrderUpdateRules.IsRemovedLine(x)).OrderBy(x => x.OrDetailId)
                .Select(x => new OrderDetailLine
                {
                    DetailId = x.OrDetailId, ProductId = x.PrId, ProductCode = x.Pr.PrJcode ?? x.Pr.PrModelNumber,
                    ProductName = x.Pr.PrName,
                    UnitPrice = x.OrQuantity == 0 ? (recordedUnitPricesOnly ? 0m : x.Pr.Price)
                        : decimal.Round(x.OrTotalPrice / x.OrQuantity, 0),
                    HasKnownUnitPrice = !recordedUnitPricesOnly || x.OrQuantity != 0,
                    Quantity = x.OrQuantity, Subtotal = x.OrTotalPrice
                }).ToList()
        };
    }

}
