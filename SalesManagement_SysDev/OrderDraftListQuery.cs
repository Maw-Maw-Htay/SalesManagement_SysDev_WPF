using System.ComponentModel;
using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

internal sealed record OrderDraftPage(IReadOnlyList<OrderListRow> Rows, IReadOnlyList<OrderListRow> CheckedOffPage, int TotalCount, int Number);

// This projection reads the existing form payload. It is not a database table or view.
internal sealed class OrderDraftListEntry
{
    public Guid DraftId { get; init; }
    public byte[] Version { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; }
    public int EmployeeId { get; init; }
    public string EmployeeName { get; init; } = "";
    public int ScopeOfficeId { get; init; }
    public int? OfficeId { get; init; }
    public string OfficeName { get; init; } = "";
    public int? CustomerId { get; init; }
    public string CustomerName { get; init; } = "";
    public DateTime? OrderDate { get; init; }
    public DateTime? DeliveryDate { get; init; }
    public string Remarks { get; init; } = "";
    public int LineCount { get; init; }
    public decimal Subtotal { get; init; }
    public string PendingProduct { get; init; } = "";

    public OrderListRow ToListRow() => new()
    {
        DraftToken = new OrderDraftToken(DraftId, Version),
        DraftSummary = $"最終保存 {UpdatedAt.ToOffset(TimeSpan.FromHours(9)):yyyy/MM/dd HH:mm}"
            + (string.IsNullOrWhiteSpace(Remarks) ? "" : $"\n備考 {Remarks}"),
        OrderDate = OrderDate, DeliveryDate = DeliveryDate,
        CustomerName = string.IsNullOrWhiteSpace(CustomerName) ? "未入力" : CustomerName,
        OfficeId = OfficeId ?? 0, OfficeName = string.IsNullOrWhiteSpace(OfficeName) ? "未入力" : OfficeName,
        EmployeeName = EmployeeName, LineCount = LineCount, Subtotal = Subtotal
    };
}

internal sealed class OrderDraftSearchLine
{
    public Guid DraftId { get; init; }
    public string? ProductName { get; init; }
    public string? ProductCode { get; init; }
}

internal static class OrderDraftListQuery
{
    internal const string ProjectionSql = """
        SELECT draft.DraftId, draft.Version, draft.UpdatedAt, draft.EmployeeId, draft.ScopeOfficeId,
               employee.EmName AS EmployeeName, form.OfficeId, form.CustomerId,
               COALESCE(office.SoName, form.OfficeText, N'') AS OfficeName,
               COALESCE(customer.ClName, form.CustomerText, N'') AS CustomerName,
               TRY_CONVERT(date, form.OrderDate, 126) AS OrderDate,
               TRY_CONVERT(date, form.DeliveryDate, 126) AS DeliveryDate,
               COALESCE(form.Remarks, N'') AS Remarks, items.LineCount,
               COALESCE(items.Subtotal, CONVERT(decimal(19, 4), 0)) AS Subtotal,
               COALESCE(form.PendingProduct, N'') AS PendingProduct
        FROM dbo.T_OrderDraft AS draft
        INNER JOIN dbo.M_Employee AS employee ON employee.EmID = draft.EmployeeId
        CROSS APPLY OPENJSON(draft.FormJson) WITH (
            OfficeId int '$.Header.Office.Id', OfficeText nvarchar(4000) '$.Header.Office.Text',
            CustomerId int '$.Header.Customer.Id', CustomerText nvarchar(4000) '$.Header.Customer.Text',
            OrderDate nvarchar(40) '$.Header.OrderDate.Value', DeliveryDate nvarchar(40) '$.Header.DeliveryDate.Value',
            Remarks nvarchar(max) '$.Header.Remarks', PendingProduct nvarchar(4000) '$.Header.PendingProduct.Text',
            Lines nvarchar(max) '$.Lines' AS JSON
        ) AS form
        LEFT JOIN dbo.M_SalesOffice AS office ON office.SoID = form.OfficeId
        LEFT JOIN dbo.M_Client AS customer ON customer.ClID = form.CustomerId AND customer.SoID = form.OfficeId
        CROSS APPLY (
            SELECT COUNT(*) AS LineCount, SUM(line.UnitPrice * line.Quantity) AS Subtotal
            FROM OPENJSON(form.Lines) WITH (
                Quantity int '$.Quantity', UnitPrice decimal(19, 4) '$.UnitPrice'
            ) AS line
        ) AS items
        WHERE draft.FormJson IS NOT NULL AND draft.RegisteredOrderId IS NULL
        """;

    internal const string SearchLinesSql = """
        SELECT draft.DraftId, line.ProductName, line.ProductCode
        FROM dbo.T_OrderDraft AS draft
        CROSS APPLY OPENJSON(draft.FormJson, '$.Lines') WITH (
            ProductName nvarchar(4000) '$.ProductName', ProductCode nvarchar(4000) '$.ProductCode'
        ) AS line
        WHERE draft.FormJson IS NOT NULL AND draft.RegisteredOrderId IS NULL
        """;

    internal static IQueryable<OrderDraftListEntry> ApplyFilters(SalesManagementContext db,
        IQueryable<OrderDraftListEntry> query, OrderSearchCriteria criteria)
    {
        if (!string.IsNullOrWhiteSpace(criteria.OrderNumber)) return query.Where(_ => false);
        if (criteria.OfficeId >= 0) query = query.Where(row => row.OfficeId == criteria.OfficeId);
        if (criteria.EmployeeId is int employeeId) query = query.Where(row => row.EmployeeId == employeeId);
        else if (!string.IsNullOrWhiteSpace(criteria.EmployeeName))
        {
            var name = criteria.EmployeeName.Trim().ToUpperInvariant();
            query = query.Where(row => row.EmployeeName.ToUpper().Contains(name));
        }
        if (criteria.CustomerId is int customerId) query = query.Where(row => row.CustomerId == customerId);
        else if (!string.IsNullOrWhiteSpace(criteria.CustomerName))
        {
            var name = criteria.CustomerName.Trim().ToUpperInvariant();
            query = query.Where(row => row.CustomerName.ToUpper().Contains(name));
        }
        if (criteria.OrderDateFrom is DateTime orderFrom) query = query.Where(row => row.OrderDate >= orderFrom.Date);
        if (criteria.OrderDateTo is DateTime orderTo) query = query.Where(row => row.OrderDate <= orderTo.Date);
        if (criteria.DeliveryDateUnsetOnly) query = query.Where(row => row.DeliveryDate == null);
        else
        {
            if (criteria.DeliveryDateFrom is DateTime deliveryFrom) query = query.Where(row => row.DeliveryDate >= deliveryFrom.Date);
            if (criteria.DeliveryDateTo is DateTime deliveryTo) query = query.Where(row => row.DeliveryDate <= deliveryTo.Date);
        }

        var keyword = criteria.QuickSearch.Trim().ToUpperInvariant();
        if (keyword.Length == 0) return query;
        var hasCustomerId = int.TryParse(keyword, NumberStyles.Integer, CultureInfo.InvariantCulture, out var matchingId) && matchingId > 0;
        return query.Where(row => (hasCustomerId && row.CustomerId == matchingId)
            || row.CustomerName.ToUpper().Contains(keyword) || row.OfficeName.ToUpper().Contains(keyword)
            || row.EmployeeName.ToUpper().Contains(keyword) || row.Remarks.ToUpper().Contains(keyword)
            || row.PendingProduct.ToUpper().Contains(keyword)
            || db.Set<OrderDraftSearchLine>().Any(line => line.DraftId == row.DraftId
                && ((line.ProductName ?? "").ToUpper().Contains(keyword) || (line.ProductCode ?? "").ToUpper().Contains(keyword))));
    }

    internal static IOrderedQueryable<OrderDraftListEntry> Sort(IQueryable<OrderDraftListEntry> query, OrderListRequest request)
    {
        IOrderedQueryable<OrderDraftListEntry> By<T>(Expression<Func<OrderDraftListEntry, T>> key) =>
            request.Direction == ListSortDirection.Descending ? query.OrderByDescending(key) : query.OrderBy(key);

        var sorted = request.Sort switch
        {
            OrderListSort.CustomerName => By(row => EF.Functions.Collate(row.CustomerName, OrderListQuery.JapaneseCollation)),
            OrderListSort.LineCount => By(row => row.LineCount),
            OrderListSort.EmployeeName => By(row => EF.Functions.Collate(row.OfficeName, OrderListQuery.JapaneseCollation)),
            OrderListSort.Total => By(row => row.Subtotal),
            // All drafts have the same owner and status, and no official order number.
            OrderListSort.OrderId or OrderListSort.Status => query.OrderByDescending(row => row.UpdatedAt),
            _ => By(row => row.OrderDate)
        };
        return sorted.ThenByDescending(row => row.UpdatedAt).ThenBy(row => row.DraftId);
    }
}
