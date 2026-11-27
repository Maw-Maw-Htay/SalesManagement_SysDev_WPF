using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public sealed class HistoryManagementFilter
{
    public string Module { get; init; } = "";
    public string Operation { get; init; } = "";
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public string Actor { get; init; } = "";
    public string Keyword { get; init; } = "";
    public string TargetNumber { get; init; } = "";
    public string TargetName { get; init; } = "";
    public int? EmployeeId { get; init; }
    public int PageIndex { get; init; }
    public int PageSize { get; init; } = 20;
}

public sealed record HistoryEventRow(
    long EventId,
    DateTimeOffset RecordedAt,
    string Module,
    string Operation,
    string TargetNumber,
    string TargetName,
    string Summary,
    string ActorName,
    string Detail,
    int? OrderId,
    int? EmployeeId,
    OrderOperationHistory? OrderEntry,
    EmployeeAssignmentHistory? EmployeeEntry)
{
    public string Timestamp => RecordedAt.ToOffset(OrderHistoryService.JapanOffset)
        .ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
    public System.Windows.Media.Brush OperationForeground => OrderEntry is { } entry
        ? new OrderHistoryDisplayRow(entry).OperationForeground : AppTheme.Accent;
    public System.Windows.Media.Brush OperationBackground => OrderEntry is { } entry
        ? new OrderHistoryDisplayRow(entry).OperationBackground : AppTheme.PaleBlue;
}

public sealed record HistoryManagementPage(IReadOnlyList<HistoryEventRow> Rows, int Total, int PageIndex, int PageSize,
    IReadOnlyDictionary<string, int> Counts);

public sealed record HistoryModule(string Key, string Label, bool HasAuditSource);

/// <summary>Read-only aggregation of operation events that are already recorded by the application.</summary>
public static class HistoryManagementService
{
    public const string OrdersModule = "受注管理";
    public const string EmployeesModule = "社員管理";
    public const string MasterModule = "マスタ";
    private static readonly HistoryModule[] modules = [new("", "すべて", true), new(OrdersModule, "受注", true),
        new("注文管理", "注文", false), new("発注管理", "発注", false), new("入庫管理", "入庫", false),
        new("出庫管理", "出庫", false), new("入荷管理", "入荷", false), new("出荷管理", "出荷", false),
        new("売上管理", "売上", false), new("在庫管理", "在庫", false), new(MasterModule, "マスタ", true)];
    public static readonly IReadOnlyList<string> EmployeeOperations = ["社員登録", "所属・権限変更"];

    public static IReadOnlyList<HistoryModule> GetModules(string role) => role switch
    {
        "管理者" => modules,
        "営業" => modules.Where(x => x.Key is "" or OrdersModule or "注文管理" or "入荷管理" or "出荷管理" or "売上管理" or MasterModule)
            .Select(x => x.Key == MasterModule ? x with { HasAuditSource = false } : x).ToArray(),
        _ => []
    };

    public static async Task<HistoryManagementPage> ReadAsync(SalesManagementContext db,
        HistoryManagementFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(filter);

        var employee = await GetAuthorizedViewerAsync(db, cancellationToken);
        var isAdmin = employee.Po.PoName == "管理者";
        var canReadOrders = isAdmin || employee.Po.PoName == "営業";
        var canReadEmployees = isAdmin;
        if (!canReadOrders && !canReadEmployees)
            throw new UnauthorizedAccessException("履歴を参照する権限がありません。");
        if (filter.Module == EmployeesModule && !canReadEmployees)
            throw new UnauthorizedAccessException("社員の変更履歴を参照する権限がありません。");
        if (filter.Module == OrdersModule && !canReadOrders)
            throw new UnauthorizedAccessException("受注履歴を参照する権限がありません。");
        if (filter.From?.Date > filter.To?.Date) throw new ArgumentException("開始日は終了日以前の日付を指定してください。");

        var orders = canReadOrders && !filter.EmployeeId.HasValue
            ? ApplyOrderFilters(db.OrderOperationHistories.AsNoTracking(), filter, employee.SoId, isAdmin) : null;
        var assignments = canReadEmployees ? ApplyEmployeeFilters(db.EmployeeAssignmentHistories.AsNoTracking(), filter) : null;
        var orderCount = orders == null ? 0 : await orders.CountAsync(cancellationToken);
        var employeeCount = assignments == null ? 0 : await assignments.CountAsync(cancellationToken);
        var counts = new Dictionary<string, int> { [""] = orderCount + employeeCount, [OrdersModule] = orderCount };
        if (canReadEmployees) counts[MasterModule] = counts[EmployeesModule] = employeeCount;
        var total = counts.GetValueOrDefault(filter.Module);
        var pageSize = Math.Clamp(filter.PageSize, 1, 50);
        var index = Math.Clamp(filter.PageIndex, 0, total == 0 ? 0 : (total - 1) / pageSize);
        if (total == 0) return new([], 0, 0, pageSize, counts);

        // Merge only scalar audit keys in SQL, then load the detailed payload for this page.
        var orderKeys = filter.Module is "" or OrdersModule ? orders?.Select(x => new { x.Id, x.RecordedAt, Source = 1 }) : null;
        var employeeKeys = filter.Module is "" or EmployeesModule or MasterModule
            ? assignments?.Select(x => new { x.Id, x.RecordedAt, Source = 0 }) : null;
        var keys = await (orderKeys == null ? employeeKeys! : employeeKeys == null ? orderKeys : orderKeys.Concat(employeeKeys))
            .OrderByDescending(x => x.RecordedAt).ThenBy(x => x.Source).ThenByDescending(x => x.Id)
            .Skip(index * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var orderIds = keys.Where(x => x.Source == 1).Select(x => x.Id).ToArray();
        var employeeIds = keys.Where(x => x.Source == 0).Select(x => x.Id).ToArray();
        var rows = new Dictionary<(int, long), HistoryEventRow>();
        if (orderIds.Length > 0)
            foreach (var entry in await orders!.Where(x => orderIds.Contains(x.Id)).ToListAsync(cancellationToken)) rows[(1, entry.Id)] = ToEventRow(entry);
        if (employeeIds.Length > 0)
            foreach (var entry in await assignments!.Where(x => employeeIds.Contains(x.Id)).ToListAsync(cancellationToken)) rows[(0, entry.Id)] = ToEventRow(entry);
        return new(keys.Where(x => rows.ContainsKey((x.Source, x.Id))).Select(x => rows[(x.Source, x.Id)]).ToArray(), total, index, pageSize, counts);
    }

    public static async Task<IReadOnlyList<HistoryEventRow>> ReadTimelineAsync(SalesManagementContext db, HistoryEventRow target,
        CancellationToken cancellationToken = default)
    {
        var employee = await GetAuthorizedViewerAsync(db, cancellationToken);
        if (target.Module == OrdersModule && target.OrderId is int orderId)
        {
            var query = db.OrderOperationHistories.AsNoTracking().Where(x => x.OrderId == orderId);
            if (employee.Po.PoName != "管理者") query = query.Where(x => x.OfficeId == employee.SoId);
            return (await query.OrderBy(x => x.RecordedAt).ThenBy(x => x.Id).ToListAsync(cancellationToken)).Select(ToEventRow).ToArray();
        }
        if (target.Module == EmployeesModule && target.EmployeeId is int employeeId && employee.Po.PoName == "管理者")
            return (await db.EmployeeAssignmentHistories.AsNoTracking().Where(x => x.EmployeeId == employeeId)
                .OrderBy(x => x.RecordedAt).ThenBy(x => x.Id).ToListAsync(cancellationToken)).Select(ToEventRow).ToArray();
        throw new UnauthorizedAccessException("対象の履歴を参照する権限がありません。");
    }

    internal static async Task<MEmployee> GetAuthorizedViewerAsync(SalesManagementContext db,
        CancellationToken cancellationToken, bool allowLogistics = false)
    {
        var sessionEmployeeId = AppSession.Employee?.EmId;
        if (!sessionEmployeeId.HasValue || sessionEmployeeId.Value <= 0)
            throw new UnauthorizedAccessException("ログイン状態を確認できません。再ログインしてください。");

        var employee = await db.MEmployees.AsNoTracking().Include(x => x.Po)
            .SingleOrDefaultAsync(x => x.EmId == sessionEmployeeId.Value, cancellationToken);
        if (employee == null || employee.EmFlag != EmployeeService.ActiveFlag || employee.Po.PoFlag != 0)
            throw new UnauthorizedAccessException("有効な社員として確認できません。再ログインしてください。");
        if (employee.Po.PoName is not ("管理者" or "営業") && !(allowLogistics && employee.Po.PoName == "物流"))
            throw new UnauthorizedAccessException("履歴を参照する権限がありません。");
        return employee;
    }

    private static IQueryable<OrderOperationHistory> ApplyOrderFilters(IQueryable<OrderOperationHistory> query,
        HistoryManagementFilter filter, int officeId, bool isAdmin)
    {
        if (!isAdmin) query = query.Where(x => x.OfficeId == officeId);
        if (!string.IsNullOrWhiteSpace(filter.TargetNumber))
            query = OrderNumberSearch.TryGetDigits(filter.TargetNumber, out _) ? OrderNumberSearch.ApplyToHistory(query, filter.TargetNumber) : query.Where(_ => false);
        if (!string.IsNullOrWhiteSpace(filter.TargetName))
        {
            var target = filter.TargetName.Trim().ToUpperInvariant();
            query = query.Where(x => x.CustomerName.ToUpper().Contains(target));
        }
        if (filter.Operation.Length > 0) query = query.Where(x => x.Operation == filter.Operation);
        if (!string.IsNullOrWhiteSpace(filter.Actor))
        {
            var actor = filter.Actor.Trim().ToUpperInvariant();
            query = query.Where(x => x.ActorName.ToUpper().Contains(actor));
        }
        if (filter.From.HasValue)
        {
            var from = new DateTimeOffset(DateTime.SpecifyKind(filter.From.Value.Date, DateTimeKind.Unspecified), OrderHistoryService.JapanOffset);
            query = query.Where(x => x.RecordedAt >= from);
        }
        if (filter.To.HasValue && filter.To.Value.Date != DateTime.MaxValue.Date)
        {
            var to = new DateTimeOffset(DateTime.SpecifyKind(filter.To.Value.Date.AddDays(1), DateTimeKind.Unspecified), OrderHistoryService.JapanOffset);
            query = query.Where(x => x.RecordedAt < to);
        }
        return ApplyOrderKeyword(query, filter.Keyword);
    }

    private static IQueryable<OrderOperationHistory> ApplyOrderKeyword(IQueryable<OrderOperationHistory> query, string keyword)
    {
        keyword = keyword.Trim();
        if (keyword.Length == 0) return query;
        var upper = keyword.ToUpperInvariant();
        var matches = query.Where(x => x.CustomerName.ToUpper().Contains(upper)
            || x.Operation.ToUpper().Contains(upper)
            || x.ActorName.ToUpper().Contains(upper)
            || x.Details.ToUpper().Contains(upper));
        if (int.TryParse(keyword, NumberStyles.None, CultureInfo.InvariantCulture, out var customerId) && customerId > 0)
            matches = matches.Union(query.Where(x => x.CustomerId == customerId));
        if (OrderNumberSearch.TryGetDigits(keyword, out _))
            matches = matches.Union(OrderNumberSearch.ApplyToHistory(query, keyword));
        return matches;
    }

    private static IQueryable<EmployeeAssignmentHistory> ApplyEmployeeFilters(
        IQueryable<EmployeeAssignmentHistory> query, HistoryManagementFilter filter)
    {
        if (filter.EmployeeId is int id) query = query.Where(x => x.EmployeeId == id);
        if (!string.IsNullOrWhiteSpace(filter.TargetNumber))
            query = TryGetEmployeeId(filter.TargetNumber, out var targetId) ? query.Where(x => x.EmployeeId == targetId) : query.Where(_ => false);
        if (!string.IsNullOrWhiteSpace(filter.TargetName))
        {
            var target = filter.TargetName.Trim().ToUpperInvariant();
            query = query.Where(x => x.EmployeeName.ToUpper().Contains(target));
        }
        if (filter.Operation.Length > 0) query = query.Where(x => x.Operation == filter.Operation);
        if (!string.IsNullOrWhiteSpace(filter.Actor))
        {
            var actor = filter.Actor.Trim().ToUpperInvariant();
            query = query.Where(x => x.ActorName.ToUpper().Contains(actor));
        }
        if (filter.From.HasValue)
        {
            var from = new DateTimeOffset(DateTime.SpecifyKind(filter.From.Value.Date, DateTimeKind.Unspecified), OrderHistoryService.JapanOffset);
            query = query.Where(x => x.RecordedAt >= from);
        }
        if (filter.To.HasValue && filter.To.Value.Date != DateTime.MaxValue.Date)
        {
            var to = new DateTimeOffset(DateTime.SpecifyKind(filter.To.Value.Date.AddDays(1), DateTimeKind.Unspecified), OrderHistoryService.JapanOffset);
            query = query.Where(x => x.RecordedAt < to);
        }

        var keyword = filter.Keyword.Trim();
        if (keyword.Length == 0) return query;
        if (keyword.StartsWith("E-", StringComparison.OrdinalIgnoreCase) && TryGetEmployeeId(keyword, out var exactId))
            return query.Where(x => x.EmployeeId == exactId);
        var upper = keyword.ToUpperInvariant();
        var matches = query.Where(x => x.EmployeeName.ToUpper().Contains(upper)
            || x.Operation.ToUpper().Contains(upper)
            || x.ActorName.ToUpper().Contains(upper)
            || (x.BeforeOfficeName ?? "").ToUpper().Contains(upper)
            || x.AfterOfficeName.ToUpper().Contains(upper)
            || (x.BeforeRoleName ?? "").ToUpper().Contains(upper)
            || x.AfterRoleName.ToUpper().Contains(upper));
        if (TryGetEmployeeId(keyword, out var employeeId)) matches = matches.Union(query.Where(x => x.EmployeeId == employeeId));
        return matches;
    }

    private static bool TryGetEmployeeId(string keyword, out int employeeId)
    {
        var value = keyword.Trim();
        if (value.StartsWith("E-", StringComparison.OrdinalIgnoreCase)) value = value[2..];
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out employeeId) && employeeId > 0;
    }

    private static HistoryEventRow ToEventRow(OrderOperationHistory entry)
    {
        var display = new OrderHistoryDisplayRow(entry);
        return new(entry.Id, entry.RecordedAt, OrdersModule, entry.Operation, display.OrderNumber,
            entry.CustomerName, display.ChangeSummary, string.IsNullOrWhiteSpace(entry.ActorName) ? "未記録" : entry.ActorName,
            entry.Details, entry.OrderId, null, entry, null);
    }

    private static HistoryEventRow ToEventRow(EmployeeAssignmentHistory entry)
    {
        var beforeOffice = entry.BeforeOfficeName ?? "未記録";
        var beforeRole = entry.BeforeRoleName ?? "未記録";
        var summary = entry.Operation == "社員登録"
            ? $"初期所属：{entry.AfterOfficeName}、役割：{entry.AfterRoleName}"
            : $"所属：{beforeOffice} → {entry.AfterOfficeName}、役割：{beforeRole} → {entry.AfterRoleName}";
        var detail = string.Join(Environment.NewLine,
            $"変更前営業所：{beforeOffice}", $"変更後営業所：{entry.AfterOfficeName}",
            $"変更前役割：{beforeRole}", $"変更後役割：{entry.AfterRoleName}");
        return new(entry.Id, entry.RecordedAt, EmployeesModule, entry.Operation, $"E-{entry.EmployeeId:0000}",
            entry.EmployeeName, summary, string.IsNullOrWhiteSpace(entry.ActorName) ? "未記録" : entry.ActorName,
            detail, null, entry.EmployeeId, null, entry);
    }
}
