using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Threading;

namespace SalesManagement_SysDev;

public enum CustomerStatusTab { All, Visible, Hidden }

public sealed record CustomerSearchCriteria(string Code, string Name, string Phone, string Address, int? OfficeId,
    string Keyword = "", int? Visibility = null);

public static class CustomerFilter
{
    public static bool Matches(CustomerRecord customer, CustomerSearchCriteria criteria, CustomerStatusTab tab, bool canViewHidden)
    {
        if (tab == CustomerStatusTab.Hidden && (!canViewHidden || customer.ManagementFlag == CustomerService.ActiveFlag)) return false;
        if (tab == CustomerStatusTab.Visible && customer.ManagementFlag != CustomerService.ActiveFlag) return false;
        if (tab == CustomerStatusTab.All && customer.ManagementFlag != CustomerService.ActiveFlag && !canViewHidden) return false;
        if (criteria.OfficeId.HasValue && customer.OfficeId != criteria.OfficeId.Value) return false;
        if (criteria.Visibility.HasValue && (customer.ManagementFlag == CustomerService.ActiveFlag ? 0 : 1) != criteria.Visibility) return false;
        if (!string.IsNullOrWhiteSpace(criteria.Code) && !MatchesId(customer.Id, criteria.Code, allowFormattedPrefix: true)) return false;
        if (!MatchesKeyword(customer, criteria.Keyword)) return false;
        if (!string.IsNullOrWhiteSpace(criteria.Name) && !customer.Name.Contains(criteria.Name.Trim(), StringComparison.CurrentCultureIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(criteria.Phone) && !MatchesPhone(customer, criteria.Phone)) return false;
        if (!string.IsNullOrWhiteSpace(criteria.Address) && !customer.Address.Contains(criteria.Address.Trim(), StringComparison.CurrentCultureIgnoreCase)) return false;
        return true;
    }

    private static bool MatchesId(int id, string input, bool allowFormattedPrefix = false)
    {
        var code = CustomerInput.Normalize(input);
        var prefixed = code.StartsWith("C-", StringComparison.OrdinalIgnoreCase);
        var digits = prefixed ? code[2..] : code;
        if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number == id) return true;
        return allowFormattedPrefix && prefixed && digits.Length > 0 && digits.All(char.IsAsciiDigit)
            && CustomerInput.FormatCode(id).StartsWith(code, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesKeyword(CustomerRecord customer, string input)
    {
        var keyword = CustomerInput.Normalize(input);
        if (keyword.Length == 0) return true;
        if (keyword.StartsWith("C-", StringComparison.OrdinalIgnoreCase) && keyword[2..].Length > 0 && keyword[2..].All(char.IsAsciiDigit))
            return MatchesId(customer.Id, keyword);
        if (keyword.All(char.IsAsciiDigit))
            return MatchesId(customer.Id, keyword) || keyword.Length >= 5 && MatchesPhone(customer, keyword);
        var matchesName = CustomerInput.Normalize(customer.Name).Contains(keyword, StringComparison.CurrentCultureIgnoreCase);
        if (keyword.All(c => char.IsAsciiDigit(c) || c is '+' or '-' or ' ' or '(' or ')'))
            return MatchesPhone(customer, keyword) || matchesName;
        return matchesName;
    }

    private static bool MatchesPhone(CustomerRecord customer, string keyword)
    {
        var digits = CustomerInput.PhoneDigits(keyword);
        return digits.Length > 0 && CustomerInput.Normalize(keyword).All(c => char.IsAsciiDigit(c) || c is '+' or '-' or ' ' or '(' or ')')
            && CustomerInput.PhoneDigits(customer.ContactPhone).Contains(digits, StringComparison.Ordinal);
    }
}

public sealed class CustomerRow : ViewModelBase
{
    private bool isSelected;
    public CustomerRecord Data { get; }
    public int Id => Data.Id;
    public string Code => CustomerInput.FormatCode(Data.Id);
    public string Name => Data.Name;
    public string Phone => Data.ContactPhone;
    public string Address => Data.Address;
    public string Office => Data.OfficeName;
    public string ManagementState => Data.ManagementFlag == CustomerService.ActiveFlag ? "表示中" : "非表示";
    public bool IsSelectable => CustomerService.CanEdit && Data.ManagementFlag == CustomerService.ActiveFlag;
    public string HiddenReason => string.IsNullOrWhiteSpace(Data.HiddenReason) ? "理由未登録" : Data.HiddenReason!;
    public bool IsSelected { get => isSelected; set { if (Set(ref isSelected, value)) SelectionChanged?.Invoke(); } }
    internal Action? SelectionChanged { get; set; }
    public CustomerRow(CustomerRecord data) => Data = data;
}

public sealed class CustomerManagementViewModel : ViewModelBase
{
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private readonly Func<(IReadOnlyList<CustomerRecord> Customers, IReadOnlyList<CustomerOffice> Offices)> loadDirectory;
    private List<CustomerRow> sourceRows = [];
    private CustomerRow[] filteredRows = [];
    private bool updatingSelection;
    private List<CustomerOffice> offices = [];
    private string quickSearch = "", codeFilter = "", nameFilter = "", phoneFilter = "", addressFilter = "", feedback = "";
    private int? officeFilterId;
    private int visibilitySelection = -1;
    private CustomerStatusTab activeTab;
    private bool filtersExpanded, isLoading, isBusy, hasLoaded;
    private int pageNumber = 1, pageSize = 20;
    private string sortColumn = nameof(CustomerRow.Code);
    private ListSortDirection sortDirection;

    public ObservableCollection<CustomerRow> Rows { get; } = [];
    public IReadOnlyList<CustomerOffice> Offices => offices;
    public IReadOnlyList<CustomerOffice> OfficeOptions => new[] { new CustomerOffice(0, "すべて") }.Concat(offices).ToArray();
    public IReadOnlyList<CustomerOffice> VisibilityOptions => CanViewHidden
        ? [new(-1, "すべて"), new(0, "表示中"), new(1, "非表示")] : [new(-1, "すべて"), new(0, "表示中")];
    public string QuickSearch { get => quickSearch; set { if (Set(ref quickSearch, value)) ScheduleSearch(); } }
    public string CodeFilter { get => codeFilter; set { if (Set(ref codeFilter, value)) ScheduleSearch(); } }
    public string NameFilter { get => nameFilter; set { if (Set(ref nameFilter, value)) ScheduleSearch(); } }
    public string PhoneFilter { get => phoneFilter; set { if (Set(ref phoneFilter, value)) ScheduleSearch(); } }
    public string AddressFilter { get => addressFilter; set { if (Set(ref addressFilter, value)) ScheduleSearch(); } }
    public int? OfficeFilterId
    {
        get => officeFilterId;
        set { var normalized = value is 0 ? null : value; if (Set(ref officeFilterId, normalized)) { Notify(nameof(OfficeFilterSelection)); ScheduleSearch(); } }
    }
    public int OfficeFilterSelection { get => OfficeFilterId ?? 0; set => OfficeFilterId = value == 0 ? null : value; }
    public int VisibilitySelection { get => visibilitySelection; set { if (Set(ref visibilitySelection, value)) ScheduleSearch(); } }
    public string Feedback { get => feedback; private set => Set(ref feedback, value); }
    public CustomerStatusTab ActiveTab { get => activeTab; private set { if (Set(ref activeTab, value)) NotifyTabState(); } }
    public bool IsFiltersExpanded { get => filtersExpanded; set { if (Set(ref filtersExpanded, value)) Notify(nameof(FiltersToggleLabel)); } }
    public string FiltersToggleLabel => IsFiltersExpanded ? "詳細条件を折りたたむ" : "詳細条件を展開";
    public bool IsLoading { get => isLoading; private set { if (Set(ref isLoading, value)) { NotifyOperations(); Notify(nameof(ShowEmptyState)); } } }
    public bool IsBusy { get => isBusy; private set { if (Set(ref isBusy, value)) NotifyOperations(); } }
    public bool CanEditCustomers => hasLoaded && CustomerService.CanEdit && CanReload;
    public bool CanRegister => CanEditCustomers && Offices.Count > 0;
    public bool CanViewHidden => CustomerService.CanViewHidden;
    public bool CanReload => !IsLoading && !IsBusy;
    public int AdvancedCount => new[] { CodeFilter, NameFilter, PhoneFilter, AddressFilter }.Count(x => !string.IsNullOrWhiteSpace(x))
        + (OfficeFilterId.HasValue ? 1 : 0) + (VisibilitySelection >= 0 ? 1 : 0);
    public bool HasAdvancedConditions => AdvancedCount > 0;
    public bool HasConditions => HasAdvancedConditions || !string.IsNullOrWhiteSpace(QuickSearch);
    public bool IsEmpty => Rows.Count == 0;
    public bool ShowEmptyState => hasLoaded && !IsLoading && IsEmpty;
    public int SelectedCount => filteredRows.Count(x => x.IsSelected);
    public bool? VisibleSelectionState => !filteredRows.Any(x => x.IsSelectable) ? false
        : filteredRows.Where(x => x.IsSelectable).All(x => x.IsSelected) ? true
        : filteredRows.Any(x => x.IsSelectable && x.IsSelected) ? null : false;
    public bool CanHideSelected => CanEditCustomers && filteredRows.Any(x => x.IsSelected && x.IsSelectable);
    public bool CanClearSelection => SelectedCount > 0;
    public bool CanSelectAll => CanEditCustomers && filteredRows.Any(x => x.IsSelectable);
    public int VisibleCount => sourceRows.Count(x => x.Data.ManagementFlag == CustomerService.ActiveFlag);
    public int HiddenCount => CanViewHidden ? sourceRows.Count(x => x.Data.ManagementFlag != CustomerService.ActiveFlag) : 0;
    public int AllCount => VisibleCount + HiddenCount;
    public string AllTabText => $"すべて　{AllCount:N0}";
    public string VisibleTabText => $"表示中　{VisibleCount:N0}";
    public string HiddenTabText => $"非表示　{HiddenCount:N0}";
    public string CountText => $"{filteredRows.Length:N0} 件該当 / 全 {CurrentTabCount:N0} 件";
    private int CurrentTabCount => ActiveTab switch { CustomerStatusTab.Hidden => HiddenCount, CustomerStatusTab.Visible => VisibleCount, _ => AllCount };

    public IReadOnlyList<int> PageSizes { get; } = [10, 20, 50, 100];
    public IReadOnlyList<int> PageNumbers { get; private set; } = [1];
    public int PageCount => Math.Max(1, (filteredRows.Length + PageSize - 1) / PageSize);
    public int PageSize { get => pageSize; set { if (PageSizes.Contains(value) && Set(ref pageSize, value)) ApplyPage(1); } }
    public int PageNumber { get => pageNumber; set { if (value > 0 && value <= PageCount && value != pageNumber) ApplyPage(value); } }
    public bool CanGoBack => !IsLoading && pageNumber > 1;
    public bool CanGoForward => !IsLoading && pageNumber < PageCount;
    public string PageRange => filteredRows.Length == 0 ? "0 件" :
        $"{(pageNumber - 1) * PageSize + 1:N0}～{(pageNumber - 1) * PageSize + Rows.Count:N0} 件 / 全 {filteredRows.Length:N0} 件";

    public CustomerManagementViewModel() : this(CustomerService.Load) { }

    internal CustomerManagementViewModel(Func<(IReadOnlyList<CustomerRecord> Customers, IReadOnlyList<CustomerOffice> Offices)> loadDirectory)
    {
        this.loadDirectory = loadDirectory;
        debounce.Tick += (_, _) => ApplyFilters(resetPage: true);
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        hasLoaded = false;
        Feedback = "顧客一覧を読み込んでいます…";
        try
        {
            var loaded = await Task.Run(loadDirectory);
            var selected = sourceRows.Where(x => x.IsSelected).Select(x => x.Id).ToHashSet();
            sourceRows = loaded.Customers.Select(x => new CustomerRow(x) { IsSelected = selected.Contains(x.Id) && x.ManagementFlag == CustomerService.ActiveFlag }).ToList();
            foreach (var row in sourceRows) row.SelectionChanged = NotifySelection;
            offices = loaded.Offices.ToList();
            Notify(nameof(Offices)); Notify(nameof(OfficeOptions)); Notify(nameof(OfficeFilterSelection));
            hasLoaded = true;
            Feedback = "";
        }
        catch (UnauthorizedAccessException)
        {
            sourceRows = [];
            Feedback = "顧客情報を表示する権限がありません。";
        }
        catch
        {
            sourceRows = [];
            Feedback = "顧客一覧を読み込めませんでした。データベースの接続状態を確認してください。";
        }
        finally { ApplyFilters(); IsLoading = false; }
    }

    public void SelectTab(CustomerStatusTab tab)
    {
        if (tab == CustomerStatusTab.Hidden && !CanViewHidden) return;
        SetSelection(sourceRows, false);
        ActiveTab = tab;
        ApplyFilters(resetPage: true);
    }

    public void SelectAllVisible(bool selected)
    {
        if (!CanEditCustomers || ActiveTab == CustomerStatusTab.Hidden) return;
        SetSelection(filteredRows, selected);
        NotifySelection();
    }

    public void ClearSelection()
    {
        SetSelection(sourceRows, false);
        NotifySelection();
    }

    private void SetSelection(IEnumerable<CustomerRow> rows, bool selected)
    {
        updatingSelection = true;
        try
        {
            foreach (var row in rows) row.IsSelected = selected && row.IsSelectable;
        }
        finally { updatingSelection = false; }
    }

    public void ClearConditions()
    {
        QuickSearch = CodeFilter = NameFilter = PhoneFilter = AddressFilter = "";
        OfficeFilterId = null;
        VisibilitySelection = -1;
        ApplyFilters(resetPage: true);
    }

    public IReadOnlyList<CustomerRow> SelectedRows => filteredRows.Where(x => x.IsSelected && x.IsSelectable).ToArray();
    public void SortBy(string column, ListSortDirection direction)
    {
        sortColumn = column; sortDirection = direction;
        ApplyFilters(resetPage: true);
    }

    public async Task<CustomerOperationResult> SaveAsync(CustomerEditorViewModel editor, bool allowPossibleDuplicate = false)
    {
        if (!CanEditCustomers) return new(false, "顧客情報を変更できません。");
        if (!editor.IsValid) return new(false, "入力内容を確認してください。");
        IsBusy = true;
        try
        {
            var actorId = AppSession.Employee?.EmId ?? 0;
            var (name, phone, postal, address, fax, office) = (editor.Name, editor.Phone, editor.PostalCode, editor.Address, editor.Fax, editor.OfficeId);
            var result = await Task.Run(() => editor.IsNew
                ? CustomerService.Create(actorId, name, phone, postal, address, fax, office, allowPossibleDuplicate)
                : CustomerService.Update(actorId, editor.Expected!, name, phone, postal, address, fax, office));
            if (result.Succeeded) await LoadAfterWriteAsync();
            return result;
        }
        finally { IsBusy = false; }
    }

    public async Task<CustomerOperationResult> RestoreAsync(CustomerRecord customer)
    {
        if (!CanEditCustomers) return new(false, "顧客情報を変更できません。");
        IsBusy = true;
        try
        {
            var employeeId = AppSession.Employee?.EmId ?? 0;
            var result = await Task.Run(() => CustomerService.Restore(employeeId, customer));
            if (result.Succeeded) await LoadAfterWriteAsync();
            return result;
        }
        finally { IsBusy = false; }
    }

    public async Task<CustomerOperationResult> HideAsync(IReadOnlyCollection<CustomerRow> targets, string? reason)
    {
        var selected = targets.Where(x => x.Data.ManagementFlag == CustomerService.ActiveFlag).DistinctBy(x => x.Id).ToArray();
        if (!CanEditCustomers) return new(false, "顧客情報を変更できません。");
        if (string.IsNullOrWhiteSpace(reason)) return new(false, "非表示理由を入力してください。");
        if (!CustomerService.CanEdit || selected.Length == 0) return new(false, "非表示にできる顧客を選択してください。");
        reason = reason.Trim();
        IsBusy = true;
        var succeeded = 0; var errors = new List<string>();
        try
        {
            var actorId = AppSession.Employee?.EmId ?? 0;
            foreach (var row in selected)
            {
                var result = await Task.Run(() => CustomerService.Hide(actorId, row.Id, reason));
                if (result.Succeeded) succeeded++;
                else errors.Add($"{row.Code}　{result.Message}");
            }
            await LoadAfterWriteAsync();
        }
        finally { IsBusy = false; }
        var message = $"成功　{succeeded:N0} 件\n失敗　{errors.Count:N0} 件";
        if (errors.Count > 0) message += "\n\n" + string.Join("\n", errors.Take(8));
        return new(errors.Count == 0, message, succeeded);
    }

    private async Task LoadAfterWriteAsync()
    {
        // Keep the current search and tab while refreshing all status counts.
        await LoadAsync();
    }

    private void ScheduleSearch()
    {
        Notify(nameof(HasConditions));
        Notify(nameof(AdvancedCount)); Notify(nameof(HasAdvancedConditions));
        debounce.Stop(); debounce.Start();
    }

    private void ApplyFilters(bool resetPage = false)
    {
        debounce.Stop();
        var criteria = new CustomerSearchCriteria(CodeFilter, NameFilter, PhoneFilter, AddressFilter, OfficeFilterId,
            QuickSearch, VisibilitySelection >= 0 ? VisibilitySelection : null);
        var matches = sourceRows.Where(row => CustomerFilter.Matches(row.Data, criteria, ActiveTab, CanViewHidden));
        Func<CustomerRow, IComparable> key = sortColumn switch
        {
            nameof(CustomerRow.Name) => x => x.Name,
            nameof(CustomerRow.Phone) => x => x.Phone,
            nameof(CustomerRow.Office) => x => x.Office,
            nameof(CustomerRow.ManagementState) => x => x.ManagementState,
            _ => x => x.Id
        };
        var ordered = sortDirection == ListSortDirection.Descending ? matches.OrderByDescending(key) : matches.OrderBy(key);
        filteredRows = ordered.ThenBy(x => x.Id).ToArray();
        var visible = filteredRows.ToHashSet();
        SetSelection(sourceRows.Where(x => x.IsSelected && !visible.Contains(x)), false);
        ApplyPage(resetPage ? 1 : pageNumber);
        NotifyList(); NotifySelection();
    }

    private void ApplyPage(int requested)
    {
        pageNumber = Math.Clamp(requested, 1, PageCount);
        Rows.Clear();
        foreach (var row in filteredRows.Skip((pageNumber - 1) * PageSize).Take(PageSize)) Rows.Add(row);
        if (PageNumbers.Count != PageCount)
        {
            PageNumbers = Enumerable.Range(1, PageCount).ToArray();
            Notify(nameof(PageNumbers)); Notify(nameof(PageCount));
        }
        Notify(nameof(PageNumber)); Notify(nameof(PageRange)); Notify(nameof(CanGoBack)); Notify(nameof(CanGoForward));
    }

    private void NotifyList()
    {
        Notify(nameof(IsEmpty)); Notify(nameof(ShowEmptyState)); Notify(nameof(CountText)); Notify(nameof(AllCount)); Notify(nameof(VisibleCount)); Notify(nameof(HiddenCount));
        Notify(nameof(AllTabText)); Notify(nameof(VisibleTabText)); Notify(nameof(HiddenTabText));
    }

    private void NotifyTabState() { Notify(nameof(CountText)); Notify(nameof(CanHideSelected)); Notify(nameof(CanSelectAll)); }
    private void NotifySelection()
    {
        if (updatingSelection) return;
        Notify(nameof(SelectedCount)); Notify(nameof(VisibleSelectionState));
        Notify(nameof(CanHideSelected)); Notify(nameof(CanClearSelection)); Notify(nameof(CanSelectAll));
    }
    private void NotifyOperations()
    {
        Notify(nameof(CanEditCustomers)); Notify(nameof(CanRegister)); Notify(nameof(CanHideSelected)); Notify(nameof(CanSelectAll)); Notify(nameof(CanReload));
        Notify(nameof(CanGoBack)); Notify(nameof(CanGoForward));
    }
}

public sealed class CustomerEditorViewModel : ViewModelBase
{
    private string name = "", phone = "", postalCode = "", address = "", fax = "";
    private int officeId;
    private bool isSaving;
    private string operationError = "";
    public bool IsNew { get; }
    public CustomerEditSnapshot? Expected { get; }
    public IReadOnlyList<CustomerOffice> Offices { get; }
    public string Header => IsNew ? "顧客登録" : "顧客更新";
    public string Subtitle => IsNew ? "新しい顧客情報を登録します。" : $"{CustomerCode}　{Expected!.Name}　／　{ManagementState}";
    public string CustomerCode => IsNew ? "登録時に自動採番" : CustomerInput.FormatCode(Expected!.Id);
    public string ManagementState => Expected?.ManagementFlag is null or CustomerService.ActiveFlag ? "表示中" : "非表示";
    public string Name { get => name; set { if (Set(ref name, value ?? "")) NotifyForm(); } }
    public string Phone { get => phone; set { if (Set(ref phone, value ?? "")) NotifyForm(); } }
    public string PostalCode { get => postalCode; set { if (Set(ref postalCode, value ?? "")) NotifyForm(); } }
    public string Address { get => address; set { if (Set(ref address, value ?? "")) NotifyForm(); } }
    public string Fax { get => fax; set { if (Set(ref fax, value ?? "")) NotifyForm(); } }
    public int OfficeId { get => officeId; set { if (Set(ref officeId, value)) NotifyForm(); } }
    public string OperationError { get => operationError; set => Set(ref operationError, value); }
    public bool IsSaving { get => isSaving; set { if (Set(ref isSaving, value)) Notify(nameof(CanSave)); } }
    public bool CanChangeOffice => AppSession.IsAdmin;
    public string NameError => string.IsNullOrWhiteSpace(Name) ? "顧客名を入力してください。" : Name.Trim().Length > 50 ? "50文字以内で入力してください。" : "";
    public string PhoneError => CustomerInput.PhoneError(Phone, Expected?.Phone);
    public string PostalError => CustomerInput.PostalError(PostalCode, Expected?.PostalCode);
    public string AddressError => Address.Trim().Length > 50 ? "50文字以内で入力してください。" : "";
    public string FaxError => Fax.Trim().Length > 13 ? "13文字以内で入力してください。" : "";
    public bool IsValid => NameError.Length == 0 && PhoneError.Length == 0 && PostalError.Length == 0
        && AddressError.Length == 0 && FaxError.Length == 0 && Offices.Any(x => x.Id == OfficeId);
    public string OfficeError => Offices.Any(x => x.Id == OfficeId) ? "" : "担当営業所を選択してください。";
    public bool HasChanges => Expected is { } original
        ? Name.Trim() != original.Name.Trim() || Phone.Trim() != original.Phone.Trim()
            || CustomerInput.NormalizePostalCode(PostalCode) != CustomerInput.NormalizePostalCode(original.PostalCode)
            || Address.Trim() != original.Address.Trim() || Fax.Trim() != original.Fax.Trim() || OfficeId != original.OfficeId
        : !string.IsNullOrWhiteSpace(Name + Phone + PostalCode + Address + Fax) || OfficeId != (Offices.FirstOrDefault()?.Id ?? 0);
    public bool CanSave => IsValid && !IsSaving && (IsNew || HasChanges);
    public string PreviewOffice => Offices.FirstOrDefault(x => x.Id == OfficeId)?.Name ?? "未選択";
    public string PreviewName => string.IsNullOrWhiteSpace(Name) ? "未入力" : Name;
    public string PreviewPhone => string.IsNullOrWhiteSpace(Phone) ? "未入力" : Phone;
    public string PreviewPostal => string.IsNullOrWhiteSpace(PostalCode) ? "未設定" : PostalCode;
    public string PreviewAddress => string.IsNullOrWhiteSpace(Address) ? "未設定" : Address;
    public string PreviewFax => string.IsNullOrWhiteSpace(Fax) ? "未設定" : Fax;

    public CustomerEditorViewModel(IReadOnlyList<CustomerOffice> offices, CustomerRecord? existing = null)
    {
        Offices = offices;
        IsNew = existing == null;
        if (existing != null)
        {
            Expected = CustomerService.Snapshot(existing);
            name = existing.Name; phone = existing.ContactPhone; postalCode = existing.PostalCode;
            address = existing.Address; fax = existing.Fax; officeId = existing.OfficeId;
        }
        else officeId = offices.FirstOrDefault()?.Id ?? 0;
    }

    private void NotifyForm()
    {
        foreach (var property in new[] { nameof(NameError), nameof(PhoneError), nameof(PostalError), nameof(AddressError), nameof(FaxError), nameof(OfficeError),
            nameof(IsValid), nameof(CanSave), nameof(HasChanges), nameof(PreviewName), nameof(PreviewPhone), nameof(PreviewPostal), nameof(PreviewAddress), nameof(PreviewFax), nameof(PreviewOffice) }) Notify(property);
    }
}
