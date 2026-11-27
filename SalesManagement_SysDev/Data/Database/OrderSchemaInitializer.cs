using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

internal static class OrderSchemaInitializer
{
    public static void EnsureReady(SalesManagementContext db)
    {
        using var transaction = db.Database.BeginTransaction();
        db.Database.ExecuteSqlRaw("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = N'SalesManagement.OrderSchema.Setup',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
            IF @result < 0 THROW 51000, N'受注の日付項目の準備に失敗しました。', 1;
            IF COL_LENGTH(N'dbo.T_Order', N'OrDeliveryDate') IS NULL
                ALTER TABLE dbo.T_Order ADD OrDeliveryDate date NULL;
            """);
        transaction.Commit();
    }
}
