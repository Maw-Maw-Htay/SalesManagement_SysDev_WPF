using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public sealed partial class BusinessService
{
    public static bool CreateOrder(int employeeId, int customerId, int officeId, DateTime orderDate,
        string? note, IReadOnlyCollection<OrderDetailUpdate> lines, out int orderId, out string message,
        DateTime? deliveryDate = null)
    {
        using var db = new SalesManagementContext();
        return CreateOrderCore(db, employeeId, customerId, officeId, orderDate, note, lines, out orderId, out message, deliveryDate);
    }

    internal static bool CreateOrderCore(SalesManagementContext db, int employeeId, int customerId, int officeId,
        DateTime orderDate, string? customerContact, IReadOnlyCollection<OrderDetailUpdate> lines,
        out int orderId, out string message, DateTime? deliveryDate = null)
    {
        orderId = 0;
        message = "";
        if (!AppSession.CanManageOrders || AppSession.Employee?.EmId != employeeId)
        { message = "受注の登録権限がありません。"; return false; }
        if (!AppSession.IsAdmin && AppSession.Employee.SoId != officeId)
        { message = "所属営業所以外の受注は登録できません。"; return false; }
        if (customerContact?.Trim().Length > OrderUpdateRules.RemarksMaxLength)
        { message = "顧客担当者名は50文字以内で入力してください。"; return false; }
        if (lines.Count == 0 || lines.Any(x => !OrderUpdateRules.TrySubtotal(x.Quantity, x.UnitPrice, out _)))
        { message = "明細を1行以上指定し、数量と単価を確認してください。"; return false; }
        using var transaction = db.Database.BeginTransaction();
        try
        {
            if (!db.MClients.Any(x => x.ClId == customerId && x.SoId == officeId && x.ClFlag == 0)
                || !db.MSalesOffices.Any(x => x.SoId == officeId && x.SoFlag == 0))
            { message = "顧客と営業所の組み合わせを確認してください。"; return false; }
            var productIds = lines.Select(x => x.ProductId).Distinct().ToList();
            if (db.MProducts.Count(x => productIds.Contains(x.PrId) && x.PrFlag == 0) != productIds.Count)
            { message = "選択した商品が存在しないか、現在利用できません。"; return false; }
            var order = new TOrder
            {
                EmId = employeeId, ClId = customerId, SoId = officeId, OrDate = orderDate.Date,
                OrDeliveryDate = deliveryDate?.Date,
                ClCharge = string.IsNullOrWhiteSpace(customerContact) ? null : customerContact.Trim(), OrStateFlag = 0, OrFlag = 0
            };
            db.TOrders.Add(order);
            db.SaveChanges();
            foreach (var line in lines) order.TOrderDetails.Add(new TOrderDetail
            {
                PrId = line.ProductId, OrQuantity = line.Quantity, OrTotalPrice = line.Quantity * line.UnitPrice
            });
            db.SaveChanges();
            OrderHistoryService.Record(db, order, OrderHistoryOperations.Created, null, "初期フラグ：状態 0（未確定）／管理 0（表示）。在庫数量の変更はありません。");
            db.SaveChanges();
            transaction.Commit();
            orderId = order.OrId;
            message = $"受注を登録しました。\n受注ID: {orderId}";
            return true;
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            message = "受注登録に失敗しました: " + ex.Message;
            return false;
        }
    }

    public static bool SetOrderVisibility(int orderId, int employeeId, bool hidden, string? reason, out string message)
    {
        if (hidden)
        {
            try
            {
                using var context = new SalesManagementContext();
                return HideOrderCore(context, orderId, employeeId, reason, out message);
            }
            catch (Exception)
            {
                message = "受注を非表示にできませんでした。一覧・履歴を確認してから再試行してください。";
                return false;
            }
        }
        message = "";
        if (!AppSession.CanManageOrders || AppSession.Employee?.EmId != employeeId)
        { message = "受注の表示状態を変更する権限がありません。"; return false; }
        if (!hidden && !AppSession.IsAdmin)
        { message = "表示復帰は管理者のみ実行できます。"; return false; }
        if (string.IsNullOrWhiteSpace(reason))
        { message = hidden ? "非表示理由を入力してください。" : "表示復帰理由を入力してください。"; return false; }
        using var db = new SalesManagementContext();
        try
        {
            using var transaction = db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable);
            var order = db.TOrders.FromSqlInterpolated($"SELECT * FROM dbo.T_Order WITH (UPDLOCK, HOLDLOCK) WHERE OrID = {orderId}")
                .Include(x => x.TOrderDetails).SingleOrDefault();
            if (order == null || (!AppSession.IsAdmin && order.SoId != AppSession.Employee.SoId))
            { message = "対象の受注が存在しないか、所属営業所の範囲外です。"; return false; }
            var nextFlag = hidden ? 2 : 0;
            if (order.OrFlag == nextFlag)
            { message = hidden ? "すでに非表示です。" : "すでに表示中です。"; return false; }
            var before = OrderHistoryService.Capture(db, order);
            order.OrFlag = nextFlag;
            order.OrHidden = hidden ? reason!.Trim() : null;
            OrderHistoryService.Record(db, order, hidden ? OrderHistoryOperations.Hidden : OrderHistoryOperations.Restored,
                before, hidden ? $"非表示理由：{reason!.Trim()}。在庫数量の変更はありません。" : $"表示復帰理由：{reason!.Trim()}。在庫数量の変更はありません。");
            db.SaveChanges();
            transaction.Commit();
            message = hidden ? "受注を非表示にしました。" : "受注の表示を復帰しました。";
            return true;
        }
        catch (Exception ex)
        { message = "受注の表示状態を変更できませんでした: " + ex.Message; return false; }
    }

    internal static bool HideOrderCore(SalesManagementContext db, int orderId, int employeeId, string? reason, out string message)
    {
        message = "";
        if (!AppSession.CanManageOrders || AppSession.Employee is not { } actor || actor.EmId != employeeId)
        { message = "受注の非表示は管理者または営業担当者のみ実行できます。"; return false; }
        if (orderId <= 0)
        { message = "対象の受注を確認してください。"; return false; }
        if (string.IsNullOrWhiteSpace(reason))
        { message = "非表示理由を入力してください。"; return false; }
        var isAdmin = AppSession.IsAdmin;
        try
        {
            using var transaction = db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable);
            // Serialize competing hide requests before checking eligibility; no schema/rowversion change required.
            var query = db.TOrders.FromSqlInterpolated($"SELECT * FROM dbo.T_Order WITH (UPDLOCK, HOLDLOCK) WHERE OrID = {orderId}");
            if (!isAdmin) query = query.Where(order => order.SoId == actor.SoId);
            var order = query.SingleOrDefault();
            if (order == null)
            { message = "対象の受注が存在しないか、所属営業所の範囲外です。"; return false; }
            if (order.OrFlag == 2)
            { message = "すでに非表示です。一覧・履歴を確認してください。"; return false; }
            var before = OrderHistoryService.Capture(db, order);
            // Recheck the session before writing or attributing the audit record.
            if (!AppSession.CanManageOrders || AppSession.Employee?.EmId != employeeId
                || AppSession.Employee.SoId != actor.SoId || AppSession.IsAdmin != isAdmin)
            { message = "ログイン情報が変更されました。画面を開き直してください。"; return false; }
            order.OrFlag = 2;
            order.OrHidden = reason.Trim();
            OrderHistoryService.Record(db, order, OrderHistoryOperations.Hidden, before,
                $"非表示理由：{order.OrHidden}。在庫数量の変更はありません。");
            db.SaveChanges();
            transaction.Commit();
            message = "受注を非表示にしました。";
            return true;
        }
        catch (Exception)
        {
            // Disposing an uncommitted transaction rolls back both the visibility and its audit entry.
            message = "受注を非表示にできませんでした。一覧・履歴を確認してから再試行してください。";
            return false;
        }
    }
}
