using System.IO;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

internal static class StockHideHistorySchemaInitializer
{
    public static void EnsureLocalDbReady(SalesManagementContext db)
    {
        var needsSetup = db.Database.SqlQueryRaw<int>("""
            SELECT CASE WHEN SERVERPROPERTY('IsLocalDB') = 1 AND USER_NAME() = N'dbo'
                AND OBJECT_ID(N'dbo.T_StockHideHistory', N'U') IS NULL
                THEN 1 ELSE 0 END AS [Value]
            """).Single() == 1;
        if (!needsSetup) return;

        using var script = typeof(StockHideHistorySchemaInitializer).Assembly
            .GetManifestResourceStream("SalesManagement.PrepareStockHideHistory.sql")
            ?? throw new InvalidOperationException("在庫の非表示履歴を準備するSQLが見つかりません。");
        using var reader = new StreamReader(script);
        db.Database.ExecuteSqlRaw(reader.ReadToEnd());
    }
}
