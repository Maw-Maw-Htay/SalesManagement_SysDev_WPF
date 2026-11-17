using System.IO;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SalesManagement_SysDev;

internal static partial class Program
{
    private static void CheckReceivingWrites()
    {
        var database = "ReceivingChecks_" + Guid.NewGuid().ToString("N");
        var output = Path.GetFullPath(AppContext.BaseDirectory);
        var dataPath = Path.Combine(output, database + ".mdf").Replace("'", "''");
        var logPath = Path.Combine(output, database + "_log.ldf").Replace("'", "''");
        var settings = new SqlConnectionStringBuilder
        {
            DataSource = @"(LocalDB)\MSSQLLocalDB", InitialCatalog = "master", IntegratedSecurity = true,
            TrustServerCertificate = true, Pooling = false
        };
        using var master = new SqlConnection(settings.ConnectionString);
        master.Open();
        Sql(master, $"CREATE DATABASE [{database}] ON (NAME=N'{database}', FILENAME=N'{dataPath}') LOG ON (NAME=N'{database}_log', FILENAME=N'{logPath}')");
        try
        {
            settings.InitialCatalog = database;
            var fixtureString = settings.ConnectionString;
            using var fixture = new SqlConnection(fixtureString);
            fixture.Open();
            RequireReceivingFixture(fixture, database);
            Sql(fixture, """
                CREATE TABLE dbo.M_Position (PoID int PRIMARY KEY, PoName nvarchar(50) NOT NULL, PoFlag int NOT NULL);
                CREATE TABLE dbo.M_Employee (EmID int PRIMARY KEY, PoID int NOT NULL, SoID int NOT NULL, EmFlag int NOT NULL);
                CREATE TABLE dbo.M_Maker (MaID int PRIMARY KEY, MaFlag int NOT NULL);
                CREATE TABLE dbo.M_Product (PrID int PRIMARY KEY, MaID int NOT NULL, ScID int NOT NULL, PrName nvarchar(50) NOT NULL,
                    Price decimal(9,0) NOT NULL, PrJCode nvarchar(13) NULL, PrSafetyStock int NOT NULL,
                    PrModelNumber nvarchar(20) NOT NULL, PrColor nvarchar(20) NOT NULL, PrReleaseDate date NOT NULL,
                    PrFlag int NOT NULL, PrHidden nvarchar(max) NULL);
                CREATE TABLE dbo.T_Stock (StID int IDENTITY PRIMARY KEY, PrID int NOT NULL REFERENCES dbo.M_Product(PrID),
                    StQuantity int NOT NULL, StFlag int NOT NULL);
                CREATE TABLE dbo.T_Hattyu (HaID int PRIMARY KEY, MaID int NOT NULL, EmID int NOT NULL, HaDate date NOT NULL,
                    WaWarehouseFlag int NULL, HaFlag int NOT NULL, HaHidden nvarchar(max) NULL);
                CREATE TABLE dbo.T_HattyuDetail (HaDetailID int PRIMARY KEY, HaID int NOT NULL REFERENCES dbo.T_Hattyu(HaID),
                    PrID int NOT NULL REFERENCES dbo.M_Product(PrID), HaQuantity int NOT NULL);
                CREATE TABLE dbo.T_Warehousing (WaID int PRIMARY KEY, HaID int NOT NULL REFERENCES dbo.T_Hattyu(HaID),
                    EmID int NOT NULL, WaDate date NOT NULL, WaShelfFlag int NULL, WaFlag int NOT NULL, WaHidden nvarchar(max) NULL);
                CREATE TABLE dbo.T_WarehousingDetail (WaDetailID int PRIMARY KEY, WaID int NOT NULL REFERENCES dbo.T_Warehousing(WaID),
                    PrID int NOT NULL REFERENCES dbo.M_Product(PrID), WaQuantity int NOT NULL);
                INSERT dbo.M_Position VALUES (1,N'管理者',0);
                INSERT dbo.M_Employee VALUES (1,1,4,0);
                INSERT dbo.M_Maker VALUES (1,0);
                INSERT dbo.M_Product VALUES (1,1,1,N'試験商品',10,NULL,5,'A',N'黒','20251001',0,NULL),
                    (2,1,1,N'別の試験商品',20,NULL,5,'B',N'白','20251001',0,NULL);
                INSERT dbo.T_Stock (PrID,StQuantity,StFlag) VALUES (1,12,0);
                INSERT dbo.T_Hattyu VALUES (101,1,1,'20251001',NULL,0,NULL);
                INSERT dbo.T_HattyuDetail VALUES (1,101,1,16),(2,101,2,4);
                INSERT dbo.T_Warehousing VALUES (1,101,1,'20251002',0,0,NULL),(2,101,1,'20251003',0,0,NULL),
                    (3,101,1,'20251004',0,2,NULL),(4,101,1,'20251005',1,2,NULL);
                INSERT dbo.T_WarehousingDetail VALUES (1,1,1,2),(2,1,1,3),(3,1,2,4),(4,2,1,6);
                """);

            AppSession.Employee = Employee("管理者");
            AppSession.Employee.EmId = 1;
            AppSession.Employee.SoId = 4;
            WarehouseMovementOperationResult Invoke(string operation, params object?[] args)
            {
                using var db = new SalesManagementContext();
                db.Database.SetConnectionString(fixtureString);
                db.Database.OpenConnection();
                RequireReceivingFixture((SqlConnection)db.Database.GetDbConnection(), database);
                var method = typeof(WarehousingService).GetMethod(operation, BindingFlags.NonPublic | BindingFlags.Static)!;
                return (WarehouseMovementOperationResult)method.Invoke(null, [db, .. args])!;
            }
            int Number(string query)
            {
                using var command = new SqlCommand(query, fixture);
                return Convert.ToInt32(command.ExecuteScalar());
            }
            int Stock() => Number("SELECT StQuantity FROM dbo.T_Stock WHERE PrID=1");
            WarehouseMovementOperationResult Confirm(int id, params WarehouseMovementLineInput[] lines) =>
                Invoke("ConfirmShelfInContext", 1, id, lines);
            var stale = Confirm(1, new(1, 4), new(1, 3), new(2, 2));
            Check(!stale.Succeeded && stale.Message.Contains("明細が更新") && Stock() == 12
                && Number("SELECT WaShelfFlag FROM dbo.T_Warehousing WHERE WaID=1") == 0,
                "changed receipt quantities are rejected before any stock changes");
            var result = Confirm(1, new(1, 2), new(1, 3), new(2, 4));
            Check(result.Succeeded && Stock() == 17 && Number("SELECT StQuantity FROM dbo.T_Stock WHERE PrID=2") == 4
                && Number("SELECT WaShelfFlag FROM dbo.T_Warehousing WHERE WaID=1") == 1,
                $"receipt confirmation increments each product once and creates only the missing balance: {result.Message}");
            Check(!Confirm(1, new(1, 2), new(1, 3), new(2, 4)).Succeeded && Stock() == 17,
                "repeating the same confirmation cannot increase stock again");
            var concurrent = Task.WhenAll(Task.Run(() => Confirm(2, new WarehouseMovementLineInput(1, 6))),
                Task.Run(() => Confirm(2, new WarehouseMovementLineInput(1, 6)))).GetAwaiter().GetResult();
            Check(concurrent.Count(x => x.Succeeded) == 1 && Stock() == 23, "two competing confirmations result in a single stock increase");
            Check(Number("SELECT COUNT(*) FROM dbo.T_Warehousing") == 4 && Number("SELECT COUNT(*) FROM dbo.T_Hattyu") == 1,
                "confirmation does not create another receipt or purchase");

            var hide = Invoke("HideInContext", 1, 1, "試験の非表示");
            Check(hide.Succeeded && Stock() == 23 && Number("SELECT WaFlag FROM dbo.T_Warehousing WHERE WaID=1") == 2,
                "hide changes visibility without touching stock");
            var hidden = Receipt(1, 1, 2, "試験の非表示");
            var restore = Invoke("RestoreInContext", 1, hidden);
            Check(restore.Succeeded && Stock() == 23 && Number("SELECT WaFlag FROM dbo.T_Warehousing WHERE WaID=1") == 0
                && Number("SELECT WaShelfFlag FROM dbo.T_Warehousing WHERE WaID=1") == 1
                && Number("SELECT HaID FROM dbo.T_Warehousing WHERE WaID=1") == 101
                && Number("SELECT COUNT(*) FROM dbo.T_Warehousing WHERE WaID=1 AND WaHidden=N'試験の非表示'") == 1,
                "restore changes only the visibility flag, preserving state, relation, reason and stock");
            Check(!Invoke("RestoreInContext", 1, hidden).Succeeded && !Invoke("RestoreInContext", 1, Receipt(4, 1, 2)).Succeeded
                && !Invoke("RestoreInContext", 1, Receipt(3, 0, 2)).Succeeded && Stock() == 23,
                "repeat, legacy reasonless and unconfirmed restore attempts are rejected");
            Sql(fixture, "UPDATE dbo.M_Employee SET EmFlag=2 WHERE EmID=1");
            Check(!Invoke("HideInContext", 1, 1, "無効なセッション").Succeeded && Stock() == 23,
                "mutation rechecks the current actor record");
        }
        finally
        {
            AppSession.Employee = null;
            Sql(master, $"DROP DATABASE [{database}]");
        }
    }

    private static void RequireReceivingFixture(SqlConnection connection, string name)
    {
        if (!name.StartsWith("ReceivingChecks_", StringComparison.Ordinal) || connection.Database != name)
            throw new InvalidOperationException("The test connection is outside the receiving fixture.");
    }

    private static void Sql(SqlConnection connection, string statement)
    {
        using var command = new SqlCommand(statement, connection);
        command.ExecuteNonQuery();
    }
}
