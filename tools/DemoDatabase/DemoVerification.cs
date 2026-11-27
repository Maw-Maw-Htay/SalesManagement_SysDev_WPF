using System.IO;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SalesManagement_SysDev;
using static SalesManagement.Demo.Program;

namespace SalesManagement.Demo;

internal sealed record ReconciledStock(string Model, string Name, int Opening, int Received, int Allocated, int Closing, int SafetyStock);

internal sealed class DemoReport(DateTime anchor)
{
    internal List<ReconciledStock> Stock { get; } = [];
    internal List<string> Checks { get; } = [];

    internal void Passed(string description)
    {
        Checks.Add(description);
        Console.WriteLine("PASS " + description);
    }

    internal void Write(string? directory)
    {
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var text = new StringBuilder("# Demo relationship and stock reconciliation\n\n");
        text.AppendLine($"Catalog `SalesManagement_Demo`, scenario date {anchor:yyyy-MM-dd} Japan time.").AppendLine();
        foreach (var check in Checks) text.AppendLine("- " + check);
        text.AppendLine().AppendLine("## Product opening and closing balances").AppendLine();
        text.AppendLine("Confirmed receipts add stock and 注文確定 deducts it. Opening + received − deducted = closing.");
        text.AppendLine().AppendLine("| Model | Product | Opening | Received | Deducted | Closing | Safety |")
            .AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var row in Stock)
            text.AppendLine($"| {row.Model} | {row.Name} | {row.Opening} | {row.Received} | {row.Allocated} | {row.Closing} | {row.SafetyStock} |");
        text.AppendLine($"| **Total** | | **{Stock.Sum(x => x.Opening)}** | **{Stock.Sum(x => x.Received)}** | **{Stock.Sum(x => x.Allocated)}** | **{Stock.Sum(x => x.Closing)}** | |");
        File.WriteAllText(Path.Combine(directory, "demo-reconciliation.md"), text.ToString(), new UTF8Encoding(false));
    }
}

internal static class DemoVerification
{
    internal static DemoReport Verify(SalesManagementContext db, DateTime anchor, IReadOnlyDictionary<int, string> credentials)
    {
        var report = new DemoReport(anchor);
        var keys = DemoKeys.Read(db);
        using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS;";
            using var reader = command.ExecuteReader();
            Require(!reader.Read(), "DBCC found an application constraint violation in the demo data.");
        }
        Require(Scalar<int>(db, "SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled = 0 AND is_not_trusted = 0;") == 50,
            "One or more of the expected foreign keys are missing, disabled, or untrusted.");
        report.Passed("SQL Server checked all constraints, and all 50 foreign keys are enabled and trusted.");

        VerifyMasters(db, keys, credentials, report);
        VerifySales(db, keys, anchor, report);
        VerifyPurchases(db, keys, anchor, report);
        VerifyStock(db, keys, report);
        VerifyQueries(db, keys, report);
        return report;
    }

    private static void VerifyMasters(SalesManagementContext db, DemoKeys keys, IReadOnlyDictionary<int, string> credentials, DemoReport report)
    {
        var products = DemoData.Products;
        for (var i = 0; i < products.Length; i++)
        {
            var row = keys.Products[i];
            var product = products[i];
            Require(row.PrName == product.Name && row.PrJcode == DemoData.Barcode(i) && row.MaId == keys.Makers[i / 5]
                && row.Price == product.Price && row.PrSafetyStock == product.SafetyStock && row.PrFlag == (i == 49 ? 2 : 0),
                $"Product master differs from the demo plan for {product.Model}.");
        }
        Require(keys.Products.Select(row => row.PrJcode).Distinct().Count() == products.Length, "Duplicate product codes.");

        var customers = db.MClients.AsNoTracking().ToDictionary(row => row.ClId);
        foreach (var customer in DemoData.Customers)
        {
            var row = customers[keys.Customers[customer.Number]];
            Require(row.SoId == keys.Offices[customer.Office] && row.ClFlag == (customer.Hidden ? 2 : 0),
                $"Customer {customer.Number} has an unexpected office or status.");
        }
        foreach (var employee in DemoData.Employees)
        {
            var row = keys.Employees[employee.Id];
            var office = employee.Id == 9104 ? 2 : employee.Office;
            var role = employee.Id == 9110 ? "物流" : employee.Role;
            Require(row.EmName == employee.Name && row.SoId == keys.Offices[office] && row.Po.PoName == role
                && row.EmFlag == (employee.Hidden ? 2 : 0) && row.EmPassword != credentials[employee.Id]
                && row.EmPasswordHash?.StartsWith("pbkdf2-sha256$", StringComparison.Ordinal) == true,
                $"Employee {employee.Id} has an unexpected assignment, state, or authentication storage.");
            var login = BusinessService.Login(employee.Id, credentials[employee.Id]);
            Require(employee.Hidden ? login == null : login?.EmId == employee.Id && login.Po.PoName == role,
                $"The real login service returned an unexpected result for employee {employee.Id}.");
        }
        Require(BusinessService.Login(DemoData.Admin, "invalid-demo-credential") == null,
            "The administrator accepted an invalid credential.");
        report.Passed("Five offices, 30 customers, ten makers and 50 products match the plan. All 14 active logins and the disabled-account rejection were verified.");
    }

    private static void VerifySales(SalesManagementContext db, DemoKeys keys, DateTime anchor, DemoReport report)
    {
        var plans = DemoData.SalesOrders(anchor);
        var orders = db.TOrders.AsNoTracking().Include(x => x.TOrderDetails).ToList();
        var orderByNote = orders.ToDictionary(x => x.ClCharge!);
        var chumon = db.TChumons.AsNoTracking().Include(x => x.TChumonDetails).ToDictionary(x => x.OrId);
        var outbound = db.TSyukkos.AsNoTracking().Include(x => x.TSyukkoDetails).ToDictionary(x => x.OrId);
        var arrivals = db.TArrivals.AsNoTracking().Include(x => x.TArrivalDetails).ToDictionary(x => x.OrId);
        var shipments = db.TShipments.AsNoTracking().Include(x => x.TShipmentDetails).ToDictionary(x => x.OrId);
        var sales = db.TSales.AsNoTracking().Include(x => x.TSaleDetails).ToDictionary(x => x.OrId);
        var history = db.OrderOperationHistories.AsNoTracking().ToLookup(x => x.OrderId);
        Require(orders.Count == 40 && chumon.Count == 31 && outbound.Count == 25 && arrivals.Count == 19 && shipments.Count == 14
            && sales.Count == 9 && history.Sum(group => group.Count()) == 72, "Sales workflow contains unexpected rows or missing stages.");

        foreach (var plan in plans)
        {
            var order = orderByNote[plan.Note];
            var customer = keys.Customers[plan.Customer.Number];
            var office = keys.Offices[plan.Customer.Office];
            var stage = plan.Stage;
            var hasOrder = stage >= SalesStage.OrderCreated;
            var hasOutbound = stage >= SalesStage.OutboundCreated;
            var hasArrival = stage >= SalesStage.ArrivalCreated;
            var hasShipment = stage >= SalesStage.ShipmentCreated;
            var hasSale = stage >= SalesStage.SaleRecorded;
            var quantities = plan.Lines.Select(line => (keys.Products[line.Product].PrId, line.Quantity)).OrderBy(line => line.PrId).ToArray();
            var amounts = plan.Lines.ToDictionary(line => keys.Products[line.Product].PrId, line =>
            {
                Require(OrderUpdateRules.TrySubtotal(line.Quantity, keys.Products[line.Product].Price, out var amount), "Demo line exceeds the production yen range.");
                return amount;
            });
            Require(order.ClId == customer && order.SoId == office && order.EmId == DemoData.Sales(plan.Customer.Office)
                && order.OrDate == plan.Date && order.OrDeliveryDate == plan.Delivery && order.OrFlag == (plan.Hidden ? 2 : 0)
                && order.OrStateFlag == (hasOrder ? 1 : 0) && Same(order.TOrderDetails.Select(x => (x.PrId, x.OrQuantity)))
                && order.TOrderDetails.All(line => line.OrTotalPrice == amounts[line.PrId]),
                $"Order {plan.Number} has an unexpected customer, status, date, product, or recorded price.");

            var display = new OrderDetailViewModel(new OrderDetailSnapshot
            {
                OrderId = order.OrId, OrderDate = order.OrDate, OfficeId = office, CustomerId = customer, EmployeeId = order.EmId,
                Lines = order.TOrderDetails.Select(line => new OrderDetailLine
                {
                    DetailId = line.OrDetailId, ProductId = line.PrId, Quantity = line.OrQuantity,
                    Subtotal = line.OrTotalPrice, UnitPrice = line.OrTotalPrice / line.OrQuantity
                }).ToList()
            }, () => { }, () => { }, () => { }, () => { }, readOnly: true);
            var expectedSubtotal = amounts.Values.Sum();
            var expectedTax = decimal.Round(expectedSubtotal / 10, 0, MidpointRounding.AwayFromZero);
            Require(display.Subtotal == expectedSubtotal && display.Tax == expectedTax && display.Total == expectedSubtotal + expectedTax,
                $"The WPF yen totals for order {plan.Number} differ from the expected subtotal and tax.");

            Require(chumon.ContainsKey(order.OrId) == hasOrder && outbound.ContainsKey(order.OrId) == hasOutbound
                && arrivals.ContainsKey(order.OrId) == hasArrival && shipments.ContainsKey(order.OrId) == hasShipment
                && sales.ContainsKey(order.OrId) == hasSale, $"Order {plan.Number} is missing a required successor or has one too many.");
            if (hasOrder)
            {
                var row = chumon[order.OrId];
                Require(row.ClId == customer && row.SoId == office && row.ChFlag == 0 && row.ChStateFlag == (hasOutbound ? 1 : 0)
                    && row.ChDate == DemoData.BusinessDays(plan.Date, 1) && Same(row.TChumonDetails.Select(x => (x.PrId, x.ChQuantity))),
                    $"注文 for order {plan.Number} is inconsistent.");
            }
            if (hasOutbound)
            {
                var row = outbound[order.OrId];
                Require(row.ClId == customer && row.SoId == office && row.SyFlag == 0 && row.SyStateFlag == (hasArrival ? 1 : 0)
                    && row.SyDate == (hasArrival ? DemoData.BusinessDays(plan.Date, 2) : null)
                    && Same(row.TSyukkoDetails.Select(x => (x.PrId, x.SyQuantity))), $"出庫 for order {plan.Number} is inconsistent.");
            }
            if (hasArrival)
            {
                var row = arrivals[order.OrId];
                Require(row.ClId == customer && row.SoId == office && row.ArFlag == 0 && row.ArStateFlag == (hasShipment ? 1 : 0)
                    && row.ArDate == (hasShipment ? DemoData.BusinessDays(plan.Date, 3) : null)
                    && Same(row.TArrivalDetails.Select(x => (x.PrId ?? 0, x.ArQuantity ?? 0))), $"入荷 for order {plan.Number} is inconsistent.");
            }
            if (hasShipment)
            {
                var row = shipments[order.OrId];
                Require(row.ClId == customer && row.SoId == office && row.ShFlag == 0 && row.ShStateFlag == (hasSale ? 1 : 0)
                    && row.ShFinishDate == (hasSale ? DemoData.BusinessDays(plan.Date, 4) : null)
                    && Same(row.TShipmentDetails.Select(x => (x.PrId, x.ShQuantity))), $"出荷 for order {plan.Number} is inconsistent.");
            }
            if (hasSale)
            {
                var row = sales[order.OrId];
                Require(row.ClId == customer && row.SoId == office && row.SaFlag == 0 && row.SaDate == DemoData.BusinessDays(plan.Date, 4)
                    && Same(row.TSaleDetails.Select(x => (x.PrId, x.SaQuantity))) && row.TSaleDetails.All(line => line.SaPrTotalPrice == amounts[line.PrId]),
                    $"売上 for order {plan.Number} differs from the seeded unit price or recorded quantities.");
            }

            var events = history[order.OrId].OrderBy(x => x.RecordedAt).ToArray();
            var desired = new List<string> { OrderHistoryOperations.Created };
            if (hasOrder) desired.Add(OrderHistoryOperations.Confirmed);
            if (plan.Hidden) desired.Add(OrderHistoryOperations.Hidden);
            Require(events.Select(x => x.Operation).SequenceEqual(desired) && events.All(x => x.OfficeId == office && x.CustomerId == customer
                && x.ActorEmployeeId.HasValue && keys.Employees.ContainsKey(x.ActorEmployeeId.Value)
                && x.ActorName.EndsWith(DemoData.AuditLabel, StringComparison.Ordinal) && x.Details.Contains("【デモデータ】")),
                $"Audit rows for order {plan.Number} have incorrect labels, actors, or chronology.");

            bool Same(IEnumerable<(int Product, int Quantity)> lines) => lines.OrderBy(line => line.Product).SequenceEqual(quantities);
        }
        Require(db.EmployeeAssignmentHistories.AsNoTracking().OrderBy(x => x.EmployeeId).AsEnumerable().Select(x =>
                (x.EmployeeId, x.BeforeOfficeId, x.AfterOfficeId, x.BeforePositionId, x.AfterPositionId, x.ActorName.EndsWith(DemoData.AuditLabel, StringComparison.Ordinal)))
            .SequenceEqual(new[] { (9104, (int?)keys.Offices[1], keys.Offices[2], (int?)keys.Roles["営業"], keys.Roles["営業"], true),
                (9110, (int?)keys.Offices[3], keys.Offices[3], (int?)keys.Roles["営業"], keys.Roles["物流"], true) }),
            "Employee demo assignment history is incomplete or unmarked.");
        report.Passed("40 受注, 31 注文, 25 出庫, 19 入荷, 14 出荷 and 9 売上 match their unique parents, branches, quantities, statuses and business dates.");
        report.Passed("Stored order and sale amounts match the fixture unit prices. All 40 受注 detail view models match the expected net subtotal, 10% yen-rounded tax, and total.");
        report.Passed("72 order events and two employee assignment events have recorded relationships and explicit demo labels.");
    }

    private static void VerifyPurchases(SalesManagementContext db, DemoKeys keys, DateTime anchor, DemoReport report)
    {
        var plans = DemoData.Purchases(anchor);
        var purchases = db.THattyus.AsNoTracking().Include(x => x.THattyuDetails).ToList();
        var receipts = db.TWarehousings.AsNoTracking().Include(x => x.TWarehousingDetails).ToLookup(x => x.HaId);
        Require(purchases.Count == plans.Length && receipts.Sum(group => group.Count()) == plans.Sum(x => x.Receipts.Count),
            "Purchase or receipt count does not match the demo plan.");
        foreach (var plan in plans)
        {
            var actor = DemoData.Logistics(plan.Office);
            var maker = keys.Makers[plan.Supplier];
            var purchase = purchases.Single(x => x.MaId == maker && x.HaDate == plan.Date && x.EmId == actor);
            Require(purchase.RegisteredOfficeId.HasValue,
                $"Purchase {purchase.HaId} has no registered office. Run seed to initialize missing demo offices.");
            Require(purchase.RegisteredOfficeId == keys.Offices[plan.Office],
                $"Purchase {purchase.HaId} already has a different office. Review the demo data before running seed again.");
            Require(purchase.HaFlag == 0 && purchase.THattyuDetails.All(line => keys.Products.Single(p => p.PrId == line.PrId).MaId == maker)
                && purchase.THattyuDetails.Select(line => (line.PrId, line.HaQuantity)).OrderBy(line => line.PrId)
                    .SequenceEqual(plan.Lines.Select(line => (keys.Products[line.Product].PrId, line.Quantity)).OrderBy(line => line.PrId)),
                $"Purchase {plan.Number} has inconsistent maker or line items.");
            var actual = receipts[purchase.HaId].OrderBy(x => x.WaDate).ToArray();
            Require(actual.Length == plan.Receipts.Count, $"Purchase {plan.Number} has an unexpected receipt count.");
            foreach (var (receipt, planned) in actual.Zip(plan.Receipts))
            {
                Require(receipt.WaDate == planned.Date && receipt.WaDate > purchase.HaDate && receipt.EmId == actor && receipt.WaFlag == 0
                    && receipt.WaShelfFlag == (planned.Confirmed ? 1 : 0)
                    && receipt.TWarehousingDetails.Select(line => (line.PrId, line.WaQuantity)).OrderBy(line => line.PrId)
                        .SequenceEqual(planned.Lines.Select(line => (keys.Products[line.Product].PrId, line.Quantity)).OrderBy(line => line.PrId)),
                    $"Receipt {receipt.WaId} has inconsistent product, quantity, date, or state.");
            }
        }
        report.Passed($"15 発注 and {receipts.Sum(group => group.Count())} 入庫 have matching maker, product and purchase IDs. Unposted receipts remain pending.");
    }

    private static void VerifyStock(SalesManagementContext db, DemoKeys keys, DemoReport report)
    {
        var stock = db.TStocks.AsNoTracking().ToDictionary(x => x.PrId);
        var incoming = db.TWarehousingDetails.AsNoTracking().Where(x => x.Wa.WaShelfFlag == 1 && x.Wa.WaFlag == 0)
            .GroupBy(x => x.PrId).ToDictionary(group => group.Key, group => group.Sum(x => x.WaQuantity));
        var outgoing = db.TChumonDetails.AsNoTracking().Where(x => x.Ch.ChStateFlag == 1 && x.Ch.ChFlag == 0)
            .GroupBy(x => x.PrId).ToDictionary(group => group.Key, group => group.Sum(x => x.ChQuantity));
        Require(stock.Count == DemoData.Products.Length, "Every product must have one stock row.");
        for (var i = 0; i < keys.Products.Length; i++)
        {
            var plan = DemoData.Products[i];
            var product = keys.Products[i];
            var row = stock[product.PrId];
            var received = incoming.GetValueOrDefault(product.PrId);
            var allocated = outgoing.GetValueOrDefault(product.PrId);
            Require(row.StFlag == 0 && row.StQuantity >= 0 && row.StQuantity == plan.Opening + received - allocated,
                $"Stock does not reconcile for {product.PrModelNumber}.");
            report.Stock.Add(new(product.PrModelNumber, product.PrName, plan.Opening, received, allocated, row.StQuantity, product.PrSafetyStock));
        }
        report.Passed("All 50 independent product balances reconcile to the documented opening quantities, confirmed receipts, and confirmed 注文. No duplicate or negative balances.");
    }

    private static void VerifyQueries(SalesManagementContext db, DemoKeys keys, DemoReport report)
    {
        AppSession.Employee = keys.Employees[DemoData.Admin];
        Require(CustomerService.Load().Customers.Count == 30 && ProductService.Load().Products.Count == 50
            && EmployeeService.Load().Employees.Count == 15 && WarehousingService.Load().Records.Count > 0
            && OutboundService.Load().Records.Count == 25 && ArrivalService.Load().Arrivals.Count == 19
            && ShipmentService.Load().Shipments.Count == 14 && SalesService.Load().Sales.Count == 9,
            "A management service returned an unexpected row count.");
        var purchases = PurchaseOrderService.Load().Orders;
        Require(purchases.Count == 15 && purchases.Count(x => x.ReceiptStatus == PurchaseReceiptStatus.Waiting) == 5
            && purchases.Count(x => x.ReceiptStatus == PurchaseReceiptStatus.Partial) == 4 && purchases.Count(x => x.IsFullyReceived) == 6,
            "The purchase UI does not calculate the planned waiting, partial and received statuses.");

        var filters = new InventoryFilters("", "", "", "", "", null, null, null, InventorySort.Code, false);
        var inventory = InventoryService.LoadPageAsync(filters, 1, 20, true, CancellationToken.None).GetAwaiter().GetResult();
        var nextInventory = InventoryService.LoadPageAsync(filters, 2, 20, false, CancellationToken.None).GetAwaiter().GetResult();
        Require(inventory.Summary is { Registered: 49, Normal: > 0, Low: > 0, Empty: 2 } && inventory.MatchCount == 49
            && inventory.Rows.Count == 20 && nextInventory.Rows.Count == 20
            && !inventory.Rows.Select(x => x.ProductId).Intersect(nextInventory.Rows.Select(x => x.ProductId)).Any(),
            "Inventory pages or safety-stock statuses do not match the demo catalog.");
        var all = HistoryManagementService.ReadAsync(db, new HistoryManagementFilter()).GetAwaiter().GetResult();
        var next = HistoryManagementService.ReadAsync(db, new HistoryManagementFilter { PageIndex = 1 }).GetAwaiter().GetResult();
        Require(all.Total == 74 && all.Rows.Count == 20 && next.Rows.Count == 20
            && !all.Rows.Select(x => (x.Module, x.EventId)).Intersect(next.Rows.Select(x => (x.Module, x.EventId))).Any()
            && all.Counts[HistoryManagementService.MasterModule] == 2, "Central history counts or pagination do not match the generated audit data.");
        var first = all.Rows.First(x => x.OrderId.HasValue);
        var timeline = HistoryManagementService.ReadTimelineAsync(db, first).GetAwaiter().GetResult();
        Require(timeline.Count > 0 && timeline.All(x => x.OrderId == first.OrderId) && timeline.Zip(timeline.Skip(1)).All(x => x.First.RecordedAt <= x.Second.RecordedAt),
            "The history service did not return the selected record in chronological order.");
        var completed = BusinessHistoryService.ReadAsync("売上管理").GetAwaiter().GetResult();
        Require(completed.Count == 9 && completed.Select(x => x.Id).Distinct().Count() == 9, "Business history did not return the completed sales.");
        AppSession.Employee = keys.Employees[DemoData.Sales(0)];
        var scoped = HistoryManagementService.ReadAsync(db, new HistoryManagementFilter()).GetAwaiter().GetResult();
        Require(scoped.Total > 0 && scoped.Total < all.Total && scoped.Rows.All(x => x.OrderEntry?.OfficeId == keys.Offices[0]),
            "Sales history is not limited to the expected office.");
        report.Passed("Management queries loaded the expected master and transaction rows. Purchase status counts are 5 waiting, 4 partial, and 6 received.");
        report.Passed($"Inventory shows {inventory.Summary!.Normal} normal, {inventory.Summary.Low} low and {inventory.Summary.Empty} zero balances. Inventory and audit pagination, timeline, business history, and sales office restrictions passed.");
    }
}
