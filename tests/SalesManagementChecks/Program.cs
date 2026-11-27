using SalesManagement_SysDev;

internal static class Program
{
    private static int checks;
    private static int Main()
    {
        try
        {
            CheckSearchAndFormatting();
            CheckPermissionsAndHideValidation();
            Console.WriteLine($"PASS: {checks} sales management checks.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { AppSession.Employee = null; }
    }

    private static void CheckSearchAndFormatting()
    {
        var sale = new SalesRecord(12, new DateTime(2026, 10, 31, 23, 59, 59), 25, 8,
            "株式会社サンプル", 5, "北大阪営業所", 11, "田中 太郎", 0, null, 2, 165_000m);
        var criteria = new SalesSearchCriteria("サンプル", new DateTime(2026, 10, 1), new DateTime(2026, 10, 31),
            11, 5, 150_000m, 170_000m);
        Check(SalesFilter.Matches(sale, criteria, SalesStatusTab.Visible), "combined filters match an authorized visible sale");
        Check(SalesFilter.Matches(sale, criteria with { Keyword = "SL-1" }, SalesStatusTab.Visible), "sale numbers support partial prefixed search");
        Check(SalesFilter.Matches(sale, criteria with { Keyword = "ORD-2" }, SalesStatusTab.Visible), "order numbers support partial prefixed search");
        Check(SalesFilter.Matches(sale, criteria with { Keyword = "C-8" }, SalesStatusTab.Visible), "customer codes support partial prefixed search");
        Check(SalesFilter.Matches(sale, criteria with { Keyword = "" }, SalesStatusTab.Visible), "end-date search includes the entire selected date");
        Check(!SalesFilter.Matches(sale, criteria with { EndDate = new DateTime(2026, 10, 30) }, SalesStatusTab.Visible), "end-date search excludes later dates");
        Check(!SalesFilter.Matches(sale, criteria with { EmployeeId = 12 }, SalesStatusTab.Visible), "employee filter excludes other employees");
        Check(!SalesFilter.Matches(sale, criteria with { OfficeId = 4 }, SalesStatusTab.Visible), "office filter excludes other offices");
        Check(!SalesFilter.Matches(sale, criteria with { MinimumAmount = 165_001m }, SalesStatusTab.Visible), "minimum amount filters stored sale totals");

        var hidden = sale with { ManagementFlag = SalesService.HiddenFlag, HiddenReason = "重複登録" };
        Check(SalesFilter.Matches(hidden, criteria with { Keyword = "" }, SalesStatusTab.Hidden), "hidden tab includes logically hidden sales");
        Check(!SalesFilter.Matches(hidden, criteria with { Keyword = "" }, SalesStatusTab.Visible), "visible tab excludes hidden sales");
        Check(SalesFilter.Validate(criteria with { StartDate = new DateTime(2026, 11, 1) }) != null, "reversed date range is rejected");
        Check(SalesFilter.Validate(criteria with { MinimumAmount = 200_000m }) != null, "minimum amount above maximum is rejected");
        Check(SalesFilter.Validate(criteria with { MaximumAmount = -1m }) != null, "negative amount range is rejected");

        Check(SalesFilter.ParseAmountRange("1,000", "2,000", out var minimum, out var maximum) == null
            && minimum == 1_000m && maximum == 2_000m, "amount range parses grouped decimal values");
        Check(SalesFilter.ParseAmountRange("abc", "", out _, out _) != null, "invalid amount input is rejected");
        Check(SalesFilter.ParseAmountRange("20", "10", out _, out _) != null, "reversed amount range is rejected");
        Check(new SalesSummary(0, 0m).AverageAmount == 0m, "average safely handles an empty month");
        Check(new SalesSummary(2, 100m).AverageAmount == 50m, "average uses sales-record count");
        Check(SalesManagementViewModel.FormatCurrency(165_000m) == "¥165,000", "currency uses yen and thousands separators");
    }

    private static void CheckPermissionsAndHideValidation()
    {
        AppSession.Employee = Employee("営業");
        Check(SalesService.CanView && SalesService.CanHide, "sales staff may hide sales in their office");
        Check(!SalesService.Hide(999, 1, "理由").Succeeded, "mismatched actor is rejected before a database write");
        AppSession.Employee = Employee("物流");
        Check(!SalesService.CanView, "logistics staff cannot open sales management");
        AppSession.Employee = Employee("管理者");
        Check(SalesService.CanView && SalesService.CanHide, "administrators can view and hide sales");
        Check(!SalesService.Hide(10, 1, " ").Succeeded, "empty hide reasons are rejected before database access");
        Check(SalesService.ValidateHideReason("重複登録") == null, "a nonempty hide reason is accepted");
        Check(SalesService.ValidateHideReason(new string('理', 501)) != null, "hide reason length is bounded");
        AppSession.Employee!.EmFlag = 2;
        Check(!SalesService.CanView && !SalesService.CanHide, "inactive administrators lose sales permissions");
        AppSession.Employee = null;
        Check(!SalesService.CanView && !SalesService.CanHide, "missing session denies sales access");
    }

    private static MEmployee Employee(string role) => new()
    {
        EmId = 10,
        EmFlag = 0,
        SoId = 5,
        Po = new MPosition { PoName = role, PoFlag = 0 },
        So = new MSalesOffice { SoId = 5, SoName = "北大阪営業所" }
    };

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        checks++;
        Console.WriteLine("PASS " + name);
    }
}
