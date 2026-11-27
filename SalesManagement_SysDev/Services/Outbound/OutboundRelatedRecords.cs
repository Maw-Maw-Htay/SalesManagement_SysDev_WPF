using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public enum OutboundArrivalStatus { None, Pending, Completed, Review }

public static partial class OutboundService
{
    private static OutboundLinks LoadLinks(SalesManagementContext db, IQueryable<TSyukko> visible, bool administrator)
    {
        // Include sibling outbounds for matching, but keep the result attached only to authorized outbounds.
        var source = db.TSyukkos.AsNoTracking().Where(x => visible.Any(v =>
            v.OrId == x.OrId && v.ClId == x.ClId && v.SoId == x.SoId));
        var lines = source.SelectMany(x => x.TSyukkoDetails.Select(d => new
        {
            Key = new OutboundKey(x.OrId, x.ClId, x.SoId),
            x.SyId, x.SyStateFlag, d.PrId, d.SyQuantity, d.Pr.PrJcode, d.Pr.PrName
        })).ToList().GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.GroupBy(x => x.SyId)
            .Select(m => new OutboundMatch(m.Key, m.First().SyStateFlag == ConfirmedState,
                m.Select(x => new WarehouseMovementLineInput(x.PrId, x.SyQuantity)).ToArray(),
                string.Join(" ", m.Select(x => $"P-{x.PrId:0000} {x.PrJcode} {x.PrName}")))).ToArray());

        var orderLines = db.TChumons.AsNoTracking()
            .Where(x => x.ChStateFlag == 1 && (administrator || x.ChFlag == ActiveFlag) &&
                visible.Any(v => v.OrId == x.OrId && v.ClId == x.ClId && v.SoId == x.SoId))
            .SelectMany(x => x.TChumonDetails.Select(d => new
            {
                Key = new OutboundKey(x.OrId, x.ClId, x.SoId), x.ChId, d.PrId, d.ChQuantity
            })).ToList();
        var orders = orderLines.GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.GroupBy(x => x.ChId)
            .Select(m => new OrderMatch(m.Key, m.Select(x => new WarehouseMovementLineInput(x.PrId, x.ChQuantity)).ToArray())).ToArray());

        var arrivalHeaders = db.TArrivals.AsNoTracking().Where(x => visible.Any(v => v.SyStateFlag == ConfirmedState
            && v.OrId == x.OrId && v.ClId == x.ClId && v.SoId == x.SoId))
            .Select(x => new ArrivalRecord(x.ArId, x.OrId, x.ClId, x.Cl.ClName, x.SoId, x.So.SoName, x.EmId,
                x.Em == null ? "未記録" : x.Em.EmName, x.ArDate, x.ArStateFlag, x.ArFlag, x.ArHidden,
                x.TArrivalDetails.Count(), x.TArrivalDetails.Sum(d => d.ArQuantity) ?? 0, "")).ToList();
        // Use every candidate for ambiguity checks. Authorization is applied before returning the linked record.
        var arrivalQuery = db.TArrivals.AsNoTracking().Where(x => visible.Any(v => v.SyStateFlag == ConfirmedState
            && v.OrId == x.OrId && v.ClId == x.ClId && v.SoId == x.SoId));
        var arrivalLines = arrivalQuery.SelectMany(x => x.TArrivalDetails.Select(d => new
        {
            x.ArId, d.PrId, d.ArQuantity
        })).ToList().GroupBy(x => x.ArId).ToDictionary(g => g.Key,
            g => g.Select(x => new ArrivalQuantityInput(x.PrId, x.ArQuantity)).ToArray());
        var arrivals = arrivalHeaders.GroupBy(x => new OutboundKey(x.OrderId, x.CustomerId, x.OfficeId))
            .ToDictionary(g => g.Key, g => g.Select(x => new ArrivalMatch(x, arrivalLines.GetValueOrDefault(x.Id) ?? [])).ToArray());
        return new OutboundLinks(lines, orders, arrivals, administrator);
    }

    internal static bool IsUniqueArrivalMatch(IReadOnlyList<WarehouseMovementLineInput> outbound,
        IReadOnlyList<ArrivalQuantityInput> arrival, IEnumerable<IReadOnlyList<WarehouseMovementLineInput>> confirmedOutbounds)
    {
        return MatchesArrival(outbound, arrival) && confirmedOutbounds.Count(candidate => MatchesArrival(candidate, arrival)) == 1;
    }

    private static bool MatchesArrival(IReadOnlyList<WarehouseMovementLineInput> outbound, IReadOnlyList<ArrivalQuantityInput> arrival)
        => ArrivalOperationRules.LinesMatch(arrival, outbound.Select(x => new ArrivalQuantityInput(x.ProductId, x.Quantity)).ToArray());

    private sealed record OutboundKey(int OrderId, int CustomerId, int OfficeId);
    private sealed record OutboundMatch(int Id, bool Confirmed, WarehouseMovementLineInput[] Lines, string Products);
    private sealed record OrderMatch(int Id, WarehouseMovementLineInput[] Lines);
    private sealed record ArrivalMatch(ArrivalRecord Record, ArrivalQuantityInput[] Lines);

    private sealed record OutboundLinks(Dictionary<OutboundKey, OutboundMatch[]> Outbounds,
        Dictionary<OutboundKey, OrderMatch[]> Orders, Dictionary<OutboundKey, ArrivalMatch[]> Arrivals, bool Administrator)
    {
        public WarehouseMovementRecord Enrich(WarehouseMovementRecord record)
        {
            if (record.CustomerId is not int customerId || record.OfficeId is not int officeId) return record;
            var key = new OutboundKey(record.ReferenceId, customerId, officeId);
            if (!Outbounds.TryGetValue(key, out var siblings) || siblings.FirstOrDefault(x => x.Id == record.Id) is not { } source)
                return record;
            var orders = (Orders.GetValueOrDefault(key) ?? []).Where(x => WarehouseMovementRules.LinesMatch(source.Lines, x.Lines)).ToArray();
            var arrivals = source.Confirmed ? Arrivals.GetValueOrDefault(key) ?? [] : [];
            var candidates = arrivals.Where(arrival => MatchesArrival(source.Lines, arrival.Lines)).ToArray();
            var match = candidates.Length == 1 && IsUniqueArrivalMatch(source.Lines, candidates[0].Lines,
                siblings.Where(x => x.Confirmed).Select(x => x.Lines)) ? candidates[0].Record : null;
            var status = match switch
            {
                { ManagementFlag: ArrivalService.ActiveFlag, IsConfirmed: true } => OutboundArrivalStatus.Completed,
                { ManagementFlag: ArrivalService.ActiveFlag, IsUnconfirmed: true } => OutboundArrivalStatus.Pending,
                _ when !arrivals.Any(x => Administrator || x.Record.ManagementFlag == ArrivalService.ActiveFlag) => OutboundArrivalStatus.None,
                _ => OutboundArrivalStatus.Review
            };
            return record with
            {
                RelatedOrderId = orders.Length == 1 ? orders[0].Id : null,
                ProductSearchText = source.Products,
                ArrivalStatus = status,
                RelatedArrival = Administrator ? match : null
            };
        }
    }
}
