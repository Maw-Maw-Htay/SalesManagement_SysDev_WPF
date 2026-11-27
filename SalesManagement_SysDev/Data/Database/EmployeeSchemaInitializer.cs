using System.Data;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

/// <summary>Idempotent, additive setup for employee credential and assignment history data.</summary>
public static class EmployeeSchemaInitializer
{
    public static void EnsureReady(SalesManagementContext db)
    {
        using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
        db.Database.ExecuteSqlRaw("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = N'SalesManagement.EmployeeSchema.Setup',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
            IF @result < 0 THROW 51001, N'社員認証情報の準備に失敗しました。', 1;

            IF COL_LENGTH(N'dbo.M_Employee', N'EmPasswordHash') IS NULL
                ALTER TABLE dbo.M_Employee ADD EmPasswordHash nvarchar(255) NULL;

            IF OBJECT_ID(N'dbo.T_EmployeeAssignmentHistory', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.T_EmployeeAssignmentHistory (
                    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_T_EmployeeAssignmentHistory PRIMARY KEY,
                    EmployeeId int NOT NULL,
                    EmployeeName nvarchar(50) NOT NULL,
                    RecordedAt datetimeoffset NOT NULL,
                    ActorEmployeeId int NOT NULL,
                    ActorName nvarchar(50) NOT NULL,
                    BeforeOfficeId int NULL,
                    BeforeOfficeName nvarchar(50) NULL,
                    AfterOfficeId int NOT NULL,
                    AfterOfficeName nvarchar(50) NOT NULL,
                    BeforePositionId int NULL,
                    BeforeRoleName nvarchar(50) NULL,
                    AfterPositionId int NOT NULL,
                    AfterRoleName nvarchar(50) NOT NULL,
                    Operation nvarchar(24) NOT NULL
                );
                CREATE INDEX IX_T_EmployeeAssignmentHistory_EmployeeId_RecordedAt
                    ON dbo.T_EmployeeAssignmentHistory (EmployeeId, RecordedAt);
            END
            """);

        transaction.Commit();
    }
}
