using System.IO;
using System.Printing;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SalesManagement_SysDev;

internal static class Program
{
    private static readonly Type Report = typeof(OrderListPrintPreviewWindow).Assembly.GetType("SalesManagement_SysDev.OrderListReport", throwOnError: true)!;
    private static readonly DateTime PrintedAt = new(2026, 11, 16, 14, 20, 0);
    private const string Yen = "¥98,765,432,100";

    [STAThread]
    private static void Main(string[] args)
    {
        var area = (Rect)Report.GetField("DefaultArea", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var rows = Enumerable.Range(1, 102).Reverse().Select(i => new OrderPrintRow
        {
            OrderId = i, OrderDate = PrintedAt.Date, DueDate = "2026-11-20",
            CustomerName = i % 2 == 0 ? string.Concat(Enumerable.Repeat("関西オフィス機器販売株式会社", 7))
                : "Example International Business Equipment Distribution and Technical Support Company Limited",
            Total = 98765432100m, Status = "確定"
        }).ToArray();
        foreach (var count in new[] { 0, 1, 10, 102 })
            CheckPages(rows.Take(count).ToArray(), area, count > 10 ? 4 : 1);
        CheckPages(rows, new Rect(68, 42, 880, 600), 6);

        if (args.Length == 0) return;
        Directory.CreateDirectory(args[0]);
        using var queue = new LocalPrintServer().GetPrintQueue("Microsoft Print to PDF");
        var (ticket, imageable) = Invoke<(PrintTicket, Rect)>("ReadPrinter", queue, queue.UserPrintTicket);
        var document = CheckPages(rows, imageable, expectedPages: null);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(document.DocumentPaginator.PageSize.Width),
            (int)Math.Ceiling(document.DocumentPaginator.PageSize.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(document.DocumentPaginator.GetPage(0).Visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(args[0], "preview.png"))) encoder.Save(file);
        PdfDriverCheck.Print(document.DocumentPaginator, ticket, Path.Combine(args[0], "order-list.pdf"));
    }

    private static FixedDocument CheckPages(OrderPrintRow[] rows, Rect area, int? expectedPages)
    {
        var document = Invoke<FixedDocument>("Create", rows, "対象：確定済み　　検索条件：営業所", PrintedAt, area);
        var paginator = document.DocumentPaginator;
        Require(Math.Abs(paginator.PageSize.Width * 25.4 / 96 - 297) < .01
            && Math.Abs(paginator.PageSize.Height * 25.4 / 96 - 210) < .01, "A4 landscape size");
        Require(expectedPages == null || paginator.PageCount == expectedPages, "page count");
        var ids = new List<string>();
        for (var i = 0; i < paginator.PageCount; i++)
        {
            var page = (FixedPage)paginator.GetPage(i).Visual;
            var body = (Canvas)page.Children[0];
            Require(Math.Abs(FixedPage.GetTop(body) - area.Top) < .01 && Math.Abs(FixedPage.GetLeft(body) - area.Left) < .01,
                "imageable-area origin");
            var table = body.Children.OfType<Grid>().Where(grid => grid.ColumnDefinitions.Count == 6).ToArray();
            Require(table.Length > 0 && ((TextBlock)((Border)table[0].Children[3]).Child).Text == "顧客名", "repeated table heading");
            foreach (var row in table.Skip(1))
            {
                var cells = row.Children.OfType<Border>().ToArray();
                var customer = (TextBlock)cells[3].Child;
                var total = (TextBlock)((Viewbox)cells[4].Child).Child;
                Require(customer.TextWrapping == TextWrapping.NoWrap && customer.TextTrimming == TextTrimming.CharacterEllipsis
                    && Math.Abs(row.ActualHeight - 22) < .01 && row.ColumnDefinitions[3].ActualWidth > row.ColumnDefinitions[0].ActualWidth * 2
                    && total.Text == Yen && Canvas.GetTop(row) + row.ActualHeight <= body.Height - 22 + .01, "compact row, column widths, and yen amount");
                ids.Add(((TextBlock)cells[0].Child).Text);
            }
            var footer = body.Children.OfType<Grid>().Single(grid => grid.ColumnDefinitions.Count == 0);
            Require(footer.Children.OfType<TextBlock>().Any(text => text.Text == $"{i + 1} / {paginator.PageCount}"), "page numbering");
            if (rows.Length == 0) Require(body.Children.OfType<TextBlock>().Any(text => text.Text == "該当する受注はありません。"), "empty state");
        }
        Require(ids.SequenceEqual(rows.Select(row => $"ORD-{row.OrderId:000000}")), "input order and exactly-once printing");
        Console.WriteLine($"PASS {rows.Length} orders on {paginator.PageCount} pages, area {area.Width:F0} × {area.Height:F0} DIP");
        return document;
    }

    private static T Invoke<T>(string name, params object[] args) => (T)Report.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;

    private static void Require(bool condition, string check)
    {
        if (!condition) throw new InvalidOperationException("Failed: " + check);
    }
}
