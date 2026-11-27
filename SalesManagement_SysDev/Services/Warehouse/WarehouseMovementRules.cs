using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public static class WarehouseMovementRules
{
    public static string? ValidateDateRange(WarehouseMovementSearchCriteria criteria) =>
        criteria.StartDate.HasValue && criteria.EndDate.HasValue
        && criteria.StartDate.Value.Date > criteria.EndDate.Value.Date
            ? "開始日は終了日以前の日付にしてください。" : null;

    public static string? ValidateReceiptSearch(WarehouseMovementSearchCriteria criteria)
    {
        if (criteria.PurchaseFrom?.Date > criteria.PurchaseTo?.Date) return "発注日の開始日は終了日以前にしてください。";
        if (criteria.ReceiptNumber.Trim().Length > 0 && ParseIdentifier(criteria.ReceiptNumber, "IN-", "RC-") == null)
            return "入庫番号は数字または IN-000001 形式で入力してください。";
        if (criteria.PurchaseNumber.Trim().Length > 0 && ParseIdentifier(criteria.PurchaseNumber, "PO-") == null)
            return "発注番号は数字または PO-000001 形式で入力してください。";
        return ValidateDateRange(criteria);
    }

    public static bool Matches(WarehouseMovementRecord record, WarehouseMovementSearchCriteria criteria,
        WarehouseManagementTab tab, bool canViewHidden)
    {
        if (ValidateDateRange(criteria) != null) return false;
        if (tab == WarehouseManagementTab.Visible && record.ManagementFlag != WarehousingService.ActiveFlag) return false;
        if (tab == WarehouseManagementTab.Hidden
            && (!canViewHidden || record.ManagementFlag != WarehousingService.HiddenFlag)) return false;
        if (tab == WarehouseManagementTab.All && !canViewHidden
            && record.ManagementFlag != WarehousingService.ActiveFlag) return false;
        if (criteria.StartDate.HasValue && (!record.MovementDate.HasValue
            || record.MovementDate.Value.Date < criteria.StartDate.Value.Date)) return false;
        if (criteria.EndDate.HasValue && (!record.MovementDate.HasValue
            || record.MovementDate.Value.Date > criteria.EndDate.Value.Date)) return false;
        if (criteria.PrimaryId.HasValue && record.PrimaryFilterId != criteria.PrimaryId.Value) return false;
        if (criteria.EmployeeId.HasValue && record.EmployeeId != criteria.EmployeeId.Value) return false;
        if (criteria.Status == WarehouseBusinessStatusFilter.Unconfirmed && !record.IsUnconfirmed) return false;
        if (criteria.Status == WarehouseBusinessStatusFilter.Confirmed && !record.IsConfirmed) return false;

        if (record.Kind == WarehouseMovementKind.Inbound)
        {
            if (ValidateReceiptSearch(criteria) != null) return false;
            if (ParseIdentifier(criteria.ReceiptNumber, "IN-", "RC-") is { } receiptId && receiptId != record.Id) return false;
            if (ParseIdentifier(criteria.PurchaseNumber, "PO-") is { } purchaseId && purchaseId != record.ReferenceId) return false;
            if (!Contains(record.PartnerName, criteria.Supplier.Trim())) return false;
            if (criteria.PurchaseFrom.HasValue && (!record.PurchaseDate.HasValue || record.PurchaseDate.Value.Date < criteria.PurchaseFrom.Value.Date)) return false;
            if (criteria.PurchaseTo.HasValue && (!record.PurchaseDate.HasValue || record.PurchaseDate.Value.Date > criteria.PurchaseTo.Value.Date)) return false;
            var code = criteria.ProductCode.Trim();
            var name = criteria.ProductName.Trim();
            if ((code.Length > 0 || name.Length > 0)
                && !record.ReceiptProducts.Any(p => (code.Length == 0 || ParseIdentifier(code, "P-") == p.Id
                    || Contains(p.Code, code) || Contains($"P-{p.Id:0000}", code)) && Contains(p.Name, name))) return false;
        }

        var keyword = criteria.Keyword.Trim();
        if (keyword.Length == 0) return true;
        if (record.Kind == WarehouseMovementKind.Inbound
            && (ParseIdentifier(keyword, "IN-", "RC-") == record.Id || ParseIdentifier(keyword, "PO-") == record.ReferenceId)) return true;
        return Contains(record.Code, keyword) || record.Id.ToString().Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
            || Contains(record.ReferenceCode, keyword) || record.ReferenceId.ToString().Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
            || Contains(record.PartnerName, keyword) || Contains(record.OfficeName, keyword)
            || Contains(record.EmployeeName, keyword) || Contains(record.ProductSearchText, keyword);
    }

    private static int? ParseIdentifier(string text, params string[] prefixes)
    {
        var value = text.Trim();
        foreach (var prefix in prefixes)
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { value = value[prefix.Length..]; break; }
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
    }

    public static string? ValidateConfirmation(int managementFlag, int? state)
    {
        if (managementFlag != WarehousingService.ActiveFlag) return "表示中のデータだけ確定できます。";
        if (state == WarehousingService.ConfirmedState) return "すでに確定済みです。重複処理は行いません。";
        return state == WarehousingService.UnconfirmedState ? null : "状態を確認できないため確定できません。";
    }

    public static string? ValidateHide(int managementFlag, int? state)
    {
        if (managementFlag != WarehousingService.ActiveFlag) return "通常表示のデータだけ非表示にできます。";
        return state is 0 or 1 ? null : "業務状態を確認してください。";
    }

    public static string? ValidateLines(IReadOnlyCollection<WarehouseMovementLineInput> lines) =>
        lines.Count == 0 || lines.Any(x => x.ProductId <= 0 || x.Quantity <= 0)
            ? "商品と1以上の数量を持つ明細が必要です。" : null;

    public static bool LinesMatch(IReadOnlyCollection<WarehouseMovementLineInput> expected,
        IReadOnlyCollection<WarehouseMovementLineInput> actual)
    {
        if (ValidateLines(expected) != null || ValidateLines(actual) != null) return false;
        var expectedByProduct = expected.GroupBy(x => x.ProductId)
            .ToDictionary(x => x.Key, x => x.Sum(line => (long)line.Quantity));
        var actualByProduct = actual.GroupBy(x => x.ProductId)
            .ToDictionary(x => x.Key, x => x.Sum(line => (long)line.Quantity));
        return expectedByProduct.Count == actualByProduct.Count
            && expectedByProduct.All(x => actualByProduct.TryGetValue(x.Key, out var quantity) && quantity == x.Value);
    }

    public static string? ValidateUniqueSourceCount(int count) => count switch
    {
        1 => null,
        0 => "対応する注文・明細を確認できません。",
        _ => "対応する注文を一意に特定できません。データを確認してください。"
    };

    private static bool Contains(string value, string keyword) =>
        value.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);
}
