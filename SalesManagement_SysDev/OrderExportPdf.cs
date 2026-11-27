using System.Globalization;
using System.IO;
using System.Text;

namespace SalesManagement_SysDev;

internal static class OrderExportPdf
{
    internal static async Task WriteAsync(Stream output, IAsyncEnumerable<OrderExportRow> rows, DateTime exportDate, CancellationToken token)
    {
        var report = new Report(output);
        await foreach (var row in rows.WithCancellation(token).ConfigureAwait(false))
            report.Add(row, token);
        token.ThrowIfCancellationRequested();
        report.Finish(exportDate, token);
    }

    private sealed class Report
    {
        private const double Margin = 29;
        private const double TableWidth = 784;
        private const double TableTop = 514;
        private const double TableBottom = 44;
        private const double HeaderHeight = 25;
        private const double LineHeight = 13;
        private const double FontSize = 9.25;
        private const string Navy = "0.024 0.102 0.239";
        private const string Ink = "0.10 0.16 0.25";
        private static readonly double[] Widths = [76, 72, 72, 145, 43, 82, 96, 127, 71];

        private readonly PdfDocumentWriter pdf;
        private readonly OrderPdfText text;
        private readonly int pagesId;
        private readonly int resourcesId;
        private readonly int summaryId;
        private readonly List<int> pages = [];
        private readonly StringBuilder content = new(16384);
        private int recordCount;
        private double top;

        public Report(Stream output)
        {
            pdf = new PdfDocumentWriter(output);
            text = new OrderPdfText(pdf);
            pagesId = pdf.ReserveObject();
            resourcesId = pdf.ReserveObject();
            summaryId = pdf.ReserveObject();
            StartPage();
        }

        public void Add(OrderExportRow row, CancellationToken token)
        {
            string[] fields = [row.Number, row.OrderDateText, row.DeliveryDateText, row.Customer,
                row.LineCount.ToString("N0", CultureInfo.InvariantCulture), row.EmployeeName, row.Office,
                row.Total.ToString("¥#,##0;-¥#,##0;¥0", CultureInfo.InvariantCulture), row.Status];
            var cells = fields.Select((field, index) => text.Wrap(field, FontSize, Widths[index] - 10)).ToArray();
            var lines = cells.Max(cell => cell.Count);
            if (lines > AvailableLines && lines <= (TableTop - HeaderHeight - TableBottom - 8) / LineHeight)
                NextPage();

            var offset = 0;
            while (offset < lines)
            {
                token.ThrowIfCancellationRequested();
                if (AvailableLines == 0) NextPage();
                var count = Math.Min(AvailableLines, lines - offset);
                var height = count * LineHeight + 8;
                var bottom = top - height;
                if (recordCount % 2 == 1)
                    content.AppendLine(FormattableString.Invariant($"0.957 0.972 0.992 rg {Margin} {bottom} {TableWidth} {height} re f"));

                var left = Margin;
                for (var column = 0; column < cells.Length; column++)
                {
                    var linesInCell = cells[column];
                    var last = Math.Min(linesInCell.Count, offset + count);
                    for (var index = offset; index < last; index++)
                    {
                        var line = linesInCell[index];
                        var x = column is 4 or 7 ? left + Widths[column] - 5 - line.Width * FontSize : left + 5;
                        text.Draw(content, line.Text, FontSize, x, top - 13 - (index - offset) * LineHeight, Ink);
                    }
                    left += Widths[column];
                }
                Rule(bottom);
                top = bottom;
                offset += count;
            }
            recordCount++;
        }

        private int AvailableLines => Math.Max(0, (int)((top - TableBottom - 8) / LineHeight));

        private void StartPage()
        {
            text.Draw(content, "受注一覧", 21, Margin, 554, Navy);
            content.AppendLine(FormattableString.Invariant($"q 1 0 0 1 {Margin} 531 cm /Summary Do Q"));
            top = TableTop - HeaderHeight;
            content.AppendLine(FormattableString.Invariant($"{Navy} rg {Margin} {top} {TableWidth} {HeaderHeight} re f"));
            var left = Margin;
            for (var i = 0; i < Widths.Length; i++)
            {
                text.Draw(content, OrderExportService.Headers[i], FontSize, left + 5, top + 9, "1 1 1");
                left += Widths[i];
            }
        }

        private void NextPage()
        {
            EndPage();
            StartPage();
        }

        private void Rule(double y) => content.AppendLine(FormattableString.Invariant($"0.82 0.87 0.92 RG 0.4 w {Margin} {y} m {Margin + TableWidth} {y} l S"));

        private void EndPage()
        {
            Rule(37);
            var pageLabel = $"{pages.Count + 1} ページ";
            text.Draw(content, pageLabel, 9, Margin + TableWidth - text.Measure(pageLabel) * 9, 23, Ink);
            var contentsId = pdf.ReserveObject();
            pdf.WriteStream(contentsId, "", Encoding.ASCII.GetBytes(content.ToString()));
            var pageId = pdf.ReserveObject();
            pdf.WriteObject(pageId, $"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 841.89 595.28] /Resources {resourcesId} 0 R /Contents {contentsId} 0 R >>");
            pages.Add(pageId);
            content.Clear();
        }

        public void Finish(DateTime exportDate, CancellationToken token)
        {
            if (recordCount == 0) text.Draw(content, "該当する受注はありません。", FontSize, Margin + 5, top - 14, Ink);
            EndPage();
            // Every page refers to this one form, so the final count comes from the streamed rows.
            text.Draw(content, FormattableString.Invariant($"出力日時  {exportDate:yyyy/MM/dd HH:mm}     件数  {recordCount:N0} 件"), 10, 0, 0, Ink);
            text.WriteFonts(token);
            pdf.WriteStream(summaryId, $"/Type /XObject /Subtype /Form /BBox [0 -4 {TableWidth} 15] /Resources << {text.FontResources} >>",
                Encoding.ASCII.GetBytes(content.ToString()));
            pdf.WriteObject(resourcesId, $"<< {text.FontResources} /XObject << /Summary {summaryId} 0 R >> >>");
            pdf.WriteObject(pagesId, $"<< /Type /Pages /Count {pages.Count} /Kids [{string.Join(' ', pages.Select(pdf.Reference))}] >>");
            pdf.Finish(pagesId, "受注一覧");
        }
    }
}
