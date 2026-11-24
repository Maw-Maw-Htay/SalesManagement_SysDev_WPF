using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

// Maps the existing lookups and registration service without changing their writes or transaction.
public interface IOrderRegistrationDataSource : IDisposable
{
    IReadOnlyList<MSalesOffice> LoadOffices();
    IReadOnlyList<MClient> LoadClients(int officeId);
    IReadOnlyList<RegistrationProduct> LoadProducts();
    InventoryStockLookup LoadStock(int productId)
    {
        var product = LoadProducts().FirstOrDefault(x => x.PrId == productId);
        if (product is { StockState: not InventoryStockState.NotRequested })
            return new InventoryStockLookup(product.StockState, product.Stock);
        return product?.Stock is int quantity
            ? new InventoryStockLookup(InventoryStockState.Available, quantity)
            : new InventoryStockLookup(InventoryStockState.MissingRecord);
    }
    IReadOnlyDictionary<int, InventoryStockLookup> LoadStocks(IReadOnlyCollection<int> productIds) =>
        productIds.Distinct().ToDictionary(id => id, id => LoadStock(id));
    bool Register(int employeeId, int customerId, int officeId, DateTime orderDate, DateTime deliveryDate,
        string remarks, IReadOnlyCollection<OrderDetailUpdate> details, out int orderId, out string message);
    OrderDetailSnapshot? LoadRegisteredOrder(int orderId);
}

internal sealed class OrderRegistrationDataSource : IOrderRegistrationDataSource
{
    private readonly SalesManagementContext db = new();
    public IReadOnlyList<MSalesOffice> LoadOffices() => db.MSalesOffices
        .Where(x => x.SoFlag == 0).OrderBy(x => x.SoId).ToList();
    public IReadOnlyList<MClient> LoadClients(int officeId) => db.MClients
        .Where(x => x.ClFlag == 0 && x.SoId == officeId).OrderBy(x => x.ClName).ToList();
    public IReadOnlyList<RegistrationProduct> LoadProducts() => db.MProducts.AsNoTracking()
        .Where(x => x.PrFlag == 0).OrderBy(x => x.PrName).Select(x => new RegistrationProduct
        {
            PrId = x.PrId, ProductCode = x.PrJcode ?? x.PrModelNumber, ProductName = x.PrName,
            ModelNumber = x.PrModelNumber, Barcode = x.PrJcode ?? "", MakerId = x.MaId, MakerName = x.Ma.MaName, UnitPrice = x.Price,
            CategoryName = x.Sc.ScName, SafetyStock = x.PrSafetyStock
        }).ToList();

    public InventoryStockLookup LoadStock(int productId) => InventoryService.ReadCurrentStock(db, [productId])[productId];

    public IReadOnlyDictionary<int, InventoryStockLookup> LoadStocks(IReadOnlyCollection<int> productIds)
    {
        // The update screen refreshes off the UI thread while product selection uses db.
        using var readContext = new SalesManagementContext();
        return InventoryService.ReadCurrentStock(readContext, productIds);
    }
    public bool Register(int employeeId, int customerId, int officeId, DateTime orderDate, DateTime deliveryDate,
        string remarks, IReadOnlyCollection<OrderDetailUpdate> details, out int orderId, out string message) =>
        BusinessService.CreateOrder(employeeId, customerId, officeId, orderDate, remarks, details, out orderId, out message, deliveryDate);

    public OrderDetailSnapshot? LoadRegisteredOrder(int orderId)
    {
        if (!AppSession.CanManageOrders || AppSession.Employee == null) return null;
        using var readContext = new SalesManagementContext();
        var query = readContext.TOrders.AsNoTracking().Include(order => order.Cl).Include(order => order.So)
            .Include(order => order.Em).Include(order => order.TOrderDetails).ThenInclude(line => line.Pr).AsQueryable();
        if (!AppSession.IsAdmin) query = query.Where(order => order.SoId == AppSession.Employee.SoId);
        var order = query.SingleOrDefault(order => order.OrId == orderId);
        if (order == null) return null;
        return new OrderDetailSnapshot
        {
            OrderId = order.OrId, OrderDate = order.OrDate, DeliveryDate = order.OrDeliveryDate,
            OfficeId = order.SoId, OfficeName = order.So.SoName, CustomerId = order.ClId,
            CustomerName = order.Cl.ClName, CustomerAddress = order.Cl.ClAddress, CustomerPhone = order.Cl.ClPhone,
            CustomerContact = order.ClCharge, EmployeeId = order.EmId, EmployeeName = order.Em.EmName,
            StateFlag = order.OrStateFlag ?? 0, ManagementFlag = order.OrFlag, HiddenReason = order.OrHidden,
            Lines = order.TOrderDetails.Where(line => line.OrQuantity != 0 || line.OrTotalPrice != 0m)
                .OrderBy(line => line.OrDetailId).Select(line => new OrderDetailLine
            {
                DetailId = line.OrDetailId, ProductId = line.PrId, ProductCode = line.Pr.PrJcode ?? line.Pr.PrModelNumber,
                ProductName = line.Pr.PrName, Quantity = line.OrQuantity, Subtotal = line.OrTotalPrice,
                UnitPrice = line.OrQuantity == 0 ? line.Pr.Price : decimal.Round(line.OrTotalPrice / line.OrQuantity, 0)
            }).ToList()
        };
    }
    public void Dispose() => db.Dispose();
}
