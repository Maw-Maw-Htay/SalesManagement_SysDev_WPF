using System.Diagnostics;
using System.IO;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SalesManagement_SysDev;

internal static partial class Program
{
    private static void BenchmarkIsolatedSalesDirectory()
    {
        const string setting = "SALESMANAGEMENT_CONNECTION_STRING";
        var original = Environment.GetEnvironmentVariable(setting);
        var catalog = "SalesDirectoryFixture_" + Guid.NewGuid().ToString("N");
        var dataPath = Path.Combine(AppContext.BaseDirectory, catalog + ".mdf").Replace("'", "''");
        var logPath = Path.Combine(AppContext.BaseDirectory, catalog + "_log.ldf").Replace("'", "''");
        var testServer = Environment.GetEnvironmentVariable("SALES_DIRECTORY_TEST_SERVER");
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = string.IsNullOrWhiteSpace(testServer) ? @"(LocalDB)\MSSQLLocalDB" : testServer,
            InitialCatalog = "master", IntegratedSecurity = true,
            Pooling = false, TrustServerCertificate = true
        };
        using var master = new SqlConnection(builder.ConnectionString);
        master.Open();
        Execute(master, $"CREATE DATABASE [{catalog}] ON (NAME=N'{catalog}', FILENAME=N'{dataPath}') LOG ON (NAME=N'{catalog}_log', FILENAME=N'{logPath}')");
        builder.InitialCatalog = catalog;
        builder.Pooling = true;
        using var fixture = new SqlConnection(builder.ConnectionString);
        try
        {
            fixture.Open();
            Seed(fixture);
            Environment.SetEnvironmentVariable(setting, builder.ConnectionString);
            using var observer = new SalesCommandObserver(catalog);
            using var listeners = DiagnosticListener.AllListeners.Subscribe(observer);
            Measure("管理者", 1, 2048, 2, SummaryFromSql(fixture, null), observer);
            Measure("営業", 2, 1024, 1, SummaryFromSql(fixture, 5), observer);
        }
        finally
        {
            Environment.SetEnvironmentVariable(setting, original);
            SqlConnection.ClearPool(fixture);
            fixture.Close();
            Execute(master, $"DROP DATABASE [{catalog}]");
        }
    }

    private static void Seed(SqlConnection fixture)
    {
        using var command = fixture.CreateCommand();
        command.Parameters.Add("@month", System.Data.SqlDbType.Date).Value = MonthStart();
        command.CommandText = """
            CREATE TABLE dbo.M_Position (PoID int PRIMARY KEY, PoName nvarchar(50) NOT NULL, PoFlag int NOT NULL);
            CREATE TABLE dbo.M_SalesOffice (SoID int PRIMARY KEY, SoName nvarchar(50) NOT NULL);
            CREATE TABLE dbo.M_Employee (EmID int PRIMARY KEY, EmName nvarchar(50) NOT NULL, EmFlag int NOT NULL,
                PoID int NOT NULL REFERENCES dbo.M_Position(PoID), SoID int NOT NULL REFERENCES dbo.M_SalesOffice(SoID));
            CREATE TABLE dbo.M_Client (ClID int PRIMARY KEY, ClName nvarchar(50) NOT NULL);
            CREATE TABLE dbo.T_Sale (SaID int PRIMARY KEY, SaDate date NOT NULL, OrID int NOT NULL,
                ClID int NOT NULL REFERENCES dbo.M_Client(ClID), EmID int NOT NULL REFERENCES dbo.M_Employee(EmID),
                SoID int NOT NULL REFERENCES dbo.M_SalesOffice(SoID), SaFlag int NOT NULL, SaHidden nvarchar(max) NULL);
            CREATE TABLE dbo.T_SaleDetail (SaDetailID int PRIMARY KEY, SaID int NOT NULL REFERENCES dbo.T_Sale(SaID),
                SaPrTotalPrice decimal(10, 0) NOT NULL);
            CREATE INDEX IX_T_SaleDetail_SaID ON dbo.T_SaleDetail(SaID);
            INSERT dbo.M_Position VALUES (1, N'管理者', 0), (2, N'営業', 0);
            INSERT dbo.M_SalesOffice VALUES (5, N'試験営業所'), (6, N'第二試験営業所');
            INSERT dbo.M_Employee VALUES (1, N'管理者 試験', 0, 1, 6), (2, N'営業 試験', 0, 2, 5);
            INSERT dbo.M_Client VALUES (1, N'試験顧客');

            WITH Digits(n) AS (SELECT * FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) d(n)),
            Numbers(n) AS (SELECT 1000*a.n + 100*b.n + 10*c.n + d.n + 1 FROM Digits a CROSS JOIN Digits b CROSS JOIN Digits c CROSS JOIN Digits d)
            INSERT dbo.T_Sale
            SELECT n, DATEADD(day, n % 30 - 10, @month), n, 1, n % 2 + 1, 6 - n % 2,
                CASE WHEN n % 11 = 0 THEN 1 WHEN n % 7 = 0 THEN 2 ELSE 0 END, NULL
            FROM Numbers WHERE n <= 2048;
            INSERT dbo.T_SaleDetail
            SELECT 2 * s.SaID + d.n, s.SaID, (s.SaID % 1000 + 50) * d.n
            FROM dbo.T_Sale s CROSS JOIN (VALUES (1), (2)) d(n) WHERE s.SaID % 13 <> 0;
            """;
        command.ExecuteNonQuery();
    }

    private static SalesSummary SummaryFromSql(SqlConnection fixture, int? officeId)
    {
        using var command = fixture.CreateCommand();
        command.Parameters.Add("@month", System.Data.SqlDbType.Date).Value = MonthStart();
        command.Parameters.Add("@office", System.Data.SqlDbType.Int).Value = (object?)officeId ?? DBNull.Value;
        command.CommandText = """
            WITH Visible AS (
                SELECT SaID FROM dbo.T_Sale
                WHERE SaDate >= @month AND SaDate < DATEADD(month, 1, @month)
                    AND SaFlag = 0 AND (@office IS NULL OR SoID = @office))
            SELECT (SELECT COUNT(*) FROM Visible), COALESCE(SUM(d.SaPrTotalPrice), 0)
            FROM Visible v JOIN dbo.T_SaleDetail d ON d.SaID = v.SaID;
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("Could not calculate the expected sales summary.");
        return new(reader.GetInt32(0), reader.GetDecimal(1));
    }

    private static void Measure(string role, int employeeId, int rowCount, int officeCount,
        SalesSummary expected, SalesCommandObserver observer)
    {
        AppSession.Employee = Employee(role);
        AppSession.Employee.EmId = employeeId;
        for (var i = 0; i < 3; i++) SalesService.Load();

        var samples = new double[21];
        var commands = new int[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            var before = observer.SelectCount;
            var start = Stopwatch.GetTimestamp();
            var directory = SalesService.Load();
            samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            commands[i] = observer.SelectCount - before;
            if (directory.MonthlyVisibleSummary != expected || directory.Sales.Count != rowCount
                || directory.Offices.Count != officeCount || directory.Employees.Count != officeCount
                || (officeCount == 1 && directory.Sales.Any(sale => sale.OfficeId != 5)))
                throw new InvalidOperationException($"Unexpected sales summary or office scope for {role}.");
        }
        if (commands[0] == 0 || commands.Any(count => count != commands[0]))
            throw new InvalidOperationException($"Inconsistent sales SELECT counts for {role}.");
        Array.Sort(samples);
        Console.WriteLine(FormattableString.Invariant(
            $"{role}: {rowCount} rows, {expected.Count} visible this month, {expected.TotalAmount} yen, {commands[0]} SELECTs, median {samples[samples.Length / 2]:F2} ms ({samples.Length} runs)"));
    }

    private static DateTime MonthStart() => DateTime.Today.AddDays(1 - DateTime.Today.Day);

    private static void Execute(SqlConnection connection, string sql)
    {
        using var command = new SqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    private sealed class SalesCommandObserver(string catalog) : IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private IDisposable? subscription;
        public int SelectCount { get; private set; }

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "Microsoft.EntityFrameworkCore")
                subscription = listener.Subscribe(this);
        }

        public void OnNext(KeyValuePair<string, object?> entry)
        {
            if (entry.Value is not CommandEventData data) return;
            if (entry.Key == RelationalEventId.CommandExecuting.Name
                && (data.Command.Connection?.Database != catalog
                    || !data.Command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Sales measurement expects SELECT commands on the disposable fixture.");
            if (entry.Key == RelationalEventId.CommandExecuted.Name) SelectCount++;
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose() => subscription?.Dispose();
    }
}
