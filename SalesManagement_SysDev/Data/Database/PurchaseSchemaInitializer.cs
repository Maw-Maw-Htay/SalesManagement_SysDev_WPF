using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

internal static class PurchaseSchemaInitializer
{
    public static void EnsureReady(SalesManagementContext db)
    {
        using var transaction = db.Database.BeginTransaction();
        db.Database.ExecuteSqlRaw("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = N'SalesManagement.PurchaseSchema.Setup',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
            IF @result < 0 THROW 51000, N'発注の営業所情報を準備できませんでした。', 1;
            IF COL_LENGTH(N'dbo.T_Hattyu', N'RegisteredOfficeId') IS NULL
                ALTER TABLE dbo.T_Hattyu ADD RegisteredOfficeId int NULL;
            """);
        transaction.Commit();
    }
}
