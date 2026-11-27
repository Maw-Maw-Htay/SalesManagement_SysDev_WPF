using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

internal enum OrderExportFormat { Pdf = 1, Excel, Csv }

internal sealed class OrderExportException(string message) : Exception(message);

internal sealed record OrderExportRow(int? OrderId, DateTime? OrderDate, DateTime? DeliveryDate, string CustomerName,
    int LineCount, string EmployeeName, string OfficeName, decimal Subtotal, string Status)
{
    public string Number => OrderId is int id ? $"ORD-{id:000000}" : "未採番";
    public string Customer => OrderId == null && string.IsNullOrWhiteSpace(CustomerName) ? "未入力" : CustomerName;
    public string Office => OrderId == null && string.IsNullOrWhiteSpace(OfficeName) ? "未入力" : OfficeName;
    public string OrderDateText => OrderDate?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) ?? "未入力";
    public string DeliveryDateText => DeliveryDate?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) ?? "未設定";
    public decimal Total => Subtotal + OrderTax.Calculate(Subtotal);
}

internal sealed record OrderExportActor(MEmployee Session, int Id, int OfficeId, string Role)
{
    private const string Denied = "受注一覧を保存する権限がありません。";

    public static OrderExportActor Capture()
    {
        if (AppSession.Employee is not { EmFlag: 0, Po: { PoFlag: 0 } position } employee
            || position.PoName is not ("管理者" or "営業")) throw new OrderExportException(Denied);
        return new(employee, employee.EmId, employee.SoId, position.PoName);
    }

    public int? OfficeScope => Role == "管理者" ? null : OfficeId;

    internal async Task VerifyAsync(SalesManagementContext db, CancellationToken token)
    {
        EnsureCurrent();
        var employee = await db.MEmployees.AsNoTracking()
            .Where(e => e.EmId == Id && e.EmFlag == 0 && e.Po.PoFlag == 0)
            .Select(e => new { e.SoId, e.Po.PoName }).SingleOrDefaultAsync(token).ConfigureAwait(false);
        EnsureCurrent();
        if (employee == null || employee.SoId != OfficeId || employee.PoName != Role)
            throw new OrderExportException("ログイン情報が変更されました。ログインし直してください。");
    }

    public void EnsureCurrent()
    {
        var current = Capture();
        if (!ReferenceEquals(current.Session, Session) || current.Id != Id || current.OfficeId != OfficeId || current.Role != Role)
            throw new OrderExportException("ログイン情報が変更されました。ログインし直してください。");
    }
}

internal sealed record OrderExportRequest(OrderListRequest List, IReadOnlyList<int> OrderIds, IReadOnlyList<Guid> DraftIds, OrderExportActor Actor);

internal static class OrderExportService
{
    internal const string FileFilter = "PDF ファイル (*.pdf)|*.pdf|Excel ファイル (*.xlsx)|*.xlsx|CSV ファイル (*.csv)|*.csv";
    internal static IReadOnlyList<string> Headers { get; } =
        ["受注番号", "受注日", "納期", "顧客名", "商品数", "担当者", "営業所", "受注合計金額（税込）", "受注状態"];

    internal static string ValidatePath(string path, OrderExportFormat format)
    {
        var extension = format switch
        {
            OrderExportFormat.Pdf => ".pdf",
            OrderExportFormat.Excel => ".xlsx",
            OrderExportFormat.Csv => ".csv",
            _ => throw new OrderExportException("PDF、Excel、CSV のいずれかを選んでください。")
        };
        if (!Path.IsPathFullyQualified(path) || Path.GetFileName(path).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new OrderExportException("有効な保存場所とファイル名を指定してください。");
        if (!string.Equals(extension, Path.GetExtension(path), StringComparison.OrdinalIgnoreCase))
            throw new OrderExportException($"選択した形式の拡張子（{extension}）をファイル名に指定してください。");
        return Path.GetFullPath(path);
    }

    internal static async Task SaveAsync(OrderExportRequest request, string path, OrderExportFormat format, bool overwrite,
        DateTime exportDate, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var destination = ValidatePath(path, format);
        await WriteAtomicallyAsync(destination, overwrite,
            stream => WriteAsync(stream, format, ReadRowsAsync(request, token), exportDate, token), request.Actor.EnsureCurrent, token)
            .ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<OrderExportRow> ReadRowsAsync(OrderExportRequest request, [EnumeratorCancellation] CancellationToken token)
    {
        if (request.List.Tab == OrderStatusTab.History || (request.List.Tab == OrderStatusTab.Draft
                ? request.OrderIds.Count != 0 : request.DraftIds.Count != 0))
            throw new OrderExportException("保存する受注一覧を表示し直してください。");

        using var db = new SalesManagementContext();
        await request.Actor.VerifyAsync(db, token).ConfigureAwait(false);
        var data = request.List.Tab == OrderStatusTab.Draft
            ? await OrderDraftService.ReadExportQueryAsync(db, request.List, request.Actor.Id, request.Actor.OfficeScope, request.DraftIds, token)
                .ConfigureAwait(false)
            : OrderListQuery.ExportRows(db, request.List, request.Actor.OfficeScope, request.OrderIds);
        if (data == null) yield break;
        await foreach (var row in data.AsAsyncEnumerable().WithCancellation(token).ConfigureAwait(false))
            yield return row;
    }

    internal static async Task WriteAtomicallyAsync(string destination, bool overwrite, Func<Stream, Task> write,
        Action beforeCommit, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".{Guid.NewGuid():N}.tmp");
        var created = false;
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                created = true;
                await write(stream).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            }
            beforeCommit();
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite);
            created = false;
        }
        finally
        {
            if (created)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    internal static Task WriteAsync(Stream stream, OrderExportFormat format, IAsyncEnumerable<OrderExportRow> rows,
        DateTime exportDate, CancellationToken token) => format switch
    {
        OrderExportFormat.Pdf => OrderExportPdf.WriteAsync(stream, rows, exportDate, token),
        OrderExportFormat.Excel => OrderExportWorkbook.WriteAsync(stream, rows, token),
        OrderExportFormat.Csv => WriteCsvAsync(stream, rows, token),
        _ => throw new OrderExportException("保存形式を確認してください。")
    };

    private static async Task WriteCsvAsync(Stream stream, IAsyncEnumerable<OrderExportRow> rows, CancellationToken token)
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 8192, leaveOpen: true);
        await writer.WriteLineAsync(string.Join(",", Headers).AsMemory(), token).ConfigureAwait(false);
        await foreach (var row in rows.WithCancellation(token).ConfigureAwait(false))
        {
            var line = string.Join(",", SpreadsheetCsv.EscapeText(row.Number), row.OrderDateText, row.DeliveryDateText,
                SpreadsheetCsv.EscapeText(row.Customer), row.LineCount.ToString(CultureInfo.InvariantCulture),
                SpreadsheetCsv.EscapeText(row.EmployeeName), SpreadsheetCsv.EscapeText(row.Office),
                row.Total.ToString(CultureInfo.InvariantCulture), SpreadsheetCsv.EscapeText(row.Status));
            await writer.WriteLineAsync(line.AsMemory(), token).ConfigureAwait(false);
        }
        await writer.FlushAsync(token).ConfigureAwait(false);
    }
}
