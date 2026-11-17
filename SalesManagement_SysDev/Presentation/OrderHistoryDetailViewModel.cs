using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace SalesManagement_SysDev;

public sealed record OrderHistoryComparison(string Field, string Before, string After, bool Changed);

public sealed record OrderHistoryProductChange(string Product, string BeforeQuantity, string AfterQuantity,
    string BeforeAmount, string AfterAmount, string UnitPrice, string ChangeKind, int ProductId)
{
    public string ProductIdText => $"ID {ProductId}";
    public bool QuantityChanged => ChangeKind == "変更" && BeforeQuantity != AfterQuantity;
    public bool AmountChanged => ChangeKind == "変更" && BeforeAmount != AfterAmount;
}

public sealed class OrderHistoryDetailViewModel
{
    public OrderHistoryDetailViewModel(OrderOperationHistory entry, Action close, Action openOrder)
    {
        Display = new OrderHistoryDisplayRow(entry);
        CloseCommand = new RelayCommand(close);
        OpenOrderCommand = new RelayCommand(openOrder);
    }

    public OrderHistoryDisplayRow Display { get; }
    public string OrderNumber => Display.OrderNumber;
    public string HeaderSubtitle => $"{OrderNumber}  |  {Display.Customer}";
    public string Operation => Display.Operation;
    public string Timestamp => Display.Timestamp;
    public string Actor => string.IsNullOrWhiteSpace(Display.Entry.ActorName) ? "未記録" : Display.Entry.ActorName;
    public string Office => string.IsNullOrWhiteSpace(Display.Entry.ActorOfficeName) ? "未記録" : Display.Entry.ActorOfficeName;
    public string FooterOrderNumber => $"受注番号：{OrderNumber}";
    public string EventId => Display.Entry.Id.ToString();
    public string ChangeSummary => Display.ChangeSummary;
    public IReadOnlyList<OrderHistoryComparison> Comparisons => Display.Comparisons;
    public IReadOnlyList<OrderHistoryComparison> RelevantComparisons => Comparisons.Where(x => x.Changed
        || x.Before.Contains("未記録", StringComparison.Ordinal) || x.After.Contains("未記録", StringComparison.Ordinal)
        || Display.Operation == OrderHistoryOperations.Deleted && x.After == "削除").ToArray();
    public IReadOnlyList<OrderHistoryProductChange> ProductChanges => Display.ProductChanges;
    public string OriginalDetails => Display.Entry.Details;
    public bool HasComparisons => Comparisons.Count > 0;
    public bool HasRelevantComparisons => RelevantComparisons.Count > 0;
    public bool HasProductChanges => ProductChanges.Count > 0;
    public bool HasAdditionalDetails => !string.IsNullOrWhiteSpace(OriginalDetails);
    public ICommand CloseCommand { get; }
    public ICommand OpenOrderCommand { get; }
}

internal static partial class OrderHistoryAuditParser
{
    private sealed record Line(int Id, string Name, int Quantity, decimal Amount);
    private static readonly Regex ChangePattern = new("^(?<name>[^：]+)：(?<before>.*?) → (?<after>.*)$", RegexOptions.Compiled);
    private static readonly Regex ProductPattern = new("^(?<name>.*)（商品 (?<id>\\d+)）：(?<quantity>[\\d,]+) 点、¥(?<amount>[\\d,]+)$", RegexOptions.Compiled);
    private static readonly HashSet<string> RecordedChangeFields = ["品目数", "総数量", "金額", "顧客", "営業所", "受注担当者", "受注日", "納品希望日", "備考・担当連絡先", "非表示理由"];

    public static IReadOnlyList<OrderHistoryComparison> Comparisons(OrderOperationHistory entry)
    {
        var rows = new List<OrderHistoryComparison>();
        AddFlag(rows, "受注状態", entry.BeforeState, entry.AfterState, entry.Operation, OrderDetailHistoryRow.StateName);
        AddFlag(rows, "管理状態", entry.BeforeManagement, entry.AfterManagement, entry.Operation, OrderDetailHistoryRow.ManagementName);
        foreach (var line in entry.Details.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = ChangePattern.Match(line);
            if (!match.Success || !RecordedChangeFields.Contains(match.Groups["name"].Value)) continue;
            var field = match.Groups["name"].Value;
            if (field is "変更前明細" or "明細") continue;
            var before = match.Groups["before"].Value;
            var after = match.Groups["after"].Value;
            rows.Add(new(field, before.Length == 0 ? "（空欄）" : before, after.Length == 0 ? "（空欄）" : after, before != after));
        }
        return rows;
    }

    public static IReadOnlyList<OrderHistoryProductChange> ProductChanges(OrderOperationHistory entry)
    {
        string? oldText = null;
        string? newText = null;
        foreach (var line in entry.Details.Split('\n'))
        {
            if (line.StartsWith("変更前明細：", StringComparison.Ordinal)) oldText = line[6..];
            else if (line.StartsWith("明細：", StringComparison.Ordinal)) newText = line[3..];
        }
        // For updates without a recorded before-list, the after-list is a snapshot.
        // Confirmations and baselines also commonly contain only a snapshot.
        if (newText == null || oldText == null && entry.Operation != OrderHistoryOperations.Created) return [];

        var oldLines = ParseLines(oldText);
        var newLines = ParseLines(newText);
        var before = oldLines.GroupBy(line => line.Id).ToDictionary(group => group.Key, group => new Queue<Line>(group));
        var after = newLines.GroupBy(line => line.Id).ToDictionary(group => group.Key, group => new Queue<Line>(group));
        var ids = oldLines.Select(line => line.Id).Union(newLines.Select(line => line.Id)).ToList();
        var result = new List<OrderHistoryProductChange>();
        foreach (var id in ids)
        {
            before.TryGetValue(id, out var oldLinesForProduct);
            after.TryGetValue(id, out var newLinesForProduct);
            var pairCount = Math.Max(oldLinesForProduct?.Count ?? 0, newLinesForProduct?.Count ?? 0);
            for (var index = 0; index < pairCount; index++)
            {
                var oldLine = oldLinesForProduct is { Count: > 0 } ? oldLinesForProduct.Dequeue() : null;
                var newLine = newLinesForProduct is { Count: > 0 } ? newLinesForProduct.Dequeue() : null;
                if (oldLine != null && newLine != null && oldLine == newLine) continue;
                var line = newLine ?? oldLine!;
                result.Add(new(line.Name, FormatQuantity(oldLine), FormatQuantity(newLine), FormatAmount(oldLine), FormatAmount(newLine),
                    FormatUnitPriceChange(oldLine, newLine), oldLine == null ? "追加" : newLine == null ? "削除" : "変更", id));
            }
        }
        return result;
    }

    public static string Summary(OrderOperationHistory entry)
    {
        if (entry.Operation == OrderHistoryOperations.Baseline)
            return "保存済みの状態を記録。過去の変更値は未記録です。";
        var details = entry.Details;
        if (entry.Operation == OrderHistoryOperations.Created)
        {
            var item = Regex.Match(details, @"品目数：([\d,]+) 件、総数量：([\d,]+) 点");
            if (item.Success) return $"新規登録：{item.Groups[1].Value}品目・{item.Groups[2].Value}点";
        }

        var values = Comparisons(entry)
            .Where(row => row.Changed && row.Field is "総数量" or "金額" or "顧客" or "営業所" or "受注担当者" or "納品希望日" or "受注日" or "備考・担当連絡先" or "非表示理由")
            .Select(row => FormatChange(row)).Take(2).ToList();
        if (values.Count == 0 && entry.BeforeState.HasValue && entry.AfterState.HasValue && entry.BeforeState != entry.AfterState)
            values.Add($"受注状態：{OrderDetailHistoryRow.StateName(entry.BeforeState.Value)} → {OrderDetailHistoryRow.StateName(entry.AfterState.Value)}");
        if (values.Count == 0 && entry.BeforeManagement.HasValue && entry.AfterManagement.HasValue && entry.BeforeManagement != entry.AfterManagement)
            values.Add($"管理状態：{OrderDetailHistoryRow.ManagementName(entry.BeforeManagement.Value)} → {OrderDetailHistoryRow.ManagementName(entry.AfterManagement.Value)}");
        if (values.Count == 0)
        {
            var first = details.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(line => !line.StartsWith("【", StringComparison.Ordinal));
            return string.IsNullOrWhiteSpace(first) ? "記録済みの変更内容を詳細で確認" : Truncate(first, 82);
        }
        var changes = Comparisons(entry).Count(row => row.Changed && row.Field is "総数量" or "金額" or "顧客" or "営業所" or "受注担当者" or "納品希望日" or "受注日" or "備考・担当連絡先" or "非表示理由");
        var productChanges = ProductChanges(entry).Count;
        var additional = Math.Max(0, changes - values.Count) + (productChanges > 0 && changes == 0 ? productChanges : 0);
        return string.Join("　", values) + (additional > 0 ? $"　ほか{additional}件" : "");
    }

    private static void AddFlag(List<OrderHistoryComparison> rows, string label, int? before, int? after, string operation, Func<int, string> format)
    {
        if (!before.HasValue && !after.HasValue) return;
        var afterText = after.HasValue ? format(after.Value) : operation == OrderHistoryOperations.Deleted ? "削除" : "操作後の値は未記録";
        rows.Add(new(label, before.HasValue ? format(before.Value) : "変更前は未記録", afterText,
            before.HasValue && after.HasValue && before != after));
    }

    private static List<Line> ParseLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return text.Split(" / ", StringSplitOptions.RemoveEmptyEntries).Select(part =>
        {
            var match = ProductPattern.Match(part);
            return match.Success && int.TryParse(match.Groups["id"].Value, out var id)
                && int.TryParse(match.Groups["quantity"].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var quantity)
                && decimal.TryParse(match.Groups["amount"].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var amount)
                ? new Line(id, match.Groups["name"].Value, quantity, amount) : null;
        }).Where(line => line != null).Select(line => line!).ToList();
    }

    private static string FormatQuantity(Line? line) => line == null ? "—" : $"{line.Quantity:N0} 点";
    private static string FormatAmount(Line? line) => line == null ? "—" : $"¥{line.Amount:N0}";
    private static string FormatUnitPriceChange(Line? before, Line? after)
    {
        var oldPrice = UnitPrice(before);
        var newPrice = UnitPrice(after);
        if (oldPrice == "—") return newPrice == "—" ? "未記録" : newPrice;
        if (newPrice == "—" || oldPrice == newPrice) return oldPrice;
        return $"{oldPrice} → {newPrice}";
    }
    private static string UnitPrice(Line? line) => line == null ? "—" : line.Quantity <= 0 ? "未記録" : $"¥{line.Amount / line.Quantity:N0}";
    private static string FormatChange(OrderHistoryComparison row) => row.Field switch
    {
        "総数量" => $"数量：{row.Before} → {row.After} 点",
        "金額" => $"合計金額：{row.Before} → {row.After}",
        _ => $"{row.Field}：{row.Before} → {row.After}"
    };
    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";
}
