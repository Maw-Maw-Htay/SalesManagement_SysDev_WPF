using System.Collections.ObjectModel;
using System.Globalization;
using System.ComponentModel;
using System.Windows.Threading;

namespace SalesManagement_SysDev;

public enum OrderStockTone { Normal, Warning, Danger }

public readonly record struct OrderStockStatus(string Text, OrderStockTone Tone, string? Details = null)
{
    public string Tooltip => Details ?? Text;

    internal static OrderStockStatus From(InventoryStockState state, int? quantity, long? requested) => state switch
    {
        InventoryStockState.Available when quantity is 0 => new("在庫がありません", OrderStockTone.Danger),
        InventoryStockState.Available when quantity is > 0 && requested > quantity =>
            new($"在庫不足：{requested - quantity:N0}点", OrderStockTone.Danger,
                $"現在庫：{quantity:N0}点 / 受注数量：{requested:N0}点"),
        InventoryStockState.Available when quantity is > 0 => new($"在庫：{quantity:N0}点", OrderStockTone.Normal),
        _ => new(InventoryService.DescribeUnavailableStock(state), OrderStockTone.Warning)
    };
}

public sealed class RegistrationProduct
{
    private int? stock;
    private InventoryStockState stockState;

    public int PrId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string ModelNumber { get; init; } = "";
    public string Barcode { get; init; } = "";
    public string MakerName { get; init; } = "";
    public decimal UnitPrice { get; init; }
    // The existing product schema has no configurable unit column. Order quantities use the
    // same item-count unit already shown by the stock UI.
    public string UnitName => "個";
    public int? Stock { get => stock; init => stock = value; }
    public InventoryStockState StockState { get => stockState; init => stockState = value; }
    public string CategoryName { get; init; } = "";
    public bool IsActive { get; init; } = true;
    public string Display => $"[{ProductCode}] {ProductName}（単価: ¥{UnitPrice:N0}）";

    internal void ApplyStockLookup(InventoryStockLookup lookup)
    {
        stock = lookup.Quantity;
        stockState = lookup.State;
    }
}

public sealed class RegistrationLine : ViewModelBase
{
    // Retained only by an update draft; registration lines do not have a saved detail ID.
    public int? SourceDetailId { get; init; }
    private OrderDetailItemRow? display;
    private int? stock;
    private InventoryStockState stockState;
    private int quantity = 1;
    private string quantityText = "1";
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string UnitName => "個";
    public decimal UnitPrice { get; init; }
    public int? Stock { get => stock; init => stock = value; }
    public InventoryStockState StockState { get => stockState; init => stockState = value; }
    public bool IsStockStatusVisible { get; private set; } = true;
    public int Quantity { get => quantity; set => QuantityText = value.ToString(CultureInfo.InvariantCulture); }
    public string QuantityText
    {
        get => quantityText;
        set
        {
            if (quantityText == value) return;
            quantityText = value;
            if (RegistrationQuantity.TryParse(value, out var count)) quantity = count;
            // Publish only after the last valid quantity is updated, so totals observe the same value.
            foreach (var name in new[] { nameof(QuantityText), nameof(Quantity), nameof(Amount), nameof(QuantityError),
                nameof(StockStatus) }) Notify(name);
        }
    }
    public int MaximumQuantity => RegistrationQuantity.Maximum(UnitPrice);
    public string QuantityError => !RegistrationQuantity.TryParse(QuantityText, out var count)
        ? "数量は1以上の整数で入力してください。"
        : !OrderUpdateRules.TrySubtotal(count, UnitPrice, out _) ? "行合計は9,999,999,999円以内で入力してください。" : "";
    public decimal Amount => UnitPrice * Quantity;
    public int Number => display?.Number ?? 0;
    public decimal TotalIncludingTax => display?.Total ?? 0;
    public OrderStockStatus StockStatus => OrderStockStatus.From(StockState, Stock,
        RegistrationQuantity.TryParse(QuantityText, out var count) ? count : null);

    internal void SetStockStatusVisible(bool visible)
    {
        if (IsStockStatusVisible == visible) return;
        IsStockStatusVisible = visible;
        Notify(nameof(IsStockStatusVisible));
    }

    internal void ApplyStockLookup(InventoryStockLookup lookup)
    {
        if (stock == lookup.Quantity && stockState == lookup.State) return;
        stock = lookup.Quantity;
        stockState = lookup.State;
        Notify(nameof(Stock)); Notify(nameof(StockState));
        Notify(nameof(StockStatus));
    }

    internal void UpdateDisplay(OrderDetailItemRow row)
    {
        display = row;
        Notify(nameof(Number));
        Notify(nameof(TotalIncludingTax));
    }
}

internal static class RegistrationQuantity
{
    internal static bool TryParse(string text, out int count) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out count) && count > 0;
    internal static int Maximum(decimal price) => price <= 0 ? int.MaxValue
        : (int)Math.Max(1m, Math.Min(int.MaxValue, decimal.Floor(OrderUpdateRules.MaximumLineAmount / price)));
}

public sealed class OrderRegistrationViewModel : ViewModelBase, IDisposable
{
    private readonly IOrderRegistrationDataSource source;
    private readonly Action close;
    private readonly Action startNextOrder;
    private readonly Func<bool> confirmDiscard;
    private readonly Action<OrderDetailSnapshot> showDetails;
    private IReadOnlyList<MSalesOffice> availableOffices = [];
    private IReadOnlyList<MClient> availableClients = [];
    private IReadOnlyList<RegistrationProduct> availableProducts = [];
    private IReadOnlyList<MSalesOffice> offices = [];
    private IReadOnlyList<MClient> clients = [];
    private IReadOnlyList<RegistrationProduct> products = [];
    private MSalesOffice? selectedOffice;
    private MClient? selectedClient;
    private RegistrationProduct? selectedProduct;
    private string officeSearch = "", customerSearch = "", productSearch = "";
    private string quantity = "1", remarks = "", feedback = "";
    private readonly HashSet<RegistrationLine> observedLines = [];
    private readonly DispatcherTimer undoTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private RegistrationLine? deletedLine;
    private int deletedIndex;
    private bool hasDetailEdits, isProductDetailsVisible;
    private RegistrationProduct? productDetails;
    private DateTime initialOrderDate = DateTime.Today;
    private DateTime initialDeliveryDate = DateTime.Today.AddDays(7);
    private DateTime? orderDate = DateTime.Today, deliveryDate = DateTime.Today.AddDays(7);
    private string orderDateText = DateTime.Today.ToString("d"), deliveryDateText = DateTime.Today.AddDays(7).ToString("d");
    private int? initialOfficeId;
    private bool isBusy, isSuccess, isCompletionDialogVisible, settingSearch, isLoaded, closeApproved;
    private int registeredOrderId;
    private string completionError = "";
    private OrderDetailViewModel preview = CreatePreview(new OrderDetailSnapshot());

    public OrderRegistrationViewModel(Action close, Func<bool>? confirmDiscard = null, IOrderRegistrationDataSource? source = null,
        Action<OrderDetailSnapshot>? showDetails = null, Action? startNextOrder = null)
    {
        this.close = close;
        this.startNextOrder = startNextOrder ?? close;
        this.confirmDiscard = confirmDiscard ?? (() => false);
        this.source = source ?? new OrderRegistrationDataSource();
        this.showDetails = showDetails ?? (_ => { });
        CloseCommand = new RelayCommand(RequestClose, () => !isBusy);
        EscapeCommand = new RelayCommand(HandleEscape, () => !isBusy);
        DismissCompletionCommand = new RelayCommand(DismissCompletionDialog, () => !isBusy && isCompletionDialogVisible);
        UndoRemoveLineCommand = new RelayCommand(UndoRemoveLine, () => !isBusy && !isSuccess && deletedLine != null);
        ShowProductDetailsCommand = new RelayCommand(() =>
        {
            productDetails = selectedProduct;
            isProductDetailsVisible = true;
            Notify(nameof(ProductDetails)); Notify(nameof(IsProductDetailsVisible)); StateChanged();
        }, () => !isBusy && !isSuccess && IsProductValid);
        CloseProductDetailsCommand = new RelayCommand(CloseProductDetails, () => isProductDetailsVisible);
        undoTimer.Tick += (_, _) => ClearUndo();
        AddLineCommand = new RelayCommand(AddLine, () => !isBusy && !isSuccess && CanAddProduct);
        RemoveLineCommand = new RelayCommand(RemoveLine,
            row => !isBusy && !isSuccess && row is RegistrationLine line && Lines.Contains(line));
        RegisterCommand = new RelayCommand(async () => await RegisterAsync(), () => !isBusy && CanRegister);
        ShowRegisteredOrderCommand = new RelayCommand(async () => await ShowRegisteredOrderAsync(), () => !isBusy && isSuccess && registeredOrderId > 0);
        ReturnToListCommand = new RelayCommand(RequestClose, () => !isBusy && isSuccess);
        StartNextOrderCommand = new RelayCommand(StartNextOrder, () => !isBusy && isSuccess);
        SelectOfficeCommand = new RelayCommand(row => { if (row is MSalesOffice office) SelectedOffice = office; }, _ => !isBusy && !isSuccess);
        SelectClientCommand = new RelayCommand(row => { if (row is MClient client) SelectedClient = client; }, _ => !isBusy && !isSuccess);
        SelectProductCommand = new RelayCommand(row => { if (row is RegistrationProduct product) SelectedProduct = product; }, _ => !isBusy && !isSuccess);
        Lines.CollectionChanged += (_, _) => LinesChanged();
    }

    public ObservableCollection<RegistrationLine> Lines { get; } = [];
    public IReadOnlyList<MSalesOffice> Offices => offices;
    public IReadOnlyList<MClient> Clients => clients;
    public IReadOnlyList<RegistrationProduct> Products => products;
    public RelayCommand CloseCommand { get; }
    public RelayCommand EscapeCommand { get; }
    public RelayCommand DismissCompletionCommand { get; }
    public RelayCommand UndoRemoveLineCommand { get; }
    public RelayCommand ShowProductDetailsCommand { get; }
    public RelayCommand CloseProductDetailsCommand { get; }
    public bool HasUndo => deletedLine != null;
    public bool IsProductDetailsVisible => isProductDetailsVisible;
    public RegistrationProduct? ProductDetails => productDetails;
    public bool HasSelectedProduct => IsProductValid;
    public string QuantityLabel => IsProductValid ? $"数量 *（{selectedProduct!.UnitName}）" : "数量 *";
    public int MaximumQuantity => RegistrationQuantity.Maximum(selectedProduct?.UnitPrice ?? 0);
    public OrderStockStatus StockStatus
    {
        get
        {
            if (!IsProductValid || selectedProduct is not { } product) return new("", OrderStockTone.Normal);
            var existing = Lines.FirstOrDefault(line => line.ProductId == product.PrId)?.Quantity ?? 0;
            long? requested = TryQuantity(out var count) ? (long)count + existing : null;
            return OrderStockStatus.From(product.StockState, product.Stock, requested);
        }
    }
    public RelayCommand AddLineCommand { get; }
    public RelayCommand RemoveLineCommand { get; }
    public RelayCommand RegisterCommand { get; }
    public RelayCommand ShowRegisteredOrderCommand { get; }
    public RelayCommand ReturnToListCommand { get; }
    public RelayCommand StartNextOrderCommand { get; }
    public RelayCommand SelectOfficeCommand { get; }
    public RelayCommand SelectClientCommand { get; }
    public RelayCommand SelectProductCommand { get; }
    public event Action<string>? RegistrationCompleted;
    public bool IsBusy => isBusy;
    public bool IsSuccess => isSuccess;
    public bool IsCompletionDialogVisible => isCompletionDialogVisible;
    public bool IsEditing => !isSuccess;
    public int RegisteredOrderId => registeredOrderId;
    public string RegisteredOrderNumber => registeredOrderId > 0 ? $"ORD-{registeredOrderId:000000}" : "";
    public string RegisterButtonText => "受注を登録";
    public string CompletionError => completionError;
    public string Feedback => feedback;
    public string EmployeeName => AppSession.Employee?.EmName ?? "未設定";
    public bool IsCartEmpty => Lines.Count == 0;
    public bool HasItems => Lines.Count > 0;
    public string QuantitySummary => $"{Lines.Count} 品目 / {Lines.Sum(line => (long)line.Quantity):N0} 点";
    public decimal Subtotal => preview.Subtotal;
    public decimal Tax => preview.Tax;
    public decimal Total => preview.Total;
    public string RoundingNotice => preview.RoundingNotice;
    public string UnitPriceDisplay => IsProductValid ? $"¥{selectedProduct!.UnitPrice:N0}" : "商品選択後に表示";
    public string PreviewCustomerCode => IsClientValid ? preview.CustomerCode : "未選択";
    public string PreviewCustomerName => IsClientValid ? preview.CustomerName : "未選択";
    public string PreviewPhone => IsClientValid ? preview.CustomerPhone : "未設定";
    public string PreviewAddress => IsClientValid ? preview.CustomerAddress : "未設定";
    public string PreviewOffice => IsOfficeValid ? selectedOffice!.SoName : "未選択";
    public string PreviewOrderDate => EffectiveOrderDate?.ToString("yyyy/MM/dd") ?? "未設定";
    public string PreviewDeliveryDate => EffectiveDeliveryDate?.ToString("yyyy/MM/dd") ?? "未設定";
    public string PreviewRemarks => string.IsNullOrWhiteSpace(Remarks) ? "未入力" : Remarks;

    public string OfficeSearch
    {
        get => officeSearch;
        set
        {
            if (!Set(ref officeSearch, value) || settingSearch) return;
            if (selectedOffice != null && value != selectedOffice.SoName) SetOffice(null);
            FilterOffices(); RefreshPreview(); StateChanged();
        }
    }
    public string CustomerSearch
    {
        get => customerSearch;
        set
        {
            if (!Set(ref customerSearch, value) || settingSearch) return;
            if (selectedClient != null && value != selectedClient.ClName) { selectedClient = null; Notify(nameof(SelectedClient)); }
            FilterClients(); RefreshPreview(); StateChanged();
        }
    }
    public string ProductSearch
    {
        get => productSearch;
        set
        {
            if (!Set(ref productSearch, value) || settingSearch) return;
            if (selectedProduct != null && value != selectedProduct.ProductName)
            {
                selectedProduct = null;
                Notify(nameof(SelectedProduct));
                RefreshLineStockStatusVisibility();
            }
            FilterProducts(); StateChanged();
        }
    }
    public MSalesOffice? SelectedOffice
    {
        get => selectedOffice;
        set
        {
            var canonical = availableOffices.FirstOrDefault(office => office.SoId == value?.SoId);
            SetOffice(canonical);
            SetSearch(ref officeSearch, canonical?.SoName ?? "", nameof(OfficeSearch));
            FilterOffices(); RefreshPreview(); StateChanged();
        }
    }
    public MClient? SelectedClient
    {
        get => selectedClient;
        set
        {
            selectedClient = availableClients.FirstOrDefault(client => client.ClId == value?.ClId && client.ClFlag == 0 && client.SoId == selectedOffice?.SoId);
            Notify();
            SetSearch(ref customerSearch, selectedClient?.ClName ?? "", nameof(CustomerSearch));
            FilterClients(); RefreshPreview(); StateChanged();
        }
    }
    public RegistrationProduct? SelectedProduct
    {
        get => selectedProduct;
        set
        {
            var next = availableProducts.FirstOrDefault(product => product.PrId == value?.PrId && product.IsActive);
            if (next != null)
            {
                try { next.ApplyStockLookup(source.LoadStock(next.PrId)); }
                catch (Exception exception)
                {
                    System.Diagnostics.Trace.TraceError("受注登録の商品在庫照会に失敗しました (PrID={0}): {1}", next.PrId, exception);
                    next.ApplyStockLookup(new InventoryStockLookup(InventoryStockState.QueryFailed));
                }
                foreach (var line in Lines.Where(line => line.ProductId == next.PrId))
                    line.ApplyStockLookup(new InventoryStockLookup(next.StockState, next.Stock));
            }
            selectedProduct = next;
            Notify();
            SetSearch(ref productSearch, selectedProduct?.ProductName ?? "", nameof(ProductSearch));
            FilterProducts(); RefreshLineStockStatusVisibility(); StateChanged();
        }
    }
    public string Quantity { get => quantity; set { if (Set(ref quantity, value)) StateChanged(); } }
    public string Remarks { get => remarks; set { if (Set(ref remarks, value)) { Notify(nameof(PreviewRemarks)); StateChanged(); } } }
    public DateTime? OrderDate
    {
        get => orderDate;
        set { if (Set(ref orderDate, value)) SetDateInputText(false, value?.ToString("d") ?? ""); }
    }
    public DateTime? DeliveryDate
    {
        get => deliveryDate;
        set { if (Set(ref deliveryDate, value)) SetDateInputText(true, value?.ToString("d") ?? ""); }
    }
    private DateTime? EffectiveOrderDate => ParseDate(orderDateText);
    private DateTime? EffectiveDeliveryDate => ParseDate(deliveryDateText);
    private static DateTime? ParseDate(string text) => DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var date) ? date.Date : null;
    public void SetDateInputText(bool delivery, string text)
    {
        if (delivery) deliveryDateText = text; else orderDateText = text;
        Notify(nameof(PreviewOrderDate)); Notify(nameof(PreviewDeliveryDate));
        StateChanged();
    }

    private bool IsOfficeValid => selectedOffice != null && availableOffices.Contains(selectedOffice) && officeSearch == selectedOffice.SoName
        && (AppSession.IsAdmin || selectedOffice.SoId == AppSession.Employee?.SoId);
    private bool IsClientValid => IsOfficeValid && selectedClient != null && availableClients.Contains(selectedClient)
        && selectedClient.ClFlag == 0 && selectedClient.SoId == selectedOffice!.SoId && customerSearch == selectedClient.ClName;
    private bool IsProductValid => selectedProduct != null && availableProducts.Contains(selectedProduct) && selectedProduct.IsActive
        && selectedProduct.UnitPrice >= 0 && productSearch == selectedProduct.ProductName;
    private bool TryQuantity(out int count) => RegistrationQuantity.TryParse(Quantity, out count);
    public bool CanAddProduct => !isBusy && !isSuccess && !isProductDetailsVisible && AppSession.CanManageOrders
        && IsProductValid && TryQuantity(out _) && QuantityError.Length == 0;
    public bool CanRegister => !closeApproved && !isBusy && !isProductDetailsVisible && isLoaded && AppSession.CanManageOrders && AppSession.Employee != null && IsClientValid
        && EffectiveOrderDate.HasValue && EffectiveDeliveryDate.HasValue && Remarks.Trim().Length <= 50 && Lines.Count > 0
        && (ProductSearch.Length == 0 || IsProductValid) && TryQuantity(out _) && QuantityError.Length == 0
        && Lines.All(line => line.QuantityError.Length == 0 && availableProducts.Any(product => product.PrId == line.ProductId
            && product.IsActive && product.UnitPrice == line.UnitPrice && line.UnitPrice >= 0));
    public string QuantityError => !TryQuantity(out var count) ? "数量は1以上の整数で入力してください。"
        : IsProductValid && !OrderUpdateRules.TrySubtotal(count, selectedProduct!.UnitPrice, out _)
            ? "行合計は9,999,999,999円以内で入力してください。" : "";
    public bool CustomerInputInvalid => CustomerSearch.Length > 0 && !IsClientValid;
    public bool OfficeInputInvalid => OfficeSearch.Length > 0 && !IsOfficeValid;
    public bool ProductInputInvalid => ProductSearch.Length > 0 && !IsProductValid;
    public string DateInputError => EffectiveOrderDate == null || EffectiveDeliveryDate == null ? "受注日・納品希望日を正しい日付で指定してください。" : "";
    public bool HasUnsavedChanges => !closeApproved && (hasDetailEdits || Lines.Count > 0 || CustomerSearch.Length > 0 || ProductSearch.Length > 0
        || Quantity != "1" || !string.IsNullOrWhiteSpace(Remarks) || EffectiveOrderDate != initialOrderDate
        || EffectiveDeliveryDate != initialDeliveryDate || selectedOffice?.SoId != initialOfficeId
        || OfficeSearch.Length > 0 && !IsOfficeValid);
    public string ValidationSummary => isBusy ? "受注を登録しています…"
        : CanRegister ? "入力内容を確認して登録してください。"
        : !IsOfficeValid ? "受付営業所を候補から選択してください。"
        : !IsClientValid ? "発注元顧客を候補から選択してください。"
        : DateInputError.Length > 0 ? DateInputError
        : Remarks.Trim().Length > 50 ? "備考は50文字以内で入力してください。"
        : ProductInputInvalid ? "商品を候補から選択するか、商品欄をクリアしてください。"
        : !TryQuantity(out _) ? "数量は1以上の整数で入力してください。"
        : QuantityError.Length > 0 ? QuantityError
        : Lines.Count == 0 ? "商品を1件以上追加してください。"
        : Lines.FirstOrDefault(line => line.QuantityError.Length > 0) is { } invalid ? $"{invalid.ProductCode}：{invalid.QuantityError}"
        : "明細の数量・単価を確認してください。";

    public void Load()
    {
        if (!AppSession.CanManageOrders) return;
        try
        {
            availableOffices = source.LoadOffices().Where(office => office.SoFlag == 0
                && (AppSession.IsAdmin || office.SoId == AppSession.Employee?.SoId)).ToArray();
            availableProducts = source.LoadProducts().Where(product => product.IsActive).ToArray();
            SelectedOffice = availableOffices.FirstOrDefault(office => office.SoId == AppSession.Employee?.SoId) ?? availableOffices.FirstOrDefault();
            initialOfficeId = selectedOffice?.SoId;
            FilterProducts(); isLoaded = true; RefreshPreview(); StateChanged();
        }
        catch { ShowFeedback("入力候補を取得できませんでした。接続を確認し、画面を開き直してください。"); }
    }

    private void SetOffice(MSalesOffice? office)
    {
        if (ReferenceEquals(selectedOffice, office)) return;
        selectedOffice = office; selectedClient = null;
        Notify(nameof(SelectedOffice)); Notify(nameof(SelectedClient));
        SetSearch(ref customerSearch, "", nameof(CustomerSearch));
        availableClients = [];
        if (office != null && AppSession.CanManageOrders)
        {
            try { availableClients = source.LoadClients(office.SoId).Where(client => client.ClFlag == 0 && client.SoId == office.SoId).ToArray(); }
            catch { ShowFeedback("顧客を取得できませんでした。営業所を選び直してください。"); }
        }
        FilterClients(); RefreshPreview();
    }
    private static bool Matches(string value, string query) => value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    private void FilterOffices()
    {
        var query = selectedOffice != null && OfficeSearch == selectedOffice.SoName ? "" : OfficeSearch.Trim();
        offices = availableOffices.Where(office => Matches(office.SoName, query) || Matches(office.SoId.ToString(), query)).Take(30).ToArray();
        Notify(nameof(Offices));
    }
    private void FilterClients()
    {
        var query = selectedClient != null && CustomerSearch == selectedClient.ClName ? "" : CustomerSearch.Trim();
        clients = availableClients.Where(client => Matches(client.ClName, query) || Matches($"C-{client.ClId:0000}", query)
            || Matches(client.ClId.ToString(), query)).Take(30).ToArray();
        Notify(nameof(Clients));
    }
    private void FilterProducts()
    {
        var query = selectedProduct != null && ProductSearch == selectedProduct.ProductName ? "" : ProductSearch.Trim();
        products = availableProducts.Where(product => Matches(product.ProductName, query) || Matches(product.ProductCode, query)
            || Matches(product.Barcode, query) || Matches(product.ModelNumber, query) || Matches(product.MakerName, query)).Take(30).ToArray();
        Notify(nameof(Products));
    }
    private void SetSearch(ref string field, string value, string name)
    {
        settingSearch = true;
        field = value; Notify(name);
        settingSearch = false;
    }

    private void AddLine()
    {
        if (!CanAddProduct || !TryQuantity(out var count)) return;
        var product = selectedProduct!;
        var existing = Lines.FirstOrDefault(line => line.ProductId == product.PrId);
        if (existing != null && (long)existing.Quantity + count > int.MaxValue)
        { ShowFeedback("合算後の数量が入力可能な範囲を超えています。"); return; }
        var mergedQuantity = (existing?.Quantity ?? 0) + count;
        if (!OrderUpdateRules.TrySubtotal(mergedQuantity, existing?.UnitPrice ?? product.UnitPrice, out _))
        { ShowFeedback("合算後の行合計は9,999,999,999円以内で入力してください。"); return; }
        if (existing == null) Lines.Add(new RegistrationLine
        {
            ProductId = product.PrId, ProductCode = product.ProductCode, ProductName = product.ProductName,
            UnitPrice = product.UnitPrice, Stock = product.Stock, StockState = product.StockState, Quantity = count
        });
        else existing.Quantity = mergedQuantity;
        hasDetailEdits = true;
        SelectedProduct = null; Quantity = "1";
        ShowFeedback(""); StateChanged();
    }

    private void LinesChanged()
    {
        foreach (var line in observedLines.Where(line => !Lines.Contains(line)).ToArray())
        { line.PropertyChanged -= LineChanged; observedLines.Remove(line); }
        foreach (var line in Lines.Where(line => !observedLines.Contains(line)))
        { line.PropertyChanged += LineChanged; observedLines.Add(line); }
        RefreshLineStockStatusVisibility();
        hasDetailEdits = true;
        RefreshPreview(); StateChanged();
    }
    private void RefreshLineStockStatusVisibility()
    {
        foreach (var line in Lines)
            line.SetStockStatusVisible(selectedProduct?.PrId != line.ProductId);
    }
    private void LineChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(RegistrationLine.QuantityText)) return;
        hasDetailEdits = true;
        RefreshPreview(); StateChanged();
    }
    private void RemoveLine(object? row)
    {
        if (row is not RegistrationLine line) return;
        deletedIndex = Lines.IndexOf(line);
        deletedLine = new RegistrationLine { SourceDetailId = line.SourceDetailId, ProductId = line.ProductId, ProductCode = line.ProductCode,
            ProductName = line.ProductName, UnitPrice = line.UnitPrice, Stock = line.Stock, StockState = line.StockState, Quantity = line.Quantity };
        Lines.Remove(line);
        undoTimer.Stop(); undoTimer.Start();
        Notify(nameof(HasUndo)); UndoRemoveLineCommand.Refresh();
    }
    private void UndoRemoveLine()
    {
        if (deletedLine is not { } saved) return;
        var duplicate = Lines.FirstOrDefault(line => line.ProductId == saved.ProductId);
        var quantity = (long)saved.Quantity + (duplicate?.Quantity ?? 0);
        if (quantity > int.MaxValue || !OrderUpdateRules.TrySubtotal((int)quantity, saved.UnitPrice, out _))
        { ShowFeedback("合算後の数量・行合計が上限を超えるため復元できません。数量を調整してください。"); return; }
        if (duplicate != null) Lines.Remove(duplicate);
        saved.Quantity = (int)quantity;
        Lines.Insert(Math.Min(deletedIndex, Lines.Count), saved);
        ClearUndo();
    }
    private void ClearUndo()
    {
        undoTimer.Stop(); deletedLine = null;
        Notify(nameof(HasUndo)); UndoRemoveLineCommand.Refresh();
    }
    private void CloseProductDetails()
    {
        isProductDetailsVisible = false;
        Notify(nameof(IsProductDetailsVisible)); CloseProductDetailsCommand.Refresh(); StateChanged();
    }

    private async Task RegisterAsync()
    {
        if (!CanRegister || isBusy) return;
        SetBusy(true);
        ShowFeedback("");
        try
        {
            var details = Lines.Select(line => new OrderDetailUpdate(null, line.ProductId, line.Quantity, line.UnitPrice)).ToList();
            var employeeId = AppSession.Employee!.EmId;
            var customerId = selectedClient!.ClId;
            var officeId = selectedOffice!.SoId;
            var date = EffectiveOrderDate!.Value;
            var delivery = EffectiveDeliveryDate!.Value;
            var note = Remarks;
            // The existing service owns its context and transaction; keep the UI responsive while it commits.
            var result = await Task.Run(() =>
            {
                var succeeded = source.Register(employeeId, customerId, officeId, date, delivery, note, details, out var orderId, out var message);
                return (succeeded, orderId, message);
            });
            if (!result.succeeded) { ShowFeedback(result.message); return; }
            ClearUndo();
            registeredOrderId = result.orderId;
            closeApproved = true;
            Notify(nameof(RegisteredOrderId)); Notify(nameof(RegisteredOrderNumber));
            ShowFeedback("受注を登録しました。", true);
            RegistrationCompleted?.Invoke($"受注を登録しました。\n受注番号：{RegisteredOrderNumber}");
        }
        catch
        {
            if (closeApproved)
            {
                completionError = "受注は登録済みです。受注一覧から登録結果を確認してください。";
                Notify(nameof(CompletionError));
            }
            else ShowFeedback("受注を登録できませんでした。接続を確認して、もう一度お試しください。");
        }
        finally { SetBusy(false); }
    }

    private async Task ShowRegisteredOrderAsync()
    {
        if (isBusy || !isSuccess || registeredOrderId <= 0) return;
        completionError = ""; Notify(nameof(CompletionError));
        SetBusy(true);
        OrderDetailSnapshot? snapshot = null;
        try
        {
            snapshot = await Task.Run(() => source.LoadRegisteredOrder(registeredOrderId));
            if (snapshot == null) completionError = "受注詳細を取得できませんでした。受注一覧から確認してください。";
        }
        catch { completionError = "受注詳細を取得できませんでした。接続を確認して、もう一度お試しください。"; }
        finally { SetBusy(false); Notify(nameof(CompletionError)); }
        if (snapshot != null)
        {
            HideCompletionDialog();
            try
            {
                showDetails(snapshot);
                ResetForNextOrder();
            }
            catch
            {
                completionError = "受注詳細を表示できませんでした。受注一覧から確認してください。";
                isCompletionDialogVisible = true;
                Notify(nameof(CompletionError));
                Notify(nameof(IsCompletionDialogVisible));
                DismissCompletionCommand.Refresh();
            }
        }
    }

    private void StartNextOrder()
    {
        if (isBusy || !isSuccess) return;
        HideCompletionDialog();
        startNextOrder();
    }

    private static OrderDetailViewModel CreatePreview(OrderDetailSnapshot snapshot) =>
        new(snapshot, () => { }, () => { }, () => { }, () => { }, () => { });
    private void RefreshPreview()
    {
        // Compose the existing read-only detail model; its tax and rounding rule remains authoritative.
        preview = CreatePreview(new OrderDetailSnapshot
        {
            CustomerId = selectedClient?.ClId ?? 0, CustomerName = selectedClient?.ClName ?? "",
            CustomerPhone = selectedClient?.ClPhone ?? "", CustomerAddress = selectedClient?.ClAddress ?? "",
            OfficeId = selectedOffice?.SoId ?? 0, OfficeName = selectedOffice?.SoName ?? "",
            EmployeeName = EmployeeName, OrderDate = EffectiveOrderDate ?? initialOrderDate, DeliveryDate = EffectiveDeliveryDate,
            Lines = Lines.Select(line => new OrderDetailLine
            {
                ProductId = line.ProductId, ProductCode = line.ProductCode, ProductName = line.ProductName,
                Quantity = line.Quantity, UnitPrice = line.UnitPrice, Subtotal = line.Amount
            }).ToList()
        });
        for (var index = 0; index < Lines.Count; index++) Lines[index].UpdateDisplay(preview.Items[index]);
        foreach (var name in new[] { nameof(Subtotal), nameof(Tax), nameof(Total), nameof(RoundingNotice), nameof(IsCartEmpty), nameof(HasItems),
            nameof(QuantitySummary), nameof(PreviewCustomerCode), nameof(PreviewCustomerName), nameof(PreviewPhone), nameof(PreviewAddress), nameof(PreviewOffice) }) Notify(name);
    }
    private void StateChanged()
    {
        foreach (var name in new[] { nameof(CanRegister), nameof(CanAddProduct), nameof(ValidationSummary), nameof(QuantityError), nameof(MaximumQuantity), nameof(HasSelectedProduct), nameof(StockStatus),
            nameof(QuantityLabel), nameof(DateInputError), nameof(UnitPriceDisplay), nameof(CustomerInputInvalid), nameof(OfficeInputInvalid), nameof(ProductInputInvalid), nameof(HasUnsavedChanges) }) Notify(name);
        AddLineCommand.Refresh(); RemoveLineCommand.Refresh(); RegisterCommand.Refresh();
        ShowProductDetailsCommand.Refresh(); CloseProductDetailsCommand.Refresh(); UndoRemoveLineCommand.Refresh();
    }
    private void ShowFeedback(string message, bool success = false)
    {
        feedback = message; isSuccess = success; isCompletionDialogVisible = success;
        if (!success) { completionError = ""; Notify(nameof(CompletionError)); }
        Notify(nameof(Feedback)); Notify(nameof(IsSuccess)); Notify(nameof(IsEditing)); Notify(nameof(IsCompletionDialogVisible));
        EscapeCommand.Refresh(); DismissCompletionCommand.Refresh();
    }
    private void HandleEscape()
    {
        if (isProductDetailsVisible) CloseProductDetails();
        else if (isCompletionDialogVisible) DismissCompletionDialog();
        else RequestClose();
    }
    private void DismissCompletionDialog()
    {
        if (isBusy || !isCompletionDialogVisible) return;
        ResetForNextOrder();
    }
    private void HideCompletionDialog()
    {
        if (!isCompletionDialogVisible) return;
        isCompletionDialogVisible = false;
        Notify(nameof(IsCompletionDialogVisible));
        DismissCompletionCommand.Refresh();
    }
    private void ResetForNextOrder()
    {
        // Clear only the input draft. The completed order has already been committed by the service.
        SetBusy(true);
        try
        {
            HideCompletionDialog();
            ClearUndo();
            isSuccess = false; closeApproved = false; isLoaded = false;
            registeredOrderId = 0; feedback = ""; completionError = "";
            isProductDetailsVisible = false; productDetails = null;
            selectedOffice = null; selectedClient = null; selectedProduct = null;
            availableOffices = []; availableClients = []; availableProducts = [];
            offices = []; clients = []; products = [];
            officeSearch = ""; customerSearch = ""; productSearch = "";
            quantity = "1"; remarks = ""; initialOfficeId = null;
            initialOrderDate = DateTime.Today; initialDeliveryDate = initialOrderDate.AddDays(7);
            orderDate = initialOrderDate; deliveryDate = initialDeliveryDate;
            orderDateText = initialOrderDate.ToString("d"); deliveryDateText = initialDeliveryDate.ToString("d");
            Lines.Clear();
            foreach (var name in new[] { nameof(IsSuccess), nameof(IsEditing), nameof(RegisteredOrderId), nameof(RegisteredOrderNumber),
                nameof(Feedback), nameof(CompletionError), nameof(IsProductDetailsVisible), nameof(ProductDetails),
                nameof(SelectedOffice), nameof(SelectedClient), nameof(SelectedProduct), nameof(OfficeSearch), nameof(CustomerSearch),
                nameof(ProductSearch), nameof(Offices), nameof(Clients), nameof(Products), nameof(Quantity), nameof(Remarks),
                nameof(OrderDate), nameof(DeliveryDate), nameof(PreviewOrderDate), nameof(PreviewDeliveryDate), nameof(PreviewRemarks) }) Notify(name);
            Load();
            hasDetailEdits = false;
            RefreshPreview(); StateChanged();
        }
        finally { SetBusy(false); }
    }
    private void SetBusy(bool busy)
    {
        isBusy = busy; Notify(nameof(IsBusy)); Notify(nameof(RegisterButtonText)); Notify(nameof(ValidationSummary));
        foreach (var command in new[] { CloseCommand, AddLineCommand, RemoveLineCommand, RegisterCommand, UndoRemoveLineCommand, ShowProductDetailsCommand, CloseProductDetailsCommand,
            SelectOfficeCommand, SelectClientCommand, SelectProductCommand, ShowRegisteredOrderCommand, ReturnToListCommand, StartNextOrderCommand,
            EscapeCommand, DismissCompletionCommand }) command.Refresh();
    }
    public bool TryApproveClose()
    {
        if (isBusy) return false;
        if (closeApproved) return true;
        if (HasUnsavedChanges && !confirmDiscard()) return false;
        closeApproved = true;
        return true;
    }
    private void RequestClose()
    {
        if (!TryApproveClose()) return;
        HideCompletionDialog();
        close();
    }
    public void Dispose()
    {
        HideCompletionDialog();
        undoTimer.Stop();
        foreach (var line in observedLines) line.PropertyChanged -= LineChanged;
        observedLines.Clear(); source.Dispose();
    }
}
