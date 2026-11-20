using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public static class OrderHistoryOperations
{
    public const string Created = "新規受注登録";
    public const string Updated = "受注管理更新";
    public const string Confirmed = "受注確定";
    public const string Hidden = "受注非表示";
    public const string Restored = "表示復帰";
    public const string Deleted = "受注削除";
    public const string Baseline = "記録開始時の状態";
    public static readonly string[] All = [Created, Updated, Confirmed, Hidden, Restored, Deleted, Baseline];
}

public sealed record OrderAuditLine(int ProductId, string ProductName, int Quantity, decimal Amount);

public sealed record OrderAuditSnapshot(int State, int Management, int CustomerId, string CustomerName,
    int OfficeId, string OfficeName, string EmployeeName, DateTime OrderDate, string Contact, string HiddenReason,
    IReadOnlyList<OrderAuditLine> Lines, DateTime? DeliveryDate = null)
{
    public decimal Total => Lines.Sum(x => x.Amount);
    public int Quantity => Lines.Sum(x => x.Quantity);
}

public sealed class OrderHistoryFilter
{
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public int OrderId { get; init; } = -1;
    public int CustomerId { get; init; } = -1;
    public int SelectedCustomerNameId { get; init; } = -1;
    public string OrderNumberText { get; init; } = "";
    public string CustomerNamePrefix { get; init; } = "";
    public int ActorEmployeeId { get; init; } = -1;
    public string ActorNamePrefix { get; init; } = "";
    public int OfficeId { get; init; } = -1;
    public int State { get; init; } = -1;
    public int Management { get; init; } = -1;
    public string CommonKeyword { get; init; } = "";
    public string Keyword { get; init; } = "";
    public string Operation { get; init; } = "";
}

public sealed record OrderHistoryPage(IReadOnlyList<OrderOperationHistory> Rows, int Total);

public static class OrderHistoryService
{
    public static readonly TimeSpan JapanOffset = TimeSpan.FromHours(9);

    public static void EnsureReady(SalesManagementContext db)
    {
        using var transaction = db.Database.BeginTransaction();
        db.Database.ExecuteSqlRaw("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = N'SalesManagement.OrderHistory.Setup',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
            IF @result < 0 THROW 51000, N'受注履歴の初期化ロックを取得できません。', 1;
            IF OBJECT_ID(N'dbo.T_OrderOperationHistory', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.T_OrderOperationHistory (
                    Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    RecordedAt datetimeoffset NOT NULL,
                    Operation nvarchar(40) NOT NULL,
                    OrderId int NOT NULL,
                    CustomerId int NOT NULL,
                    CustomerName nvarchar(100) NOT NULL,
                    OfficeId int NOT NULL,
                    BeforeState int NULL, AfterState int NULL,
                    BeforeManagement int NULL, AfterManagement int NULL,
                    Details nvarchar(max) NOT NULL,
                    ActorEmployeeId int NULL,
                    ActorName nvarchar(100) NOT NULL,
                    ActorOfficeName nvarchar(100) NOT NULL
                );
                CREATE INDEX IX_T_OrderOperationHistory_OfficeId_RecordedAt
                    ON dbo.T_OrderOperationHistory (OfficeId, RecordedAt);
                CREATE INDEX IX_T_OrderOperationHistory_OrderId ON dbo.T_OrderOperationHistory (OrderId);
            END
            """);

        var existing = db.TOrders.AsNoTracking()
            .Include(x => x.Cl).Include(x => x.So).Include(x => x.Em)
            .Include(x => x.TOrderDetails).ThenInclude(x => x.Pr)
            .Where(x => !db.OrderOperationHistories.Any(history => history.OrderId == x.OrId))
            .ToList();
        var recordedAt = DateTimeOffset.UtcNow;
        foreach (var order in existing)
        {
            var snapshot = CaptureLoaded(order);
            db.OrderOperationHistories.Add(new OrderOperationHistory
            {
                RecordedAt = recordedAt, Operation = OrderHistoryOperations.Baseline,
                OrderId = order.OrId, CustomerId = order.ClId, CustomerName = snapshot.CustomerName,
                OfficeId = order.SoId, AfterState = snapshot.State, AfterManagement = snapshot.Management,
                ActorName = "システム（初期記録）",
                Details = "履歴記録の開始時点で保存されていた状態です。過去の操作日時・実行者・変更前の値は未記録です。\n"
                    + Summary(snapshot) + "\n" + LinesText(snapshot)
                    + (snapshot.Contact.Length > 0 ? "\n備考・担当連絡先：" + snapshot.Contact : "")
                    + (snapshot.HiddenReason.Length > 0 ? "\n非表示理由：" + snapshot.HiddenReason : "")
            });
        }
        db.SaveChanges();
        transaction.Commit();
    }

    public static OrderAuditSnapshot Capture(SalesManagementContext db, TOrder order)
    {
        if (db.Entry(order).State != EntityState.Added)
            db.Entry(order).Collection(x => x.TOrderDetails).Load();
        var customer = db.MClients.Find(order.ClId);
        var office = db.MSalesOffices.Find(order.SoId);
        var employee = db.MEmployees.Find(order.EmId);
        var productIds = order.TOrderDetails.Select(x => x.PrId).Distinct().ToList();
        var products = db.MProducts.Where(x => productIds.Contains(x.PrId)).ToDictionary(x => x.PrId, x => x.PrName);
        var lines = order.TOrderDetails.Where(x => db.Entry(x).State != EntityState.Deleted && !OrderUpdateRules.IsRemovedLine(x))
            .OrderBy(x => x.PrId).ThenBy(x => x.OrDetailId)
            .Select(x => new OrderAuditLine(x.PrId, products.GetValueOrDefault(x.PrId, $"商品 {x.PrId}"), x.OrQuantity, x.OrTotalPrice)).ToList();
        return new OrderAuditSnapshot(order.OrStateFlag ?? 0, order.OrFlag, order.ClId, customer?.ClName ?? "未登録顧客",
            order.SoId, office?.SoName ?? "", employee?.EmName ?? "", order.OrDate, order.ClCharge ?? "", order.OrHidden ?? "", lines, order.OrDeliveryDate);
    }

    private static OrderAuditSnapshot CaptureLoaded(TOrder order) => new(
        order.OrStateFlag ?? 0, order.OrFlag, order.ClId, order.Cl.ClName, order.SoId, order.So.SoName,
        order.Em.EmName, order.OrDate, order.ClCharge ?? "", order.OrHidden ?? "",
        order.TOrderDetails.Where(x => !OrderUpdateRules.IsRemovedLine(x)).OrderBy(x => x.PrId).ThenBy(x => x.OrDetailId)
            .Select(x => new OrderAuditLine(x.PrId, x.Pr.PrName, x.OrQuantity, x.OrTotalPrice)).ToList(), order.OrDeliveryDate);

    public static void Record(SalesManagementContext db, TOrder order, string operation,
        OrderAuditSnapshot? before, string note = "", bool deleted = false)
    {
        if (!AppSession.CanManageOrders || AppSession.Employee is not { } actor)
            throw new UnauthorizedAccessException("受注操作の履歴を記録する権限がありません。");
        var after = deleted ? before! : Capture(db, order);
        var changes = new List<string> { $"【{operation}】{Summary(after)}" };
        if (before != null)
        {
            void Change(string label, object oldValue, object newValue)
            {
                if (!Equals(oldValue, newValue)) changes.Add($"{label}：{oldValue} → {newValue}");
            }
            Change("品目数", before.Lines.Count, after.Lines.Count);
            Change("総数量", before.Quantity, after.Quantity);
            Change("金額", $"¥{before.Total:N0}", $"¥{after.Total:N0}");
            Change("顧客", before.CustomerName, after.CustomerName);
            Change("営業所", before.OfficeName, after.OfficeName);
            Change("受注担当者", before.EmployeeName, after.EmployeeName);
            Change("受注日", before.OrderDate.ToString("yyyy/MM/dd"), after.OrderDate.ToString("yyyy/MM/dd"));
            Change("納品希望日", before.DeliveryDate?.ToString("yyyy/MM/dd") ?? "未設定", after.DeliveryDate?.ToString("yyyy/MM/dd") ?? "未設定");
            Change("備考・担当連絡先", before.Contact, after.Contact);
            Change("非表示理由", before.HiddenReason, after.HiddenReason);
            if (!before.Lines.SequenceEqual(after.Lines))
                changes.Add("変更前明細：" + LinesText(before));
        }
        changes.Add("明細：" + LinesText(after));
        if (after.Contact.Length > 0) changes.Add("備考・担当連絡先：" + after.Contact);
        if (after.HiddenReason.Length > 0) changes.Add("非表示理由：" + after.HiddenReason);
        if (!string.IsNullOrWhiteSpace(note)) changes.Add(note);
        db.OrderOperationHistories.Add(new OrderOperationHistory
        {
            RecordedAt = DateTimeOffset.UtcNow, Operation = operation, OrderId = order.OrId,
            CustomerId = after.CustomerId, CustomerName = after.CustomerName, OfficeId = after.OfficeId,
            BeforeState = before?.State, AfterState = deleted ? null : after.State,
            BeforeManagement = before?.Management, AfterManagement = deleted ? null : after.Management,
            Details = string.Join("\n", changes), ActorEmployeeId = actor.EmId,
            ActorName = actor.EmName, ActorOfficeName = actor.So?.SoName ?? ""
        });
    }

    public static string Summary(OrderAuditSnapshot snapshot) =>
        $"品目数：{snapshot.Lines.Count} 件、総数量：{snapshot.Quantity:N0} 点、金額：¥{snapshot.Total:N0}。顧客：{snapshot.CustomerName}、営業所：{snapshot.OfficeName}、受注日：{snapshot.OrderDate:yyyy/MM/dd}、納品希望日：{snapshot.DeliveryDate?.ToString("yyyy/MM/dd") ?? "未設定"}、受注担当者：{snapshot.EmployeeName}。";

    private static string LinesText(OrderAuditSnapshot snapshot) => string.Join(" / ",
        snapshot.Lines.Select(x => $"{x.ProductName}（商品 {x.ProductId}）：{x.Quantity:N0} 点、¥{x.Amount:N0}"));

    public static OrderHistoryPage Read(SalesManagementContext db, OrderHistoryFilter filter)
    {
        var query = BuildBaseQuery(db, filter);
        var total = query.Count();
        query = ApplyKeyword(query, filter.Keyword);
        if (!string.IsNullOrWhiteSpace(filter.Operation)) query = query.Where(x => x.Operation == filter.Operation);
        return new OrderHistoryPage(query.OrderByDescending(x => x.RecordedAt).ThenByDescending(x => x.Id).ToList(), total);
    }

    public static async Task<OrderHistoryPage> ReadAsync(SalesManagementContext db, OrderHistoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = BuildBaseQuery(db, filter);
        var total = await query.CountAsync(cancellationToken);
        query = ApplyKeyword(query, filter.Keyword);
        if (!string.IsNullOrWhiteSpace(filter.Operation)) query = query.Where(x => x.Operation == filter.Operation);
        var rows = await query.OrderByDescending(x => x.RecordedAt).ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
        return new OrderHistoryPage(rows, total);
    }

    private static IQueryable<OrderOperationHistory> BuildBaseQuery(SalesManagementContext db, OrderHistoryFilter filter)
    {
        if (!AppSession.CanManageOrders || AppSession.Employee is not { } actor)
            throw new UnauthorizedAccessException("受注履歴は管理者または営業担当者のみ参照できます。");
        var query = db.OrderOperationHistories.AsNoTracking().AsQueryable();
        if (!AppSession.IsAdmin) query = query.Where(x => x.OfficeId == actor.SoId);
        if (filter.OrderId >= 0) query = query.Where(x => x.OrderId == filter.OrderId);
        if (!string.IsNullOrWhiteSpace(filter.OrderNumberText))
            query = OrderNumberSearch.ApplyToHistory(query, filter.OrderNumberText);
        if (filter.CustomerId >= 0) query = query.Where(x => x.CustomerId == filter.CustomerId);
        if (filter.SelectedCustomerNameId >= 0) query = query.Where(x => x.CustomerId == filter.SelectedCustomerNameId);
        if (!string.IsNullOrWhiteSpace(filter.CustomerNamePrefix))
        {
            var customerName = filter.CustomerNamePrefix.Trim().ToUpperInvariant();
            query = query.Where(x => x.CustomerName.ToUpper().Contains(customerName));
        }
        if (filter.ActorEmployeeId >= 0) query = query.Where(x => x.ActorEmployeeId == filter.ActorEmployeeId);
        if (!string.IsNullOrWhiteSpace(filter.ActorNamePrefix))
        {
            var actorName = filter.ActorNamePrefix.Trim().ToUpperInvariant();
            query = query.Where(x => x.ActorName.ToUpper().Contains(actorName));
        }
        if (filter.OfficeId >= 0) query = query.Where(x => x.OfficeId == filter.OfficeId);
        if (filter.State >= 0) query = query.Where(x => x.AfterState == filter.State);
        if (filter.Management >= 0) query = query.Where(x => x.AfterManagement == filter.Management);
        if (filter.From.HasValue)
        {
            var start = new DateTimeOffset(DateTime.SpecifyKind(filter.From.Value.Date, DateTimeKind.Unspecified), JapanOffset);
            query = query.Where(x => x.RecordedAt >= start);
        }
        if (filter.To.HasValue)
        {
            var end = new DateTimeOffset(DateTime.SpecifyKind(filter.To.Value.Date.AddDays(1), DateTimeKind.Unspecified), JapanOffset);
            query = query.Where(x => x.RecordedAt < end);
        }
        return ApplyKeyword(query, filter.CommonKeyword);
    }
    private static IQueryable<OrderOperationHistory> ApplyKeyword(IQueryable<OrderOperationHistory> query, string keyword)
    {
        keyword = keyword.Trim();
        if (keyword.Length == 0) return query;
        var upperKeyword = keyword.ToUpperInvariant();
        var matches = query.Where(x => x.CustomerName.ToUpper().Contains(upperKeyword)
            || x.ActorName.ToUpper().Contains(upperKeyword)
            || x.ActorOfficeName.ToUpper().Contains(upperKeyword)
            || x.Operation.ToUpper().Contains(upperKeyword)
            || x.Details.ToUpper().Contains(upperKeyword));
        if (int.TryParse(keyword, NumberStyles.None, CultureInfo.InvariantCulture, out var customerId) && customerId > 0)
            matches = matches.Union(query.Where(x => x.CustomerId == customerId));
        if (OrderNumberSearch.TryGetDigits(keyword, out _))
            matches = matches.Union(OrderNumberSearch.ApplyToHistory(query, keyword));
        return matches;
    }
}
