using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public sealed record SalesRecord(int Id, DateTime SaleDate, int OrderId, int CustomerId, string CustomerName,
    int OfficeId, string OfficeName, int EmployeeId, string EmployeeName, int ManagementFlag,
    string? HiddenReason, int LineCount, decimal Amount)
{
    public string Code => $"SL-{Id:0000}";
    public string DetailHeader => $"売上詳細 — {Code}";
    public string AmountText => SalesManagementViewModel.FormatCurrency(Amount);
    public bool HasHiddenReason => !string.IsNullOrWhiteSpace(HiddenReason);
    public string OrderCode => $"ORD-{OrderId:000000}";
    public string CustomerCode => $"C-{CustomerId:0000}";
    public string ManagementLabel => ManagementFlag switch
    {
        SalesService.ActiveFlag => "表示中",
        SalesService.HiddenFlag => "非表示",
        _ => "要確認"
    };
}

public sealed record SalesEmployeeOption(int Id, string Name);
public sealed record SalesOfficeOption(int Id, string Name);
public sealed record SalesSummary(int Count, decimal TotalAmount)
{
    public decimal AverageAmount => Count == 0 ? 0m : TotalAmount / Count;
}
public sealed record SalesDirectory(IReadOnlyList<SalesRecord> Sales,
    IReadOnlyList<SalesEmployeeOption> Employees, IReadOnlyList<SalesOfficeOption> Offices,
    SalesSummary MonthlyVisibleSummary);
public sealed record SalesLineRecord(int ProductId, string? JanCode, string ProductName, int Quantity, decimal StoredAmount)
{
    public string ProductCode => $"P-{ProductId:0000}";
}
public sealed record SalesDetail(SalesRecord Sale, IReadOnlyList<SalesLineRecord> Lines);
public sealed record SalesOperationResult(bool Succeeded, string Message, int? SalesId = null);
public sealed record SalesSearchCriteria(string Keyword, DateTime? StartDate, DateTime? EndDate,
    int? EmployeeId, int? OfficeId, decimal? MinimumAmount, decimal? MaximumAmount);

public enum SalesStatusTab { All, Visible, Hidden }

public static class SalesFilter
{
    public static string? Validate(SalesSearchCriteria criteria)
    {
        if (criteria.StartDate.HasValue && criteria.EndDate.HasValue
            && criteria.StartDate.Value.Date > criteria.EndDate.Value.Date)
            return "売上日の開始日は終了日以前の日付を指定してください。";
        if (criteria.MinimumAmount is < 0 || criteria.MaximumAmount is < 0)
            return "売上金額は0以上で入力してください。";
        if (criteria.MinimumAmount.HasValue && criteria.MaximumAmount.HasValue
            && criteria.MinimumAmount.Value > criteria.MaximumAmount.Value)
            return "最小金額は最大金額以下にしてください。";
        return null;
    }

    public static string? ParseAmountRange(string? minimumText, string? maximumText,
        out decimal? minimum, out decimal? maximum)
    {
        minimum = null; maximum = null;
        if (!TryAmount(minimumText, out minimum) || !TryAmount(maximumText, out maximum))
            return "金額は0以上の数値で入力してください。";
        if (minimum.HasValue && maximum.HasValue && minimum.Value > maximum.Value)
            return "最小金額は最大金額以下にしてください。";
        return null;
    }

    public static bool Matches(SalesRecord sale, SalesSearchCriteria criteria, SalesStatusTab tab)
    {
        if (tab == SalesStatusTab.Visible && sale.ManagementFlag != SalesService.ActiveFlag) return false;
        if (tab == SalesStatusTab.Hidden && sale.ManagementFlag != SalesService.HiddenFlag) return false;
        if (criteria.StartDate.HasValue && sale.SaleDate.Date < criteria.StartDate.Value.Date) return false;
        // SaDate is a SQL date today; comparing date values includes every record on the selected end date.
        if (criteria.EndDate.HasValue && sale.SaleDate.Date > criteria.EndDate.Value.Date) return false;
        if (criteria.EmployeeId is > 0 && sale.EmployeeId != criteria.EmployeeId.Value) return false;
        if (criteria.OfficeId is > 0 && sale.OfficeId != criteria.OfficeId.Value) return false;
        if (criteria.MinimumAmount.HasValue && sale.Amount < criteria.MinimumAmount.Value) return false;
        if (criteria.MaximumAmount.HasValue && sale.Amount > criteria.MaximumAmount.Value) return false;
        if (string.IsNullOrWhiteSpace(criteria.Keyword)) return true;

        var keyword = criteria.Keyword.Trim();
        return MatchNumber(sale.Code, "SL-", sale.Id, keyword)
            || MatchNumber(sale.OrderCode, "ORD-", sale.OrderId, keyword)
            || MatchNumber(sale.CustomerCode, "C-", sale.CustomerId, keyword)
            || Contains(sale.CustomerName, keyword)
            || Contains(sale.EmployeeName, keyword) || Contains(sale.OfficeName, keyword)
            ;
    }

    private static bool MatchNumber(string formatted, string prefix, int number, string keyword)
    {
        if (Contains(formatted, keyword)) return true;
        var digits = keyword.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase)
            ? keyword[prefix.Length..] : keyword;
        return digits.Length > 0 && Contains(number.ToString(CultureInfo.InvariantCulture), digits);
    }

    private static bool TryAmount(string? text, out decimal? amount)
    {
        amount = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed)
            && !decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out parsed)) return false;
        if (parsed < 0) return false;
        amount = parsed;
        return true;
    }

    private static bool Contains(string source, string value) =>
        source.Contains(value, StringComparison.CurrentCultureIgnoreCase);
}

/// <summary>Read-only sales reporting and logical hiding over the existing sales tables.</summary>
public static class SalesService
{
    public const int ActiveFlag = 0;
    // The application uses 0 for visible and 2 for logically hidden records across its existing modules.
    public const int HiddenFlag = 2;

    public static bool CanView => AppSession.Employee is { EmFlag: ActiveFlag }
        && (AppSession.IsAdmin || AppSession.IsSales);
    public static bool CanHide => CanView;

    public static SalesDirectory Load()
    {
        if (!CanView) throw new UnauthorizedAccessException("売上情報を表示する権限がありません。");
        using var db = new SalesManagementContext();
        var actor = GetCurrentActor(db);
        var scopedSales = Scope(db.TSales.AsNoTracking(), actor);

        // Sale rows use only stored header fields and stored detail totals; individual line details
        // are loaded only when one record's detail dialog is opened.
        var sales = scopedSales.OrderByDescending(x => x.SaDate).ThenByDescending(x => x.SaId)
            .Select(x => new SalesRecord(x.SaId, x.SaDate, x.OrId, x.ClId, x.Cl.ClName,
                x.SoId, x.So.SoName, x.EmId, x.Em.EmName, x.SaFlag, x.SaHidden,
                x.TSaleDetails.Count(), x.TSaleDetails.Select(d => (decimal?)d.SaPrTotalPrice).Sum() ?? 0m))
            .ToList();

        var employeesQuery = db.MEmployees.AsNoTracking();
        var officesQuery = db.MSalesOffices.AsNoTracking();
        if (!actor.IsAdministrator)
        {
            employeesQuery = employeesQuery.Where(x => x.SoId == actor.OfficeId);
            officesQuery = officesQuery.Where(x => x.SoId == actor.OfficeId);
        }
        var employees = employeesQuery.OrderBy(x => x.EmName)
            .Select(x => new SalesEmployeeOption(x.EmId, x.EmName)).ToList();
        var offices = officesQuery.OrderBy(x => x.SoName)
            .Select(x => new SalesOfficeOption(x.SoId, x.SoName)).ToList();

        // Reuse the authorized list's stored totals so the summary describes the same snapshot.
        return new(sales, employees, offices, SummarizeVisibleMonth(sales, DateTime.Today));
    }

    internal static SalesSummary SummarizeVisibleMonth(IReadOnlyList<SalesRecord> sales, DateTime today)
    {
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1);
        var count = 0;
        var amount = 0m;
        foreach (var sale in sales)
        {
            if (sale.ManagementFlag != ActiveFlag || sale.SaleDate < monthStart || sale.SaleDate >= monthEnd) continue;
            count++;
            amount += sale.Amount;
        }
        return new(count, amount);
    }

    public static SalesDetail? GetDetail(int salesId)
    {
        if (salesId <= 0) return null;
        if (!CanView) throw new UnauthorizedAccessException("売上情報を表示する権限がありません。");
        using var db = new SalesManagementContext();
        var actor = GetCurrentActor(db);
        var sale = Scope(db.TSales.AsNoTracking(), actor).Where(x => x.SaId == salesId)
            .Select(x => new SalesRecord(x.SaId, x.SaDate, x.OrId, x.ClId, x.Cl.ClName,
                x.SoId, x.So.SoName, x.EmId, x.Em.EmName, x.SaFlag, x.SaHidden,
                x.TSaleDetails.Count(), x.TSaleDetails.Select(d => (decimal?)d.SaPrTotalPrice).Sum() ?? 0m))
            .SingleOrDefault();
        if (sale == null) return null;
        var lines = db.TSaleDetails.AsNoTracking().Where(x => x.SaId == salesId)
            .OrderBy(x => x.SaDetailId)
            .Select(x => new SalesLineRecord(x.PrId, x.Pr.PrJcode, x.Pr.PrName, x.SaQuantity, x.SaPrTotalPrice))
            .ToList();
        return new(sale, lines);
    }

    public static SalesOperationResult Hide(int actorId, int salesId, string? reason)
    {
        if (actorId <= 0 || salesId <= 0 || AppSession.Employee?.EmId != actorId
            || !CanHide) return Denied();
        var invalidReason = ValidateHideReason(reason);
        if (invalidReason != null) return new(false, invalidReason);
        using var db = new SalesManagementContext();
        try
        {
            using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
            var actor = GetCurrentActor(db);
            var sale = db.TSales.FromSqlInterpolated(
                $"SELECT * FROM dbo.T_Sale WITH (UPDLOCK, HOLDLOCK) WHERE SaID = {salesId}").SingleOrDefault();
            if (sale == null) return new(false, "対象の売上が見つかりません。");
            if (!actor.IsAdministrator && sale.SoId != actor.OfficeId) return Denied();
            if (sale.SaFlag != ActiveFlag) return new(false, "表示中の売上だけを非表示にできます。");

            // Hiding changes only sales management metadata; order, shipment and stock rows are untouched.
            sale.SaFlag = HiddenFlag;
            sale.SaHidden = reason!.Trim();
            db.SaveChanges();
            transaction.Commit();
            return new(true, "売上を非表示にしました。売上・受注・出荷・在庫データは削除・変更していません。", salesId);
        }
        catch (UnauthorizedAccessException ex) { return new(false, ex.Message); }
        catch (Exception)
        {
            return new(false, "売上を非表示にできませんでした。一覧を再読込してからお試しください。");
        }
    }

    public static string? ValidateHideReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "非表示理由を入力してください。";
        if (reason.Trim().Length > 500) return "非表示理由は500文字以内で入力してください。";
        return null;
    }

    private static IQueryable<TSale> Scope(IQueryable<TSale> query, SalesActor actor) =>
        actor.IsAdministrator ? query : query.Where(x => x.SoId == actor.OfficeId);

    private static SalesActor GetCurrentActor(SalesManagementContext db)
    {
        var employeeId = AppSession.Employee?.EmId ?? 0;
        var actor = db.MEmployees.AsNoTracking()
            .Where(x => x.EmId == employeeId && x.EmFlag == 0 && x.Po.PoFlag == 0)
            .Select(x => new SalesActor(x.EmId, x.SoId, x.Po.PoName))
            .SingleOrDefault();
        if (actor == null || (!actor.IsAdministrator && !actor.IsSales))
            throw new UnauthorizedAccessException("売上情報を表示する権限がありません。ログイン状態を確認してください。");
        return actor;
    }

    private sealed record SalesActor(int EmployeeId, int OfficeId, string RoleName)
    {
        public bool IsAdministrator => RoleName == "管理者";
        public bool IsSales => RoleName == "営業";
    }

    private static SalesOperationResult Denied() =>
        new(false, "売上を非表示にする権限がありません。役割と対象の営業所を確認してください。");
}
