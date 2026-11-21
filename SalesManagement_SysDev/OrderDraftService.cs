using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public sealed class OrderDraftException(string message) : Exception(message);

public sealed class OrderDraftService : IOrderDraftStore
{
    private const string Unavailable = "下書きが見つからないか、参照できません。一覧を開き直してください。";
    private const string Changed = "下書きが別の画面で更新されました。下書き一覧から開き直してください。";
    private const string Denied = "受注の下書きを利用する権限がありません。";
    private sealed record Actor(int EmployeeId, int OfficeId, bool IsAdmin)
    {
        public bool CanUseOffice(int officeId) => IsAdmin || OfficeId == officeId;
    }
    private sealed record EmployeeIdentity(int Id, int OfficeId, string Role);

    public IReadOnlyList<OrderDraftSnapshot> List()
    {
        using var db = OpenDraftContext();
        var actor = Authorize(db);
        return ActiveDrafts(db, actor)
            .OrderByDescending(draft => draft.UpdatedAt).ThenBy(draft => draft.DraftId)
            .ToArray().Select(ToSnapshot).ToArray();
    }

    internal static async Task<OrderDraftPage> ReadPageAsync(SalesManagementContext db, OrderListRequest request,
        int pageNumber, int pageSize, IReadOnlyList<Guid> checkedIds, CancellationToken token)
    {
        var session = RequireSession();
        var employee = await ReadEmployee(db, session.EmId).SingleOrDefaultAsync(token);
        var actor = VerifyActor(session, employee);
        // Older databases can have no drafts yet. Listing registered orders needs no DDL privileges.
        var tableExists = await db.Database.SqlQueryRaw<int>(
            "SELECT CASE WHEN OBJECT_ID(N'dbo.T_OrderDraft', N'U') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync(token);
        if (tableExists == 0) return new OrderDraftPage([], [], 0, 1);

        var scoped = db.Set<OrderDraftListEntry>().Where(row => row.EmployeeId == actor.EmployeeId
            && (actor.IsAdmin || row.ScopeOfficeId == actor.OfficeId));
        var filtered = OrderDraftListQuery.ApplyFilters(db, scoped, request.Criteria);
        var count = await filtered.CountAsync(token);
        var page = Math.Clamp(pageNumber, 1, OrderListQuery.PageCount(count, pageSize));
        if (request.Tab != OrderStatusTab.Draft || count == 0) return new OrderDraftPage([], [], count, page);

        var rows = await OrderDraftListQuery.Sort(filtered, request).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(token);
        var offPageIds = checkedIds.Except(rows.Select(row => row.DraftId)).ToArray();
        var checkedOffPage = offPageIds.Length == 0 ? []
            : await filtered.Where(row => offPageIds.Contains(row.DraftId)).ToListAsync(token);
        return new OrderDraftPage(rows.Select(row => row.ToListRow()).ToArray(),
            checkedOffPage.Select(row => row.ToListRow()).ToArray(), count, page);
    }

    public OrderDraftSnapshot Open(Guid id)
    {
        using var db = OpenDraftContext();
        var actor = Authorize(db);
        var draft = ActiveDrafts(db, actor).SingleOrDefault(draft => draft.DraftId == id);
        return draft == null ? throw new OrderDraftException(Unavailable) : ToSnapshot(draft);
    }

    public OrderDraftToken Save(OrderDraftToken token, OrderDraftForm form)
    {
        using var db = OpenDraftContext();
        using var transaction = db.Database.BeginTransaction(IsolationLevel.RepeatableRead);
        var actor = Authorize(db);
        var draft = LockDraft(db, actor, token.Id);
        var json = JsonSerializer.Serialize(form);
        // A retry after losing a save response can recover the committed version for the same input.
        if (draft is not null && draft.FormJson == json) return ToToken(draft);
        EnsureWritableVersion(draft, token);
        var officeId = form.Header.Office.Id;
        var scope = ValidateOffice(actor, officeId);
        if (officeId.HasValue && !db.MSalesOffices.Any(office => office.SoId == officeId && office.SoFlag == 0))
            throw new OrderDraftException("受付営業所を選び直してください。");
        if (form.Header.Customer.Id is int customerId
            && (!officeId.HasValue || !db.MClients.Any(customer => customer.ClId == customerId && customer.SoId == officeId)))
            throw new OrderDraftException("受付営業所に所属する顧客を選択してください。");

        draft ??= AddDraft(db, actor, token, scope);
        draft.ScopeOfficeId = scope;
        draft.UpdatedAt = DateTimeOffset.UtcNow;
        draft.FormJson = json;
        db.SaveChanges();
        transaction.Commit();
        return ToToken(draft);
    }

    public void Delete(OrderDraftToken token)
    {
        using var db = OpenDraftContext();
        using var transaction = db.Database.BeginTransaction(IsolationLevel.RepeatableRead);
        var actor = Authorize(db);
        var draft = LockDraft(db, actor, token.Id);
        if (draft == null || draft.RegisteredOrderId != null) throw new OrderDraftException(Unavailable);
        if (draft.FormJson == null) return;
        EnsureWritableVersion(draft, token);
        draft.FormJson = null;
        draft.UpdatedAt = DateTimeOffset.UtcNow;
        db.SaveChanges();
        transaction.Commit();
    }

    public OrderDraftRegistration Register(OrderDraftToken token, OrderDraftForm form)
    {
        using var db = OpenDraftContext();
        using var transaction = db.Database.BeginTransaction(IsolationLevel.RepeatableRead);
        var actor = Authorize(db);
        var draft = LockDraft(db, actor, token.Id);
        if (draft?.RegisteredOrderId is int registeredId) return new OrderDraftRegistration(registeredId, AlreadyRegistered: true);
        // A first save may have committed even if the client did not receive its version.
        if (token.Version != null || draft != null && draft.FormJson != JsonSerializer.Serialize(form))
            EnsureWritableVersion(draft, token);
        var header = form.Header;
        if (header.Office.Id is not int officeId || header.Customer.Id is not int customerId
            || header.OrderDate.Value is not DateTime orderDate || header.DeliveryDate.Value is not DateTime deliveryDate
            || header.Remarks.Trim().Length > OrderUpdateRules.RemarksMaxLength)
            throw new OrderDraftException("顧客・受付営業所・日付・備考を確認してください。");
        var scope = ValidateOffice(actor, officeId);
        if (form.Lines.Count == 0 || form.Lines.Select(line => line.ProductId).Distinct().Count() != form.Lines.Count
            || form.Lines.Any(line => !int.TryParse(line.QuantityText, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity)
                || quantity != line.Quantity || !OrderUpdateRules.TrySubtotal(quantity, line.UnitPrice, out _)))
            throw new OrderDraftException("明細を1行以上指定し、数量と単価を確認してください。");

        var lines = form.Lines.Select(line => new OrderDetailUpdate(null, line.ProductId, line.Quantity, line.UnitPrice)).ToArray();
        var order = BusinessService.CreateOrderInTransaction(db, actor.EmployeeId, customerId, officeId, orderDate,
            deliveryDate, header.Remarks, lines, out var error, requireCurrentPrices: true);
        if (order == null) throw new OrderDraftException(error);
        draft ??= AddDraft(db, actor, token, scope);
        draft.ScopeOfficeId = scope;
        draft.RegisteredOrderId = order.OrId;
        draft.FormJson = null;
        draft.UpdatedAt = DateTimeOffset.UtcNow;
        db.SaveChanges();
        transaction.Commit();
        return new OrderDraftRegistration(order.OrId);
    }

    private static SalesManagementContext OpenDraftContext()
    {
        RequireSession();
        var db = new SalesManagementContext();
        try
        {
            OrderSchemaInitializer.EnsureDraftsReady(db);
            return db;
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    private static Actor Authorize(SalesManagementContext db)
    {
        var session = RequireSession();
        return VerifyActor(session, ReadEmployee(db, session.EmId).SingleOrDefault());
    }

    private static MEmployee RequireSession() => AppSession.CanManageOrders && AppSession.Employee is { EmFlag: 0 } session
        ? session : throw new OrderDraftException(Denied);

    private static IQueryable<EmployeeIdentity> ReadEmployee(SalesManagementContext db, int employeeId) =>
        db.MEmployees.AsNoTracking().Where(employee => employee.EmId == employeeId
            && employee.EmFlag == 0 && employee.Po.PoFlag == 0)
            .Select(employee => new EmployeeIdentity(employee.EmId, employee.SoId, employee.Po.PoName));

    private static Actor VerifyActor(MEmployee session, EmployeeIdentity? employee)
    {
        if (employee == null || employee.Role is not ("管理者" or "営業") || employee.OfficeId != session.SoId || employee.Role != AppSession.Role
            || AppSession.Employee?.EmId != session.EmId)
            throw new OrderDraftException("ログイン情報が変更されました。ログインし直してください。");
        return new Actor(employee.Id, employee.OfficeId, employee.Role == "管理者");
    }

    private static IQueryable<OrderDraftRecord> ActiveDrafts(SalesManagementContext db, Actor actor) =>
        InScope(db.OrderDrafts.AsNoTracking(), actor).Where(draft => draft.FormJson != null && draft.RegisteredOrderId == null);

    private static IQueryable<OrderDraftRecord> InScope(IQueryable<OrderDraftRecord> query, Actor actor) =>
        query.Where(draft => draft.EmployeeId == actor.EmployeeId
            && (actor.IsAdmin || draft.ScopeOfficeId == actor.OfficeId));

    private static OrderDraftRecord? LockDraft(SalesManagementContext db, Actor actor, Guid id) =>
        InScope(db.OrderDrafts.FromSqlInterpolated($"SELECT * FROM dbo.T_OrderDraft WITH (UPDLOCK, HOLDLOCK) WHERE DraftId = {id}"), actor)
            .SingleOrDefault();

    private static int ValidateOffice(Actor actor, int? officeId)
    {
        var scope = officeId ?? actor.OfficeId;
        if (!actor.CanUseOffice(scope)) throw new OrderDraftException("所属営業所以外の下書きは保存できません。");
        return scope;
    }

    private static void EnsureWritableVersion(OrderDraftRecord? draft, OrderDraftToken token)
    {
        if (draft == null && token.Version == null) return;
        if (draft?.FormJson == null) throw new OrderDraftException(Unavailable);
        if (token.Version == null || !token.Version.SequenceEqual(draft.Version)) throw new OrderDraftException(Changed);
    }

    private static OrderDraftRecord AddDraft(SalesManagementContext db, Actor actor, OrderDraftToken token, int scope)
    {
        var draft = new OrderDraftRecord { DraftId = token.Id, EmployeeId = actor.EmployeeId, ScopeOfficeId = scope };
        db.OrderDrafts.Add(draft);
        return draft;
    }

    private static OrderDraftToken ToToken(OrderDraftRecord draft) => new(draft.DraftId, draft.Version);

    private static OrderDraftSnapshot ToSnapshot(OrderDraftRecord draft) =>
        new(ToToken(draft), draft.UpdatedAt, JsonSerializer.Deserialize<OrderDraftForm>(draft.FormJson!)
            ?? throw new OrderDraftException("下書きの内容を読み込めませんでした。"));
}
