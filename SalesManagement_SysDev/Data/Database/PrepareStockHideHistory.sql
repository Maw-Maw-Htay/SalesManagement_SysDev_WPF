IF OBJECT_ID(N'dbo.T_StockHideHistory', N'U') IS NULL
BEGIN
    BEGIN TRANSACTION;
    BEGIN TRY
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
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
