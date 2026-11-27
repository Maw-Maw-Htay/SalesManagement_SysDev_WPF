using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Threading;

namespace SalesManagement_SysDev;

public enum ProductStatusTab { All, Visible, Hidden }

public sealed class ProductRow : ViewModelBase
{
    private bool isSelected;
    public ProductRowData Data { get; }
    public int Id => Data.Id;
    public string Code => Data.Code;
    public string JanCode => string.IsNullOrWhiteSpace(Data.JanCode) ? "—" : Data.JanCode;
    public string Name => Data.Name;
    public string Maker => Data.Maker;
    public string Classification => Data.Classification;
    public string ModelNumber => Data.ModelNumber;
    public decimal Price => Data.Price;
    public long? StockQuantity => Data.StockQuantity;
    public string StockText => Data.Inventory.QuantityText;
    public string ManagementState => Data.ManagementFlag == ProductService.ActiveFlag ? "表示中" : "非表示";
    public bool IsSelectable => ProductService.CanEdit && Data.ManagementFlag == ProductService.ActiveFlag;
    public bool IsSelected { get => isSelected; set { if (Set(ref isSelected, value)) SelectionChanged?.Invoke(); } }
    internal Action? SelectionChanged { get; set; }
    public ProductRow(ProductRowData data) => Data = data;
}

public sealed class ProductManagementViewModel : ViewModelBase
{
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private readonly Func<ProductCatalog> loadCatalog;
    private List<ProductRow> sourceRows = [];
    private ProductRow[] filteredRows = [];
    private bool updatingSelection;
    private IReadOnlyList<ProductMasterOption> makers = [];
    private IReadOnlyList<ProductMasterOption> classifications = [];
    private string quickSearch = "", codeFilter = "", nameFilter = "", janFilter = "", modelFilter = "", feedback = "", filterError = "";
    private string lowPrice = "", highPrice = "";
    private int? makerFilter, classificationFilter;
    private DateTime? releaseFrom, releaseTo;
    private int visibilitySelection = -1, stockSelection = -1;
    private ProductStatusTab activeTab;
    private bool filtersExpanded, isLoading, isBusy, hasLoaded;
    private int pageNumber = 1, pageSize = 20;
    private int lastPageCount = 1;
    private string sortColumn = nameof(ProductRow.Code);
    private ListSortDirection sortDirection;

    public ObservableCollection<ProductRow> Rows { get; } = [];
    public IReadOnlyList<ProductMasterOption> Makers => makers;
    public IReadOnlyList<ProductMasterOption> Classifications => classifications;
    public IReadOnlyList<ProductMasterOption> MakerOptions => [new(0, "すべて"), .. makers];
    public IReadOnlyList<ProductMasterOption> ClassificationOptions => [new(0, "すべて"), .. classifications];
    public IReadOnlyList<ProductMasterOption> VisibilityOptions => CanViewHidden
        ? [new(-1, "すべて"), new(0, "表示中"), new(1, "非表示")] : [new(-1, "すべて"), new(0, "表示中")];
    public IReadOnlyList<ProductMasterOption> StockOptions { get; } =
        [new(-1, "すべて"), new((int)InventoryCondition.Normal, "正常"), new((int)InventoryCondition.Low, "在庫不足"),
         new((int)InventoryCondition.Empty, "在庫切れ"), new((int)InventoryCondition.Unavailable, "要確認")];
    public string QuickSearch { get => quickSearch; set { if (Set(ref quickSearch, value)) ScheduleSearch(); } }
    public string CodeFilter { get => codeFilter; set { if (Set(ref codeFilter, value)) ScheduleSearch(); } }
    public string NameFilter { get => nameFilter; set { if (Set(ref nameFilter, value)) ScheduleSearch(); } }
    public string JanFilter { get => janFilter; set { if (Set(ref janFilter, value)) ScheduleSearch(); } }
    public string ModelFilter { get => modelFilter; set { if (Set(ref modelFilter, value)) ScheduleSearch(); } }
    public int? MakerFilter { get => makerFilter; set { var normalized = value is 0 ? null : value; if (Set(ref makerFilter, normalized)) { Notify(nameof(MakerSelection)); ScheduleSearch(); } } }
    public int? ClassificationFilter { get => classificationFilter; set { var normalized = value is 0 ? null : value; if (Set(ref classificationFilter, normalized)) { Notify(nameof(ClassificationSelection)); ScheduleSearch(); } } }
    public int MakerSelection { get => MakerFilter ?? 0; set => MakerFilter = value; }
    public int ClassificationSelection { get => ClassificationFilter ?? 0; set => ClassificationFilter = value; }
    public string MinimumPrice { get => lowPrice; set { if (Set(ref lowPrice, value)) ScheduleSearch(); } }
    public string MaximumPrice { get => highPrice; set { if (Set(ref highPrice, value)) ScheduleSearch(); } }
    public DateTime? ReleaseFrom { get => releaseFrom; set { if (Set(ref releaseFrom, value)) ScheduleSearch(); } }
    public DateTime? ReleaseTo { get => releaseTo; set { if (Set(ref releaseTo, value)) ScheduleSearch(); } }
    public int VisibilitySelection { get => visibilitySelection; set { if (Set(ref visibilitySelection, value)) ScheduleSearch(); } }
    public int StockSelection { get => stockSelection; set { if (Set(ref stockSelection, value)) ScheduleSearch(); } }
    public string FilterError { get => filterError; private set { if (Set(ref filterError, value)) Notify(nameof(HasFilterError)); } }
    public bool HasFilterError => FilterError.Length != 0;
    public string Feedback { get => feedback; private set => Set(ref feedback, value); }
    public ProductStatusTab ActiveTab { get => activeTab; private set => Set(ref activeTab, value); }
    public bool IsFiltersExpanded { get => filtersExpanded; set { if (Set(ref filtersExpanded, value)) Notify(nameof(FiltersToggleLabel)); } }
    public string FiltersToggleLabel => IsFiltersExpanded ? "詳細条件を折りたたむ" : "詳細条件を展開";
    public bool IsLoading { get => isLoading; private set { if (Set(ref isLoading, value)) NotifyState(); } }
    public bool IsBusy { get => isBusy; private set { if (Set(ref isBusy, value)) NotifyState(); } }
    public bool CanEditProducts => hasLoaded && ProductService.CanEdit && CanReload;
    public bool CanRegister => CanEditProducts && Makers.Count > 0 && Classifications.Count > 0;
    public bool CanViewHidden => ProductService.CanViewHidden;
    public bool CanReload => !IsBusy && !IsLoading;
    public int AdvancedCount => new[] { CodeFilter, NameFilter, JanFilter, ModelFilter, MinimumPrice, MaximumPrice }
        .Count(x => !string.IsNullOrWhiteSpace(x))
        + (MakerFilter.HasValue ? 1 : 0) + (ClassificationFilter.HasValue ? 1 : 0) + (VisibilitySelection >= 0 ? 1 : 0)
        + (StockSelection >= 0 ? 1 : 0) + (ReleaseFrom.HasValue ? 1 : 0) + (ReleaseTo.HasValue ? 1 : 0);
    public bool HasAdvancedConditions => AdvancedCount > 0;
    public bool HasConditions => HasAdvancedConditions || !string.IsNullOrWhiteSpace(QuickSearch) || HasFilterError;
    public int SelectedCount => filteredRows.Count(x => x.IsSelected);
    public IReadOnlyList<ProductRow> SelectedRows => filteredRows.Where(x => x.IsSelected && x.IsSelectable).ToArray();
    public int AllCount => sourceRows.Count;
    public int VisibleCount => sourceRows.Count(x => x.Data.ManagementFlag == ProductService.ActiveFlag);
    public int HiddenCount => CanViewHidden ? sourceRows.Count(x => x.Data.ManagementFlag != ProductService.ActiveFlag) : 0;
    public string AllTabText => $"すべて　{AllCount:N0}";
    public string VisibleTabText => $"表示中　{VisibleCount:N0}";
    public string HiddenTabText => $"非表示　{HiddenCount:N0}";
    public string CountText => $"{filteredRows.Length:N0} 件該当 / 全 {TabCount:N0} 件";
    public string SelectionText => $"選択 {SelectedCount:N0} 件";
    public bool IsEmpty => Rows.Count == 0;
    public bool ShowEmptyState => hasLoaded && !IsLoading && IsEmpty && !HasFilterError;
    public bool CanClearSelection => SelectedCount > 0;
    public bool CanHideSelected => CanEditProducts && ActiveTab != ProductStatusTab.Hidden
        && filteredRows.Any(x => x.IsSelected && x.IsSelectable);
    public bool CanSelectAll => CanEditProducts && ActiveTab != ProductStatusTab.Hidden
        && filteredRows.Any(x => x.IsSelectable);
    public bool? VisibleSelectionState => !filteredRows.Any(x => x.IsSelectable) ? false
        : filteredRows.Where(x => x.IsSelectable).All(x => x.IsSelected) ? true
        : filteredRows.Any(x => x.IsSelectable && x.IsSelected) ? null : false;
    private int TabCount => ActiveTab switch
    {
        ProductStatusTab.Visible => VisibleCount,
        ProductStatusTab.Hidden => HiddenCount,
        _ => AllCount
    };

    public IReadOnlyList<int> PageSizes { get; } = [10, 20, 50, 100];
    public IReadOnlyList<int> PageNumbers { get; private set; } = [1];
    public int PageCount => Math.Max(1, (filteredRows.Length + PageSize - 1) / PageSize);
    public int PageSize { get => pageSize; set { if (PageSizes.Contains(value) && Set(ref pageSize, value)) ApplyPage(1); } }
    public int PageNumber { get => pageNumber; set { if (value > 0 && value <= PageCount && value != pageNumber) ApplyPage(value); } }
    public bool CanGoBack => !IsLoading && PageNumber > 1;
    public bool CanGoForward => !IsLoading && PageNumber < PageCount;
    public string PageRange => filteredRows.Length == 0 ? "0 件" :
        $"{(PageNumber - 1) * PageSize + 1:N0}～{(PageNumber - 1) * PageSize + Rows.Count:N0} 件 / 全 {filteredRows.Length:N0} 件";

    public ProductManagementViewModel() : this(ProductService.Load) { }

    internal ProductManagementViewModel(Func<ProductCatalog> loadCatalog)
    {
        this.loadCatalog = loadCatalog;
        debounce.Tick += (_, _) => ApplyFilters(resetPage: true);
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (IsLoading || IsBusy) return;
        IsLoading = true;
        Feedback = "商品一覧を読み込んでいます…";
        try
        {
            var catalog = await Task.Run(loadCatalog);
            SetCatalog(catalog);
            Feedback = sourceRows.Count == 0 ? "商品が登録されていません。" : "";
        }
        catch (UnauthorizedAccessException ex) { ClearCatalog(); Feedback = ex.Message; }
        catch (Exception) { ClearCatalog(); Feedback = "商品一覧を読み込めませんでした。データベースの状態を確認してください。"; }
        finally { IsLoading = false; }
    }

    public void SelectTab(ProductStatusTab tab)
    {
        if (tab == ProductStatusTab.Hidden && !CanViewHidden) return;
        SetSelection(sourceRows, false);
        ActiveTab = tab;
        ApplyFilters(resetPage: true);
    }
    public void ClearSelection()
    {
        SetSelection(sourceRows, false);
        NotifySelection();
    }
    public void SelectAllVisible(bool selected)
    {
        if (!CanSelectAll) return;
        SetSelection(filteredRows.Where(x => x.IsSelectable), selected);
        NotifySelection();
    }

    private void SetSelection(IEnumerable<ProductRow> rows, bool selected)
    {
        updatingSelection = true;
        try
        {
            foreach (var row in rows) row.IsSelected = selected;
        }
        finally { updatingSelection = false; }
    }

    public void ClearConditions()
    {
        QuickSearch = CodeFilter = NameFilter = JanFilter = ModelFilter = MinimumPrice = MaximumPrice = "";
        MakerFilter = null; ClassificationFilter = null;
        ReleaseFrom = ReleaseTo = null; VisibilitySelection = StockSelection = -1;
        ApplyFilters(resetPage: true);
    }

    public void ReportInvalidDate()
    {
        FilterError = "有効な発売日を指定してください。無効な入力は適用されません。";
        Notify(nameof(HasConditions)); Notify(nameof(ShowEmptyState));
    }

    public void SortBy(string column, ListSortDirection direction)
    {
        sortColumn = column; sortDirection = direction;
        ApplyFilters(resetPage: true);
    }

    public async Task<ProductOperationResult> CreateAsync(ProductInput input) =>
        await MutateAsync(() => ProductService.Create(CurrentEmployeeId, input));

    public async Task<ProductOperationResult> UpdateAsync(ProductEditSnapshot expected, ProductInput input) =>
        await MutateAsync(() => ProductService.Update(CurrentEmployeeId, expected, input));

    public Task<ProductOperationResult> RestoreAsync(ProductRowData expected) =>
        MutateAsync(() => ProductService.Restore(CurrentEmployeeId, expected));

    public async Task<ProductOperationResult> HideAsync(IReadOnlyCollection<ProductRow> targets, string? reason)
    {
        if (!CanEditProducts) return new(false, "商品情報を変更できません。");
        var selected = targets.Where(x => x.IsSelectable).DistinctBy(x => x.Id).ToArray();
        if (!ProductService.CanEdit || selected.Length == 0)
            return new(false, "非表示にできる商品を選択してください。");
        var errors = new List<string>();
        var succeeded = 0;
        IsBusy = true;
        try
        {
            foreach (var row in selected)
            {
                var result = await Task.Run(() => ProductService.Hide(CurrentEmployeeId, row.Id, reason));
                if (result.Succeeded) succeeded++;
                else errors.Add($"{row.Code}　{result.Message}");
            }
            await ReloadCatalogAsync();
        }
        finally { IsBusy = false; }
        var message = $"成功　{succeeded:N0} 件\n失敗　{errors.Count:N0} 件";
        if (errors.Count > 0) message += "\n\n" + string.Join("\n", errors.Take(8));
        return new(errors.Count == 0, message, succeeded);
    }

    private int CurrentEmployeeId => AppSession.Employee?.EmId ?? 0;

    private async Task<ProductOperationResult> MutateAsync(Func<ProductOperationResult> operation)
    {
        if (!CanEditProducts) return new(false, "商品情報を変更できません。");
        IsBusy = true;
        try
        {
            var result = await Task.Run(operation);
            if (result.Succeeded) await ReloadCatalogAsync();
            return result;
        }
        finally { IsBusy = false; }
    }

    private async Task ReloadCatalogAsync()
    {
        try { SetCatalog(await Task.Run(loadCatalog)); Feedback = ""; }
        catch (Exception) { ClearCatalog(); Feedback = "操作は完了しましたが、一覧の再読込に失敗しました。再読込を実行してください。"; }
    }

    private void ClearCatalog()
    {
        hasLoaded = false; sourceRows = []; makers = []; classifications = [];
        NotifyCatalog();
        ApplyFilters();
    }

    private void SetCatalog(ProductCatalog catalog)
    {
        var selected = sourceRows.Where(x => x.IsSelected).Select(x => x.Id).ToHashSet();
        hasLoaded = true;
        makers = catalog.Makers;
        classifications = catalog.Classifications;
        sourceRows = catalog.Products
            .Where(x => CanViewHidden || x.ManagementFlag == ProductService.ActiveFlag)
            .Select(x => new ProductRow(x) { IsSelected = x.ManagementFlag == ProductService.ActiveFlag && selected.Contains(x.Id) }).ToList();
        foreach (var row in sourceRows) row.SelectionChanged = NotifySelection;
        NotifyCatalog();
        ApplyFilters();
    }

    private void NotifyCatalog()
    {
        Notify(nameof(Makers)); Notify(nameof(Classifications)); Notify(nameof(MakerOptions));
        Notify(nameof(ClassificationOptions)); Notify(nameof(AllCount)); Notify(nameof(VisibleCount));
        Notify(nameof(HiddenCount)); Notify(nameof(MakerSelection)); Notify(nameof(ClassificationSelection));
    }

    private void ScheduleSearch()
    {
        Notify(nameof(HasConditions)); Notify(nameof(AdvancedCount)); Notify(nameof(HasAdvancedConditions));
        debounce.Stop(); debounce.Start();
    }

    private void ApplyFilters(bool resetPage = false)
    {
        debounce.Stop();
        var minValid = TryPrice(MinimumPrice, out var min);
        var maxValid = TryPrice(MaximumPrice, out var max);
        FilterError = !minValid || !maxValid ? "販売単価の範囲は0～999,999,999の整数で入力してください。"
            : min > max ? "販売単価の下限は上限以下を指定してください。"
            : ReleaseFrom?.Date > ReleaseTo?.Date ? "発売日の開始日は終了日以前を指定してください。" : "";
        var keyword = QuickSearch.Trim();
        var visible = sourceRows.Where(row =>
        {
            if (HasFilterError) return false;
            var data = row.Data;
            if (ActiveTab == ProductStatusTab.Visible && data.ManagementFlag != ProductService.ActiveFlag) return false;
            if (ActiveTab == ProductStatusTab.Hidden && data.ManagementFlag == ProductService.ActiveFlag) return false;
            if (VisibilitySelection >= 0 && (data.ManagementFlag == ProductService.ActiveFlag ? 0 : 1) != VisibilitySelection) return false;
            if (StockSelection >= 0 && (int)data.Inventory.Condition != StockSelection) return false;
            if (keyword.Length != 0 && !MatchCode(data, keyword) && !data.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
                && !data.Classification.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
                && !(data.JanCode ?? "").Contains(keyword, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrWhiteSpace(CodeFilter) && !MatchCode(data, CodeFilter.Trim())) return false;
            if (!string.IsNullOrWhiteSpace(NameFilter) && !data.Name.Contains(NameFilter.Trim(), StringComparison.CurrentCultureIgnoreCase)) return false;
            if (!string.IsNullOrWhiteSpace(JanFilter) && !(data.JanCode ?? "").Contains(JanFilter.Trim(), StringComparison.CurrentCultureIgnoreCase)) return false;
            if (!string.IsNullOrWhiteSpace(ModelFilter) && !data.ModelNumber.Contains(ModelFilter.Trim(), StringComparison.CurrentCultureIgnoreCase)) return false;
            if (MakerFilter.HasValue && data.MakerId != MakerFilter.Value) return false;
            if (ClassificationFilter.HasValue && data.ClassificationId != ClassificationFilter.Value) return false;
            if (min.HasValue && data.Price < min) return false;
            if (max.HasValue && data.Price > max) return false;
            if (ReleaseFrom.HasValue && data.ReleaseDate.Date < ReleaseFrom.Value.Date) return false;
            if (ReleaseTo.HasValue && data.ReleaseDate.Date > ReleaseTo.Value.Date) return false;
            return true;
        });
        Func<ProductRow, IComparable> key = sortColumn switch
        {
            nameof(ProductRow.Name) => x => x.Name,
            nameof(ProductRow.Classification) => x => x.Classification,
            nameof(ProductRow.Price) => x => x.Price,
            nameof(ProductRow.StockQuantity) => x => x.StockQuantity ?? long.MinValue,
            nameof(ProductRow.ManagementState) => x => x.ManagementState,
            _ => x => x.Id
        };
        var ordered = sortDirection == ListSortDirection.Descending ? visible.OrderByDescending(key) : visible.OrderBy(key);
        filteredRows = ordered.ThenBy(x => x.Id).ToArray();
        var visibleIds = filteredRows.Select(x => x.Id).ToHashSet();
        SetSelection(sourceRows.Where(x => x.IsSelected && !visibleIds.Contains(x.Id)), false);
        ApplyPage(resetPage ? 1 : PageNumber);
        Notify(nameof(HasConditions)); Notify(nameof(IsEmpty)); Notify(nameof(ShowEmptyState));
        Notify(nameof(CountText)); Notify(nameof(AllTabText)); Notify(nameof(VisibleTabText));
        Notify(nameof(HiddenTabText)); Notify(nameof(CanHideSelected)); Notify(nameof(CanSelectAll));
        NotifySelection();
    }

    private static bool TryPrice(string text, out decimal? price)
    {
        price = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount)
            || amount < 0 || amount > 999_999_999 || decimal.Truncate(amount) != amount) return false;
        price = amount;
        return true;
    }

    private static bool MatchCode(ProductRowData row, string input)
    {
        var digits = input.StartsWith("P-", StringComparison.OrdinalIgnoreCase) ? input[2..] : input;
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id == row.Id
            || row.Code.Contains(input, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyPage(int requested)
    {
        pageNumber = Math.Clamp(requested, 1, PageCount);
        Rows.Clear();
        foreach (var row in filteredRows.Skip((PageNumber - 1) * PageSize).Take(PageSize)) Rows.Add(row);
        var start = Math.Clamp(PageNumber - 2, 1, Math.Max(1, PageCount - 4));
        var pages = Enumerable.Range(start, Math.Min(PageCount, 5)).ToArray();
        if (!PageNumbers.SequenceEqual(pages))
        {
            PageNumbers = pages;
            Notify(nameof(PageNumbers));
        }
        if (lastPageCount != PageCount) { lastPageCount = PageCount; Notify(nameof(PageCount)); }
        Notify(nameof(PageNumber)); Notify(nameof(PageRange)); Notify(nameof(CanGoBack)); Notify(nameof(CanGoForward));
    }

    private void NotifySelection()
    {
        if (updatingSelection) return;
        Notify(nameof(SelectedCount)); Notify(nameof(SelectionText)); Notify(nameof(CanClearSelection));
        Notify(nameof(CanHideSelected)); Notify(nameof(VisibleSelectionState));
    }

    private void NotifyState()
    {
        Notify(nameof(CanEditProducts)); Notify(nameof(CanRegister)); Notify(nameof(CanReload)); Notify(nameof(CanHideSelected));
        Notify(nameof(CanSelectAll)); Notify(nameof(ShowEmptyState)); Notify(nameof(CanGoBack)); Notify(nameof(CanGoForward));
    }
}
