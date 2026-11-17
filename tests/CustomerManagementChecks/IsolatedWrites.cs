using System.IO;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SalesManagement_SysDev;

internal static partial class Program
{
    private static void CheckIsolatedWrites()
    {
        // This mode never uses the application's SalesManagement catalog.
        var catalog = "CustomerManagementFixture_" + Guid.NewGuid().ToString("N");
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
            var fixtureConnectionString = builder.ConnectionString;
            using var fixture = new SqlConnection(fixtureConnectionString);
            fixture.Open();
            RequireFixture(fixture, catalog);
            Execute(fixture, """
                CREATE TABLE dbo.M_Position (PoID int PRIMARY KEY, PoName nvarchar(30) NOT NULL, PoFlag int NOT NULL);
                CREATE TABLE dbo.M_SalesOffice (SoID int PRIMARY KEY, SoName nvarchar(50) NOT NULL, SoFlag int NOT NULL);
                CREATE TABLE dbo.M_Employee (EmID int PRIMARY KEY, SoID int NOT NULL, PoID int NOT NULL, EmFlag int NOT NULL);
                CREATE TABLE dbo.M_Client (
                    ClID int IDENTITY PRIMARY KEY, SoID int NOT NULL REFERENCES dbo.M_SalesOffice(SoID),
                    ClName nvarchar(50) NOT NULL, ClPhone nvarchar(13) NOT NULL, ClPostal nvarchar(7) NOT NULL,
                    ClAddress nvarchar(50) NOT NULL, ClFAX nvarchar(13) NOT NULL, ClFlag int NOT NULL, ClHidden nvarchar(max) NULL
                );
                CREATE TABLE dbo.T_Order (OrID int PRIMARY KEY, ClID int NOT NULL REFERENCES dbo.M_Client(ClID), Amount decimal(10, 0));
                CREATE TABLE dbo.T_Stock (StID int PRIMARY KEY, Quantity int NOT NULL);
                INSERT dbo.M_Position VALUES (1, N'管理者', 0), (2, N'営業', 0);
                INSERT dbo.M_SalesOffice VALUES (4, N'試験営業所', 0), (5, N'第二試験営業所', 0);
                INSERT dbo.M_Employee VALUES (1, 4, 1, 0), (2, 4, 2, 0);
                INSERT dbo.M_Client (SoID, ClName, ClPhone, ClPostal, ClAddress, ClFAX, ClFlag, ClHidden)
                    VALUES (4, N'既存試験顧客', '06-1111-2222', '5300001', N'試験住所一丁目', '', 0, NULL),
                           (5, N'範囲外試験顧客', '06-3333-4444', '', '', '', 1, N'試験の非表示理由');
                INSERT dbo.T_Order VALUES (1, 1, 2500);
                INSERT dbo.T_Stock VALUES (1, 12);
                """);

            CustomerOperationResult Invoke(string method, params object[] args)
            {
                using var db = new SalesManagementContext();
                db.Database.SetConnectionString(fixtureConnectionString);
                db.Database.OpenConnection();
                RequireFixture((SqlConnection)db.Database.GetDbConnection(), catalog);
                var action = typeof(CustomerService).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!;
                return (CustomerOperationResult)action.Invoke(null, [db, .. args])!;
            }
            CustomerRecord Read(int id)
            {
                using var db = new SalesManagementContext();
                db.Database.SetConnectionString(fixtureConnectionString);
                db.Database.OpenConnection();
                RequireFixture((SqlConnection)db.Database.GetDbConnection(), catalog);
                var row = db.MClients.AsNoTracking().Single(x => x.ClId == id);
                return new CustomerRecord(row.ClId, row.SoId, "", row.ClName, row.ClPhone, row.ClPostal, row.ClAddress, row.ClFax, row.ClFlag, row.ClHidden);
            }

            AppSession.Employee = Employee("管理者", 4);
            var warning = Invoke("Create", 1, "新規試験顧客", "0611112222", "530-0001", "別の試験住所", "", 4, false);
            Check(!warning.Succeeded && warning.PossibleDuplicates == 1 && Scalar(fixture, "SELECT COUNT(*) FROM dbo.M_Client") == 2,
                "duplicate warning does not insert a record before explicit confirmation");
            var first = Invoke("Create", 1, "新規試験顧客", "0611112222", "530-0001", "別の試験住所", "", 4, true);
            var second = Invoke("Create", 1, "別の試験顧客", "06-5555-6666", "", "", "", 4, false);
            Check(first.Succeeded && second.Succeeded && first.CustomerId.HasValue && second.CustomerId.HasValue && first.CustomerId != second.CustomerId
                && Read(first.CustomerId.Value).PostalCode == "5300001", "identity registration uses unique IDs and stores a seven-digit postcode");

            var original = Read(1);
            var before = CustomerService.Snapshot(original);
            var edit = Invoke("Update", 1, before, "編集済み試験顧客", original.ContactPhone, original.PostalCode, original.Address, "", 4);
            Check(edit.Succeeded && edit.CustomerId == 1 && Read(1).Name == "編集済み試験顧客"
                && Scalar(fixture, "SELECT ClID FROM dbo.T_Order WHERE OrID=1") == 1, "editing preserves the customer ID and order foreign key");
            Check(!Invoke("Update", 1, before, "古い画面からの更新", original.ContactPhone, original.PostalCode, original.Address, "", 4).Succeeded,
                "stale edits cannot overwrite the current record");

            var hidden = Invoke("Hide", 1, 1, "試験の非表示理由");
            var hiddenRecord = Read(1);
            Check(hidden.Succeeded && hiddenRecord.ManagementFlag == 1 && hiddenRecord.HiddenReason == "試験の非表示理由"
                && !Invoke("Hide", 1, 1, "再実行").Succeeded, "hide writes the verified flag and rejects a repeat");
            var restored = Invoke("Restore", 1, hiddenRecord);
            Check(restored.Succeeded && Read(1).ManagementFlag == 0 && Read(1).HiddenReason == null
                && !Invoke("Restore", 1, hiddenRecord).Succeeded, "restore clears only visibility and rejects a stale repeat");
            Check(Scalar(fixture, "SELECT Amount FROM dbo.T_Order WHERE OrID=1") == 2500
                && Scalar(fixture, "SELECT Quantity FROM dbo.T_Stock WHERE StID=1") == 12,
                "customer visibility changes leave historical amounts and inventory unchanged");

            AppSession.Employee = Employee("営業", 4);
            AppSession.Employee.EmId = 2;
            Check(!Invoke("Restore", 2, Read(2)).Succeeded && Read(2).ManagementFlag == 1
                && !Invoke("Create", 2, "範囲外登録", "06-8888-9999", "", "", "", 5, true).Succeeded,
                "SQL write checks enforce sales office scope on restore and registration");
        }
        finally
        {
            AppSession.Employee = null;
            // Only the freshly generated fixture is disposed here.
            Execute(master, $"DROP DATABASE [{catalog}]");
        }
    }

    private static void RequireFixture(SqlConnection connection, string catalog)
    {
        if (!catalog.StartsWith("CustomerManagementFixture_", StringComparison.Ordinal) || connection.Database != catalog)
            throw new InvalidOperationException("Write verification requires the isolated fixture catalog.");
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
