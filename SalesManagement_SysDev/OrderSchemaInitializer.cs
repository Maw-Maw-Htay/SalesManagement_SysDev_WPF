using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

internal static class OrderSchemaInitializer
{
    public static void EnsureReady(SalesManagementContext db)
    {
        using (var transaction = db.Database.BeginTransaction())
        {
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
        EnsureDraftsReady(db);
    }

    // The login health check is read-only. Existing databases add this table on first draft use.
    public static void EnsureDraftsReady(SalesManagementContext db) => db.Database.ExecuteSqlRaw("""
        IF OBJECT_ID(N'dbo.T_OrderDraft', N'U') IS NOT NULL RETURN;
        BEGIN TRANSACTION;
        BEGIN TRY
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = N'SalesManagement.OrderDraftSchema.Setup',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
            IF @result < 0 THROW 51000, N'受注の下書きテーブルの準備に失敗しました。', 1;
            IF OBJECT_ID(N'dbo.T_OrderDraft', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.T_OrderDraft (
                    DraftId uniqueidentifier NOT NULL CONSTRAINT PK_T_OrderDraft PRIMARY KEY,
                    EmployeeId int NOT NULL REFERENCES dbo.M_Employee (EmID),
                    ScopeOfficeId int NOT NULL REFERENCES dbo.M_SalesOffice (SoID),
                    UpdatedAt datetimeoffset NOT NULL,
                    FormJson nvarchar(max) NULL,
                    RegisteredOrderId int NULL REFERENCES dbo.T_Order (OrID),
                    Version rowversion NOT NULL,
                    CONSTRAINT CK_T_OrderDraft_Form CHECK (
                        FormJson IS NULL OR (RegisteredOrderId IS NULL AND ISJSON(FormJson) = 1))
                );
                CREATE INDEX IX_T_OrderDraft_EmployeeId_UpdatedAt ON dbo.T_OrderDraft (EmployeeId, UpdatedAt);
            END;
            COMMIT TRANSACTION;
        END TRY
        BEGIN CATCH
            IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
            THROW;
        END CATCH;
        """);
}
