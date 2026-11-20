using System.Globalization;
using System.Linq.Expressions;

namespace SalesManagement_SysDev;

public sealed record OrderSearchCriteria
{
    public string OrderNumber { get; init; } = "";
    public int? CustomerId { get; init; }
    public string CustomerName { get; init; } = "";
    public int? EmployeeId { get; init; }
    public string EmployeeName { get; init; } = "";
    public int OfficeId { get; init; } = -1;
    public DateTime? OrderDateFrom { get; init; }
    public DateTime? OrderDateTo { get; init; }
    public DateTime? DeliveryDateFrom { get; init; }
    public DateTime? DeliveryDateTo { get; init; }
    public bool DeliveryDateUnsetOnly { get; init; }
    public string QuickSearch { get; init; } = "";

    public IQueryable<TOrder> ApplyTo(IQueryable<TOrder> query)
    {
        if (OrderDateFrom.HasValue) query = query.Where(order => order.OrDate >= OrderDateFrom.Value.Date);
        if (OrderDateTo.HasValue) query = query.Where(order => order.OrDate < OrderDateTo.Value.Date.AddDays(1));
        if (DeliveryDateUnsetOnly) query = query.Where(order => order.OrDeliveryDate == null);
        else
        {
            if (DeliveryDateFrom.HasValue) query = query.Where(order => order.OrDeliveryDate >= DeliveryDateFrom.Value.Date);
            if (DeliveryDateTo.HasValue) query = query.Where(order => order.OrDeliveryDate < DeliveryDateTo.Value.Date.AddDays(1));
        }
        if (!string.IsNullOrWhiteSpace(OrderNumber)) query = OrderNumberSearch.ApplyToOrders(query, OrderNumber);
        if (CustomerId.HasValue)
        {
            var id = CustomerId.Value;
            query = query.Where(order => order.ClId == id);
        }
        else if (!string.IsNullOrWhiteSpace(CustomerName))
        {
            var name = CustomerName.Trim().ToUpperInvariant();
            query = query.Where(order => order.Cl.ClName.ToUpper().Contains(name));
        }
        if (EmployeeId.HasValue)
        {
            var id = EmployeeId.Value;
            query = query.Where(order => order.EmId == id);
        }
        else if (!string.IsNullOrWhiteSpace(EmployeeName))
        {
            var name = EmployeeName.Trim().ToUpperInvariant();
            query = query.Where(order => order.Em.EmName.ToUpper().Contains(name));
        }
        if (OfficeId >= 0) query = query.Where(order => order.SoId == OfficeId);

        var keyword = QuickSearch.Trim();
        if (keyword.Length == 0) return query;
        var upperKeyword = keyword.ToUpperInvariant();
        var matches = query.Where(order =>
            order.Cl.ClName.ToUpper().Contains(upperKeyword)
            || order.So.SoName.ToUpper().Contains(upperKeyword)
            || order.Em.EmName.ToUpper().Contains(upperKeyword)
            || (order.ClCharge ?? "").ToUpper().Contains(upperKeyword)
            || (order.OrHidden ?? "").ToUpper().Contains(upperKeyword)
            || order.TOrderDetails.Any(detail => (detail.OrQuantity != 0 || detail.OrTotalPrice != 0m)
                && (detail.Pr.PrName.ToUpper().Contains(upperKeyword)
                    || (detail.Pr.PrJcode ?? "").ToUpper().Contains(upperKeyword))));
        if (int.TryParse(keyword, NumberStyles.Integer, CultureInfo.InvariantCulture, out var customerId) && customerId > 0)
            matches = matches.Union(query.Where(order => order.ClId == customerId));
        if (OrderNumberSearch.TryGetDigits(keyword, out _))
            matches = matches.Union(OrderNumberSearch.ApplyToOrders(query, keyword));
        return matches;
    }
}

public static class OrderNumberSearch
{
    public static bool TryGetDigits(string? input, out string digits)
    {
        digits = "";
        var value = input?.Trim() ?? "";
        if (value.StartsWith("ORD-", StringComparison.OrdinalIgnoreCase)) value = value[4..];
        if (value.Length == 0 || value.Any(character => character is < '0' or > '9')) return false;
        digits = value;
        return true;
    }

    public static IQueryable<TOrder> ApplyToOrders(IQueryable<TOrder> query, string input) =>
        Apply(query, input, nameof(TOrder.OrId));

    public static IQueryable<OrderOperationHistory> ApplyToHistory(IQueryable<OrderOperationHistory> query, string input) =>
        Apply(query, input, nameof(OrderOperationHistory.OrderId));

    private static IQueryable<TEntity> Apply<TEntity>(IQueryable<TEntity> query, string input, string idProperty)
    {
        if (!TryGetDigits(input, out var digits)) return query.Where(_ => false);
        var parameter = Expression.Parameter(typeof(TEntity), "record");
        var id = Expression.Property(parameter, idProperty);
        Expression body;
        if (digits.Length > 6)
        {
            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var exactId))
                return query.Where(_ => false);
            body = Expression.Equal(id, Expression.Constant(exactId));
        }
        else
        {
            var pattern = int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
            var modulus = PowerOfTen(digits.Length);
            body = Expression.Constant(false);
            for (var position = 0; position <= 6 - digits.Length; position++)
            {
                var divisor = PowerOfTen(6 - digits.Length - position);
                var quotient = Expression.Divide(id, Expression.Constant(divisor));
                var remainder = Expression.Modulo(quotient, Expression.Constant(modulus));
                body = Expression.OrElse(body, Expression.Equal(remainder, Expression.Constant(pattern)));
            }
        }
        return query.Where(Expression.Lambda<Func<TEntity, bool>>(body, parameter));
    }

    private static int PowerOfTen(int exponent)
    {
        var result = 1;
        for (var index = 0; index < exponent; index++) result *= 10;
        return result;
    }
}
