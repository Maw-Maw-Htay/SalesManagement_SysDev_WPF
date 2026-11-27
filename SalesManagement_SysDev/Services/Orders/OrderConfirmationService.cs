using System.Data;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public sealed partial class BusinessService
{
    public static bool ConfirmOrder(int orderId, int employeeId, out string message)
    {
        if (!AppSession.CanManageOrders || AppSession.Employee?.EmId != employeeId)
        {
            message = "受注を確定する権限がありません。";
            return false;
        }
        using var db = new SalesManagementContext();
        return ConfirmOrderCore(db, orderId, employeeId, out message);
    }

    internal static bool ConfirmOrderCore(SalesManagementContext db, int orderId, int employeeId, out string message)
    {
        using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            var order = db.TOrders.Include(x => x.TOrderDetails).FirstOrDefault(x => x.OrId == orderId && x.OrFlag == 0);
            if (order == null || (order.OrStateFlag ?? 0) != 0)
            {
                message = "対象の受注は未確定・表示中ではないか、存在しません。";
                return false;
            }
            var actor = db.MEmployees.AsNoTracking()
                .Where(x => x.EmId == employeeId && x.EmFlag == 0 && x.Po.PoFlag == 0 && x.So.SoFlag == 0)
                .Select(x => new { x.SoId, x.Po.PoName }).SingleOrDefault();
            if (actor == null || AppSession.Employee?.EmId != employeeId || actor.SoId != AppSession.Employee.SoId
                || actor.PoName != AppSession.Role || actor.PoName is not ("管理者" or "営業"))
            {
                message = "受注の確定権限を確認できません。再ログインしてください。";
                return false;
            }
            if (actor.PoName != "管理者" && order.SoId != actor.SoId)
            {
                message = "所属営業所以外の受注は確定できません。";
                return false;
            }
            if (db.TChumons.Any(x => x.OrId == orderId))
            {
                message = "この受注には注文がすでに作成されています。重複確定はできません。";
                return false;
            }
            if (!db.MClients.Any(x => x.ClId == order.ClId && x.SoId == order.SoId && x.ClFlag == 0)
                || !db.MSalesOffices.Any(x => x.SoId == order.SoId && x.SoFlag == 0))
            {
                message = "顧客と営業所が有効か確認してください。";
                return false;
            }
            var activeDetails = order.TOrderDetails.Where(x => !OrderUpdateRules.IsRemovedLine(x)).ToList();
            if (activeDetails.Count == 0 || activeDetails.Any(x => x.OrQuantity <= 0 || x.OrTotalPrice < 0))
            {
                message = "受注明細の数量または金額を確認してください。";
                return false;
            }
            var productIds = activeDetails.Select(x => x.PrId).Distinct().ToList();
            if (db.MProducts.Count(x => productIds.Contains(x.PrId) && x.PrFlag == 0) != productIds.Count)
            {
                message = "無効または未登録の商品を含むため確定できません。";
                return false;
            }
            var before = OrderHistoryService.Capture(db, order);
            order.OrStateFlag = 1;
            var generatedOrder = new TChumon
            {
                SoId = order.SoId,
                ClId = order.ClId,
                OrId = order.OrId,
                EmId = null,
                ChDate = DateTime.Today,
                ChStateFlag = 0,
                ChFlag = 0
            };
            db.TChumons.Add(generatedOrder);
            db.SaveChanges();
            foreach (var detail in activeDetails)
            {
                db.TChumonDetails.Add(new TChumonDetail
                {
                    ChId = generatedOrder.ChId,
                    PrId = detail.PrId,
                    ChQuantity = detail.OrQuantity
                });
            }
            OrderHistoryService.Record(db, order, OrderHistoryOperations.Confirmed, before,
                $"注文 {generatedOrder.ChId} を作成しました。在庫数量の減算は注文確定時に実行され、この操作では変更していません。");
            db.SaveChanges();
            transaction.Commit();
            message = "受注を確定し、注文を作成しました。";
            return true;
        }
        catch (Exception exception)
        {
            transaction.Rollback();
            message = "確定に失敗しました: " + exception.Message;
            return false;
        }
    }
}
