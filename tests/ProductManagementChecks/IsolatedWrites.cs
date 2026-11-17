using System.IO;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SalesManagement_SysDev;

internal static partial class Program
{
    private static void CheckIsolatedWrites()
    {
        // Never route this opt-in check to the application's existing catalog.
        var catalog = "ProductManagementFixture_" + Guid.NewGuid().ToString("N");
        var folder = Path.GetFullPath(AppContext.BaseDirectory);
        var mdf = Path.Combine(folder, catalog + ".mdf").Replace("'", "''");
        var ldf = Path.Combine(folder, catalog + "_log.ldf").Replace("'", "''");
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = @"(LocalDB)\MSSQLLocalDB", InitialCatalog = "master", IntegratedSecurity = true,
            Pooling = false, TrustServerCertificate = true
        };
        using var master = new SqlConnection(builder.ConnectionString);
        master.Open();
        Execute(master, $"CREATE DATABASE [{catalog}] ON (NAME=N'{catalog}', FILENAME=N'{mdf}') LOG ON (NAME=N'{catalog}_log', FILENAME=N'{ldf}')");
        try
        {
            builder.InitialCatalog = catalog;
            var fixtureString = builder.ConnectionString;
            using var fixture = new SqlConnection(fixtureString);
            fixture.Open();
            RequireFixture(fixture, catalog);
            Execute(fixture, """
                CREATE TABLE dbo.M_Position (PoID int PRIMARY KEY, PoName nvarchar(30) NOT NULL, PoFlag int NOT NULL);
                CREATE TABLE dbo.M_Employee (EmID int PRIMARY KEY, SoID int NOT NULL, PoID int NOT NULL, EmFlag int NOT NULL);
                CREATE TABLE dbo.M_Maker (MaID int PRIMARY KEY, MaFlag int NOT NULL);
                CREATE TABLE dbo.M_MajorClassification (McID int PRIMARY KEY, McFlag int NOT NULL);
                CREATE TABLE dbo.M_SmallClassification (ScID int PRIMARY KEY, ScFlag int NOT NULL, McID int NOT NULL REFERENCES dbo.M_MajorClassification(McID));
                CREATE TABLE dbo.M_Product (
                    PrID int IDENTITY PRIMARY KEY, MaID int NOT NULL REFERENCES dbo.M_Maker(MaID), PrName nvarchar(50) NOT NULL,
                    Price decimal(9,0) NOT NULL, PrJCode nvarchar(13) NULL, PrSafetyStock int NOT NULL,
                    ScID int NOT NULL REFERENCES dbo.M_SmallClassification(ScID), PrModelNumber nvarchar(20) NOT NULL,
                    PrColor nvarchar(20) NOT NULL, PrReleaseDate date NOT NULL, PrFlag int NOT NULL, PrHidden nvarchar(max) NULL
                );
                CREATE TABLE dbo.T_Stock (StID int IDENTITY PRIMARY KEY, PrID int NOT NULL REFERENCES dbo.M_Product(PrID), StQuantity int NOT NULL, StFlag int NOT NULL);
                CREATE TABLE dbo.T_Order (OrID int PRIMARY KEY, OrFlag int NOT NULL, OrStateFlag int NULL);
                CREATE TABLE dbo.T_OrderDetail (OrDetailID int PRIMARY KEY, OrID int NOT NULL REFERENCES dbo.T_Order(OrID),
                    PrID int NOT NULL REFERENCES dbo.M_Product(PrID), OrQuantity int NOT NULL, OrTotalPrice decimal(10,0) NOT NULL);
                CREATE TABLE dbo.T_ChumonDetail (DetailID int PRIMARY KEY, PrID int NOT NULL REFERENCES dbo.M_Product(PrID), Amount decimal(10,0));
                CREATE TABLE dbo.T_SaleDetail (DetailID int PRIMARY KEY, PrID int NOT NULL REFERENCES dbo.M_Product(PrID), Amount decimal(10,0));
                INSERT dbo.M_Position VALUES (1, N'管理者', 0);
                INSERT dbo.M_Employee VALUES (1, 4, 1, 0);
                INSERT dbo.M_Maker VALUES (1, 0);
                INSERT dbo.M_MajorClassification VALUES (1, 0);
                INSERT dbo.M_SmallClassification VALUES (1, 0, 1);
                INSERT dbo.M_Product (MaID, PrName, Price, PrJCode, PrSafetyStock, ScID, PrModelNumber, PrColor, PrReleaseDate, PrFlag)
                    VALUES (1,N'既存試験商品',128000,'4900000000001',10,1,N'TEST-1',N'黒','20250101',0),
                           (1,N'保留試験商品',6200,NULL,0,1,N'TEST-2',N'白','20250101',0);
                INSERT dbo.T_Stock (PrID,StQuantity,StFlag) VALUES (1,48,0),(2,15,0);
                INSERT dbo.T_Order VALUES (1,0,1),(2,0,0);
                INSERT dbo.T_OrderDetail VALUES (1,1,1,2,256000),(2,2,2,1,6200);
                INSERT dbo.T_ChumonDetail VALUES (1,1,256000);
                INSERT dbo.T_SaleDetail VALUES (1,1,256000);
                """);

            SalesManagementContext OpenFixture()
            {
                var db = new SalesManagementContext();
                try
                {
                    db.Database.SetConnectionString(fixtureString);
                    db.Database.OpenConnection();
                    RequireFixture((SqlConnection)db.Database.GetDbConnection(), catalog);
                    return db;
                }
                catch { db.Dispose(); throw; }
            }
            ProductOperationResult Invoke(string operation, params object?[] args)
            {
                using var db = OpenFixture();
                var method = typeof(ProductService).GetMethod(operation, BindingFlags.Static | BindingFlags.NonPublic)!;
                return (ProductOperationResult)method.Invoke(null, [db, .. args])!;
            }
            ProductRowData Read(int id)
            {
                using var db = OpenFixture();
                var product = db.MProducts.AsNoTracking().Single(x => x.PrId == id);
                return new(product.PrId, product.PrJcode, product.PrName, product.MaId, "", product.ScId, "",
                    product.PrModelNumber, product.PrColor, product.PrReleaseDate, product.Price, product.PrSafetyStock,
                    null, product.PrFlag, product.PrHidden);
            }

            AppSession.Employee = Employee("管理者");
            var input = new ProductInput("4900000000003", "新しい試験商品", 1, 1, "TEST-3", "青", new DateTime(2025, 1, 1), 3000, 8);
            var created = Invoke("Create", 1, input);
            Check(created.Succeeded && created.ProductId is > 2 && Scalar(fixture,
                $"SELECT SUM(StQuantity) FROM dbo.T_Stock WHERE PrID={created.ProductId}") == 0, "registration assigns a new ID with a zero balance");
            Check(!Invoke("Create", 1, input).Succeeded && Scalar(fixture, "SELECT COUNT(*) FROM dbo.M_Product") == 3,
                "duplicate visible JAN is rejected without a second insertion");

            var original = Read(1);
            var snapshot = ProductService.Snapshot(original);
            var priceUpdate = new ProductInput(original.JanCode, original.Name, original.MakerId, original.ClassificationId,
                original.ModelNumber, original.Color, original.ReleaseDate, 148_000, original.SafetyStock);
            Check(Invoke("Update", 1, snapshot, priceUpdate).Succeeded && Read(1).Price == 148_000m
                && Scalar(fixture, "SELECT PrID FROM dbo.T_OrderDetail WHERE OrDetailID=1") == 1,
                "updating the selling price keeps the original key and transaction relationship");
            Check(!Invoke("Update", 1, snapshot, priceUpdate with { Price = 198_000 }).Succeeded, "stale updates cannot overwrite the new price");
            Check(!Invoke("Hide", 1, 2, "試験").Succeeded && Read(2).ManagementFlag == 0,
                "a product used on an open order remains visible");
            Check(Invoke("Hide", 1, 1, "試験の非表示").Succeeded && Read(1).ManagementFlag == 1
                && !Invoke("Hide", 1, 1, "再操作").Succeeded, "hide writes the verified product flag only once");
            var hidden = Read(1);
            var replacement = Invoke("Create", 1, input with { JanCode = hidden.JanCode });
            Check(replacement.Succeeded && !Invoke("Restore", 1, hidden).Succeeded && Read(1).ManagementFlag == 1,
                "restore cannot introduce a duplicate active JAN");
            Check(Invoke("Hide", 1, replacement.ProductId!.Value, null).Succeeded && Invoke("Restore", 1, hidden).Succeeded
                && Read(1).ManagementFlag == 0 && Read(1).HiddenReason == null && !Invoke("Restore", 1, hidden).Succeeded,
                "restore only clears visibility and rejects a stale repeat");
            Check(Scalar(fixture, "SELECT StQuantity FROM dbo.T_Stock WHERE PrID=1") == 48
                && Scalar(fixture, "SELECT OrTotalPrice FROM dbo.T_OrderDetail WHERE OrDetailID=1") == 256_000
                && Scalar(fixture, "SELECT Amount FROM dbo.T_ChumonDetail WHERE DetailID=1") == 256_000
                && Scalar(fixture, "SELECT Amount FROM dbo.T_SaleDetail WHERE DetailID=1") == 256_000,
                "price updates, hide and restore leave order, sales and stock values unchanged");
            Execute(fixture, "UPDATE dbo.M_Employee SET EmFlag=2 WHERE EmID=1");
            Check(!Invoke("Hide", 1, 1, "無効なセッション").Succeeded && Read(1).ManagementFlag == 0,
                "write authorization rechecks the actor record at execution");
        }
        finally
        {
            AppSession.Employee = null;
            Execute(master, $"DROP DATABASE [{catalog}]");
        }
    }

    private static void RequireFixture(SqlConnection connection, string catalog)
    {
        if (!catalog.StartsWith("ProductManagementFixture_", StringComparison.Ordinal) || connection.Database != catalog)
            throw new InvalidOperationException("Write verification requires the disposable product catalog.");
    }

    private static void Execute(SqlConnection connection, string sql)
    {
        using var command = new SqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    private static int Scalar(SqlConnection connection, string sql)
    {
        using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
