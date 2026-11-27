using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;

namespace SalesManagement_SysDev;

public enum EmployeeStatusTab { All, Visible, Hidden }

public sealed record EmployeeListCriteria(string Keyword = "", string Number = "", string Name = "",
    int? OfficeId = null, int? PositionId = null, string Phone = "", int? ManagementFlag = null,
    bool? CanLogin = null, DateTime? HiredFrom = null, DateTime? HiredTo = null)
{
    public bool IsDateRangeValid => !HiredFrom.HasValue || !HiredTo.HasValue || HiredFrom.Value.Date <= HiredTo.Value.Date;

    public bool Matches(EmployeeRecord employee)
    {
        var keyword = Keyword.Trim();
        return IsDateRangeValid
            && (keyword.Length == 0 || employee.Code.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || employee.LoginId.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || employee.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase))
            && (!OfficeId.HasValue || employee.OfficeId == OfficeId)
            && (!PositionId.HasValue || employee.PositionId == PositionId)
            && (!CanLogin.HasValue || employee.CanLogin == CanLogin)
            && (!HiredFrom.HasValue || employee.HireDate.Date >= HiredFrom.Value.Date)
            && (!HiredTo.HasValue || employee.HireDate.Date <= HiredTo.Value.Date)
            && EmployeeFilter.Matches(employee, Number, Name, "", "", Phone, ManagementFlag);
    }
}

public sealed class EmployeeRow : ViewModelBase
{
    private bool isSelected;
    public EmployeeRecord Data { get; }
    public string Code => Data.Code;
    public string Name => Data.Name;
    public string Office => Data.OfficeName;
    public string LoginId => Data.LoginId;
    public string Role => Data.RoleLabel;
    public string ManagementState => Data.ManagementLabel;
    public string LoginState => Data.CanLogin ? "利用可能" : "無効";
    public bool IsSelectable => Data.ManagementFlag == EmployeeService.ActiveFlag
        && Data.Id != AppSession.Employee?.EmId;
    public bool IsSelected { get => isSelected; set { if (Set(ref isSelected, value)) SelectionChanged?.Invoke(); } }
    internal Action? SelectionChanged { get; set; }
    public EmployeeRow(EmployeeRecord data) => Data = data;
}

public sealed class EmployeeManagementViewModel : ViewModelBase
{
    private readonly Func<EmployeeDirectory> loadDirectory;
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private List<EmployeeRow> sourceRows = [];
    private EmployeeRow[] filteredRows = [];
    private bool updatingSelection;
    private IReadOnlyList<EmployeeOffice> offices = [];
    private IReadOnlyList<EmployeeRole> roles = [];
    private EmployeeListCriteria criteria = new();
    private string feedback = "", filterError = "";
    private string sortColumn = nameof(EmployeeRow.Code);
    private ListSortDirection sortDirection;
    private EmployeeStatusTab activeTab;
    private bool isFiltersExpanded, isLoading, isBusy, hasLoaded;
    private int pageNumber = 1, pageSize = 20;

    public sealed record FilterOption<T>(T Value, string Label);

    public ObservableCollection<EmployeeRow> Rows { get; } = [];
    public IReadOnlyList<EmployeeOffice> Offices => offices;
    public IReadOnlyList<EmployeeRole> Roles => roles;
    public IReadOnlyList<FilterOption<int?>> OfficeOptions { get; private set; } = [new(null, "すべて")];
    public IReadOnlyList<FilterOption<int?>> RoleOptions { get; private set; } = [new(null, "すべて")];
    public IReadOnlyList<FilterOption<int?>> ManagementOptions { get; } =
        [new(null, "すべて"), new(EmployeeService.ActiveFlag, "表示中"), new(EmployeeService.HiddenFlag, "非表示")];
    public IReadOnlyList<FilterOption<bool?>> LoginOptions { get; } = [new(null, "すべて"), new(true, "利用可能"), new(false, "無効")];
    public string QuickSearch { get => criteria.Keyword; set => SetCriteria(criteria with { Keyword = value }, nameof(QuickSearch)); }
    public string IdFilter { get => criteria.Number; set => SetCriteria(criteria with { Number = value }, nameof(IdFilter)); }
    public string NameFilter { get => criteria.Name; set => SetCriteria(criteria with { Name = value }, nameof(NameFilter)); }
    public int? OfficeFilter { get => criteria.OfficeId; set => SetCriteria(criteria with { OfficeId = value }, nameof(OfficeFilter)); }
    public int? RoleFilter { get => criteria.PositionId; set => SetCriteria(criteria with { PositionId = value }, nameof(RoleFilter)); }
    public string PhoneFilter { get => criteria.Phone; set => SetCriteria(criteria with { Phone = value }, nameof(PhoneFilter)); }
    public int? ManagementFilter { get => criteria.ManagementFlag; set => SetCriteria(criteria with { ManagementFlag = value }, nameof(ManagementFilter)); }
    public bool? LoginFilter { get => criteria.CanLogin; set => SetCriteria(criteria with { CanLogin = value }, nameof(LoginFilter)); }
    public DateTime? HiredFrom { get => criteria.HiredFrom; set => SetCriteria(criteria with { HiredFrom = value }, nameof(HiredFrom)); }
    public DateTime? HiredTo { get => criteria.HiredTo; set => SetCriteria(criteria with { HiredTo = value }, nameof(HiredTo)); }
    public string Feedback { get => feedback; private set => Set(ref feedback, value); }
    public EmployeeStatusTab ActiveTab { get => activeTab; private set { if (Set(ref activeTab, value)) NotifyTabs(); } }
    public bool IsFiltersExpanded { get => isFiltersExpanded; set { if (Set(ref isFiltersExpanded, value)) Notify(nameof(FiltersToggleLabel)); } }
    public string FiltersToggleLabel => IsFiltersExpanded ? "詳細条件を折りたたむ" : "詳細条件を展開";
    public bool IsLoading { get => isLoading; private set { if (Set(ref isLoading, value)) { Notify(nameof(ShowEmptyState)); NotifyActions(); } } }
    public bool IsBusy { get => isBusy; private set { if (Set(ref isBusy, value)) NotifyActions(); } }
    public bool CanReload => !IsLoading && !IsBusy;
    public bool CanCreate => hasLoaded && EmployeeService.CanManage && CanReload && Offices.Count > 0 && Roles.Count > 0;
    public bool CanEdit => hasLoaded && EmployeeService.CanManage && CanReload;
    public bool CanViewRoles => hasLoaded && EmployeeService.CanManage && CanReload && Roles.Count > 0;
    public bool CanHide => CanEdit && SelectedCount == 1 && Rows.Any(x => x.IsSelected && x.IsSelectable);
    public bool CanClearSelection => Rows.Any(x => x.IsSelected);
    public int SelectedCount => Rows.Count(x => x.IsSelected);
    public string FilterError { get => filterError; private set { if (Set(ref filterError, value)) Notify(nameof(HasFilterError)); } }
    public bool HasFilterError => FilterError.Length != 0;
    public bool IsEmpty => Rows.Count == 0;
    public bool ShowEmptyState => hasLoaded && !IsLoading && IsEmpty && !HasFilterError;
    public int TotalCount => sourceRows.Count;
    public int VisibleCount => sourceRows.Count(x => x.Data.ManagementFlag == EmployeeService.ActiveFlag);
    public int HiddenCount => sourceRows.Count(x => x.Data.ManagementFlag == EmployeeService.HiddenFlag);
    public string AllTabText => $"すべて　{TotalCount:N0}";
    public string VisibleTabText => $"表示中　{VisibleCount:N0}";
    public string HiddenTabText => $"非表示　{HiddenCount:N0}";
    public string CountText => $"{filteredRows.Length:N0} 件該当 / 全 {CurrentTabCount:N0} 件";
    public string SelectionText => $"選択 {SelectedCount:N0} 件";
    private int CurrentTabCount => ActiveTab switch { EmployeeStatusTab.Visible => VisibleCount, EmployeeStatusTab.Hidden => HiddenCount, _ => TotalCount };
    public IReadOnlyList<int> PageSizes { get; } = [10, 20, 50, 100];
    public int PageSize
    {
        get => pageSize;
        set { if (PageSizes.Contains(value) && Set(ref pageSize, value)) ApplyPage(1); }
    }
    public int PageNumber
    {
        get => pageNumber;
        set { if (value >= 1 && value <= PageCount && value != pageNumber) ApplyPage(value); }
    }
    public int PageCount => Math.Max(1, (filteredRows.Length + PageSize - 1) / PageSize);
    public IReadOnlyList<int> PageNumbers { get; private set; } = [1];
    public bool CanGoBack => !IsLoading && pageNumber > 1;
    public bool CanGoForward => !IsLoading && pageNumber < PageCount;
    public string PageRange => filteredRows.Length == 0 ? "0 件" :
        $"{(pageNumber - 1) * PageSize + 1:N0}–{(pageNumber - 1) * PageSize + Rows.Count:N0} / {filteredRows.Length:N0} 件";

    public EmployeeManagementViewModel() : this(EmployeeService.Load) { }

    internal EmployeeManagementViewModel(Func<EmployeeDirectory> loadDirectory)
    {
        this.loadDirectory = loadDirectory;
        debounce.Tick += (_, _) => { debounce.Stop(); ApplyFilters(resetPage: true); };
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true; hasLoaded = false; Feedback = "社員一覧を読み込んでいます…";
        try
        {
            var directory = await Task.Run(loadDirectory);
            sourceRows = directory.Employees.Select(x => new EmployeeRow(x)).ToList();
            foreach (var row in sourceRows) row.SelectionChanged = NotifySelection;
            offices = directory.Offices; roles = directory.Roles;
            OfficeOptions = Options(directory.Employees.Select(x => (x.OfficeId, x.OfficeName))
                .Concat(offices.Select(x => (x.Id, x.Name))));
            RoleOptions = Options(directory.Employees.Select(x => (x.PositionId, x.RoleLabel))
                .Concat(roles.Select(x => (x.Id, x.Label))));
            Notify(nameof(Offices)); Notify(nameof(Roles)); Notify(nameof(OfficeOptions)); Notify(nameof(RoleOptions));
            if (!OfficeOptions.Any(x => x.Value == OfficeFilter)) OfficeFilter = null;
            if (!RoleOptions.Any(x => x.Value == RoleFilter)) RoleFilter = null;
            hasLoaded = true;
            Feedback = sourceRows.Count == 0 ? "社員が登録されていません。社員登録から追加してください。" : "";
        }
        catch (UnauthorizedAccessException ex) { sourceRows = []; Feedback = ex.Message; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Employee list load failed: {ex}");
            sourceRows = []; Feedback = "社員一覧を読み込めませんでした。LocalDB の接続状態を確認してください。";
        }
        finally { IsLoading = false; NotifyCounts(); ApplyFilters(); }
    }

    public void SelectTab(EmployeeStatusTab tab)
    {
        ClearSelection(sourceRows);
        ActiveTab = tab; ApplyFilters(resetPage: true);
    }
    public void ToggleFilters() => IsFiltersExpanded = !IsFiltersExpanded;
    public void ReportInvalidDate()
    {
        FilterError = "有効な入社日を指定してください。無効な入力は適用されません。";
        Notify(nameof(ShowEmptyState));
    }
    public void ClearSelection()
    {
        ClearSelection(sourceRows);
        NotifySelection();
    }

    private void ClearSelection(IEnumerable<EmployeeRow> rows)
    {
        updatingSelection = true;
        try
        {
            foreach (var row in rows) row.IsSelected = false;
        }
        finally { updatingSelection = false; }
    }

    public void ClearConditions()
    {
        QuickSearch = IdFilter = NameFilter = PhoneFilter = "";
        OfficeFilter = RoleFilter = ManagementFilter = null;
        LoginFilter = null;
        HiredFrom = HiredTo = null;
        ApplyFilters(resetPage: true);
    }

    public void SortBy(string column, ListSortDirection direction)
    {
        sortColumn = column; sortDirection = direction;
        ApplyFilters(resetPage: true);
    }

    public async Task<EmployeeOperationResult> CreateAsync(int employeeId, string name, string phone, int officeId, int roleId, string password)
    {
        if (!CanCreate) return new(false, "社員を登録する権限がありません。");
        IsBusy = true;
        try
        {
            var result = await Task.Run(() => EmployeeService.Create(AppSession.Employee!.EmId, employeeId, name, phone, officeId, roleId, password));
            if (result.Succeeded) await LoadAfterMutationAsync();
            return result;
        }
        finally { IsBusy = false; }
    }

    public async Task<EmployeeOperationResult> UpdateAsync(EmployeeRecord employee, string name, string phone, int officeId, int roleId)
    {
        if (!CanEdit) return new(false, "社員情報を更新する権限がありません。");
        IsBusy = true;
        try
        {
            var result = await Task.Run(() => EmployeeService.Update(AppSession.Employee!.EmId, EmployeeService.Snapshot(employee), name, phone, officeId, roleId));
            if (result.Succeeded) await LoadAfterMutationAsync();
            return result;
        }
        finally { IsBusy = false; }
    }

    public async Task<EmployeeOperationResult> HideAsync(EmployeeRow row, string? reason)
    {
        if (!CanEdit || !row.IsSelectable) return new(false, "この社員のアカウントを無効化できません。");
        IsBusy = true;
        try
        {
            var result = await Task.Run(() => EmployeeService.Hide(AppSession.Employee!.EmId, row.Data.Id, reason));
            if (result.Succeeded) { row.IsSelected = false; await LoadAfterMutationAsync(); }
            return result;
        }
        finally { IsBusy = false; }
    }

    private async Task LoadAfterMutationAsync() => await LoadAsync();
    private static FilterOption<int?>[] Options(IEnumerable<(int Id, string Name)> records) =>
        new[] { new FilterOption<int?>(null, "すべて") }.Concat(records.DistinctBy(x => x.Id)
            .OrderBy(x => x.Name).Select(x => new FilterOption<int?>(x.Id, x.Name))).ToArray();

    private void SetCriteria(EmployeeListCriteria value, string property)
    {
        if (criteria == value) return;
        criteria = value;
        Notify(property);
        debounce.Stop(); debounce.Start();
    }

    private void ApplyFilters(bool resetPage = false)
    {
        debounce.Stop();
        FilterError = criteria.IsDateRangeValid ? "" : "入社日の開始日は終了日以前を指定してください。";
        var flag = ActiveTab switch { EmployeeStatusTab.Visible => EmployeeService.ActiveFlag, EmployeeStatusTab.Hidden => EmployeeService.HiddenFlag, _ => (int?)null };
        var matches = sourceRows.Where(x => (!flag.HasValue || x.Data.ManagementFlag == flag) && criteria.Matches(x.Data));
        Func<EmployeeRow, IComparable> key = sortColumn switch
        {
            nameof(EmployeeRow.Name) => x => x.Name,
            nameof(EmployeeRow.Office) => x => x.Office,
            nameof(EmployeeRow.Role) => x => x.Role,
            nameof(EmployeeRow.ManagementState) => x => x.ManagementState,
            nameof(EmployeeRow.LoginState) => x => x.LoginState,
            _ => x => x.Data.Id
        };
        var ordered = sortDirection == ListSortDirection.Descending ? matches.OrderByDescending(key) : matches.OrderBy(key);
        filteredRows = ordered.ThenBy(x => x.Data.Id).ToArray();
        ApplyPage(resetPage ? 1 : pageNumber);
        Notify(nameof(CountText));
    }

    private void ApplyPage(int requested)
    {
        pageNumber = Math.Clamp(requested, 1, PageCount);
        var page = filteredRows.Skip((pageNumber - 1) * PageSize).Take(PageSize).ToArray();
        var inPage = page.ToHashSet();
        ClearSelection(Rows.Where(x => x.IsSelected && !inPage.Contains(x)));
        Rows.Clear(); foreach (var row in page) Rows.Add(row);
        if (PageNumbers.Count != PageCount)
        {
            PageNumbers = Enumerable.Range(1, PageCount).ToArray();
            Notify(nameof(PageNumbers));
            Notify(nameof(PageCount));
        }
        Notify(nameof(PageNumber)); Notify(nameof(PageRange));
        Notify(nameof(CanGoBack)); Notify(nameof(CanGoForward));
        Notify(nameof(IsEmpty)); Notify(nameof(ShowEmptyState)); NotifySelection();
    }
    private void NotifySelection()
    {
        if (updatingSelection) return;
        Notify(nameof(SelectedCount)); Notify(nameof(SelectionText)); Notify(nameof(CanClearSelection)); Notify(nameof(CanHide));
    }
    private void NotifyTabs()
    {
        Notify(nameof(AllTabText)); Notify(nameof(VisibleTabText)); Notify(nameof(HiddenTabText)); Notify(nameof(CountText)); Notify(nameof(CanHide));
    }
    private void NotifyCounts()
    {
        Notify(nameof(TotalCount)); Notify(nameof(VisibleCount)); Notify(nameof(HiddenCount)); NotifyTabs();
    }
    private void NotifyActions()
    {
        Notify(nameof(CanCreate)); Notify(nameof(CanEdit)); Notify(nameof(CanHide)); Notify(nameof(CanReload));
        Notify(nameof(CanViewRoles)); Notify(nameof(CanGoBack)); Notify(nameof(CanGoForward));
    }
}
