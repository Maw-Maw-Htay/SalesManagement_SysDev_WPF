using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.VisualBasic.FileIO;
using SalesManagement_SysDev;

internal static partial class Program
{
    private static readonly DateTime ExportFixtureDate = new(2026, 11, 27, 13, 10, 0);

    private static string ExportDirectory()
    {
        var directory = Path.Combine(output, "order-export");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void CheckOrderWorkspaceExport(SalesManagementContext db, FrameworkElement root, OrderManagementViewModel model,
        TOrder[] orders, (OrderDraftToken Token, OrderDraftForm Form)[] drafts)
    {
        var admin = AppSession.Employee;
        var visible = orders.Where(order => order.OrFlag == 0).OrderByDescending(order => order.OrDate).ThenByDescending(order => order.OrId).ToArray();
        var request = new OrderListRequest(new OrderSearchCriteria { QuickSearch = OrderListFixture }, OrderStatusTab.All,
            model.SortColumn, model.SortDirection);
        var save = Descendants<Button>(root).Single(button => button.Name == "saveOrdersButton");
        Check(save.IsEnabled && save.ContextMenu == null, "the loaded list exposes a single native-dialog toolbar command");
        model.IsExporting = true;
        model.ShowExportProgress = true;
        FlushBindings();
        Check(!save.IsEnabled && Descendants<Border>(root).Single(border => border.Name == "orderExportProgress").IsVisible,
            "long saves show inline progress and disable another save");
        model.ShowExportProgress = false;
        model.IsExporting = false;
        model.SetExportNotice(Path.Combine(ExportDirectory(), "受注一覧_20261127.csv"));
        FlushBindings();
        var toast = Descendants<Border>(root).Single(border => border.Name == "orderExportNotice");
        Check(toast.IsVisible && Descendants<Button>(toast).Count(button => button.IsVisible) == 3,
            "the success toast offers folder, file and dismissal actions");
        model.SetExportNotice();
        var before = (db.TOrders.Count(), db.OrderDrafts.Count(), db.OrderOperationHistories.Count(), db.TStocks.Sum(stock => stock.StQuantity));

        var allRows = ExportCsv(request, expectedQueries: 2);
        Check(Ids(allRows).SequenceEqual(visible.Select(order => order.OrId)), "export without selection includes all matching pages in list order");
        var sample = visible[0];
        var row = allRows[1];
        var subtotal = sample.TOrderDetails.Sum(detail => detail.OrTotalPrice);
        Check(row[1] == sample.OrDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)
            && int.Parse(row[4], CultureInfo.InvariantCulture) == sample.TOrderDetails.Count - 1
            && decimal.Parse(row[7], CultureInfo.InvariantCulture) == subtotal + OrderTax.Calculate(subtotal)
            && row[5] == model.Rows[0].EmployeeName && row[6] == model.Rows[0].OfficeName,
            "exported columns match the displayed active line count, assigned employees, branches and tax");

        var first = model.Rows[0];
        first.IsChecked = true;
        model.NextPageCommand.Execute(null);
        WaitUntil(() => !model.IsLoading);
        var last = model.Rows[^1];
        last.IsChecked = true;
        var selected = model.SelectedOrderIds;
        Check(selected.Count == 2 && Ids(ExportCsv(request, selected, expectedQueries: 2)).SequenceEqual([first.OrderId, last.OrderId]),
            "checked orders on two pages export in the current sort order");
        var reverse = request with { Sort = OrderListSort.OrderId, Direction = ListSortDirection.Ascending };
        Check(Ids(ExportCsv(reverse, selected)).SequenceEqual(selected.Order()), "selected rows use the requested ascending sort, independent of check order");
        model.ClearSelection();
        model.GoToPageCommand.Execute(new OrderPageLink(1, false));
        WaitUntil(() => !model.IsLoading);

        foreach (var (tab, matches) in new (OrderStatusTab, IEnumerable<TOrder>)[]
        {
            (OrderStatusTab.Pending, visible.Where(order => (order.OrStateFlag ?? 0) == 0)),
            (OrderStatusTab.Confirmed, visible.Where(order => order.OrStateFlag == 1)),
            (OrderStatusTab.Hidden, orders.Where(order => order.OrFlag == 2).OrderByDescending(order => order.OrDate).ThenByDescending(order => order.OrId))
        })
            Check(Ids(ExportCsv(request with { Tab = tab })).SequenceEqual(matches.Select(order => order.OrId)), $"{tab} export respects the status tab");

        var draftRequest = request with { Tab = OrderStatusTab.Draft, Direction = ListSortDirection.Ascending };
        var draftRows = ExportCsv(draftRequest, expectedQueries: 3);
        Check(draftRows.Skip(1).All(entry => entry[0] == "未採番" && entry[8] == "下書き")
            && draftRows.Skip(1).Select(entry => entry[1]).SequenceEqual(drafts.Select(draft => draft.Form.Header.OrderDate.Value!.Value.ToString("yyyy/MM/dd"))),
            "draft export is owner-scoped and sorted without registered orders");
        var twoDrafts = ExportCsv(draftRequest, drafts: [drafts[^1].Token.Id, drafts[0].Token.Id]);
        Check(twoDrafts.Length == 3 && twoDrafts[1][1] == draftRows[1][1] && twoDrafts[2][1] == draftRows[^1][1],
            "selected drafts use their IDs even when every row is unnumbered");
        Check(ExportCsv(draftRequest, drafts: [Guid.NewGuid()]).Length == 1, "a selected draft that no longer matches never falls back to all drafts");

        try
        {
            var sales = ReadEmployee(3);
            AppSession.Employee = sales;
            Check(Ids(ExportCsv(request, visible.Select(order => order.OrId).ToArray())).SequenceEqual(visible.Where(order => order.SoId == sales.SoId).Select(order => order.OrId)),
                "a cross-office selection cannot bypass sales office authorization");
            Check(Ids(ExportCsv(request with { Tab = OrderStatusTab.Hidden })).SequenceEqual(orders
                .Where(order => order.OrFlag == 2 && order.SoId == sales.SoId).OrderByDescending(order => order.OrDate)
                .ThenByDescending(order => order.OrId).Select(order => order.OrId)),
                "the explicit hidden tab retains the salesperson's office scope");
            sales.SoId = admin!.SoId;
            ExpectExportFailure<OrderExportException>(() => ExportCsv(request), "export rejects a session whose office differs from the database");
        }
        finally { AppSession.Employee = admin; }

        Check(before == (db.TOrders.Count(), db.OrderDrafts.Count(), db.OrderOperationHistories.Count(), db.TStocks.Sum(stock => stock.StQuantity)),
            "export does not change orders, drafts, stock or audit records");
        model.SelectTab(OrderStatusTab.History);
        Check(!model.SaveCommand.CanExecute(null), "receipt history cannot be exported by the order-list command");
        model.SelectTab(OrderStatusTab.All);
    }

    private static int[] Ids(string[][] rows) => rows.Skip(1).Select(row => int.Parse(row[0].AsSpan(4), CultureInfo.InvariantCulture)).ToArray();

    private static string[][] ExportCsv(OrderListRequest list, IReadOnlyList<int>? orders = null, IReadOnlyList<Guid>? drafts = null, int? expectedQueries = null)
    {
        var path = Path.Combine(ExportDirectory(), "query-result.csv");
        var request = new OrderExportRequest(list, orders ?? [], drafts ?? [], OrderExportActor.Capture());
        using var observer = new ExportCommands();
        Wait(Task.Run(() => OrderExportService.SaveAsync(request, path, OrderExportFormat.Csv, overwrite: true, ExportFixtureDate, default)));
        if (expectedQueries is int count)
            Check(observer.Count == count, $"{list.Tab} export uses {count} SQL reads including authorization");
        return ReadExportCsv(path);
    }

    private static string[][] ReadExportCsv(string path)
    {
        using var parser = new TextFieldParser(path, Encoding.UTF8) { TrimWhiteSpace = false, HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(",");
        var rows = new List<string[]>();
        while (!parser.EndOfData) rows.Add(parser.ReadFields()!);
        return rows.ToArray();
    }

    private static void CheckOrderExportFiles(bool benchmark)
    {
        var directory = ExportDirectory();
        foreach (var (format, extension) in new[] { (OrderExportFormat.Csv, ".csv"), (OrderExportFormat.Excel, ".xlsx"), (OrderExportFormat.Pdf, ".pdf") })
        {
            var path = Path.Combine(directory, "受注一覧_検証" + extension);
            Wait(Task.Run(async () =>
            {
                await using var stream = File.Create(path);
                await OrderExportService.WriteAsync(stream, format, ExportFixtureRows(72), ExportFixtureDate, default);
            }));
            Check(new FileInfo(path).Length > 100, $"{format} writer completes a populated file");
            if (benchmark) MeasureExport(format, extension);
        }

        var csvPath = Path.Combine(directory, "受注一覧_検証.csv");
        var csv = ReadExportCsv(csvPath);
        Check(File.ReadAllBytes(csvPath).AsSpan(0, 3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf })
            && csv.Length == 73 && csv[0].SequenceEqual(OrderExportService.Headers), "CSV has a UTF-8 BOM and all nine Japanese headers");
        Check(csv[1][3] == "' ＝1+1,\"受注\"\r\n備考" && csv[2][3] == "髙﨑・𠮷田電機株式会社" && csv[1][7] == "1359",
            "CSV escapes quotes, commas and line breaks, protects formulas and preserves Japanese text and numeric yen");
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using (var workbook = ZipFile.OpenRead(Path.Combine(directory, "受注一覧_検証.xlsx")))
        using (var worksheet = workbook.GetEntry("xl/worksheets/sheet1.xml")!.Open())
        {
            var xml = XDocument.Load(worksheet);
            var rows = xml.Descendants(spreadsheet + "row").ToArray();
            var cells = rows[1].Elements(spreadsheet + "c").ToArray();
            Check(rows.Length == 73 && xml.Descendants(spreadsheet + "autoFilter").Single().Attribute("ref")?.Value == "A1:I73"
                && xml.Descendants(spreadsheet + "pane").Single().Attribute("state")?.Value == "frozen",
                "Excel workbook retains all rows, column filtering and the frozen header");
            Check(cells.Select(cell => cell.Attribute("r")?.Value).SequenceEqual("ABCDEFGHI".Select(column => $"{column}2"))
                && cells[1].Element(spreadsheet + "v") != null && cells[4].Value == "1" && cells[7].Value == "1359"
                && cells[3].Attribute("t")?.Value == "inlineStr" && cells[3].Value == " ＝1+1,\"受注\"\r\n備考",
                "Excel uses typed dates, numbers and inert text");
        }
        var pdf = Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(directory, "受注一覧_検証.pdf")));
        Check(pdf.StartsWith("%PDF-1.7", StringComparison.Ordinal) && pdf.Contains("/MediaBox [0 0 841.89 595.28]")
            && pdf.Contains("/FontFile2") && pdf.Contains("/ToUnicode") && pdf.Contains("/Count 4"),
            "PDF is A4 landscape, spans pages and embeds a Unicode font");
        CheckAtomicOrderExport(directory);
    }

    private static async IAsyncEnumerable<OrderExportRow> ExportFixtureRows(int count, Action? sampleMemory = null)
    {
        await Task.CompletedTask;
        for (var id = 1; id <= count; id++)
        {
            if (id % 512 == 0) sampleMemory?.Invoke();
            yield return new OrderExportRow(id, ExportFixtureDate.Date, id % 2 == 0 ? null : ExportFixtureDate.AddDays(7).Date,
                id == 1 ? " ＝1+1,\"受注\"\r\n備考" : id == 2 ? "髙﨑・𠮷田電機株式会社" : "北大阪サンプル電機株式会社",
                id, "生田徳次郎", "北大阪営業所", 1235, id % 2 == 0 ? "確定" : "未確定");
        }
    }

    private static void MeasureExport(OrderExportFormat format, string extension)
    {
        const int count = 30_000;
        var path = Path.Combine(ExportDirectory(), "benchmark" + extension);
        var baseBytes = GC.GetTotalMemory(forceFullCollection: true);
        var peakBytes = baseBytes;
        var ticks = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) => ticks++;
        timer.Start();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            Wait(Task.Run(async () =>
            {
                await using var stream = File.Create(path);
                await OrderExportService.WriteAsync(stream, format, ExportFixtureRows(count,
                    () => peakBytes = Math.Max(peakBytes, GC.GetTotalMemory(false))), ExportFixtureDate, default);
            }));
        }
        finally { timer.Stop(); }
        Console.WriteLine(FormattableString.Invariant($"BENCHMARK {format}: {count} rows, {stopwatch.Elapsed.TotalMilliseconds:F0} ms, {new FileInfo(path).Length:N0} bytes, sampled managed heap delta {(peakBytes - baseBytes) / 1048576d:F1} MiB, Dispatcher ticks {ticks}"));
    }

    private static void CheckAtomicOrderExport(string directory)
    {
        var path = Path.Combine(directory, "replace-check.csv");
        File.WriteAllText(path, "original");
        using var cancellation = new CancellationTokenSource();
        ExpectExportFailure<OperationCanceledException>(() => Wait(OrderExportService.WriteAtomicallyAsync(path, true, async stream =>
        {
            await stream.WriteAsync("partial"u8.ToArray());
            cancellation.Cancel();
        }, () => { }, cancellation.Token)), "cancellation during writing leaves the prior file in place");
        Check(File.ReadAllText(path) == "original" && !Directory.EnumerateFiles(directory, ".*.tmp").Any(), "cancelled export removes the temporary file");
        ExpectExportFailure<IOException>(() => Wait(OrderExportService.WriteAtomicallyAsync(path, false,
            stream => stream.WriteAsync("new"u8.ToArray()).AsTask(), () => { }, default)), "a file that appears without overwrite consent is preserved");
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var blocked = false;
            try
            {
                Wait(OrderExportService.WriteAtomicallyAsync(path, true,
                    stream => stream.WriteAsync("new"u8.ToArray()).AsTask(), () => { }, default));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { blocked = true; }
            Check(blocked, "a locked file cannot be overwritten");
        }
        Check(File.ReadAllText(path) == "original", "collision and lock failures preserve existing contents");
        Wait(OrderExportService.WriteAtomicallyAsync(path, true, stream => stream.WriteAsync("complete"u8.ToArray()).AsTask(), () => { }, default));
        Check(File.ReadAllText(path) == "complete" && !Directory.EnumerateFiles(directory, ".*.tmp").Any(), "authorized overwrite replaces the file only after successful output");
        ExpectExportFailure<OrderExportException>(() => OrderExportService.ValidatePath(path, OrderExportFormat.Pdf), "a mismatched extension is rejected");
        ExpectExportFailure<OrderExportException>(() => OrderExportService.ValidatePath("relative.csv", OrderExportFormat.Csv), "a relative path is rejected");
    }

    private static void ExpectExportFailure<T>(Action export, string message) where T : Exception
    {
        try { export(); }
        catch (T) { Check(true, message); return; }
        Check(false, message);
    }

    private sealed class ExportCommands : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly IDisposable all;
        private IDisposable? subscription;
        private int count;
        public int Count => count;
        public ExportCommands() => all = DiagnosticListener.AllListeners.Subscribe(this);
        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "Microsoft.EntityFrameworkCore") subscription = listener.Subscribe(this);
        }
        public void OnNext(KeyValuePair<string, object?> entry)
        {
            if (entry.Key == RelationalEventId.CommandExecuted.Name) Interlocked.Increment(ref count);
        }
        public void OnError(Exception error) { }
        public void OnCompleted() { }
        public void Dispose() { subscription?.Dispose(); all.Dispose(); }
    }
}
