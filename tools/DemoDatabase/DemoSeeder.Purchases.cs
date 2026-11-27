using System.Data;
using Microsoft.EntityFrameworkCore;
using SalesManagement_SysDev;
using static SalesManagement.Demo.Program;

namespace SalesManagement.Demo;

internal sealed partial class DemoSeeder
{
    internal (int Offices, int ReceiptDates) PrepareExistingPurchases()
    {
        using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
        var purchases = db.THattyus.Include(x => x.THattyuDetails).ToList();
        if (purchases.Count == 0) return (0, 0);

        var demoKeys = DemoKeys.Read(db);
        Require(EmployeePasswordHasher.Verify(credentials[DemoData.Admin], demoKeys.Employees[DemoData.Admin].EmPasswordHash),
            "The encrypted demo administrator credential does not match the database.");
        var receiptsByPurchase = db.TWarehousings.Include(x => x.TWarehousingDetails).ToLookup(x => x.HaId);
        var matchedPurchases = new HashSet<int>();
        var offices = 0;
        var receiptDates = 0;
        foreach (var plan in DemoData.Purchases(anchor))
        {
            var maker = demoKeys.Makers[plan.Supplier];
            var employee = DemoData.Logistics(plan.Office);
            var purchase = purchases.SingleOrDefault(x => x.MaId == maker && x.HaDate == plan.Date && x.EmId == employee);
            if (purchase == null) continue;
            matchedPurchases.Add(purchase.HaId);

            var expected = plan.Lines.Select(x => (demoKeys.Products[x.Product].PrId, x.Quantity)).OrderBy(x => x.PrId);
            var actual = purchase.THattyuDetails.Select(x => (x.PrId, x.HaQuantity)).OrderBy(x => x.PrId);
            Require(purchase.HaFlag == 0 && actual.SequenceEqual(expected),
                $"Purchase {purchase.HaId} does not match the demo plan. Seeding was stopped.");
            var office = demoKeys.Offices[plan.Office];
            Require(purchase.RegisteredOfficeId == null || purchase.RegisteredOfficeId == office,
                $"Purchase {purchase.HaId} already has a different office. Review the demo data before running seed again.");
            if (purchase.RegisteredOfficeId == null)
            {
                purchase.RegisteredOfficeId = office;
                offices++;
            }

            var plannedReceipts = plan.Receipts.Select(x => x with
            {
                Lines = x.Lines.Select(line => (demoKeys.Products[line.Product].PrId, line.Quantity)).ToArray()
            }).ToArray();
            foreach (var (receipt, date) in DemoReceiptMatcher.MatchLegacyReceiptDates(purchase.HaId, employee, receiptsByPurchase[purchase.HaId], plannedReceipts))
            {
                receipt.WaDate = date;
                receiptDates++;
            }
        }

        Require(matchedPurchases.Count == purchases.Count,
            "Existing purchases outside the demo plan were found. Seeding was stopped.");
        db.SaveChanges();
        transaction.Commit();
        db.ChangeTracker.Clear();
        return (offices, receiptDates);
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
                var result = WarehousingService.ConfirmDemoShelf(actor, receipt.WaId, lines, planned.Date);
                Require(result.Succeeded, $"Receipt {receipt.WaId} failed. {result.Message}");
            }
        }
    }
}
