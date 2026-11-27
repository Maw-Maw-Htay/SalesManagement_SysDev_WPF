using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public static partial class InventoryService
{
    private const int ActiveStockFlag = 0;
    private const int HiddenStockFlag = 2;

    public static bool CanRestore => CanView && AppSession.IsAdmin;

    public sealed record HiddenStock(int Id, int ProductId, string ProductName, int Quantity)
    {
        public string Label => $"P-{ProductId:0000}  {ProductName}  在庫 {Quantity:N0}";
    }

    public static IReadOnlyList<HiddenStock> LoadHiddenStocks()
    {
        using var db = new SalesManagementContext();
        EnsureCanRestore(db);
        return db.TStocks.AsNoTracking().Where(x => x.StFlag == HiddenStockFlag)
            .OrderBy(x => x.PrId).ThenBy(x => x.StId)
            .Select(x => new HiddenStock(x.StId, x.PrId, x.Pr.PrName, x.StQuantity)).ToList();
    }

    public static (bool Succeeded, string Message) Restore(HiddenStock expected)
    {
        if (!CanRestore) return (false, "在庫の復元は管理者のみ実行できます。");
        try
        {
            using var db = new SalesManagementContext();
            using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
            EnsureCanRestore(db);

            var rows = db.TStocks.FromSqlInterpolated(
                $"SELECT * FROM dbo.T_Stock WITH (UPDLOCK, HOLDLOCK) WHERE PrID = {expected.ProductId}").ToList();
            var stock = rows.SingleOrDefault(x => x.StId == expected.Id && x.StFlag == HiddenStockFlag);
            if (stock == null || stock.StQuantity != expected.Quantity)
                return (false, "在庫の状態が変わりました。非表示在庫を開き直してください。");
            if (stock.StQuantity < 0 || rows.Any(x => x.StFlag == ActiveStockFlag)
                || !db.MProducts.Any(x => x.PrId == stock.PrId && x.PrFlag == ProductService.ActiveFlag))
                return (false, "有効な在庫の重複、商品状態、または在庫数量を確認してください。");

            stock.StFlag = ActiveStockFlag;
            db.SaveChanges();
            transaction.Commit();
            return (true, "在庫を表示に戻しました。");
        }
        catch (UnauthorizedAccessException error)
        {
            return (false, error.Message);
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("在庫の復元に失敗しました: {0}", error);
            return (false, "在庫を復元できませんでした。");
        }
    }

    private static void EnsureCanRestore(SalesManagementContext db)
    {
        if (!CanRestore || AppSession.Employee is not { } session
            || WarehouseActorResolver.Resolve(db, session.EmId) is not { IsAdministrator: true })
            throw new UnauthorizedAccessException("在庫の復元は管理者のみ実行できます。");
    }

    public static bool CanMaintain(InventoryRowData row) =>
        CanView && row.StockId.HasValue && row.Quantity is >= 0;

    public static (bool Succeeded, string Message) UpdateQuantity(InventoryRowData expected, int quantity) =>
        quantity < 0 ? (false, "在庫数は0以上の整数で入力してください。") : SaveStock(expected, quantity);

    public const int MaximumHideReasonLength = 500;

    public static (bool Succeeded, string Message) Hide(InventoryRowData expected, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return (false, "在庫の非表示理由を入力してください。");
        var trimmed = reason.Trim();
        return trimmed.Length > MaximumHideReasonLength
            ? (false, $"非表示理由は{MaximumHideReasonLength}文字以内で入力してください。")
            : SaveStock(expected, null, trimmed);
    }

    private static (bool Succeeded, string Message) SaveStock(InventoryRowData expected, int? quantity, string? hideReason = null)
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
            else
            {
                try
                {
                    db.Database.ExecuteSqlInterpolated($"""
                        INSERT dbo.T_StockHideHistory (StID, PrID, EmID, Reason)
                        VALUES ({stock.StId}, {stock.PrId}, {session.EmId}, {hideReason})
                        """);
                }
                catch (SqlException error) when (error.Number is 208 or 229)
                {
                    System.Diagnostics.Trace.TraceError("在庫の非表示履歴を保存できませんでした: {0}", error);
                    return (false, "在庫の非表示履歴を保存できません。DB管理者に履歴テーブルの準備と書き込み権限の確認を依頼してください。");
                }
                stock.StFlag = HiddenStockFlag;
            }

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
