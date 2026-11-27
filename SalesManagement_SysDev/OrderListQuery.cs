using System.ComponentModel;
using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public enum OrderListSort { OrderDate, OrderId, CustomerName, LineCount, EmployeeName, Total, Status }

internal sealed record OrderListRequest(OrderSearchCriteria Criteria, OrderStatusTab Tab, OrderListSort Sort, ListSortDirection Direction);

internal sealed record OrderListCounts(int Pending, int Confirmed, int Hidden)
{
    public int All => Pending + Confirmed;
    public int For(OrderStatusTab tab) => tab switch
    {
        OrderStatusTab.All => All,
        OrderStatusTab.Pending => Pending,
        OrderStatusTab.Confirmed => Confirmed,
        OrderStatusTab.Hidden => Hidden,
        _ => throw new ArgumentOutOfRangeException(nameof(tab))
    };
}

internal sealed record OrderListPage(IReadOnlyList<OrderListRow> Rows, IReadOnlyList<OrderListRow> CheckedOffPage,
    OrderListCounts Counts, int TotalCount, int Number);

public sealed class OrderListRow : ViewModelBase
{
    private bool isChecked;
    public bool IsChecked { get => isChecked; set => Set(ref isChecked, value); }
    public int OrderId { get; init; }
    public string OrderNumber => IsDraft ? "未採番" : $"ORD-{OrderId:000000}";
    public DateTime? OrderDate { get; init; }
    public string OrderDateLabel => OrderDate?.ToString("yyyy-MM-dd") ?? "未入力";
    public DateTime? DeliveryDate { get; init; }
    public string DueDate => DeliveryDate?.ToString("yyyy-MM-dd") ?? "未設定";
    public string DateSummary => $"受注日 {OrderDateLabel}\n納期 {DueDate}";
    public string CustomerName { get; init; } = "";
    public string OfficeName { get; init; } = "";
    public int OfficeId { get; init; }
    public string EmployeeName { get; init; } = "";
    public bool EmployeeInOffice { get; init; }
    public int LineCount { get; init; }
    public decimal Subtotal { get; init; }
    public decimal Total => Subtotal + OrderTax.Calculate(Subtotal);
    public int StateFlag { get; init; }
    public int ManagementFlag { get; init; }
    public bool HasLinkedOrder { get; init; }
    public OrderDraftToken? DraftToken { get; init; }
    public string? DraftSummary { get; init; }
    internal (int? OrderId, Guid? DraftId) SelectionKey => DraftToken is { } draft ? (null, draft.Id) : (OrderId, null);
    public bool IsDraft => DraftToken != null;
    public bool IsRegisteredOrder => !IsDraft;
    public bool IsHidden => ManagementFlag == 2;
    public bool IsConfirmed => StateFlag == 1;
    public string StatusText => IsDraft ? "下書き" : IsHidden ? "非表示" : IsConfirmed ? "確定" : "未確定";
    public bool CanEdit => CanManage && StateFlag == 0 && ManagementFlag == 0 && !HasLinkedOrder;
    public bool CanConfirm => CanEdit && EmployeeInOffice && LineCount > 0;
    public bool CanHide => CanManage && !IsHidden;
    public bool CanRestore => CanManage && AppSession.IsAdmin && IsHidden;
    private bool CanManage => IsRegisteredOrder && AppSession.CanManageOrders && AppSession.Employee is { } employee
        && (AppSession.IsAdmin || employee.SoId == OfficeId);
}

internal static class OrderListQuery
{
    internal const string JapaneseCollation = "Japanese_XJIS_100_CI_AS";

    public static async Task<OrderListPage> ReadPageAsync(SalesManagementContext db, OrderListRequest request,
        int number, int size, IReadOnlyList<int> checkedIds, CancellationToken token)
    {
        var filtered = Filter(db, request.Criteria);
        var counts = await CountAsync(filtered, token);
        var total = counts.For(request.Tab);
        var page = Math.Clamp(number, 1, PageCount(total, size));
        var matchingRows = Project(ApplyStatusTab(filtered, request.Tab));
        var rows = await Sort(matchingRows, request.Sort, request.Direction)
            .Skip((page - 1) * size).Take(size).ToListAsync(token);
        var offPageIds = checkedIds.Except(rows.Select(row => row.OrderId)).ToArray();
        var checkedOffPage = offPageIds.Length == 0 ? []
            : await matchingRows.Where(row => offPageIds.Contains(row.OrderId)).ToListAsync(token);
        return new OrderListPage(rows, checkedOffPage, counts, total, page);
    }

    internal static Task<OrderListCounts> CountAsync(SalesManagementContext db, OrderSearchCriteria criteria, CancellationToken token)
        => CountAsync(Filter(db, criteria), token);

    private static async Task<OrderListCounts> CountAsync(IQueryable<TOrder> filtered, CancellationToken token) =>
        await filtered.GroupBy(_ => 1).Select(group => new OrderListCounts(
            group.Count(order => order.OrFlag != 2 && (order.OrStateFlag ?? 0) == 0),
            group.Count(order => order.OrFlag != 2 && (order.OrStateFlag ?? 0) == 1),
            group.Count(order => order.OrFlag == 2))).SingleOrDefaultAsync(token) ?? new OrderListCounts(0, 0, 0);

    public static async Task<IReadOnlyList<OrderPrintRow>> ReadPrintRowsAsync(SalesManagementContext db, OrderListRequest request,
        CancellationToken token)
    {
        var rows = await Sort(Project(ApplyStatusTab(Filter(db, request.Criteria), request.Tab)), request.Sort, request.Direction)
            .Select(row => new { row.OrderId, row.OrderDate, row.DeliveryDate, row.CustomerName, row.Subtotal, row.StateFlag, row.ManagementFlag })
            .ToListAsync(token);
        return rows.Select(row => new OrderPrintRow
        {
            OrderId = row.OrderId, OrderDate = row.OrderDate!.Value, DueDate = row.DeliveryDate?.ToString("yyyy-MM-dd") ?? "未設定",
            CustomerName = row.CustomerName, Total = row.Subtotal + OrderTax.Calculate(row.Subtotal),
            Status = row.ManagementFlag == 2 ? "非表示" : row.StateFlag == 1 ? "確定" : "未確定"
        }).ToArray();
    }

    internal static IQueryable<OrderExportRow> ExportRows(SalesManagementContext db, OrderListRequest request,
        int? officeScope, IReadOnlyList<int> selectedIds)
    {
        var query = ApplyStatusTab(Filter(db, request.Criteria, officeScope), request.Tab);
        if (selectedIds.Count > 0)
        {
            // A single SQL parameter also supports selections spanning many pages.
            var ids = db.Database.SqlQuery<int>(
                $"SELECT [Value] FROM OPENJSON({JsonSerializer.Serialize(selectedIds)}) WITH ([Value] int '$')");
            query = query.Where(order => ids.Contains(order.OrId));
        }

        return Sort(Project(query), request.Sort, request.Direction)
            .Select(row => new OrderExportRow(row.OrderId, row.OrderDate, row.DeliveryDate, row.CustomerName,
                row.LineCount, row.EmployeeName, row.OfficeName, row.Subtotal,
                row.ManagementFlag == 2 ? "非表示" : row.StateFlag == 1 ? "確定" : "未確定"));
    }

    internal static int PageCount(int total, int size) => total == 0 ? 1 : (total - 1) / size + 1;

    private static IQueryable<TOrder> Filter(SalesManagementContext db, OrderSearchCriteria criteria)
    {
        if (!AppSession.CanManageOrders || AppSession.Employee is not { } employee)
            throw new UnauthorizedAccessException("受注一覧を表示する権限がありません。");
        return Filter(db, criteria, AppSession.IsAdmin ? null : employee.SoId);
    }

    private static IQueryable<TOrder> Filter(SalesManagementContext db, OrderSearchCriteria criteria, int? officeScope)
    {
        var query = db.TOrders.AsNoTracking();
        if (officeScope is int officeId) query = query.Where(order => order.SoId == officeId);
        return criteria.ApplyTo(query);
    }

    private static IQueryable<TOrder> ApplyStatusTab(IQueryable<TOrder> query, OrderStatusTab tab) => tab switch
    {
        OrderStatusTab.All => query.Where(order => order.OrFlag != 2 && ((order.OrStateFlag ?? 0) == 0 || order.OrStateFlag == 1)),
        OrderStatusTab.Pending => query.Where(order => order.OrFlag != 2 && (order.OrStateFlag ?? 0) == 0),
        OrderStatusTab.Confirmed => query.Where(order => order.OrFlag != 2 && (order.OrStateFlag ?? 0) == 1),
        OrderStatusTab.Hidden => query.Where(order => order.OrFlag == 2),
        _ => throw new ArgumentOutOfRangeException(nameof(tab))
    };

    private static IQueryable<OrderListRow> Project(IQueryable<TOrder> query) => query.Select(order => new OrderListRow
    {
        OrderId = order.OrId, OrderDate = order.OrDate, DeliveryDate = order.OrDeliveryDate,
        CustomerName = order.Cl.ClName, OfficeId = order.SoId, OfficeName = order.So.SoName,
        EmployeeName = order.Em.EmName, EmployeeInOffice = order.Em.SoId == order.SoId && order.Em.EmFlag == 0,
        StateFlag = order.OrStateFlag ?? 0, ManagementFlag = order.OrFlag, HasLinkedOrder = order.TChumons.Any(),
        // Cancelled details are retained as zero-quantity, zero-amount rows.
        LineCount = order.TOrderDetails.Count(detail => detail.OrQuantity != 0 || detail.OrTotalPrice != 0m),
        Subtotal = order.TOrderDetails.Sum(detail => detail.OrTotalPrice)
    });

    private static IQueryable<OrderListRow> Sort(IQueryable<OrderListRow> query, OrderListSort sort, ListSortDirection direction)
    {
        IOrderedQueryable<OrderListRow> By<T>(Expression<Func<OrderListRow, T>> key) => direction == ListSortDirection.Descending
            ? query.OrderByDescending(key) : query.OrderBy(key);

        if (sort == OrderListSort.OrderId) return By(row => row.OrderId);
        var sorted = sort switch
        {
            OrderListSort.CustomerName => By(row => EF.Functions.Collate(row.CustomerName, JapaneseCollation)),
            OrderListSort.LineCount => By(row => row.LineCount),
            OrderListSort.EmployeeName => SortEmployeeAndOffice(),
            // Applying the existing rounded tax is monotonic, so sorting by subtotal gives the same order.
            OrderListSort.Total => By(row => row.Subtotal),
            OrderListSort.Status => By(row => row.ManagementFlag == 2 ? 2 : row.StateFlag == 1 ? 1 : 0),
            _ => By(row => row.OrderDate)
        };
        return sorted.ThenByDescending(row => row.OrderId);

        IOrderedQueryable<OrderListRow> SortEmployeeAndOffice()
        {
            var employees = By(row => EF.Functions.Collate(row.EmployeeName, JapaneseCollation));
            return direction == ListSortDirection.Descending
                ? employees.ThenByDescending(row => EF.Functions.Collate(row.OfficeName, JapaneseCollation))
                : employees.ThenBy(row => EF.Functions.Collate(row.OfficeName, JapaneseCollation));
        }
    }
}
