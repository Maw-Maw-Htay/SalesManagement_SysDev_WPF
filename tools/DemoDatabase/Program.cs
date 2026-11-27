using System.Globalization;
using System.IO;
using Microsoft.EntityFrameworkCore;
using SalesManagement_SysDev;

namespace SalesManagement.Demo;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (!args.Contains("--demo-database", StringComparer.Ordinal))
                throw new InvalidOperationException("Run this tool with --demo-database. It only accepts SalesManagement_Demo.");

            var mode = args.FirstOrDefault(arg => arg is "seed" or "verify" or "smoke");
            if (mode == null) throw new ArgumentException("Specify seed, verify, or smoke.");

            using var db = new SalesManagementContext();
            db.Database.OpenConnection();
            var anchor = VerifyTarget(db);
            if (mode == "seed") AcquireSeedLock(db);
            var credentials = ReadCredentials();
            var ready = ReadProperty(db, "KansaiOADemoReady");
            Require(ready == null || ready == DemoData.Marker, "Unknown demo dataset version.");
            var seeder = new DemoSeeder(db, anchor, credentials);
            if (mode == "seed")
            {
                PurchaseSchemaInitializer.EnsureReady(db);
                var (offices, receiptDates) = seeder.PrepareExistingPurchases();
                if (offices > 0) Console.WriteLine($"Assigned offices to {offices} existing demo purchase orders.");
                if (receiptDates > 0) Console.WriteLine($"Corrected the dates of {receiptDates} confirmed demo receipts without changing stock.");
            }

            if (mode == "seed" && ready == null)
            {
                seeder.Seed();
                var report = DemoVerification.Verify(db, anchor, credentials);
                db.Database.ExecuteSqlRaw("""
                    IF DB_NAME() <> N'SalesManagement_Demo' THROW 51000, N'Invalid demo target.', 1;
                    EXEC sys.sp_addextendedproperty @name=N'KansaiOADemoReady', @value=N'KansaiOA/v1';
                    """);
                report.Write(GetOption(args, "--report-dir"));
                Console.WriteLine("SalesManagement_Demo is ready.");
                return 0;
            }

            Require(ready == DemoData.Marker, "The demo dataset has not been fully prepared. Run seed first.");
            if (mode == "smoke") DemoUiChecks.Run(credentials[DemoData.Admin]);
            else
            {
                if (mode == "seed") Console.WriteLine("The completed fixture exists. Verifying it after the purchase and receipt compatibility check.");
                DemoVerification.Verify(db, anchor, credentials).Write(GetOption(args, "--report-dir"));
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally
        {
            AppSession.Employee = null;
            HistoryNotificationService.MarkAllRead();
        }
    }

    internal static DateTime VerifyTarget(SalesManagementContext db)
    {
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT CASE WHEN DB_NAME() = N'SalesManagement_Demo' AND SERVERPROPERTY('IsLocalDB') = 1 THEN 1 ELSE 0 END;";
        Require(command.ExecuteScalar() is 1, "Refusing any target other than the isolated SalesManagement_Demo LocalDB database.");
        Require(ReadProperty(db, "KansaiOADemoFixture") == DemoData.Marker,
            "Demo fixture marker is missing. Create the schema with New-DemoSchema.ps1.");
        var anchor = ReadProperty(db, "KansaiOADemoAnchor");
        Require(DateTime.TryParseExact(anchor, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date),
            "Demo planning date is missing or invalid.");
        return date;
    }

    internal static string? ReadProperty(SalesManagementContext db, string name)
    {
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT CONVERT(nvarchar(64), value) FROM sys.extended_properties WHERE class = 0 AND name = @name;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@name";
        parameter.Value = name;
        command.Parameters.Add(parameter);
        return command.ExecuteScalar() as string;
    }

    private static void AcquireSeedLock(SalesManagementContext db)
    {
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource=N'KansaiOA.DemoSeed', @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0;
            SELECT @result;
            """;
        Require(command.ExecuteScalar() is int result && result >= 0, "Another demo seed process is already running.");
    }

    private static Dictionary<int, string> ReadCredentials()
    {
        Require(Console.IsInputRedirected, "Use Invoke-DemoData.ps1 to provide the encrypted local demo credentials.");
        var credentials = new Dictionary<int, string>();
        while (Console.ReadLine() is { } line)
        {
            var fields = line.Split('\t');
            if (fields.Length != 2 || !int.TryParse(fields[0], out var id) || fields[1].Length != 10
                || !fields[1].All(char.IsAsciiLetterOrDigit) || !credentials.TryAdd(id, fields[1]))
                throw new InvalidDataException("Invalid input from the demo credential store.");
        }
        Require(credentials.Keys.Order().SequenceEqual(DemoData.Employees.Select(employee => employee.Id)),
            "The encrypted demo credential store does not match the fixture account IDs.");
        return credentials;
    }

    private static string? GetOption(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static T Scalar<T>(SalesManagementContext db, string query)
    {
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = query;
        return (T)command.ExecuteScalar()!;
    }
}
