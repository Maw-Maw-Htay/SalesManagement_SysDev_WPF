using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public enum InventoryCondition { Normal, Low, Empty, Unavailable }
public enum InventorySort { Code, Name, Category, Quantity, SafetyStock, Condition }
public enum InventoryStockState { NotRequested, Available, MissingRecord, InactiveRecord, DuplicateActiveRecords, InvalidQuantity, QueryFailed }
public readonly record struct InventoryStockLookup(InventoryStockState State, int? Quantity = null);

public sealed record InventoryRowData(
    int? StockId, int ProductId, string ProductCode, string ProductName, string Maker,
    string Classification, string ModelNumber, string Color, DateTime ReleaseDate,
    long? Quantity, int? SafetyStock, int ManagementFlag)
{
    public InventoryStockState? LookupState { get; init; }
    public InventoryStockState StockState => LookupState ?? (Quantity switch
    {
        null => InventoryStockState.MissingRecord,
        < 0 => InventoryStockState.InvalidQuantity,
        _ => InventoryStockState.Available
    });
    public string DisplayCode => $"P-{ProductId:0000}";
    public InventoryCondition Condition => InventoryService.ClassifyStock(Quantity, SafetyStock);
    public string ConditionLabel => Condition switch
    {
        InventoryCondition.Empty => "在庫切れ",
        InventoryCondition.Low => "在庫不足",
        InventoryCondition.Normal => "正常",
        InventoryCondition.Unavailable when StockState == InventoryStockState.MissingRecord => "未登録",
        _ => "要確認"
    };
    public string ConditionDetail => StockState == InventoryStockState.Available
        ? Condition == InventoryCondition.Unavailable ? "安全在庫数を確認してください" : ConditionLabel
        : InventoryService.DescribeUnavailableStock(StockState);
    public string QuantityText => Quantity is >= 0 ? Quantity.Value.ToString("N0")
        : StockState == InventoryStockState.MissingRecord ? "未登録" : "—";
    public string SafetyStockText => SafetyStock is >= 0 ? SafetyStock.Value.ToString("N0") : "未設定";
    public DateTime? LastUpdatedAt => null; // T_Stock has no recorded update time.
    public string LastUpdatedText => LastUpdatedAt?.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture) ?? "未記録";
    public string ManagementLabel => ManagementFlag == 0 ? "表示中" : "要確認";
    public bool IsMarked { get; set; }
}

public sealed record InventoryFilters(string QuickSearch, string Code, string Name, string Maker, string Category,
    int? Minimum, int? Maximum, InventoryCondition? Condition, InventorySort Sort, bool Descending);

public sealed record InventorySummary(int Registered, int Normal, int Low, int Empty, long TotalQuantity);
public sealed record InventoryPage(IReadOnlyList<InventoryRowData> Rows, int MatchCount, int Number, InventorySummary? Summary);

/// <summary>Reads and maintains shared product stock balances.</summary>
public static partial class InventoryService
{
    public static bool CanView => AppSession.Employee is { EmFlag: 0 }
        && (AppSession.IsAdmin || AppSession.IsLogistics);

    internal static InventoryCondition ClassifyStock(long? quantity, int? safetyStock) => quantity switch
    {
        0 => InventoryCondition.Empty,
        > 0 when safetyStock is >= 0 => quantity < safetyStock ? InventoryCondition.Low : InventoryCondition.Normal,
        _ => InventoryCondition.Unavailable
    };

    internal static IReadOnlyDictionary<int, InventoryStockLookup> ReadCurrentStock(
        SalesManagementContext db, IReadOnlyCollection<int> productIds)
    {
        var ids = productIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<int, InventoryStockLookup>();

        // Order screens provide product IDs from their own authorized catalog or order.
        // T_Stock stores product-level balances and has no branch or warehouse key.
        try
        {
            var balances = QueryStockBalances(db.TStocks.AsNoTracking().Where(stock => ids.Contains(stock.PrId)))
                .ToDictionary(row => row.ProductId!.Value, row => new InventoryStockLookup(row.State, row.Quantity));
            foreach (var id in ids)
                balances.TryAdd(id, new InventoryStockLookup(InventoryStockState.MissingRecord));
            return balances;
        }
        catch (Exception exception)
        {
            Trace.TraceError("在庫情報の取得に失敗しました (PrID={0}): {1}", string.Join(",", ids), exception);
            return ids.ToDictionary(id => id, _ => new InventoryStockLookup(InventoryStockState.QueryFailed));
        }
    }

    internal static string DescribeUnavailableStock(InventoryStockState state) => state switch
    {
        InventoryStockState.MissingRecord => "在庫情報が未登録です",
        InventoryStockState.InactiveRecord => "有効な在庫情報がありません",
        InventoryStockState.DuplicateActiveRecords => "在庫情報が重複しています",
        InventoryStockState.QueryFailed => "在庫情報の取得に失敗しました",
        _ => "在庫情報を確認できません"
    };

    public static async Task<InventoryPage> LoadPageAsync(InventoryFilters filters, int page, int pageSize,
        bool includeSummary, CancellationToken cancellationToken)
    {
        EnsureCanView();
        using var db = new SalesManagementContext();
        return await ReadPageAsync(db, filters, page, pageSize, includeSummary, cancellationToken);
    }

    internal static async Task<InventoryPage> ReadPageAsync(SalesManagementContext db, InventoryFilters filters,
        int page, int pageSize, bool includeSummary, CancellationToken cancellationToken)
    {
        var balances = QueryBalances(db);
        var summary = includeSummary
            ? await balances.GroupBy(_ => 1).Select(g => new InventorySummary(g.Count(),
                g.Count(x => x.Condition == InventoryCondition.Normal),
                g.Count(x => x.Condition == InventoryCondition.Low),
                g.Count(x => x.Condition == InventoryCondition.Empty),
                g.Sum(x => x.Quantity ?? 0))).SingleOrDefaultAsync(cancellationToken)
                ?? new InventorySummary(0, 0, 0, 0, 0)
            : null;
        var filtered = ApplyFilters(balances, filters);
        var count = await filtered.CountAsync(cancellationToken);
        page = Math.Clamp(page, 1, count == 0 ? 1 : (count - 1) / pageSize + 1);
        var rows = await ProjectRows(SortRows(filtered, filters).Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync(cancellationToken);
        return new InventoryPage(rows, count, page, summary);
    }

    public static async Task ExportAsync(InventoryFilters filters, TextWriter output, CancellationToken cancellationToken = default)
        => await ExportAsync(filters, null, output, cancellationToken).ConfigureAwait(false);

    internal static async Task ExportAsync(InventoryFilters filters, int[]? productIds, TextWriter output,
        CancellationToken cancellationToken)
    {
        EnsureCanView();
        using var db = new SalesManagementContext();
        var balances = QueryBalances(db);
        if (productIds is not null) balances = balances.Where(x => productIds.Contains(x.ProductId));
        var rows = ProjectRows(SortRows(ApplyFilters(balances, filters), filters));
        await WriteCsvAsync(rows.AsAsyncEnumerable(), output, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task WriteCsvAsync(IAsyncEnumerable<InventoryRowData> rows, TextWriter output,
        CancellationToken cancellationToken)
    {
        await output.WriteLineAsync("商品コード,商品名,カテゴリ,現在庫数,安全在庫数,在庫状態,最終更新日時".AsMemory(), cancellationToken).ConfigureAwait(false);
        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var line = string.Join(",", SpreadsheetCsv.EscapeText(row.DisplayCode), SpreadsheetCsv.EscapeText(row.ProductName),
                SpreadsheetCsv.EscapeText(row.Classification), row.Quantity?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.SafetyStock is >= 0 ? row.SafetyStock.Value.ToString(CultureInfo.InvariantCulture) : "",
                SpreadsheetCsv.EscapeText(row.ConditionLabel), SpreadsheetCsv.EscapeText(row.LastUpdatedAt?.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture) ?? ""));
            await output.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    private static void EnsureCanView()
    {
        if (!CanView) throw new UnauthorizedAccessException("在庫情報を表示する権限がありません。");
    }

    private static IQueryable<Balance> QueryBalances(SalesManagementContext db)
    {
        var currentStock = QueryStockBalances(db.TStocks.AsNoTracking());

        // Active products remain visible even when a trusted balance is unavailable.
        return from product in db.MProducts.AsNoTracking().Where(x => x.PrFlag == ProductService.ActiveFlag
                   && (!db.TStocks.Any(s => s.PrId == x.PrId && s.StFlag == HiddenStockFlag)
                       || db.TStocks.Any(s => s.PrId == x.PrId && s.StFlag == ActiveStockFlag)))
               join balance in currentStock on (int?)product.PrId equals balance.ProductId into balances
               from balance in balances.DefaultIfEmpty()
               select new Balance
               {
                   ProductId = product.PrId, StockId = balance.StockId, Quantity = balance.Quantity,
                   StockState = balance.ProductId == null ? InventoryStockState.MissingRecord : balance.State,
                   DisplayCode = (product.PrId < 10 ? "P-000" : product.PrId < 100 ? "P-00"
                       : product.PrId < 1000 ? "P-0" : "P-") + product.PrId.ToString(),
                   ProductCode = product.PrJcode ?? "", Name = product.PrName, Maker = product.Ma.MaName,
                   Category = product.Sc.Mc.McName + " / " + product.Sc.ScName,
                   ModelNumber = product.PrModelNumber, Color = product.PrColor, ReleaseDate = product.PrReleaseDate,
                   Threshold = (int?)product.PrSafetyStock, ManagementFlag = product.PrFlag,
                   Condition = balance.Quantity == 0 ? InventoryCondition.Empty
                       : balance.Quantity > 0 && (int?)product.PrSafetyStock >= 0
                           ? balance.Quantity < product.PrSafetyStock ? InventoryCondition.Low : InventoryCondition.Normal
                           : InventoryCondition.Unavailable
               };
    }

    private static IQueryable<StockBalance> QueryStockBalances(IQueryable<TStock> stocks) => stocks.GroupBy(stock => stock.PrId)
        .Select(rows => new
        {
            ProductId = rows.Key,
            ActiveCount = rows.Count(stock => stock.StFlag == 0),
            StockId = rows.Max(stock => stock.StFlag == 0 ? (int?)stock.StId : null),
            Quantity = rows.Max(stock => stock.StFlag == 0 ? (int?)stock.StQuantity : null)
        })
        .Select(balance => new StockBalance
        {
            ProductId = balance.ProductId,
            StockId = balance.ActiveCount == 1 ? balance.StockId : null,
            Quantity = balance.ActiveCount == 1 && balance.Quantity >= 0 ? balance.Quantity : null,
            State = balance.ActiveCount == 0 ? InventoryStockState.InactiveRecord
                : balance.ActiveCount > 1 ? InventoryStockState.DuplicateActiveRecords
                : balance.Quantity >= 0 ? InventoryStockState.Available : InventoryStockState.InvalidQuantity
        });

    private static IQueryable<Balance> ApplyFilters(IQueryable<Balance> query, InventoryFilters filters)
    {
        // A numeric suffix from a displayed P- code can be searched with or without its prefix/padding.
        if (filters.QuickSearch.Length > 0)
        {
            var text = filters.QuickSearch;
            var code = text.ToUpperInvariant();
            query = query.Where(x => x.DisplayCode.Contains(code) || x.ProductCode.Contains(text) || x.Name.Contains(text)
                || x.Maker.Contains(text) || x.Category.Contains(text));
        }
        if (filters.Code.Length > 0)
        {
            var code = filters.Code;
            var displayCode = code.ToUpperInvariant();
            query = query.Where(x => x.ProductCode.Contains(code) || x.DisplayCode.Contains(displayCode));
        }
        if (filters.Name.Length > 0) query = query.Where(x => x.Name.Contains(filters.Name));
        if (filters.Maker.Length > 0) query = query.Where(x => x.Maker.Contains(filters.Maker));
        if (filters.Category.Length > 0) query = query.Where(x => x.Category.Contains(filters.Category));
        if (filters.Minimum.HasValue) query = query.Where(x => x.Quantity >= filters.Minimum);
        if (filters.Maximum.HasValue) query = query.Where(x => x.Quantity <= filters.Maximum);
        if (filters.Condition.HasValue) query = query.Where(x => x.Condition == filters.Condition);
        return query;
    }

    private static IOrderedQueryable<Balance> SortRows(IQueryable<Balance> query, InventoryFilters filters)
    {
        var ordered = (filters.Sort, filters.Descending) switch
        {
            (InventorySort.Name, false) => query.OrderBy(x => x.Name),
            (InventorySort.Name, true) => query.OrderByDescending(x => x.Name),
            (InventorySort.Category, false) => query.OrderBy(x => x.Category),
            (InventorySort.Category, true) => query.OrderByDescending(x => x.Category),
            (InventorySort.Quantity, false) => query.OrderBy(x => x.Quantity),
            (InventorySort.Quantity, true) => query.OrderByDescending(x => x.Quantity),
            (InventorySort.SafetyStock, false) => query.OrderBy(x => x.Threshold),
            (InventorySort.SafetyStock, true) => query.OrderByDescending(x => x.Threshold),
            (InventorySort.Condition, false) => query.OrderBy(x => x.Condition),
            (InventorySort.Condition, true) => query.OrderByDescending(x => x.Condition),
            (_, true) => query.OrderByDescending(x => x.ProductId),
            _ => query.OrderBy(x => x.ProductId)
        };
        return filters.Sort == InventorySort.Code ? ordered : ordered.ThenBy(x => x.ProductId);
    }

    private static IQueryable<InventoryRowData> ProjectRows(IQueryable<Balance> rows) => rows.Select(x =>
        new InventoryRowData(x.StockId, x.ProductId, x.ProductCode, x.Name, x.Maker, x.Category,
            x.ModelNumber, x.Color, x.ReleaseDate, x.Quantity, x.Threshold, x.ManagementFlag) { LookupState = x.StockState });

    private sealed class StockBalance
    {
        public int? ProductId { get; init; } // Nullable when the inventory list left-joins a product without any stock rows.
        public int? StockId { get; init; }
        public int? Quantity { get; init; }
        public InventoryStockState State { get; init; }
    }

    private sealed class Balance
    {
        public int ProductId { get; init; }
        public int? StockId { get; init; }
        public string DisplayCode { get; init; } = "";
        public string ProductCode { get; init; } = "";
        public string Name { get; init; } = "";
        public string Maker { get; init; } = "";
        public string Category { get; init; } = "";
        public string ModelNumber { get; init; } = "";
        public string Color { get; init; } = "";
        public DateTime ReleaseDate { get; init; }
        public long? Quantity { get; init; }
        public int? Threshold { get; init; }
        public int ManagementFlag { get; init; }
        public InventoryCondition Condition { get; init; }
        public InventoryStockState StockState { get; init; }
    }
}
