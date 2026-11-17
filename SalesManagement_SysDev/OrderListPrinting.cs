using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

namespace SalesManagement_SysDev;

public sealed class OrderListPrintPreviewWindow : Window
{
    private readonly OrderPrintRow[] rows;
    private readonly string filter;
    private readonly DateTime printedAt = DateTime.Now;
    private readonly PrintDialog dialog = new() { UserPageRangeEnabled = false };
    private readonly DocumentViewer viewer = new() { Background = new SolidColorBrush(Color.FromRgb(238, 242, 247)) };
    private readonly TextBlock info = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private Rect printArea = OrderListReport.DefaultArea;

    public OrderListPrintPreviewWindow(IReadOnlyCollection<OrderPrintRow> rows, string filter = "")
    {
        this.rows = rows.ToArray();
        this.filter = filter;
        Title = "受注一覧 印刷プレビュー（A4横）";
        Width = 1100;
        Height = 760;
        MinWidth = 680;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(toolbar, Dock.Top);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(buttons, Dock.Right);
        var print = new Button { Content = "印刷...", MinWidth = 92, Margin = new Thickness(0, 0, 8, 0) };
        print.Click += PrintDocument;
        buttons.Children.Add(print);
        buttons.Children.Add(new Button { Content = "閉じる", MinWidth = 82, IsCancel = true });
        toolbar.Children.Add(buttons);
        toolbar.Children.Add(info);
        root.Children.Add(toolbar);
        root.Children.Add(viewer);
        // Keep the installed viewer template, while routing its Print command to the same dialog.
        viewer.CommandBindings.Add(new System.Windows.Input.CommandBinding(System.Windows.Input.ApplicationCommands.Print, PrintDocument));
        Content = root;
        Loaded += (_, _) => viewer.FitToWidth();

        try
        {
            if (dialog.PrintQueue is { } queue)
            {
                var settings = OrderListReport.ReadPrinter(queue, dialog.PrintTicket);
                dialog.PrintTicket = settings.Ticket;
                printArea = settings.Area;
            }
        }
        catch (Exception ex) when (IsPrinterError(ex))
        {
            // A preview is still useful before a compatible printer has been chosen.
            dialog.PrintTicket = OrderListReport.A4LandscapeTicket();
        }
        ShowPages($"A4 横向き　{rows.Count:N0} 件");
    }

    private void ShowPages(string message)
    {
        viewer.Document = OrderListReport.Create(rows, filter, printedAt, printArea);
        info.Text = message;
    }

    private void PrintDocument(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        try
        {
            if (dialog.ShowDialog() != true) return;
            var settings = OrderListReport.ReadPrinter(dialog.PrintQueue, dialog.PrintTicket);
            dialog.PrintTicket = settings.Ticket;
            dialog.PageRangeSelection = PageRangeSelection.AllPages;
            if (settings.Area != printArea)
            {
                printArea = settings.Area;
                ShowPages("プリンターの余白を適用しました。プレビューを確認し、もう一度「印刷...」を押してください。");
                return;
            }

            dialog.PrintDocument(viewer.Document.DocumentPaginator, "受注一覧");
        }
        catch (Exception ex) when (IsPrinterError(ex))
        {
            MessageBox.Show(this, $"A4横で印刷できませんでした。\n{ex.Message}", "受注一覧の印刷",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static bool IsPrinterError(Exception error) =>
        error is PrintSystemException or System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException;
}

internal static class OrderListReport
{
    internal static readonly Size Paper = new(297 * 96 / 25.4, 210 * 96 / 25.4);
    internal static readonly Rect DefaultArea = new(32, 32, Paper.Width - 64, Paper.Height - 64);
    private const double HeadingHeight = 94;
    private const double TableHeaderHeight = 27;
    private const double RowHeight = 22;
    private const double FooterHeight = 22;
    private static readonly double[] ColumnShares = [.15, .13, .13, .34, .15, .10];
    private static readonly string[] Labels = ["受注番号", "受注日", "納期", "顧客名", "受注合計金額", "受注状態"];
    private static readonly FontFamily ReportFont = new("Meiryo");
    private static readonly Brush Ink = Brushes.Black;
    private static readonly Brush Rule = Brushes.LightGray;
    private static readonly Brush Header = Brushes.WhiteSmoke;
    private static readonly CultureInfo Japanese = CultureInfo.GetCultureInfo("ja-JP");

    internal static PrintTicket A4LandscapeTicket() => new()
    {
        PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4),
        PageOrientation = PageOrientation.Landscape,
        PagesPerSheet = 1,
        PageScalingFactor = 100
    };

    internal static (PrintTicket Ticket, Rect Area) ReadPrinter(PrintQueue queue, PrintTicket preferences)
    {
        var ticket = queue.MergeAndValidatePrintTicket(preferences, A4LandscapeTicket()).ValidatedPrintTicket;
        if (ticket.PageMediaSize?.PageMediaSizeName != PageMediaSizeName.ISOA4 || ticket.PageOrientation != PageOrientation.Landscape
            || ticket.PagesPerSheet is > 1 || ticket.PageScalingFactor is int scale && scale != 100)
            throw new InvalidOperationException("A4横・1ページ/枚に対応したプリンターを選択してください。");
        if (queue.GetPrintCapabilities(ticket).PageImageableArea is not { ExtentWidth: > 0, ExtentHeight: > 0 } reported)
            throw new InvalidOperationException("プリンターの印刷可能領域を取得できませんでした。");

        var area = Rect.Intersect(DefaultArea, new Rect(reported.OriginWidth, reported.OriginHeight,
            reported.ExtentWidth, reported.ExtentHeight));
        if (area.IsEmpty || area.Width < 700 || area.Height < HeadingHeight + TableHeaderHeight + RowHeight + FooterHeight)
            throw new InvalidOperationException("プリンターの印刷可能領域が受注一覧には狭すぎます。");
        return (ticket, area);
    }

    internal static FixedDocument Create(IReadOnlyList<OrderPrintRow> rows, string filter, DateTime printedAt, Rect area)
    {
        var perPage = (int)Math.Floor((area.Height - HeadingHeight - TableHeaderHeight - FooterHeight) / RowHeight);
        var pageCount = Math.Max(1, (rows.Count + perPage - 1) / perPage);
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = Paper;
        for (var index = 0; index < pageCount; index++)
        {
            var page = new FixedPage { Width = Paper.Width, Height = Paper.Height, Background = Brushes.White, Language = XmlLanguage.GetLanguage("ja-JP") };
            var body = new Canvas { Width = area.Width, Height = area.Height, ClipToBounds = true };
            FixedPage.SetLeft(body, area.Left);
            FixedPage.SetTop(body, area.Top);
            page.Children.Add(body);

            Add(body, Line("受注一覧", 25, bold: true), 0, 0, area.Width);
            Add(body, Line($"出力日時：{printedAt:yyyy/MM/dd HH:mm}　　件数：{rows.Count:N0} 件", 13), 0, 37, area.Width);
            if (filter.Length > 0) Add(body, Line(filter, 12), 0, 62, area.Width);
            Add(body, TableRow(Labels, area.Width, header: true), 0, HeadingHeight, area.Width);

            var first = index * perPage;
            var end = Math.Min(rows.Count, first + perPage);
            for (var i = first; i < end; i++)
            {
                var row = rows[i];
                string[] fields = [$"ORD-{row.OrderId:000000}", row.OrderDate.ToString("yyyy-MM-dd"), row.DueDate,
                    row.CustomerName, row.Total.ToString("¥#,##0;−¥#,##0;¥0", Japanese), row.Status];
                Add(body, TableRow(fields, area.Width, header: false), 0, HeadingHeight + TableHeaderHeight + (i - first) * RowHeight, area.Width);
            }
            if (rows.Count == 0) Add(body, Line("該当する受注はありません。", 13), 8, HeadingHeight + TableHeaderHeight + 8, area.Width - 16);

            var footer = new Grid { Height = FooterHeight };
            footer.Children.Add(new Border { BorderBrush = Rule, BorderThickness = new Thickness(0, 1, 0, 0) });
            footer.Children.Add(Line("販売・在庫管理システム", 11));
            var pageLabel = Line($"{index + 1} / {pageCount}", 12);
            pageLabel.HorizontalAlignment = HorizontalAlignment.Right;
            footer.Children.Add(pageLabel);
            Add(body, footer, 0, area.Height - FooterHeight, area.Width);
            document.Pages.Add(new PageContent { Child = page });
        }
        return document;
    }

    private static Grid TableRow(string[] fields, double width, bool header)
    {
        var row = new Grid { Height = header ? TableHeaderHeight : RowHeight, Background = header ? Header : Brushes.White };
        for (var i = 0; i < fields.Length; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width * ColumnShares[i]) });
            var text = Line(fields[i], header ? 12 : 13, header);
            FrameworkElement content = !header && i == 4
                ? new Viewbox { Child = text, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }
                : text;
            if (header || i is 1 or 2 or 5) text.TextAlignment = TextAlignment.Center;
            var cell = new Border
            {
                Padding = new Thickness(7, 0, 7, 0), BorderBrush = Rule, ClipToBounds = true,
                BorderThickness = new Thickness(i == 0 ? .6 : 0, header ? .6 : 0, .6, .6), Child = content
            };
            Grid.SetColumn(cell, i);
            row.Children.Add(cell);
        }
        return row;
    }

    private static TextBlock Line(string value, double size, bool bold = false) => new()
    {
        Text = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '),
        FontFamily = ReportFont, FontSize = size, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis
    };

    private static void Add(Canvas canvas, FrameworkElement element, double x, double y, double width)
    {
        element.Width = width;
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        canvas.Children.Add(element);
    }
}
