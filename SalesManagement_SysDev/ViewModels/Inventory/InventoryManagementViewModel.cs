using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;

namespace SalesManagement_SysDev;

public sealed class InventoryManagementViewModel : ViewModelBase
{
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private readonly Func<InventoryFilters, int, int, bool, CancellationToken, Task<InventoryPage>> loadPage;
    private CancellationTokenSource? request;
    private InventoryFilters? displayedFilters;
    private InventorySummary? summary;
    private string quickSearch = "", codeFilter = "", nameFilter = "", makerFilter = "", classificationFilter = "", minQuantity = "", maxQuantity = "", feedback = "";
    private InventoryCondition? selectedCondition;
    private InventorySort sort;
    private bool descending, isFiltersExpanded, isLoading, isExporting, queryPending, summaryNeeded = true;
    private int currentPage = 1, pageSize = 20, matchCount;

    public ObservableCollection<InventoryRowData> Rows { get; } = [];
    public string QuickSearch { get => quickSearch; set { if (Set(ref quickSearch, value ?? "")) ScheduleSearch(); } }
    public string CodeFilter { get => codeFilter; set { if (Set(ref codeFilter, value)) ScheduleSearch(); } }
    public string NameFilter { get => nameFilter; set { if (Set(ref nameFilter, value)) ScheduleSearch(); } }
    public string MakerFilter { get => makerFilter; set { if (Set(ref makerFilter, value)) ScheduleSearch(); } }
    public string ClassificationFilter { get => classificationFilter; set { if (Set(ref classificationFilter, value)) ScheduleSearch(); } }
    public string MinQuantity { get => minQuantity; set { if (Set(ref minQuantity, value)) ScheduleSearch(); } }
    public string MaxQuantity { get => maxQuantity; set { if (Set(ref maxQuantity, value)) ScheduleSearch(); } }
    public string Feedback { get => feedback; private set => Set(ref feedback, value); }
    public bool IsFiltersExpanded { get => isFiltersExpanded; set { if (Set(ref isFiltersExpanded, value)) Notify(nameof(FiltersToggleLabel)); } }
    public string FiltersToggleLabel => IsFiltersExpanded ? "詳細条件を折りたたむ" : "詳細条件を展開";
    public bool CanReload => !isExporting;
    public bool CanExport => !isLoading && !queryPending && !isExporting && displayedFilters != null && matchCount > 0;
    public bool ShowEmpty => !isLoading && !queryPending && Rows.Count == 0 && Feedback.Length == 0;
    public bool HasConditions => !string.IsNullOrWhiteSpace(QuickSearch) || !string.IsNullOrWhiteSpace(CodeFilter) || !string.IsNullOrWhiteSpace(NameFilter)
        || !string.IsNullOrWhiteSpace(MakerFilter) || !string.IsNullOrWhiteSpace(ClassificationFilter)
        || !string.IsNullOrWhiteSpace(MinQuantity) || !string.IsNullOrWhiteSpace(MaxQuantity);
    public int? TotalCount => summary?.Registered;
    public int? NormalCount => summary?.Normal;
    public int? LowCount => summary?.Low;
    public int? EmptyCount => summary?.Empty;
    public long? TotalQuantity => summary?.TotalQuantity;
    public string CountText => TotalCount is int total ? $"{matchCount:N0} 件／全 {total:N0} 件" : "";
    public int CurrentPage => currentPage;
    public int PageCount => matchCount == 0 ? 1 : (matchCount - 1) / pageSize + 1;
    public IReadOnlyList<int> PageSizes { get; } = [20, 50, 100];
    public int PageSize
    {
        get => pageSize;
        set { if (PageSizes.Contains(value) && Set(ref pageSize, value)) _ = RefreshAsync(1); }
    }
    public bool CanPrevious => !isLoading && !queryPending && currentPage > 1;
    public bool CanNext => !isLoading && !queryPending && currentPage < PageCount;
    public string PageRangeText => matchCount == 0 || Rows.Count == 0 ? "0件 / 全0件"
        : $"{(currentPage - 1) * pageSize + 1:N0}～{(currentPage - 1) * pageSize + Rows.Count:N0}件 / 全{matchCount:N0}件";

    public InventoryManagementViewModel() : this(InventoryService.LoadPageAsync) { }

    internal InventoryManagementViewModel(Func<InventoryFilters, int, int, bool, CancellationToken, Task<InventoryPage>> loadPage)
    {
        this.loadPage = loadPage;
        debounce.Tick += (_, _) => { debounce.Stop(); _ = RefreshAsync(1); };
    }

    public Task LoadAsync()
    {
        summaryNeeded = true;
        return RefreshAsync(queryPending ? 1 : currentPage);
    }

    public async Task ExportAsync(TextWriter output)
    {
        if (!CanExport || displayedFilters is not { } filters) return;
        isExporting = true;
        NotifyPage();
        try { await InventoryService.ExportAsync(filters, output); }
        finally { isExporting = false; NotifyPage(); }
    }

    public Task SelectConditionAsync(InventoryCondition? condition)
    {
        selectedCondition = condition;
        return RefreshAsync(1);
    }

    public Task SortAsync(InventorySort column, bool reverse)
    {
        sort = column;
        descending = reverse;
        return RefreshAsync(1);
    }

    public Task GoToPageAsync(int page) =>
        isLoading || queryPending || page < 1 || page > PageCount || page == currentPage
            ? Task.CompletedTask
            : RefreshAsync(page);

    public void ToggleFilters() => IsFiltersExpanded = !IsFiltersExpanded;

    public Task ClearConditionsAsync()
    {
        Set(ref quickSearch, "", nameof(QuickSearch));
        Set(ref codeFilter, "", nameof(CodeFilter));
        Set(ref nameFilter, "", nameof(NameFilter));
        Set(ref makerFilter, "", nameof(MakerFilter));
        Set(ref classificationFilter, "", nameof(ClassificationFilter));
        Set(ref minQuantity, "", nameof(MinQuantity));
        Set(ref maxQuantity, "", nameof(MaxQuantity));
        return RefreshAsync(1);
    }

    public void StopRequests()
    {
        debounce.Stop();
        var previous = request;
        request = null;
        previous?.Cancel();
        isLoading = queryPending = false;
    }

    private void ScheduleSearch()
    {
        request?.Cancel();
        queryPending = true;
        debounce.Stop();
        debounce.Start();
        NotifyPage();
    }

    private async Task RefreshAsync(int page)
    {
        debounce.Stop();
        request?.Cancel();
        using var next = new CancellationTokenSource();
        request = next;
        queryPending = false;
        isLoading = true;
        Feedback = "";
        NotifyPage();
        try
        {
            if (!TryReadQuantity(MinQuantity, out var min) || !TryReadQuantity(MaxQuantity, out var max))
            {
                ClearPage("在庫数の条件は0以上の整数で入力してください。");
                return;
            }
            if (min > max)
            {
                ClearPage("在庫数の最小値は最大値以下にしてください。");
                return;
            }
            var filters = new InventoryFilters(QuickSearch.Trim(), CodeFilter.Trim(), NameFilter.Trim(),
                MakerFilter.Trim(), ClassificationFilter.Trim(), min, max, selectedCondition, sort, descending);
            var result = await loadPage(filters, page, pageSize, summaryNeeded, next.Token);
            if (!ReferenceEquals(request, next) || next.IsCancellationRequested) return;
            if (result.Summary is { } totals) { summary = totals; summaryNeeded = false; }
            displayedFilters = filters;
            matchCount = result.MatchCount;
            currentPage = result.Number;
            Rows.Clear();
            foreach (var row in result.Rows) Rows.Add(row);
        }
        catch (OperationCanceledException) when (next.IsCancellationRequested) { }
        catch (UnauthorizedAccessException ex) when (ReferenceEquals(request, next) && !next.IsCancellationRequested)
        {
            summary = null;
            summaryNeeded = true;
            ClearPage(ex.Message);
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(request, next) || next.IsCancellationRequested) return;
            System.Diagnostics.Debug.WriteLine($"Inventory load failed: {ex}");
            summary = null;
            summaryNeeded = true;
            ClearPage(InventoryService.DescribeUnavailableStock(InventoryStockState.QueryFailed) + "。接続を確認して再読込してください。");
        }
        finally
        {
            if (ReferenceEquals(request, next)) { request = null; isLoading = false; NotifyPage(); }
        }
    }

    private static bool TryReadQuantity(string text, out int? quantity)
    {
        quantity = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!int.TryParse(text, out var value) || value < 0) return false;
        quantity = value;
        return true;
    }

    private void ClearPage(string message)
    {
        Rows.Clear();
        displayedFilters = null;
        matchCount = 0;
        currentPage = 1;
        Feedback = message;
    }

    private void NotifyPage()
    {
        foreach (var name in new[] { nameof(TotalCount), nameof(NormalCount), nameof(LowCount), nameof(EmptyCount),
                     nameof(TotalQuantity), nameof(CountText), nameof(CurrentPage), nameof(PageCount), nameof(PageRangeText),
                     nameof(CanPrevious), nameof(CanNext), nameof(CanExport), nameof(CanReload), nameof(ShowEmpty), nameof(HasConditions) })
            Notify(name);
    }
}
