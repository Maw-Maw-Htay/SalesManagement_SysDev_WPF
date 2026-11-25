using SalesManagement_SysDev;
using static SalesManagement.Demo.Program;

namespace SalesManagement.Demo;

internal static class DemoReceiptMatcher
{
    internal static IReadOnlyList<(TWarehousing Receipt, DateTime Date)> MatchLegacyReceiptDates(
        int purchaseId, int employee, IEnumerable<TWarehousing> receipts, IReadOnlyList<ReceiptSeed> planned)
    {
        var plansByDate = planned.ToDictionary(x => x.Date);
        var unmatchedDates = new HashSet<DateTime>(plansByDate.Keys);
        var legacy = new List<TWarehousing>();
        var corrections = new List<(TWarehousing, DateTime)>();

        // Reserve exact dates before matching rows whose dates were overwritten by older versions.
        foreach (var receipt in receipts)
        {
            Require(receipt.EmId == employee && receipt.WaFlag == 0 && receipt.WaShelfFlag is 0 or 1,
                $"Receipt {receipt.WaId} has an unexpected employee or state. Seeding was stopped.");
            if (receipt.WaDate is DateTime date && plansByDate.TryGetValue(date, out var plan))
            {
                Require(unmatchedDates.Remove(date) && LinesMatch(receipt, plan)
                    && (receipt.WaShelfFlag == 0 || plan.Confirmed),
                    $"Receipt {receipt.WaId} does not match its planned date and details. Seeding was stopped.");
            }
            else
            {
                legacy.Add(receipt);
            }
        }

        foreach (var receipt in legacy)
        {
            Require(receipt.WaShelfFlag == 1 && receipt.WaDate.HasValue,
                $"Receipt {receipt.WaId} has an unexpected date and is not a dated confirmed receipt. Seeding was stopped.");
            var matches = planned.Where(plan => plan.Confirmed && unmatchedDates.Contains(plan.Date) && LinesMatch(receipt, plan))
                .ToArray();
            Require(matches.Length == 1,
                $"Receipt {receipt.WaId} for purchase {purchaseId} cannot be matched uniquely. Seeding was stopped before updating stock.");
            unmatchedDates.Remove(matches[0].Date);
            corrections.Add((receipt, matches[0].Date));
        }
        return corrections;

        static bool LinesMatch(TWarehousing receipt, ReceiptSeed plan) =>
            receipt.TWarehousingDetails.Select(line => (line.PrId, line.WaQuantity)).OrderBy(line => line.PrId)
                .SequenceEqual(plan.Lines.OrderBy(line => line.Product));
    }
}
