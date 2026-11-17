using System.Globalization;
using System.IO;
using System.Printing;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace SalesManagement_SysDev;

internal static class PurchaseOrderCsv
{
    public static void Save(Window? owner, IReadOnlyList<PurchaseOrderRecord> rows)
    {
        if (rows.Count == 0 || !PurchaseOrderService.CanView) return;
        var dialog = new SaveFileDialog
        {
            FileName = $"発注一覧_{DateTime.Today:yyyyMMdd}.csv",
            Filter = "CSV ファイル (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true
        };
        if (dialog.ShowDialog(owner) != true) return;
        try
        {
            using (var output = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true)))
                Write(output, rows);
            F_OrderNotice.Show(owner, "CSV出力", $"{rows.Count:N0} 件を保存しました。", OrderNoticeKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            F_OrderNotice.Show(owner, "CSV出力", "CSVを保存できませんでした。保存先を確認してください。", OrderNoticeKind.Error);
        }
    }

    internal static void Write(TextWriter output, IEnumerable<PurchaseOrderRecord> rows)
    {
        output.WriteLine("発注番号,発注日,仕入先,発注担当者,明細数,発注数量,入庫済数量,未入庫数量,入庫状況,管理状態");
        foreach (var row in rows)
        {
            output.WriteLine(string.Join(",", Escape(row.Code), row.OrderDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
                Escape(row.MakerName), Escape(row.EmployeeName), row.ProductLineCount, row.OrderedQuantity, row.ReceivedQuantity,
                row.RemainingQuantity, Escape(row.ProgressLabel), Escape(row.ManagementLabel)));
        }
    }

    private static string Escape(string value)
    {
        var start = value.TrimStart();
        if (start.Length > 0 && start[0] is '=' or '+' or '-' or '@' or '＝' or '＋' or '－' or '＠') value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

internal sealed class PurchaseOrderPrintPreview : Window
{
    internal const double A4Width = 793.7;
    internal const double A4Height = 1122.5;
    private readonly FixedDocument document;

    internal PurchaseOrderPrintPreview(PurchaseOrderDetail detail)
    {
        Title = $"発注書 印刷プレビュー - {detail.Order.Code}";
        Background = AppTheme.Canvas;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _ = new RegistrationWindowPlacement(this, 980, 780, 620, 460);
        document = CreateDocument(detail);

        var root = new DockPanel { Margin = new Thickness(12) };
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var close = new Button { Content = "閉じる", IsCancel = true, Height = 34, Margin = new Thickness(0, 0, 6, 0) };
        close.Click += (_, _) => Close();
        var print = new Button { Content = "印刷", Height = 34, Background = AppTheme.Primary, Foreground = Brushes.White };
        print.Click += (_, _) => Print(detail.Order.Code);
        buttons.Children.Add(close);
        buttons.Children.Add(print);
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        bar.Children.Add(new TextBlock { Text = $"{detail.Order.Code}　A4 縦", VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        var viewer = new DocumentViewer { Document = document };
        CommandManager.AddPreviewExecutedHandler(viewer, (_, e) =>
        {
            if (e.Command != ApplicationCommands.Print) return;
            e.Handled = true;
            Print(detail.Order.Code);
        });
        root.Children.Add(viewer);
        Content = root;
    }

    internal static FixedDocument CreateDocument(PurchaseOrderDetail detail)
    {
        const int linesPerPage = 21;
        var pageCount = Math.Max(1, (detail.Lines.Count + linesPerPage - 1) / linesPerPage);
        var output = new FixedDocument();
        output.DocumentPaginator.PageSize = new Size(A4Width, A4Height);
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var page = new FixedPage
            {
                Width = A4Width, Height = A4Height, Background = Brushes.White,
                Language = XmlLanguage.GetLanguage("ja-JP")
            };
            var body = new StackPanel { Width = A4Width - 84 };
            TextElement.SetFontFamily(body, AppTypography.FontFamily);
            FixedPage.SetLeft(body, 42);
            FixedPage.SetTop(body, 42);
            page.Children.Add(body);
            body.Children.Add(Text("発 注 書", 25, HorizontalAlignment.Center, bold: true));
            body.Children.Add(Text($"{detail.Order.Code}　　{detail.Order.OrderDate:yyyy/MM/dd}　　{pageIndex + 1} / {pageCount}", 12, HorizontalAlignment.Right));
            body.Children.Add(Text($"{detail.Order.MakerName}　御中", 18, bold: true));
            if (!string.IsNullOrWhiteSpace(detail.SupplierPhone))
                body.Children.Add(Text($"仕入先電話　{detail.SupplierPhone}", 12));
            body.Children.Add(Text($"発注担当者　{detail.Order.EmployeeName}", 13, HorizontalAlignment.Right));
            if (!string.IsNullOrWhiteSpace(detail.OfficeName))
                body.Children.Add(Text($"担当者の現在の営業所　{detail.OfficeName}　{detail.OfficePhone}", 12, HorizontalAlignment.Right));
            body.Children.Add(Text("仕入単価・金額・納品予定日は未記録です。", 11));

            body.Children.Add(DocumentRow(["No.", "商品コード", "商品名", "数量"], header: true));
            var lines = detail.Lines.Skip(pageIndex * linesPerPage).Take(linesPerPage);
            var number = pageIndex * linesPerPage;
            foreach (var line in lines)
                body.Children.Add(DocumentRow([(++number).ToString(CultureInfo.InvariantCulture), line.ProductCode,
                    line.ProductName, line.OrderedQuantity.ToString("N0")]));
            body.Children.Add(Text($"明細 {detail.Order.ProductLineCount:N0} 行　合計数量 {detail.Order.OrderedQuantity:N0}",
                13, HorizontalAlignment.Right, bold: true));
            var pageContent = new PageContent();
            ((IAddChild)pageContent).AddChild(page);
            output.Pages.Add(pageContent);
        }
        return output;
    }

    private static Border DocumentRow(string[] values, bool header = false)
    {
        var row = new Grid();
        foreach (var size in new[] { new GridLength(46), new GridLength(134), new GridLength(1, GridUnitType.Star), new GridLength(88) })
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = size });
        for (var i = 0; i < values.Length; i++)
        {
            var cell = Text(values[i], 12, i == 3 ? HorizontalAlignment.Right : HorizontalAlignment.Left, header);
            cell.Margin = new Thickness(7, 0, 7, 0);
            cell.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(cell, i);
            row.Children.Add(cell);
        }
        return new Border
        {
            Child = row, Height = 30, BorderBrush = AppTheme.Border,
            BorderThickness = new Thickness(1, header ? 1 : 0, 1, 1),
            Background = header ? AppTheme.Subtle : Brushes.White
        };
    }

    private static TextBlock Text(string value, int size, HorizontalAlignment alignment = HorizontalAlignment.Left, bool bold = false) =>
        new()
        {
            Text = value, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = AppTheme.Primary, HorizontalAlignment = alignment,
            TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 3)
        };

    private void Print(string orderCode)
    {
        if (!PurchaseOrderService.CanView) return;
        try
        {
            var dialog = new PrintDialog { PrintTicket = new PrintTicket { PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4), PageOrientation = PageOrientation.Portrait } };
            if (dialog.ShowDialog() != true) return;
            var ticket = dialog.PrintTicket ?? new PrintTicket();
            ticket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
            ticket.PageOrientation = PageOrientation.Portrait;
            dialog.PrintTicket = ticket;
            dialog.PrintDocument(document.DocumentPaginator, $"発注書 {orderCode}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            F_OrderNotice.Show(this, "発注書", "印刷を開始できませんでした。プリンターの設定を確認してください。", OrderNoticeKind.Error);
        }
    }
}
