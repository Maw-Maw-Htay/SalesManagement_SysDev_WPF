using System.Data;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public static partial class InventoryService
{
    private const int ActiveStockFlag = 0;
    private const int HiddenStockFlag = 2;

    public static bool CanMaintain(InventoryRowData row) =>
        CanView && row.StockId.HasValue && row.Quantity is >= 0;

    public static (bool Succeeded, string Message) UpdateQuantity(InventoryRowData expected, int quantity) =>
        quantity < 0 ? (false, "在庫数は0以上の整数で入力してください。") : SaveStock(expected, quantity);

    public static (bool Succeeded, string Message) Hide(InventoryRowData expected) =>
        SaveStock(expected, null);

    private static (bool Succeeded, string Message) SaveStock(InventoryRowData expected, int? quantity)
    {
        if (!CanMaintain(expected) || AppSession.Employee is not { } session)
            return (false, "変更できる在庫を選択してください。");

        try
        {
            using var db = new SalesManagementContext();
            using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
            if (WarehouseActorResolver.Resolve(db, session.EmId) is null)
                return (false, "在庫を変更する権限がありません。");

            var active = db.TStocks.FromSqlInterpolated(
                    $"SELECT * FROM dbo.T_Stock WITH (UPDLOCK, HOLDLOCK) WHERE PrID = {expected.ProductId}")
                .Where(x => x.StFlag == ActiveStockFlag && x.Pr.PrFlag == ProductService.ActiveFlag)
                .Take(2).ToList();
            if (active.Count != 1 || active[0].StId != expected.StockId || active[0].StQuantity != expected.Quantity)
                return (false, "在庫が変更されました。一覧を再読込してからお試しください。");

            var stock = active[0];
            if (quantity.HasValue) stock.StQuantity = quantity.Value;
            else stock.StFlag = HiddenStockFlag;

            db.SaveChanges();
            transaction.Commit();
            return (true, quantity.HasValue ? "在庫数を更新しました。" : "在庫を非表示にしました。");
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("在庫の変更に失敗しました: {0}", error);
            return (false, "在庫を変更できませんでした。一覧を再読込してからお試しください。");
        }
    }
}
