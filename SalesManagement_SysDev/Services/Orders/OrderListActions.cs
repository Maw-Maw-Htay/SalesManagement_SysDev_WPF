using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace SalesManagement_SysDev;

public sealed class CsvOrderImportOrder
{
    public string ExternalOrderNumber { get; init; } = "";
    public DateTime OrderDate { get; init; }
    public DateTime? DeliveryDate { get; init; }
    public int SalesOfficeId { get; init; }
    public int EmployeeId { get; init; }
    public int ClientId { get; init; }
    public string SalesOfficeName { get; init; } = "";
    public string EmployeeName { get; init; } = "";
    public string ClientName { get; init; } = "";
    public List<CsvOrderImportLine> Lines { get; init; } = new();
    public decimal Total => Lines.Sum(x => x.LineTotal);
}

public sealed class CsvOrderImportLine
{
    public int ProductId { get; init; }
    public string ProductName { get; init; } = "";
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal LineTotal { get; init; }
}

public sealed class CsvOrderImportPreview
{
    public List<CsvOrderImportOrder> Orders { get; } = new();
    public List<string> Errors { get; } = new();
}

public static class CsvOrderImportService
{
    private static readonly string[] RequiredHeaders =
    {
        "外部受注番号", "受注日", "納期", "営業所ID", "担当者ID", "顧客ID", "商品ID", "数量", "単価"
    };

    public static CsvOrderImportPreview BuildPreview(string path)
    {
        var result = new CsvOrderImportPreview();
        List<CsvRecord> records;
        try
        {
            records = ParseCsv(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception ex)
        {
            result.Errors.Add($"CSVファイルを読み込めません: {ex.Message}");
            return result;
        }

        if (records.Count == 0)
        {
            result.Errors.Add("CSVファイルにヘッダー行がありません。");
            return result;
        }

        var header = records[0].Cells.Select(x => x.Trim().TrimStart('\uFEFF')).ToList();
        var missing = RequiredHeaders.Where(required => !header.Contains(required, StringComparer.Ordinal)).ToList();
        if (missing.Count > 0)
        {
            result.Errors.Add($"必須列がありません: {string.Join("、", missing)}");
            return result;
        }
        var positions = RequiredHeaders.ToDictionary(name => name, name => header.IndexOf(name), StringComparer.Ordinal);

        var parsedLines = new List<(int Row, string ExternalId, DateTime Date, DateTime? DeliveryDate, int OfficeId, int EmployeeId, int ClientId, int ProductId, int Quantity, decimal UnitPrice)>();
        foreach (var record in records.Skip(1))
        {
            if (record.Cells.All(string.IsNullOrWhiteSpace)) continue;
            if (record.Cells.Count < header.Count)
            {
                result.Errors.Add($"CSV行 {record.LineNumber}: 列数がヘッダーと一致しません。");
                continue;
            }

            string Get(string name) => record.Cells[positions[name]].Trim();
            var rowErrors = new List<string>();
            var externalId = Get("外部受注番号");
            if (string.IsNullOrWhiteSpace(externalId)) rowErrors.Add("外部受注番号が空です");
            if (!DateTime.TryParseExact(Get("受注日"), new[] { "yyyy-MM-dd", "yyyy/M/d" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var orderDate))
                rowErrors.Add("受注日は yyyy-MM-dd 形式で入力してください");
            DateTime? deliveryDate = null;
            var deliveryText = Get("納期");
            if (!string.IsNullOrWhiteSpace(deliveryText))
            {
                if (DateTime.TryParseExact(deliveryText, new[] { "yyyy-MM-dd", "yyyy/M/d" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDeliveryDate)) deliveryDate = parsedDeliveryDate.Date;
                else rowErrors.Add("納期は空欄または yyyy-MM-dd 形式で入力してください");
            }
            var officeOk = int.TryParse(Get("営業所ID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var officeId) && officeId > 0;
            var employeeOk = int.TryParse(Get("担当者ID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var employeeId) && employeeId > 0;
            var clientOk = int.TryParse(Get("顧客ID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var clientId) && clientId > 0;
            var productOk = int.TryParse(Get("商品ID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var productId) && productId > 0;
            var quantityOk = int.TryParse(Get("数量"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) && quantity > 0;
            var priceOk = decimal.TryParse(Get("単価"), NumberStyles.Number, CultureInfo.InvariantCulture, out var unitPrice) && unitPrice >= 0;
            if (!officeOk) rowErrors.Add("営業所IDは正の整数で入力してください");
            if (!employeeOk) rowErrors.Add("担当者IDは正の整数で入力してください");
            if (!clientOk) rowErrors.Add("顧客IDは正の整数で入力してください");
            if (!productOk) rowErrors.Add("商品IDは正の整数で入力してください");
            if (!quantityOk) rowErrors.Add("数量は1以上の整数で入力してください");
            if (!priceOk) rowErrors.Add("単価は0以上の数値で入力してください");
            if (rowErrors.Count > 0)
            {
                result.Errors.Add($"CSV行 {record.LineNumber} ({externalId}): {string.Join("、", rowErrors)}");
                continue;
            }
            parsedLines.Add((record.LineNumber, externalId, orderDate.Date, deliveryDate, officeId, employeeId, clientId, productId, quantity, unitPrice));
        }

        if (parsedLines.Count == 0)
        {
            if (result.Errors.Count == 0) result.Errors.Add("取込対象の明細がありません。");
            return result;
        }

        using var db = new SalesManagementContext();
        var offices = db.MSalesOffices.AsNoTracking().Where(x => x.SoFlag == 0).ToDictionary(x => x.SoId);
        var employees = db.MEmployees.AsNoTracking().Where(x => x.EmFlag == 0).ToDictionary(x => x.EmId);
        var clients = db.MClients.AsNoTracking().Where(x => x.ClFlag == 0).ToDictionary(x => x.ClId);
        var products = db.MProducts.AsNoTracking().Where(x => x.PrFlag == 0).ToDictionary(x => x.PrId);
        var invalidExternalIds = new HashSet<string>(StringComparer.Ordinal);
        var groups = parsedLines.GroupBy(x => x.ExternalId, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var first = group.First();
            var headerMismatch = group.Any(x => x.Date != first.Date || x.DeliveryDate != first.DeliveryDate || x.OfficeId != first.OfficeId || x.EmployeeId != first.EmployeeId || x.ClientId != first.ClientId);
            if (headerMismatch)
            {
                result.Errors.Add($"外部受注番号 {group.Key}: 同じ受注番号の受注日・納期・営業所・担当者・顧客が行ごとに異なります。");
                invalidExternalIds.Add(group.Key);
                continue;
            }

            if (!offices.TryGetValue(first.OfficeId, out var office))
            {
                result.Errors.Add($"外部受注番号 {group.Key}: 営業所ID {first.OfficeId} が存在しないか、利用停止です。");
                invalidExternalIds.Add(group.Key);
                continue;
            }
            if (!employees.TryGetValue(first.EmployeeId, out var employee) || employee.SoId != first.OfficeId)
            {
                result.Errors.Add($"外部受注番号 {group.Key}: 担当者ID {first.EmployeeId} が存在しない、利用停止、または営業所と一致しません。");
                invalidExternalIds.Add(group.Key);
                continue;
            }
            if (!clients.TryGetValue(first.ClientId, out var client) || client.SoId != first.OfficeId)
            {
                result.Errors.Add($"外部受注番号 {group.Key}: 顧客ID {first.ClientId} が存在しないか、利用停止です。");
                invalidExternalIds.Add(group.Key);
                continue;
            }
            if (!AppSession.IsAdmin && first.OfficeId != AppSession.Employee?.SoId)
            {
                result.Errors.Add($"外部受注番号 {group.Key}: ログイン利用者の許可営業所外です。");
                invalidExternalIds.Add(group.Key);
                continue;
            }

            var lines = new List<CsvOrderImportLine>();
            foreach (var item in group)
            {
                if (!products.TryGetValue(item.ProductId, out var product))
                {
                    result.Errors.Add($"CSV行 {item.Row} ({group.Key}): 商品ID {item.ProductId} が存在しないか、利用停止です。");
                    invalidExternalIds.Add(group.Key);
                    continue;
                }
                var lineTotal = decimal.Round(item.UnitPrice * item.Quantity, 0, MidpointRounding.AwayFromZero);
                if (lineTotal > 9999999999m)
                {
                    result.Errors.Add($"CSV行 {item.Row} ({group.Key}): 明細金額がDBの上限を超えています。");
                    invalidExternalIds.Add(group.Key);
                    continue;
                }
                lines.Add(new CsvOrderImportLine { ProductId = item.ProductId, ProductName = product.PrName, Quantity = item.Quantity, UnitPrice = item.UnitPrice, LineTotal = lineTotal });
            }
            if (lines.Count == group.Count() && !invalidExternalIds.Contains(group.Key))
            {
                result.Orders.Add(new CsvOrderImportOrder
                {
                    ExternalOrderNumber = group.Key,
                    OrderDate = first.Date,
                    DeliveryDate = first.DeliveryDate,
                    SalesOfficeId = first.OfficeId,
                    EmployeeId = first.EmployeeId,
                    ClientId = first.ClientId,
                    SalesOfficeName = office.SoName,
                    EmployeeName = employee.EmName,
                    ClientName = client.ClName,
                    Lines = lines
                });
            }
        }

        var existingFingerprints = db.TOrders.AsNoTracking()
            .Include(x => x.TOrderDetails)
            .ToList()
            .Select(ToFingerprint)
            .ToHashSet(StringComparer.Ordinal);
        var seenInFile = new HashSet<string>(StringComparer.Ordinal);
        foreach (var order in result.Orders.ToList())
        {
            var fingerprint = ToFingerprint(order);
            if (existingFingerprints.Contains(fingerprint))
            {
                result.Errors.Add($"外部受注番号 {order.ExternalOrderNumber}: 同一内容の受注がデータベースに既にあります。");
                result.Orders.Remove(order);
            }
            else if (!seenInFile.Add(fingerprint))
            {
                result.Errors.Add($"外部受注番号 {order.ExternalOrderNumber}: CSV内に同一内容の受注が重複しています。");
                result.Orders.Remove(order);
            }
        }
        return result;
    }

    public static int ImportApproved(IReadOnlyCollection<CsvOrderImportOrder> orders)
    {
        if (orders.Count == 0) throw new InvalidOperationException("取込対象がありません。");
        if (!AppSession.CanManageOrders || AppSession.Employee is not { } session)
            throw new UnauthorizedAccessException("CSV取込は管理者または営業担当者のみ実行できます。");

        using var db = new SalesManagementContext();
        using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
        if (BusinessService.ResolveOrderActor(db, session.EmId) is not { } actor)
            throw new UnauthorizedAccessException("CSV取込の権限を確認できません。再ログインしてください。");

        var officeIds = db.MSalesOffices.Where(x => x.SoFlag == 0).Select(x => x.SoId).ToHashSet();
        var employees = db.MEmployees.Where(x => x.EmFlag == 0).ToDictionary(x => x.EmId, x => x.SoId);
        var clients = db.MClients.Where(x => x.ClFlag == 0).ToDictionary(x => x.ClId, x => x.SoId);
        var productIds = db.MProducts.Where(x => x.PrFlag == 0).Select(x => x.PrId).ToHashSet();
        var errors = new List<string>();
        foreach (var order in orders)
        {
            if (!officeIds.Contains(order.SalesOfficeId)) errors.Add($"{order.ExternalOrderNumber}: 営業所が利用できません。");
            if (!employees.TryGetValue(order.EmployeeId, out var employeeOffice) || employeeOffice != order.SalesOfficeId) errors.Add($"{order.ExternalOrderNumber}: 担当者が利用できないか、営業所が一致しません。");
            if (!clients.TryGetValue(order.ClientId, out var clientOffice) || clientOffice != order.SalesOfficeId) errors.Add($"{order.ExternalOrderNumber}: 顧客が利用できないか、営業所と一致しません。");
            if (actor.Role != "管理者" && order.SalesOfficeId != actor.SoId) errors.Add($"{order.ExternalOrderNumber}: 許可営業所外です。");
            if (order.Lines.Count == 0 || order.Lines.Any(x => !productIds.Contains(x.ProductId) || x.Quantity <= 0 || x.LineTotal < 0 || x.LineTotal > 9999999999m)) errors.Add($"{order.ExternalOrderNumber}: 明細商品・数量・金額を再確認してください。");
        }

        var known = db.TOrders.Include(x => x.TOrderDetails).ToList().Select(ToFingerprint).ToHashSet(StringComparer.Ordinal);
        foreach (var order in orders)
        {
            if (!known.Add(ToFingerprint(order))) errors.Add($"{order.ExternalOrderNumber}: 同一内容の受注が既にあります。");
        }
        if (errors.Count > 0) throw new InvalidOperationException("保存前の再検証でエラーが見つかりました。\n" + string.Join("\n", errors));

                var createdOrders = new List<TOrder>();
        foreach (var item in orders)
        {
            var order = new TOrder
            {
                SoId = item.SalesOfficeId,
                EmId = item.EmployeeId,
                ClId = item.ClientId,
                ClCharge = null,
                OrDate = item.OrderDate.Date,
                OrDeliveryDate = item.DeliveryDate?.Date,
                OrStateFlag = 0,
                OrFlag = 0,
                OrHidden = null,
                TOrderDetails = item.Lines.Select(line => new TOrderDetail
                {
                    PrId = line.ProductId,
                    OrQuantity = line.Quantity,
                    OrTotalPrice = line.LineTotal
                }).ToList()
            };
            db.TOrders.Add(order);
            createdOrders.Add(order);
        }
        db.SaveChanges();
        foreach (var order in createdOrders)
            OrderHistoryService.Record(db, order, OrderHistoryOperations.Created, null, "CSV取込。初期状態は未確定・表示中。在庫数量の変更はありません。");
        db.SaveChanges();
        transaction.Commit();
        return orders.Count;
    }

    private static string ToFingerprint(TOrder order) => MakeFingerprint(
        order.SoId, order.EmId, order.ClId, order.OrDate, order.OrDeliveryDate,
        order.TOrderDetails.Where(x => x.OrQuantity != 0 || x.OrTotalPrice != 0m)
            .Select(x => (x.PrId, x.OrQuantity, x.OrTotalPrice)));

    private static string ToFingerprint(CsvOrderImportOrder order) => MakeFingerprint(
        order.SalesOfficeId, order.EmployeeId, order.ClientId, order.OrderDate, order.DeliveryDate,
        order.Lines.Select(x => (x.ProductId, x.Quantity, x.LineTotal)));

    private static string MakeFingerprint(int officeId, int employeeId, int clientId, DateTime date, DateTime? deliveryDate, IEnumerable<(int ProductId, int Quantity, decimal Total)> lines)
    {
        var detailKey = string.Join(";", lines.OrderBy(x => x.ProductId).ThenBy(x => x.Quantity).ThenBy(x => x.Total)
            .Select(x => $"{x.ProductId}:{x.Quantity}:{x.Total.ToString("0", CultureInfo.InvariantCulture)}"));
        return $"{officeId}|{employeeId}|{clientId}|{date:yyyyMMdd}|{deliveryDate:yyyyMMdd}|{detailKey}";
    }

    private static List<CsvRecord> ParseCsv(string text)
    {
        var rows = new List<CsvRecord>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var rowStart = 1;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"') inQuotes = false;
                else
                {
                    cell.Append(c);
                    if (c == '\n') line++;
                }
            }
            else if (c == '"' && cell.Length == 0) inQuotes = true;
            else if (c == ',')
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                cells.Add(cell.ToString());
                cell.Clear();
                rows.Add(new CsvRecord(rowStart, cells));
                cells = new List<string>();
                line++;
                rowStart = line;
            }
            else cell.Append(c);
        }
        if (inQuotes) throw new FormatException("閉じていない引用符があります。");
        if (cell.Length > 0 || cells.Count > 0)
        {
            cells.Add(cell.ToString());
            rows.Add(new CsvRecord(rowStart, cells));
        }
        return rows;
    }

    private sealed record CsvRecord(int LineNumber, List<string> Cells);
}

public sealed class CsvOrderImportPreviewWindow : Window
{
    private readonly CsvOrderImportPreview preview;

    public CsvOrderImportPreviewWindow(CsvOrderImportPreview preview)
    {
        this.preview = preview;
        Title = "CSV受注取込プレビュー";
        Width = 980;
        Height = 650;
        MinWidth = 800;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Meiryo");
        Background = new SolidColorBrush(Color.FromRgb(246, 248, 251));
        BuildUi();
    }

    private void BuildUi()
    {
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock { Text = "CSV受注取込の確認", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brush("#061A3D") });
        header.Children.Add(new TextBlock { Text = "列: 外部受注番号, 受注日(yyyy-MM-dd), 納期(空欄可), 営業所ID, 担当者ID, 顧客ID, 商品ID, 数量, 単価。受注番号が同じ明細行を1件の受注にまとめます。納期列は現行DBで未管理です。", Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Brush("#53657C"), FontSize = 11 });
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition());
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            Background = Brushes.White,
            ItemsSource = preview.Orders.Select(x => new CsvImportPreviewRow
            {
                外部受注番号 = x.ExternalOrderNumber,
                受注日 = x.OrderDate.ToString("yyyy-MM-dd"),
                納期 = x.DeliveryDate?.ToString("yyyy-MM-dd") ?? "未設定",
                営業所 = x.SalesOfficeName,
                担当者 = x.EmployeeName,
                顧客名 = x.ClientName,
                明細数 = x.Lines.Count,
                合計金額 = x.Total
            }).ToList()
        };
        AddPreviewColumn(grid, "外部受注番号", nameof(CsvImportPreviewRow.外部受注番号), 145);
        AddPreviewColumn(grid, "受注日", nameof(CsvImportPreviewRow.受注日), 95);
        AddPreviewColumn(grid, "納期", nameof(CsvImportPreviewRow.納期), 95);
        AddPreviewColumn(grid, "営業所", nameof(CsvImportPreviewRow.営業所), 110);
        AddPreviewColumn(grid, "担当者", nameof(CsvImportPreviewRow.担当者), 110);
        AddPreviewColumn(grid, "顧客名", nameof(CsvImportPreviewRow.顧客名), new DataGridLength(1, DataGridLengthUnitType.Star));
        AddPreviewColumn(grid, "明細数", nameof(CsvImportPreviewRow.明細数), 65);
        AddPreviewColumn(grid, "合計金額", nameof(CsvImportPreviewRow.合計金額), 105, "¥#,##0");
        Grid.SetRow(grid, 0);
        body.Children.Add(grid);

        var errorPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var valid = preview.Errors.Count == 0 && preview.Orders.Count > 0;
        errorPanel.Children.Add(new TextBlock
        {
            Text = valid ? $"検証済み: {preview.Orders.Count}件。内容を確認してから取込を確定してください。" : $"検証エラー: {preview.Errors.Count}件。エラーを解消するまで取込できません。",
            Foreground = valid ? Brush("#087443") : Brush("#A12828"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12
        });
        if (preview.Errors.Count > 0)
        {
            var errors = new ListBox { ItemsSource = preview.Errors, MaxHeight = 105, Margin = new Thickness(0, 5, 0, 0), FontSize = 11 };
            errorPanel.Children.Add(errors);
        }
        else if (preview.Orders.Count == 0)
        {
            errorPanel.Children.Add(new TextBlock { Text = "取込対象がありません。", Foreground = Brush("#A12828"), Margin = new Thickness(0, 4, 0, 0) });
        }
        Grid.SetRow(errorPanel, 1);
        body.Children.Add(errorPanel);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "キャンセル", Width = 100, Height = 34, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var import = new Button { Content = "取込を確定", Width = 112, Height = 34, Background = Brush("#061A3D"), Foreground = Brushes.White, BorderBrush = Brush("#061A3D"), IsDefault = false, IsEnabled = valid };
        import.Click += (_, _) =>
        {
            if (MessageBox.Show(this, $"検証済みの{preview.Orders.Count}件をデータベースに登録します。続行しますか？", "CSV取込の確認", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(import);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
    }

    private static void AddPreviewColumn(DataGrid grid, string header, string path, double width, string? format = null) => AddPreviewColumn(grid, header, path, new DataGridLength(width), format);

    private static void AddPreviewColumn(DataGrid grid, string header, string path, DataGridLength width, string? format = null)
    {
        var binding = new System.Windows.Data.Binding(path);
        if (format != null) binding.StringFormat = format;
        grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = binding, Width = width });
    }

    private static SolidColorBrush Brush(string value) => (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;

    private sealed class CsvImportPreviewRow
    {
        public string 外部受注番号 { get; init; } = "";
        public string 受注日 { get; init; } = "";
        public string 納期 { get; init; } = "";
        public string 営業所 { get; init; } = "";
        public string 担当者 { get; init; } = "";
        public string 顧客名 { get; init; } = "";
        public int 明細数 { get; init; }
        public decimal 合計金額 { get; init; }
    }
}

public sealed class OrderPrintRow
{
    public int OrderId { get; init; }
    public DateTime OrderDate { get; init; }
    public string DueDate { get; init; } = "未設定";
    public string CustomerName { get; init; } = "";
    public decimal Total { get; init; }
    public string Status { get; init; } = "";
}

