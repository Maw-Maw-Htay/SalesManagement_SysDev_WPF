using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Markup;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

/// <summary>
/// Reads the latest persisted values for the one order selected in the detail window.
/// This is deliberately separate from the filtered order-list print workflow.
/// </summary>
public static class OrderSlipPrintService
{
    public static bool TryLoadConfirmedOrder(int orderId, out OrderSlipPrintData? slip, out string message)
    {
        slip = null;
        if (!AppSession.CanManageOrders || AppSession.Employee == null)
        {
            message = "受注伝票を印刷する権限がありません。";
            return false;
        }

        using var db = new SalesManagementContext();
        var query = db.TOrders.AsNoTracking()
            .Include(order => order.Cl)
            .Include(order => order.So)
            .Include(order => order.Em)
            .Include(order => order.TOrderDetails)
                .ThenInclude(detail => detail.Pr)
            .Where(order => order.OrId == orderId);
        if (!AppSession.IsAdmin)
            query = query.Where(order => order.SoId == AppSession.Employee.SoId);

        var order = query.SingleOrDefault();
        if (order == null)
        {
            message = "受注が見つからないか、受注伝票を印刷する権限がありません。";
            return false;
        }
        if ((order.OrStateFlag ?? 0) != 1)
        {
            message = "受注確定後に伝票を印刷できます。";
            return false;
        }

        var lines = order.TOrderDetails
            .Where(detail => detail.OrQuantity != 0 || detail.OrTotalPrice != 0m)
            .OrderBy(detail => detail.OrDetailId)
            .Select(detail => new OrderSlipPrintLine
            {
                ProductCode = string.IsNullOrWhiteSpace(detail.Pr.PrJcode) ? detail.Pr.PrModelNumber : detail.Pr.PrJcode,
                ProductName = detail.Pr.PrName,
                UnitPrice = detail.OrQuantity == 0
                    ? detail.Pr.Price
                    : decimal.Round(detail.OrTotalPrice / detail.OrQuantity, 0),
                Quantity = detail.OrQuantity,
                Subtotal = detail.OrTotalPrice
            })
            .ToArray();

        var subtotal = lines.Sum(line => line.Subtotal);
        var tax = OrderTax.Calculate(subtotal);
        slip = new OrderSlipPrintData
        {
            OrderId = order.OrId,
            OrderDate = order.OrDate,
            DeliveryDate = order.OrDeliveryDate,
            CustomerId = order.ClId,
            CustomerName = order.Cl.ClName,
            CustomerAddress = order.Cl.ClAddress,
            CustomerPhone = order.Cl.ClPhone,
            OfficeId = order.SoId,
            OfficeName = order.So.SoName,
            EmployeeId = order.EmId,
            EmployeeName = order.Em.EmName,
            OrderStatus = "確定済み",
            Remarks = order.ClCharge,
            Lines = lines,
            Subtotal = subtotal,
            Tax = tax,
            Total = subtotal + tax
        };
        message = "";
        return true;
    }
}

public sealed class OrderSlipPrintData
{
    public int OrderId { get; init; }
    public DateTime OrderDate { get; init; }
    public DateTime? DeliveryDate { get; init; }
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = "";
    public string CustomerAddress { get; init; } = "";
    public string CustomerPhone { get; init; } = "";
    public int OfficeId { get; init; }
    public string OfficeName { get; init; } = "";
    public int EmployeeId { get; init; }
    public string EmployeeName { get; init; } = "";
    public string OrderStatus { get; init; } = "";
    public string? Remarks { get; init; }
    public IReadOnlyList<OrderSlipPrintLine> Lines { get; init; } = [];
    public decimal Subtotal { get; init; }
    public decimal Tax { get; init; }
    public decimal Total { get; init; }
}

public sealed class OrderSlipPrintLine
{
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
    public decimal Subtotal { get; init; }
}

/// <summary>A page-view preview for one confirmed order slip.</summary>
public sealed class OrderSlipPrintPreviewWindow : Window
{
    private const double A4PortraitWidth = 793.7;
    private const double A4PortraitHeight = 1122.5;
    private const string Navy = "#061A3D";
    private const string Border = "#CBD5E1";
    private const string LabelBackground = "#F1F5F9";
    private const string PaleBlue = "#E3F2FD";
    private static readonly CultureInfo JapaneseCulture = CultureInfo.GetCultureInfo("ja-JP");
    private readonly FlowDocument document;
    private readonly int orderId;

    public OrderSlipPrintPreviewWindow(OrderSlipPrintData slip)
    {
        ArgumentNullException.ThrowIfNull(slip);
        Title = $"受注伝票 印刷プレビュー - ORD-{slip.OrderId:000000}";
        Width = 1000;
        Height = 800;
        MinWidth = 720;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Noto Sans JP, Yu Gothic UI, Meiryo");
        Background = Brush("#EEF2F7");
        _ = new RegistrationWindowPlacement(this, preferredWidth: 1000, preferredHeight: 800, minimumWidth: 720, minimumHeight: 520);
        orderId = slip.OrderId;
        document = CreateDocument(slip);
        BuildUi(slip);
    }

    internal FlowDocument PreviewDocument => document;

    internal static FlowDocument CreateDocument(OrderSlipPrintData slip)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Noto Sans JP, Yu Gothic UI, Meiryo"),
            FontSize = 9.5,
            Foreground = Brush("#1E293B"),
            Background = Brushes.White,
            Language = XmlLanguage.GetLanguage("ja-JP"),
            PageWidth = A4PortraitWidth,
            PageHeight = A4PortraitHeight,
            PagePadding = new Thickness(42, 44, 42, 44),
            ColumnWidth = A4PortraitWidth - 84
        };

        document.Blocks.Add(new Paragraph(new Run("受注伝票"))
        {
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(Navy),
            Margin = new Thickness(0, 0, 0, 2)
        });
        document.Blocks.Add(new Paragraph(new Run($"受注番号　ORD-{slip.OrderId:000000}"))
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#475569"),
            Margin = new Thickness(0, 0, 0, 18)
        });

        document.Blocks.Add(SectionHeading("受注情報"));
        document.Blocks.Add(CreateInformationTable(
            ("受注番号", $"ORD-{slip.OrderId:000000}", "受注日", slip.OrderDate.ToString("yyyy年MM月dd日", JapaneseCulture)),
            ("納品希望日", slip.DeliveryDate?.ToString("yyyy年MM月dd日", JapaneseCulture) ?? "未設定", "受注状態", slip.OrderStatus),
            ("受付営業所", $"{slip.OfficeName}（ID: {slip.OfficeId}）", "受注受付担当者", $"{slip.EmployeeName}（ID: {slip.EmployeeId}）")));

        document.Blocks.Add(SectionHeading("顧客情報"));
        document.Blocks.Add(CreateInformationTable(
            ("顧客ID", slip.CustomerId.ToString(JapaneseCulture), "顧客名", slip.CustomerName),
            ("顧客住所", slip.CustomerAddress, "", ""),
            ("電話番号", slip.CustomerPhone, "", "")));

        document.Blocks.Add(SectionHeading("商品明細"));
        document.Blocks.Add(CreateProductTable(slip.Lines));

        var totalTable = CreateTotalsTable(slip);
        totalTable.Margin = new Thickness(0, 12, 0, string.IsNullOrWhiteSpace(slip.Remarks) ? 0 : 14);
        document.Blocks.Add(totalTable);

        if (!string.IsNullOrWhiteSpace(slip.Remarks))
        {
            document.Blocks.Add(SectionHeading("顧客担当者名"));
            document.Blocks.Add(new Paragraph(new Run(slip.Remarks.Trim()))
            {
                Margin = new Thickness(0),
                Padding = new Thickness(8),
                BorderBrush = Brush(Border),
                BorderThickness = new Thickness(0.7)
            });
        }

        return document;
    }

    private void BuildUi(OrderSlipPrintData slip)
    {
        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());

        var toolbar = new DockPanel { Margin = new Thickness(2, 0, 2, 8) };
        var hint = new TextBlock
        {
            Text = $"A4 縦 · 受注番号 ORD-{slip.OrderId:000000} の伝票",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("#53657C"),
            FontSize = 12
        };
        DockPanel.SetDock(hint, Dock.Left);
        toolbar.Children.Add(hint);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var close = new Button
        {
            Content = "閉じる",
            Width = 84,
            Height = 34,
            Margin = new Thickness(4, 0, 4, 0),
            IsCancel = true
        };
        close.Click += (_, _) => Close();
        var print = new Button
        {
            Content = "印刷",
            Width = 92,
            Height = 34,
            Margin = new Thickness(4, 0, 4, 0),
            Background = Brush(Navy),
            Foreground = Brushes.White,
            BorderBrush = Brush(Navy),
            FontWeight = FontWeights.SemiBold
        };
        print.Click += PrintDocument;
        buttons.Children.Add(close);
        buttons.Children.Add(print);
        DockPanel.SetDock(buttons, Dock.Right);
        toolbar.Children.Add(buttons);
        root.Children.Add(toolbar);

        var reader = new FlowDocumentReader
        {
            Document = document,
            ViewingMode = FlowDocumentReaderViewingMode.Page,
            IsFindEnabled = false,
            IsPrintEnabled = false,
            IsTwoPageViewEnabled = false,
            Background = Brushes.White,
            BorderBrush = Brush("#D5DDE8"),
            BorderThickness = new Thickness(1)
        };
        Grid.SetRow(reader, 1);
        root.Children.Add(reader);
        Content = root;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    private void PrintDocument(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new PrintDialog
            {
                PrintTicket = new PrintTicket
                {
                    PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4),
                    PageOrientation = PageOrientation.Portrait
                }
            };
            if (dialog.ShowDialog() != true) return;

            // Keep the print page in sync with the A4 portrait page shown in the preview.
            var ticket = dialog.PrintTicket ?? new PrintTicket();
            ticket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
            ticket.PageOrientation = PageOrientation.Portrait;
            dialog.PrintTicket = ticket;
            var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
            paginator.PageSize = new Size(A4PortraitWidth, A4PortraitHeight);
            dialog.PrintDocument(paginator, $"受注伝票 ORD-{orderId:000000}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "印刷を開始できませんでした。\n" + ex.Message, "受注伝票の印刷", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static Paragraph SectionHeading(string text) => new(new Run(text))
    {
        FontSize = 12,
        FontWeight = FontWeights.Bold,
        Foreground = Brush(Navy),
        Margin = new Thickness(0, 0, 0, 6),
        Padding = new Thickness(0, 0, 0, 4),
        BorderBrush = Brush("#94A3B8"),
        BorderThickness = new Thickness(0, 0, 0, 0.8),
        KeepWithNext = true
    };

    private static Table CreateInformationTable(
        (string FirstLabel, string FirstValue, string SecondLabel, string SecondValue) first,
        (string FirstLabel, string FirstValue, string SecondLabel, string SecondValue) second,
        (string FirstLabel, string FirstValue, string SecondLabel, string SecondValue) third,
        (string FirstLabel, string FirstValue, string SecondLabel, string SecondValue)? fourth = null)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 14) };
        table.Columns.Add(new TableColumn { Width = new GridLength(90) });
        table.Columns.Add(new TableColumn { Width = new GridLength(205) });
        table.Columns.Add(new TableColumn { Width = new GridLength(98) });
        table.Columns.Add(new TableColumn { Width = new GridLength(316) });
        var group = new TableRowGroup();
        AddInformationRow(group, first);
        AddInformationRow(group, second);
        AddInformationRow(group, third);
        if (fourth.HasValue) AddInformationRow(group, fourth.Value);
        table.RowGroups.Add(group);
        return table;
    }

    private static void AddInformationRow(TableRowGroup group,
        (string FirstLabel, string FirstValue, string SecondLabel, string SecondValue) values)
    {
        var row = new TableRow();
        row.Cells.Add(InformationCell(values.FirstLabel, true));
        if (string.IsNullOrEmpty(values.SecondLabel))
        {
            var value = InformationCell(values.FirstValue, false);
            value.ColumnSpan = 3;
            row.Cells.Add(value);
        }
        else
        {
            row.Cells.Add(InformationCell(values.FirstValue, false));
            row.Cells.Add(InformationCell(values.SecondLabel, true));
            row.Cells.Add(InformationCell(values.SecondValue, false));
        }
        group.Rows.Add(row);
    }

    private static TableCell InformationCell(string value, bool label)
    {
        var paragraph = new Paragraph(new Run(value))
        {
            Margin = new Thickness(0),
            FontWeight = label ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = label ? Brush(Navy) : Brush("#1E293B")
        };
        return new TableCell(paragraph)
        {
            Background = label ? Brush(LabelBackground) : Brushes.White,
            Padding = new Thickness(5, 5, 5, 5),
            BorderBrush = Brush(Border),
            BorderThickness = new Thickness(0.55),
            TextAlignment = TextAlignment.Left
        };
    }

    private static Table CreateProductTable(IReadOnlyList<OrderSlipPrintLine> lines)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 2) };
        table.Columns.Add(new TableColumn { Width = new GridLength(95) });
        table.Columns.Add(new TableColumn { Width = new GridLength(286) });
        table.Columns.Add(new TableColumn { Width = new GridLength(95) });
        table.Columns.Add(new TableColumn { Width = new GridLength(56) });
        table.Columns.Add(new TableColumn { Width = new GridLength(177) });

        var headerGroup = new TableRowGroup();
        var header = new TableRow();
        foreach (var title in new[] { "商品コード", "商品名", "単価", "数量", "税抜小計" })
            header.Cells.Add(ProductCell(title, true, title is "単価" or "税抜小計" ? TextAlignment.Right : TextAlignment.Left));
        headerGroup.Rows.Add(header);
        table.RowGroups.Add(headerGroup);

        var body = new TableRowGroup();
        foreach (var line in lines)
        {
            var row = new TableRow();
            row.Cells.Add(ProductCell(line.ProductCode, false));
            row.Cells.Add(ProductCell(line.ProductName, false));
            row.Cells.Add(ProductCell(Money(line.UnitPrice), false, TextAlignment.Right));
            row.Cells.Add(ProductCell(line.Quantity.ToString("N0", JapaneseCulture), false, TextAlignment.Right));
            row.Cells.Add(ProductCell(Money(line.Subtotal), false, TextAlignment.Right));
            body.Rows.Add(row);
        }
        if (lines.Count == 0)
        {
            var empty = new TableRow();
            var cell = ProductCell("明細はありません。", false);
            cell.ColumnSpan = 5;
            empty.Cells.Add(cell);
            body.Rows.Add(empty);
        }
        table.RowGroups.Add(body);
        return table;
    }

    private static TableCell ProductCell(string value, bool header, TextAlignment alignment = TextAlignment.Left)
    {
        var paragraph = new Paragraph(new Run(value))
        {
            Margin = new Thickness(0),
            FontSize = header ? 9 : 9.25,
            FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = header ? Brushes.White : Brush("#1E293B"),
            TextAlignment = alignment
        };
        return new TableCell(paragraph)
        {
            Background = header ? Brush(Navy) : Brushes.White,
            Padding = new Thickness(4, 4, 4, 4),
            BorderBrush = Brush(Border),
            BorderThickness = new Thickness(0.55),
            TextAlignment = alignment
        };
    }

    private static Table CreateTotalsTable(OrderSlipPrintData slip)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0) };
        table.Columns.Add(new TableColumn { Width = new GridLength(524) });
        table.Columns.Add(new TableColumn { Width = new GridLength(185) });
        var group = new TableRowGroup();
        group.Rows.Add(TotalRow("税抜小計", Money(slip.Subtotal)));
        group.Rows.Add(TotalRow("消費税（10%）", Money(slip.Tax)));
        group.Rows.Add(TotalRow("税込合計", Money(slip.Total), true));
        table.RowGroups.Add(group);
        return table;
    }

    private static TableRow TotalRow(string label, string value, bool grandTotal = false)
    {
        var background = grandTotal ? Brush(PaleBlue) : Brushes.White;
        var foreground = grandTotal ? Brush(Navy) : Brush("#1E293B");
        var fontSize = grandTotal ? 13 : 10;
        var weight = grandTotal ? FontWeights.Bold : FontWeights.Normal;
        return new TableRow
        {
            Background = background,
            Cells =
            {
                new TableCell(new Paragraph(new Run(label))
                {
                    Margin = new Thickness(0), FontSize = fontSize, FontWeight = weight,
                    Foreground = foreground, TextAlignment = TextAlignment.Right
                }) { Padding = new Thickness(8, 5, 8, 5), BorderBrush = Brush(Border), BorderThickness = new Thickness(0.55), Background = background },
                new TableCell(new Paragraph(new Run(value))
                {
                    Margin = new Thickness(0), FontSize = fontSize, FontWeight = weight,
                    Foreground = foreground, TextAlignment = TextAlignment.Right
                }) { Padding = new Thickness(8, 5, 8, 5), BorderBrush = Brush(Border), BorderThickness = new Thickness(0.55), Background = background }
            }
        };
    }

    private static string Money(decimal value) => $"¥{value.ToString("N0", JapaneseCulture)}";
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color)!);
}
