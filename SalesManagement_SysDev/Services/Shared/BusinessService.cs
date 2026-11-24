using System.Data;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public sealed partial class BusinessService
{
    public static MEmployee? Login(int id, string password)
    {
        using var db = new SalesManagementContext();
        var employee = db.MEmployees.Include(x => x.Po).Include(x => x.So)
            .FirstOrDefault(x => x.EmId == id && x.EmFlag == 0 && x.Po.PoFlag == 0);
        if (employee == null || !VerifyEmployeePassword(employee, password)) return null;
        // The session never needs credential material.
        employee.EmPassword = EmployeePasswordHasher.CreateLegacyPlaceholder();
        employee.EmPasswordHash = null;
        return employee;
    }

    public static bool ChangePassword(int id, string oldPassword, string newPassword, out string message)
    {
        message = "";
        if (id <= 0 || string.IsNullOrEmpty(oldPassword))
        { message = "社員IDと現在のパスワードを確認してください。"; return false; }
        if (newPassword.Length < 4 || newPassword.Length > 10 || !newPassword.All(char.IsAsciiLetterOrDigit))
        { message = "新しいパスワードは半角英数字4～10文字で入力してください。"; return false; }
        if (string.Equals(oldPassword, newPassword, StringComparison.Ordinal))
        { message = "新しいパスワードは現在のパスワードと異なるものにしてください。"; return false; }
        using var db = new SalesManagementContext();
        var employee = db.MEmployees.FirstOrDefault(x => x.EmId == id && x.EmFlag == 0);
        if (employee == null || !VerifyEmployeePassword(employee, oldPassword))
        { message = "現在のパスワードが正しくありません。"; return false; }
        employee.EmPassword = newPassword;
        employee.EmPasswordHash = null;
        db.SaveChanges();
        if (AppSession.Employee?.EmId == id)
        {
            AppSession.Employee.EmPassword = EmployeePasswordHasher.CreateLegacyPlaceholder();
            AppSession.Employee.EmPasswordHash = null;
        }
        message = "パスワードを変更しました。次回から新しいパスワードでログインできます。";
        return true;
    }

    private static bool VerifyEmployeePassword(MEmployee employee, string password)
    {
        if (!string.IsNullOrWhiteSpace(employee.EmPasswordHash))
            return EmployeePasswordHasher.Verify(password, employee.EmPasswordHash);
        return EmployeePasswordHasher.VerifyLegacy(password, employee.EmPassword);
    }

    public static void EnsureStock(MProduct product, SalesManagementContext db)
    {
        if (!db.TStocks.Any(x => x.PrId == product.PrId)) db.TStocks.Add(new TStock { PrId = product.PrId, StQuantity = 0, StFlag = 0 });
    }

    public static bool ConfirmOrder(int orderId, int employeeId, out string message)
    {
        if (!AppSession.CanManageOrders || AppSession.Employee?.EmId != employeeId)
        { message = "受注を確定する権限がありません。"; return false; }
        message = ""; using var db = new SalesManagementContext(); using var tx = db.Database.BeginTransaction(IsolationLevel.Serializable);
        try {
            var o = db.TOrders.Include(x => x.TOrderDetails).FirstOrDefault(x => x.OrId == orderId && x.OrFlag == 0);
            if (o == null || (o.OrStateFlag ?? 0) != 0) { message = "対象の受注は未確定・表示中ではないか、存在しません。"; return false; }
            if (!AppSession.IsAdmin && o.SoId != AppSession.Employee.SoId)
            { message = "所属営業所以外の受注は確定できません。"; return false; }
            if (db.TChumons.Any(x => x.OrId == orderId))
            { message = "この受注には注文がすでに作成されています。重複確定はできません。"; return false; }
            if (!db.MClients.Any(x => x.ClId == o.ClId && x.SoId == o.SoId && x.ClFlag == 0)
                || !db.MSalesOffices.Any(x => x.SoId == o.SoId && x.SoFlag == 0)
                || !db.MEmployees.Any(x => x.EmId == o.EmId && x.SoId == o.SoId && x.EmFlag == 0))
            { message = "顧客・営業所・担当者が有効か確認してください。"; return false; }
            var activeDetails = o.TOrderDetails.Where(x => !OrderUpdateRules.IsRemovedLine(x)).ToList();
            if (activeDetails.Count == 0 || activeDetails.Any(x => x.OrQuantity <= 0 || x.OrTotalPrice < 0))
            { message = "受注明細の数量または金額を確認してください。"; return false; }
            var productIds = activeDetails.Select(x => x.PrId).Distinct().ToList();
            if (db.MProducts.Count(x => productIds.Contains(x.PrId) && x.PrFlag == 0) != productIds.Count)
            { message = "無効または未登録の商品を含むため確定できません。"; return false; }
            var before = OrderHistoryService.Capture(db, o);
            o.OrStateFlag = 1;
            var c = new TChumon { SoId=o.SoId, ClId=o.ClId, OrId=o.OrId, EmId=null, ChDate=DateTime.Today, ChStateFlag=0, ChFlag=0 };
            db.TChumons.Add(c); db.SaveChanges();
            foreach (var d in activeDetails) db.TChumonDetails.Add(new TChumonDetail { ChId=c.ChId, PrId=d.PrId, ChQuantity=d.OrQuantity });
            OrderHistoryService.Record(db, o, OrderHistoryOperations.Confirmed, before,
                $"注文 {c.ChId} を作成しました。在庫数量の減算は注文確定時に実行され、この操作では変更していません。");
            db.SaveChanges(); tx.Commit(); message = "受注を確定し、注文を作成しました。"; return true;
        } catch (Exception ex) { tx.Rollback(); message = "確定に失敗しました: " + ex.Message; return false; }
    }

    public static bool UpdateOrder(int orderId, int employeeId, int customerId, int officeId, int assignedEmployeeId, DateTime orderDate, DateTime? deliveryDate, int stateFlag, int managementFlag, string? hiddenReason, string? customerContact, IReadOnlyCollection<OrderDetailUpdate> updates, OrderDetailSnapshot expected, out string message)
    {
        using var db = new SalesManagementContext();
        return UpdateOrderCore(db, orderId, employeeId, customerId, officeId, assignedEmployeeId, orderDate, deliveryDate,
            stateFlag, managementFlag, hiddenReason, customerContact, updates, expected, out message);
    }

    internal static bool UpdateOrderCore(SalesManagementContext db, int orderId, int employeeId, int customerId, int officeId,
        int assignedEmployeeId, DateTime orderDate, DateTime? deliveryDate, int stateFlag, int managementFlag, string? hiddenReason,
        string? customerContact, IReadOnlyCollection<OrderDetailUpdate> updates, OrderDetailSnapshot expected, out string message)
    {
        message = "";
        if (!AppSession.CanManageOrders || AppSession.Employee?.EmId != employeeId)
        {
            message = "受注の更新は管理者または営業担当者のみ実行できます。";
            return false;
        }

        using var tx = db.Database.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            var order = db.TOrders
                .Include(x => x.TOrderDetails)
                .FirstOrDefault(x => x.OrId == orderId && x.OrFlag == 0 && (x.OrStateFlag ?? 0) == 0);
            if (order == null)
            {
                message = "未確定かつ表示中の受注だけを更新できます。";
                return false;
            }

            if (db.TChumons.Any(x => x.OrId == orderId))
            {
                message = "注文へ連携済みの受注は更新できません。";
                return false;
            }
            if (!AppSession.IsAdmin && order.SoId != AppSession.Employee!.SoId)
            {
                message = "所属営業所以外の受注は更新できません。";
                return false;
            }
            if (!OrderUpdateRules.Matches(expected, order))
            {
                message = "他の利用者がこの受注を変更しました。入力内容は保持されています。現在の内容を確認するため、受注詳細を開き直してください。";
                return false;
            }
            if (officeId != order.SoId || assignedEmployeeId != order.EmId || stateFlag != 0 || managementFlag != 0)
            {
                message = "受付営業所・受付担当者・受注状態は変更できません。";
                return false;
            }
            if (!db.MClients.Any(x => x.ClId == customerId && x.ClFlag == 0 && x.SoId == order.SoId)
                || !db.MSalesOffices.Any(x => x.SoId == order.SoId && x.SoFlag == 0)
                || !db.MEmployees.Any(x => x.EmId == order.EmId && x.EmFlag == 0 && x.SoId == order.SoId))
            {
                message = "顧客・営業所・担当者が有効か確認してください。";
                return false;
            }
            if (customerContact?.Trim().Length > OrderUpdateRules.RemarksMaxLength)
            {
                message = "備考は50文字以内で入力してください。";
                return false;
            }

            var before = OrderHistoryService.Capture(db, order);
            var existingDetails = order.TOrderDetails.Where(x => !OrderUpdateRules.IsRemovedLine(x))
                .ToDictionary(x => x.OrDetailId);
            var submittedIds = updates.Where(x => x.DetailId.HasValue).Select(x => x.DetailId!.Value).ToList();
            if (submittedIds.Distinct().Count() != submittedIds.Count
                || submittedIds.Any(id => !existingDetails.ContainsKey(id))
                || updates.Any(x => x.DetailId.HasValue && existingDetails[x.DetailId.Value].PrId != x.ProductId)
                || updates.Where(x => !x.DetailId.HasValue).GroupBy(x => x.ProductId).Any(group => group.Count() > 1)
                || updates.Any(x => !x.DetailId.HasValue && updates.Any(other => other.DetailId.HasValue && other.ProductId == x.ProductId)))
            {
                message = "保存済み明細の商品差し替えや重複した商品の追加はできません。商品を削除するか数量を変更してください。";
                return false;
            }
            var submittedProductIds = updates.Select(x => x.ProductId).Distinct().ToList();
            var productPrices = db.MProducts.Where(x => submittedProductIds.Contains(x.PrId) && x.PrFlag == 0)
                .ToDictionary(x => x.PrId, x => x.Price);
            if (productPrices.Count != submittedProductIds.Count)
            {
                message = "無効または未登録の商品を含むため更新できません。";
                return false;
            }
            // Match the existing detail display, using stored data rather than a client-supplied price or snapshot price.
            var unitPrices = existingDetails.Values.Where(x => submittedIds.Contains(x.OrDetailId)).ToDictionary(x => x.OrDetailId,
                x => x.OrQuantity == 0 ? productPrices[x.PrId] : decimal.Round(x.OrTotalPrice / x.OrQuantity, 0));
            decimal Price(OrderDetailUpdate input) => input.DetailId.HasValue ? unitPrices[input.DetailId.Value] : productPrices[input.ProductId];
            if (updates.Any(x => x.UnitPrice != Price(x)))
            {
                message = "受注更新では単価を変更できません。追加商品の現在の単価も確認してください。";
                return false;
            }
            if (updates.Count == 0 || updates.Any(x => !OrderUpdateRules.TrySubtotal(x.Quantity, Price(x), out _)))
            {
                message = "商品明細を1行以上指定し、数量は1以上、行合計は9,999,999,999円以内で入力してください。";
                return false;
            }

            var normalizedRemarks = string.IsNullOrWhiteSpace(customerContact) ? null : customerContact.Trim();
            if (updates.Count == existingDetails.Count && customerId == order.ClId && orderDate.Date == order.OrDate.Date && deliveryDate?.Date == order.OrDeliveryDate?.Date
                && normalizedRemarks == order.ClCharge && updates.All(input => input.DetailId.HasValue
                    && input.Quantity == existingDetails[input.DetailId.Value].OrQuantity))
            {
                message = "変更はありません。";
                return false;
            }
            order.ClId = customerId;
            order.OrDate = orderDate.Date;
            order.OrDeliveryDate = deliveryDate?.Date;
            order.ClCharge = normalizedRemarks;
            var keptIds = submittedIds.ToHashSet();
            foreach (var detail in existingDetails.Values.Where(detail => !keptIds.Contains(detail.OrDetailId)))
            {
                // The existing schema has no detail deletion flag. Keep the row as an inactive audit tombstone.
                detail.OrQuantity = 0;
                detail.OrTotalPrice = 0m;
            }
            foreach (var input in updates)
            {
                if (!input.DetailId.HasValue)
                {
                    db.TOrderDetails.Add(new TOrderDetail { Or = order, PrId = input.ProductId, OrQuantity = input.Quantity,
                        OrTotalPrice = decimal.Round(Price(input) * input.Quantity, 0, MidpointRounding.AwayFromZero) });
                    continue;
                }
                var detail = existingDetails[input.DetailId!.Value];
                // Do not reprice unchanged legacy lines when only remarks or another line was edited.
                if (input.Quantity == detail.OrQuantity) continue;
                detail.OrQuantity = input.Quantity;
                detail.OrTotalPrice = decimal.Round(unitPrices[detail.OrDetailId] * input.Quantity, 0, MidpointRounding.AwayFromZero);
            }
            OrderHistoryService.Record(db, order, OrderHistoryOperations.Updated, before,
                "顧客・日付・備考・数量・追加商品・受注明細を更新しました。在庫数量の変更はありません。");
            db.SaveChanges();
            tx.Commit();
            HistoryNotificationService.RecordNewManagementEdit();
            message = "受注を更新しました。";
            return true;
        }
        catch (Exception ex)
        {
            tx.Rollback();
            message = "受注詳細の更新に失敗しました: " + ex.Message;
            return false;
        }
    }

    public static bool DeleteOrder(int orderId, int employeeId, out string message)
    {
        message = "受注の物理削除はできません。理由を入力して非表示にしてください。";
        return false;
    }

    public static bool ConfirmChumon(int id, int employeeId, out string message, out int? syukkoId)
    {
        using var db = new SalesManagementContext();
        return ConfirmChumonCore(db, id, employeeId, out message, out syukkoId);
    }

    internal static bool ConfirmChumonCore(SalesManagementContext db, int id, int employeeId, out string message, out int? syukkoId)
    {
        syukkoId = null;
        if (!AppSession.CanManageOrders || AppSession.Employee?.EmId != employeeId)
        { message = "注文を確定する権限がありません。"; return false; }

        using var tx = db.Database.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            var order = db.TChumons.Include(x => x.TChumonDetails)
                .FirstOrDefault(x => x.ChId == id && x.ChFlag == 0);
            if (order == null) { message = "対象の注文は通常表示ではないか、存在しません。"; return false; }
            if ((order.ChStateFlag ?? 0) != 0) { message = "すでに確定済みの注文です。"; return false; }
            if (!AppSession.IsAdmin && order.SoId != AppSession.Employee!.SoId)
            { message = "所属営業所以外の注文は確定できません。"; return false; }
            if (order.TChumonDetails.Count == 0 || order.TChumonDetails.Any(x => x.ChQuantity <= 0))
            { message = "注文に有効な明細がありません。"; return false; }
            if (db.TSyukkos.Any(x => x.OrId == order.OrId))
            { message = "この注文に対応する出庫情報がすでに存在します。"; return false; }

            // Aggregate repeated product rows so the stock check and decrement use the full order quantity.
            var quantities = order.TChumonDetails.GroupBy(x => x.PrId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.ChQuantity));
            var stocks = db.TStocks.Where(x => x.StFlag == 0 && quantities.Keys.Contains(x.PrId)).ToList();
            if (stocks.GroupBy(x => x.PrId).Any(g => g.Count() != 1))
            { message = "商品在庫の登録状態を確認してください。"; return false; }
            foreach (var (productId, quantity) in quantities)
            {
                var stock = stocks.SingleOrDefault(x => x.PrId == productId);
                if (stock == null || stock.StQuantity < quantity)
                { message = "在庫が不足しているため確定できません。"; return false; }
                stock.StQuantity -= quantity;
            }

            var outbound = new TSyukko
            {
                ClId = order.ClId, SoId = order.SoId, OrId = order.OrId,
                SyStateFlag = 0, SyFlag = 0
            };
            db.TSyukkos.Add(outbound);
            db.SaveChanges();
            foreach (var (productId, quantity) in quantities)
                db.TSyukkoDetails.Add(new TSyukkoDetail { SyId = outbound.SyId, PrId = productId, SyQuantity = quantity });
            order.ChStateFlag = 1;
            order.EmId = employeeId;
            db.SaveChanges();
            tx.Commit();
            syukkoId = outbound.SyId;
            message = "注文を確定し、在庫を更新して出庫情報を作成しました。";
            return true;
        }
        catch (Exception)
        {
            tx.Rollback();
            message = "注文確定に失敗しました。データベースの状態を確認して再度お試しください。";
            return false;
        }
    }

    public static bool HideChumon(int id, int employeeId, string? reason, out string message)
    {
        using var db = new SalesManagementContext();
        return HideChumonCore(db, id, employeeId, reason, out message);
    }

    internal static bool HideChumonCore(SalesManagementContext db, int id, int employeeId, string? reason, out string message)
    {
        message = "";
        if (!AppSession.CanManageOrders || AppSession.Employee?.EmId != employeeId)
        { message = "注文を非表示にする権限がありません。"; return false; }
        using var tx = db.Database.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            var order = db.TChumons.FirstOrDefault(x => x.ChId == id && x.ChFlag == 0);
            if (order == null) { message = "対象の注文は通常表示ではないか、存在しません。"; return false; }
            if (!AppSession.IsAdmin && order.SoId != AppSession.Employee!.SoId)
            { message = "所属営業所以外の注文は非表示にできません。"; return false; }
            order.ChFlag = 2;
            order.ChHidden = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            db.SaveChanges();
            tx.Commit();
            message = "注文を非表示にしました。";
            return true;
        }
        catch (Exception)
        {
            tx.Rollback();
            message = "注文の非表示に失敗しました。データベースの状態を確認してください。";
            return false;
        }
    }

    public static bool ConfirmSyukko(int id, int employeeId, out string message)
    {
        var result = OutboundService.Confirm(employeeId, id);
        message = result.Message;
        return result.Succeeded;
    }

    public static bool ConfirmArrival(int id, int employeeId, out string message)
    {
        var result = ArrivalService.Confirm(employeeId, id);
        message = result.Message;
        return result.Succeeded;
    }

    public static bool ConfirmShipment(int id, int employeeId, out string message)
    {
        var result = ShipmentService.Confirm(employeeId, id);
        message = result.Message;
        return result.Succeeded;
    }
}

public sealed record OrderDetailUpdate(int? DetailId, int ProductId, int Quantity, decimal UnitPrice);
