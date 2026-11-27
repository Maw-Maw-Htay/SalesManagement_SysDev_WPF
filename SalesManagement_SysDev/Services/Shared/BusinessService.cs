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

    public static bool UpdateOrder(int orderId, int employeeId, int customerId, int officeId, int assignedEmployeeId, DateTime orderDate, DateTime? deliveryDate, int stateFlag, int managementFlag, string? hiddenReason, string? customerContact, IReadOnlyCollection<OrderDetailUpdate> updates, OrderDetailSnapshot expected, out string message)
    {
        message = "保存済みの受注内容は変更できません。";
        return false;
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
            if (ResolveOrderActor(db, employeeId) is not { } actor)
            {
                message = "注文の確定権限を確認できません。再ログインしてください。";
                return false;
            }

            var order = db.TChumons.Include(x => x.TChumonDetails)
                .FirstOrDefault(x => x.ChId == id && x.ChFlag == 0);
            if (order == null) { message = "対象の注文は通常表示ではないか、存在しません。"; return false; }
            if ((order.ChStateFlag ?? 0) != 0) { message = "すでに確定済みの注文です。"; return false; }
            if (actor.Role != "管理者" && order.SoId != actor.SoId)
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

    internal static (int SoId, string Role)? ResolveOrderActor(SalesManagementContext db, int employeeId)
    {
        var session = AppSession.Employee;
        if (session == null || session.EmId != employeeId) return null;

        var actor = db.MEmployees.AsNoTracking()
            .Where(x => x.EmId == employeeId && x.EmFlag == 0 && x.Po.PoFlag == 0 && x.So.SoFlag == 0)
            .Select(x => new { x.SoId, Role = x.Po.PoName }).SingleOrDefault();
        if (actor == null || actor.SoId != session.SoId || actor.Role != session.Po?.PoName
            || actor.Role is not ("管理者" or "営業")) return null;

        return (actor.SoId, actor.Role);
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
            if (ResolveOrderActor(db, employeeId) is not { } actor)
            {
                message = "注文を非表示にする権限を確認できません。再ログインしてください。";
                return false;
            }

            var order = db.TChumons.FirstOrDefault(x => x.ChId == id && x.ChFlag == 0);
            if (order == null) { message = "対象の注文は通常表示ではないか、存在しません。"; return false; }
            if (actor.Role != "管理者" && order.SoId != actor.SoId)
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
