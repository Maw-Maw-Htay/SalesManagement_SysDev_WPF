using Microsoft.EntityFrameworkCore;
using SalesManagement_SysDev;
using static SalesManagement.Demo.Program;

namespace SalesManagement.Demo;

internal sealed class DemoSeeder(SalesManagementContext db, DateTime anchor, IReadOnlyDictionary<int, string> credentials)
{
    private const string HiddenReason = "【デモ】契約終了による参照専用データ";
    private static readonly TimeSpan JapanOffset = TimeSpan.FromHours(9);
    private DemoKeys keys = null!;

    internal void Seed()
    {
        if (!db.MEmployees.Any()) SeedMasters();
        keys = DemoKeys.Read(db);
        Require(EmployeePasswordHasher.Verify(credentials[DemoData.Admin], keys.Employees[DemoData.Admin].EmPasswordHash),
            "The encrypted demo administrator credential does not match the database.");

        SeedStaffAssignments();
        foreach (var purchase in DemoData.Purchases(anchor)) SeedPurchase(purchase);
        Console.WriteLine("Prepared 15 purchase orders and their staged receipts.");
        foreach (var order in DemoData.SalesOrders(anchor)) SeedSalesOrder(order);
        Console.WriteLine("Prepared 40 customer orders through the planned workflow stages.");
        DateAndMarkDemoOperations();
    }

    private void SeedMasters()
    {
        Require(Scalar<long>(db, """
            SELECT COALESCE(SUM(row_count), CONVERT(bigint,0))
            FROM sys.dm_db_partition_stats
            WHERE index_id <= 1 AND object_id IN (SELECT object_id FROM sys.tables WHERE is_ms_shipped = 0);
            """) == 0, "Demo masters can only be initialized in the empty provisioned database.");

        using var transaction = db.Database.BeginTransaction();
        var offices = DemoData.Offices.Select((office, index) => new MSalesOffice
        {
            SoName = office.Name, SoAddress = office.City + " 架空OA業務団地",
            SoPhone = PlaceholderPhone(100 + index), SoFax = PlaceholderPhone(110 + index), SoPostal = "0000000", SoFlag = 0
        }).ToArray();
        var roles = new[] { "管理者", "営業", "物流" }.ToDictionary(role => role, role => new MPosition { PoName = role, PoFlag = 0 });
        var makers = DemoData.Suppliers.Select((supplier, index) => new MMaker
        {
            MaName = supplier.Name, MaAddress = "関西圏 架空機器産業団地 " + (index + 1), MaPostal = "0000000",
            MaPhone = PlaceholderPhone(300 + index), MaFax = PlaceholderPhone(320 + index), MaFlag = 0
        }).ToArray();
        var categories = DemoData.Suppliers.Select(supplier => supplier.Category).Distinct().ToDictionary(
            category => category, category => new MMajorClassification { McName = category, McFlag = 0 });
        var products = DemoData.Suppliers.SelectMany((supplier, maker) =>
        {
            var classification = new MSmallClassification { ScName = supplier.Group, ScFlag = 0, Mc = categories[supplier.Category] };
            return supplier.Products.Select((product, model) =>
            {
                var index = maker * 5 + model;
                return new MProduct
                {
                    Ma = makers[maker], Sc = classification, PrName = product.Name, PrModelNumber = product.Model,
                    Price = product.Price, PrJcode = DemoData.Barcode(index), PrColor = product.Color,
                    PrSafetyStock = product.SafetyStock, PrReleaseDate = anchor.AddMonths(-6 - index % 17),
                    PrFlag = index == 49 ? 2 : 0, PrHidden = index == 49 ? "【デモ】旧モデルの取扱終了" : null,
                    TStocks = [new() { StFlag = 0, StQuantity = product.Opening }]
                };
            });
        }).ToArray();

        db.MSalesOffices.AddRange(offices);
        db.MPositions.AddRange(roles.Values);
        db.MMakers.AddRange(makers);
        db.MProducts.AddRange(products);
        db.MEmployees.AddRange(DemoData.Employees.Select(employee => new MEmployee
        {
            EmId = employee.Id, EmName = employee.Name, So = offices[employee.Office], Po = roles[employee.Role],
            EmHiredate = new DateTime(2017 + employee.Id % 8, 4, 1), EmPhone = PlaceholderPhone(employee.Id),
            EmPassword = EmployeePasswordHasher.CreateLegacyPlaceholder(), EmPasswordHash = EmployeePasswordHasher.Hash(credentials[employee.Id]),
            EmFlag = employee.Hidden ? 2 : 0, EmHidden = employee.Hidden ? "【デモ】退職によるアカウント無効化" : null
        }));
        db.MClients.AddRange(DemoData.Customers.Select(customer => new MClient
        {
            So = offices[customer.Office], ClName = customer.Name,
            ClAddress = DemoData.Offices[customer.Office].City + $" 架空業務街 第{customer.Number:00}棟",
            ClPhone = PlaceholderPhone(400 + customer.Number), ClFax = PlaceholderPhone(500 + customer.Number), ClPostal = "0000000",
            ClFlag = customer.Hidden ? 2 : 0, ClHidden = customer.Hidden ? HiddenReason : null
        }));
        db.SaveChanges();
        transaction.Commit();
        db.ChangeTracker.Clear();
        Console.WriteLine("Created fictional master rows and one documented opening balance per product.");
    }

    private static string PlaceholderPhone(int suffix) => $"000-0000-{suffix:0000}";

    private void SeedStaffAssignments()
    {
        ActAs(DemoData.Admin);
        Transfer(9104, keys.Offices[2], keys.Roles["営業"]);
        Transfer(9110, keys.Offices[3], keys.Roles["物流"]);
        keys = DemoKeys.Read(db);

        void Transfer(int id, int officeId, int roleId)
        {
            var employee = EmployeeService.GetDetail(id)!.Employee;
            if (employee.OfficeId == officeId && employee.PositionId == roleId) return;
            var result = EmployeeService.Update(DemoData.Admin, EmployeeService.Snapshot(employee), employee.Name, employee.Phone, officeId, roleId);
            Require(result.Succeeded, $"Preparing demo assignment for {id} failed. {result.Message}");
        }
    }

    private void SeedPurchase(PurchaseSeed plan)
    {
        var actor = DemoData.Logistics(plan.Office);
        ActAs(actor);
        var maker = keys.Makers[plan.Supplier];
        var purchase = db.THattyus.AsNoTracking().SingleOrDefault(row => row.MaId == maker && row.HaDate == plan.Date && row.EmId == actor);
        var purchaseId = purchase?.HaId;
        if (purchaseId == null)
        {
            var lines = plan.Lines.Select(line => new PurchaseLineInput(keys.Products[line.Product].PrId, line.Quantity)).ToArray();
            var result = PurchaseOrderService.Register(actor, maker, plan.Date, lines);
            Require(result.Succeeded && result.OrderId.HasValue, $"Purchase {plan.Number} failed. {result.Message}");
            purchaseId = result.OrderId;
        }

        foreach (var planned in plan.Receipts)
        {
            var lines = planned.Lines.Select(line => new WarehouseMovementLineInput(keys.Products[line.Product].PrId, line.Quantity)).ToArray();
            var receipt = db.TWarehousings.AsNoTracking().SingleOrDefault(row => row.HaId == purchaseId && row.WaDate == planned.Date);
            if (receipt == null)
            {
                // The current application confirms existing receipts. The fixture stages the inbound documents.
                receipt = new TWarehousing
                {
                    HaId = purchaseId!.Value, EmId = actor, WaDate = planned.Date, WaFlag = 0, WaShelfFlag = 0,
                    TWarehousingDetails = lines.Select(line => new TWarehousingDetail { PrId = line.ProductId, WaQuantity = line.Quantity }).ToList()
                };
                db.TWarehousings.Add(receipt);
                db.SaveChanges();
                db.ChangeTracker.Clear();
            }
            if (planned.Confirmed && receipt.WaShelfFlag == 0)
            {
                var result = WarehousingService.ConfirmShelf(actor, receipt.WaId, lines);
                Require(result.Succeeded, $"Receipt {receipt.WaId} failed. {result.Message}");
            }
        }
    }

    private void SeedSalesOrder(SaleSeed plan)
    {
        var sales = DemoData.Sales(plan.Customer.Office);
        var logistics = DemoData.Logistics(plan.Customer.Office);
        var office = keys.Offices[plan.Customer.Office];
        ActAs(sales);
        var order = db.TOrders.AsNoTracking().SingleOrDefault(row => row.ClCharge == plan.Note);
        if (order == null)
        {
            var lines = plan.Lines.Select(line => new OrderDetailUpdate(null, keys.Products[line.Product].PrId,
                line.Quantity, DemoData.Products[line.Product].Price)).ToArray();
            var created = BusinessService.CreateOrder(sales, keys.Customers[plan.Customer.Number], office, plan.Date,
                plan.Note, lines, out var id, out var message, plan.Delivery);
            Require(created, $"Order {plan.Number} failed. {message}");
            order = db.TOrders.AsNoTracking().Single(row => row.OrId == id);
        }

        if (plan.Hidden && order.OrFlag == 0)
        {
            ActAs(DemoData.Admin);
            var hidden = BusinessService.SetOrderVisibility(order.OrId, DemoData.Admin, true, "【デモ】見積の重複依頼を整理", out var reason);
            Require(hidden, $"Hiding order {plan.Number} failed. {reason}");
        }

        if (plan.Stage < SalesStage.OrderCreated) return;
        if (order.OrStateFlag == 0)
        {
            var confirmed = BusinessService.ConfirmOrder(order.OrId, sales, out var message);
            Require(confirmed, $"Order confirmation {plan.Number} failed. {message}");
        }

        if (plan.Stage < SalesStage.OutboundCreated) return;
        var chumon = db.TChumons.AsNoTracking().Single(row => row.OrId == order.OrId);
        if (chumon.ChStateFlag == 0)
        {
            var confirmed = BusinessService.ConfirmChumon(chumon.ChId, sales, out var message, out _);
            Require(confirmed, $"Stock deduction for order {plan.Number} failed. {message}");
        }

        if (plan.Stage < SalesStage.ArrivalCreated) return;
        var outbound = db.TSyukkos.AsNoTracking().Single(row => row.OrId == order.OrId);
        if (outbound.SyStateFlag == 0)
        {
            ActAs(logistics);
            var result = OutboundService.Confirm(logistics, outbound.SyId);
            Require(result.Succeeded, $"Outbound confirmation {plan.Number} failed. {result.Message}");
        }

        if (plan.Stage < SalesStage.ShipmentCreated) return;
        var arrival = db.TArrivals.AsNoTracking().Single(row => row.OrId == order.OrId);
        if (arrival.ArStateFlag == 0)
        {
            ActAs(sales);
            var result = ArrivalService.Confirm(sales, arrival.ArId);
            Require(result.Succeeded, $"Arrival confirmation {plan.Number} failed. {result.Message}");
        }

        if (plan.Stage < SalesStage.SaleRecorded) return;
        var shipment = db.TShipments.AsNoTracking().Single(row => row.OrId == order.OrId);
        if (shipment.ShStateFlag == 0)
        {
            ActAs(sales);
            var result = ShipmentService.Confirm(sales, shipment.ShId);
            Require(result.Succeeded && !result.SaleReused, $"Shipment confirmation {plan.Number} failed. {result.Message}");
        }
    }

    private void DateAndMarkDemoOperations()
    {
        // Operational services stamp today's date. Move only the dates and audit labels of the isolated fiction.
        // Status flags, document lines, recorded amounts and stock quantities from the services remain authoritative.
        using var transaction = db.Database.BeginTransaction();
        foreach (var plan in DemoData.SalesOrders(anchor))
        {
            var order = db.TOrders.AsNoTracking().Single(row => row.ClCharge == plan.Note);
            var chumon = db.TChumons.SingleOrDefault(row => row.OrId == order.OrId);
            if (chumon != null) chumon.ChDate = DemoData.BusinessDays(plan.Date, 1);

            var outbound = db.TSyukkos.SingleOrDefault(row => row.OrId == order.OrId);
            if (outbound?.SyStateFlag == 1) outbound.SyDate = DemoData.BusinessDays(plan.Date, 2);

            var arrival = db.TArrivals.SingleOrDefault(row => row.OrId == order.OrId);
            if (arrival?.ArStateFlag == 1) arrival.ArDate = DemoData.BusinessDays(plan.Date, 3);

            var shipment = db.TShipments.SingleOrDefault(row => row.OrId == order.OrId);
            if (shipment?.ShStateFlag == 1) shipment.ShFinishDate = DemoData.BusinessDays(plan.Date, 4);

            var sale = db.TSales.SingleOrDefault(row => row.OrId == order.OrId);
            if (sale != null) sale.SaDate = DemoData.BusinessDays(plan.Date, 4);

            foreach (var audit in db.OrderOperationHistories.Where(row => row.OrderId == order.OrId))
            {
                var actionDate = audit.Operation == OrderHistoryOperations.Confirmed ? DemoData.BusinessDays(plan.Date, 1) : plan.Date;
                var actionHour = audit.Operation == OrderHistoryOperations.Created ? 9 : 11;
                audit.RecordedAt = JapanTime(actionDate, actionHour, 5 + plan.Number);
                if (!audit.ActorName.EndsWith(DemoData.AuditLabel, StringComparison.Ordinal)) audit.ActorName += DemoData.AuditLabel;
                if (!audit.Details.Contains("【デモデータ】", StringComparison.Ordinal))
                    audit.Details += "\n【デモデータ】既存サービスで生成した架空の操作です。";
            }
        }
        foreach (var audit in db.EmployeeAssignmentHistories)
        {
            audit.RecordedAt = JapanTime(DemoData.BusinessDays(anchor, -10), 14, audit.EmployeeId % 60);
            if (!audit.ActorName.EndsWith(DemoData.AuditLabel, StringComparison.Ordinal)) audit.ActorName += DemoData.AuditLabel;
        }
        db.SaveChanges();
        transaction.Commit();
        db.ChangeTracker.Clear();
    }

    private static DateTimeOffset JapanTime(DateTime day, int hour, int minute) =>
        new(DateTime.SpecifyKind(day.Date.AddHours(hour).AddMinutes(minute), DateTimeKind.Unspecified), JapanOffset);

    private void ActAs(int id) => AppSession.Employee = keys.Employees[id];
}

internal sealed record DemoKeys(int[] Offices, int[] Makers, MProduct[] Products,
    IReadOnlyDictionary<int, int> Customers, IReadOnlyDictionary<string, int> Roles, IReadOnlyDictionary<int, MEmployee> Employees)
{
    internal static DemoKeys Read(SalesManagementContext db)
    {
        var offices = db.MSalesOffices.AsNoTracking().ToDictionary(row => row.SoName, row => row.SoId);
        var makers = db.MMakers.AsNoTracking().ToDictionary(row => row.MaName, row => row.MaId);
        var products = db.MProducts.AsNoTracking().ToDictionary(row => row.PrModelNumber);
        var customers = db.MClients.AsNoTracking().ToDictionary(row => row.ClName, row => row.ClId);
        var roles = db.MPositions.AsNoTracking().ToDictionary(row => row.PoName, row => row.PoId);
        var employees = db.MEmployees.AsNoTracking().Include(row => row.Po).Include(row => row.So).ToDictionary(row => row.EmId);
        Require(offices.Count == DemoData.Offices.Length && makers.Count == DemoData.Suppliers.Length
            && products.Count == DemoData.Products.Length && customers.Count == DemoData.Customers.Length
            && roles.Count == 3 && employees.Keys.Order().SequenceEqual(DemoData.Employees.Select(row => row.Id)),
            "The target does not contain the expected demo master records.");
        return new(DemoData.Offices.Select(row => offices[row.Name]).ToArray(),
            DemoData.Suppliers.Select(row => makers[row.Name]).ToArray(), DemoData.Products.Select(row => products[row.Model]).ToArray(),
            DemoData.Customers.ToDictionary(row => row.Number, row => customers[row.Name]), roles, employees);
    }
}
