using System.Data;
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

            if (hideReason != null) EnsureHideHistoryReady(db);

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
                stock.StFlag = HiddenStockFlag;
                db.Database.ExecuteSqlInterpolated($"""
                    INSERT dbo.T_StockHideHistory (StID, PrID, EmID, Reason)
                    VALUES ({stock.StId}, {stock.PrId}, {session.EmId}, {hideReason})
                    """);
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

    private static void EnsureHideHistoryReady(SalesManagementContext db)
    {
        db.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'dbo.T_StockHideHistory', N'U') IS NULL
            BEGIN
                DECLARE @lockResult int;
                EXEC @lockResult = sys.sp_getapplock
                    @Resource = N'SalesManagement.StockHideHistory.Schema',
                    @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
                IF @lockResult < 0
                    THROW 51000, 'Could not initialize stock hide history.', 1;
                IF OBJECT_ID(N'dbo.T_StockHideHistory', N'U') IS NULL
                    CREATE TABLE dbo.T_StockHideHistory
                    (
                        Id bigint IDENTITY(1,1) PRIMARY KEY,
                        StID int NOT NULL,
                        PrID int NOT NULL,
                        EmID int NOT NULL,
                        Reason nvarchar(500) NOT NULL,
                        RecordedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
                    );
            END
            """);
    }
}
