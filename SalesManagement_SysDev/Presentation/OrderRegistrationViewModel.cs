using System.Collections.ObjectModel;
using System.Globalization;
using System.ComponentModel;
using System.Windows.Threading;

namespace SalesManagement_SysDev;

public enum OrderStockTone { Normal, Warning, Danger }

public readonly record struct OrderStockStatus(string Text, OrderStockTone Tone, string? Details = null)
{
    public string Tooltip => Details ?? Text;
    public bool IsMissingRecord { get; init; }

    internal static OrderStockStatus From(InventoryStockState state, int? quantity, long? requested) => state switch
    {
        InventoryStockState.Available when quantity is 0 => new("在庫がありません", OrderStockTone.Danger),
        InventoryStockState.Available when quantity is > 0 && requested > quantity =>
            new($"在庫不足：{requested - quantity:N0}点", OrderStockTone.Danger,
                $"現在庫：{quantity:N0}点 / 受注数量：{requested:N0}点"),
        InventoryStockState.Available when quantity is > 0 => new($"在庫：{quantity:N0}点", OrderStockTone.Normal),
        _ => new(InventoryService.DescribeUnavailableStock(state), OrderStockTone.Warning)
        {
            IsMissingRecord = state == InventoryStockState.MissingRecord
        }
    };
}

public sealed class RegistrationProduct : ViewModelBase
{
    private int? stock;
    private InventoryStockState stockState;

    public int PrId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string ModelNumber { get; init; } = "";
    public string Barcode { get; init; } = "";
    public int MakerId { get; init; }
    public string MakerName { get; init; } = "";
    public string MakerDisplayName => string.IsNullOrWhiteSpace(MakerName) ? "未設定" : MakerName;
    public decimal UnitPrice { get; init; }
    // The existing product schema has no configurable unit column. Order quantities use the
    // same item-count unit already shown by the stock UI.
    public string UnitName => "個";
    public int? Stock { get => stock; init => stock = value; }
    public InventoryStockState StockState { get => stockState; init => stockState = value; }
    public int? SafetyStock { get; init; }
    public string CategoryName { get; init; } = "";
    public bool IsActive { get; init; } = true;
    public string Display => $"[{ProductCode}] {ProductName}（単価: ¥{UnitPrice:N0}）";
    public string StockLabel => StockState switch
    {
        InventoryStockState.Available when Stock is int count => $"在庫：{count:N0}点",
        InventoryStockState.MissingRecord => "在庫：未登録",
        _ => "在庫：要確認"
    };
    public string MakerAndStockLabel => $"メーカー：{MakerDisplayName} | {StockLabel}";
    private int? AvailableStock => StockState == InventoryStockState.Available && Stock is >= 0 ? Stock : null;
    public string DetailStockText => AvailableStock is int count ? $"{count:N0} 個" : "取得できません";
    public InventoryCondition DetailStockCondition => InventoryService.ClassifyStock(AvailableStock, SafetyStock);

    internal void ApplyStockLookup(InventoryStockLookup lookup)
    {
        if (stock == lookup.Quantity && stockState == lookup.State) return;
        stock = lookup.Quantity;
        stockState = lookup.State;
        Notify(nameof(Stock));
        Notify(nameof(StockState));
        Notify(nameof(StockLabel));
        Notify(nameof(MakerAndStockLabel));
        Notify(nameof(DetailStockText));
        Notify(nameof(DetailStockCondition));
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
    private static readonly ProductMasterOption AllMakers = new(0, "すべてのメーカー");
    private readonly IOrderRegistrationDataSource source;
    private readonly Action close;
    private readonly Action startNextOrder;
    private readonly Func<OrderDraftNavigationChoice> confirmNavigation;
    private readonly IOrderDraftStore draftStore;
    private readonly Action<OrderDetailSnapshot> showDetails;
    private IReadOnlyList<MSalesOffice> availableOffices = [];
    private IReadOnlyList<MClient> availableClients = [];
    private IReadOnlyList<RegistrationProduct> availableProducts = [];
    private IReadOnlyList<MSalesOffice> offices = [];
    private IReadOnlyList<MClient> clients = [];
    private IReadOnlyList<RegistrationProduct> products = [];
    private IReadOnlyList<ProductMasterOption> makerOptions = [AllMakers];
    private int selectedMakerId;
    private MSalesOffice? selectedOffice;
    private MClient? selectedClient;
    private RegistrationProduct? selectedProduct;
    private string officeSearch = "", customerSearch = "", productSearch = "";
    private string quantity = "1", remarks = "", feedback = "";
    private readonly HashSet<RegistrationLine> observedLines = [];
    private readonly DispatcherTimer undoTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private RegistrationLine? deletedLine;
    private int deletedIndex;
    private bool isProductDetailsVisible;
    private RegistrationProduct? productDetails;
    private DateTime initialOrderDate = DateTime.Today;
    private DateTime? orderDate = DateTime.Today, deliveryDate = DateTime.Today.AddDays(7);
    private string orderDateText = DateTime.Today.ToString("d"), deliveryDateText = DateTime.Today.AddDays(7).ToString("d");
    private bool isBusy, isSuccess, isCompletionDialogVisible, settingSearch, isLoaded, approvingClose, disposed;
    private bool draftSaveAttempted, isDraftSaveFeedback;
    private OrderDraftSnapshot? draftToRestore;
    private OrderDraftToken draftToken = new(Guid.NewGuid());
    private OrderDraftForm? savedForm;
    private int registeredOrderId;
    private string completionError = "", busyMessage = "";
    private bool alreadyRegistered;
    private OrderDetailViewModel preview = CreatePreview(new OrderDetailSnapshot());

    public OrderRegistrationViewModel(Action close, Func<OrderDraftNavigationChoice>? confirmNavigation = null,
        IOrderRegistrationDataSource? source = null, Action<OrderDetailSnapshot>? showDetails = null, Action? startNextOrder = null,
        IOrderDraftStore? draftStore = null, OrderDraftSnapshot? draft = null)
    {
        this.close = close;
        this.startNextOrder = startNextOrder ?? close;
        this.confirmNavigation = confirmNavigation ?? (() => OrderDraftNavigationChoice.ContinueEditing);
        this.draftStore = draftStore ?? new OrderDraftService();
        draftToRestore = draft;
        this.source = source ?? new OrderRegistrationDataSource();
        this.showDetails = showDetails ?? (_ => { });
        CloseCommand = new RelayCommand(async () => await RequestCloseAsync(), () => !isBusy);
        EscapeCommand = new RelayCommand(async () => await HandleEscapeAsync(), () => !isBusy);
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
        SaveDraftCommand = new RelayCommand(async () => await SaveDraftAsync(), () => CanSaveDraft);
        ShowRegisteredOrderCommand = new RelayCommand(async () => await ShowRegisteredOrderAsync(), () => !isBusy && isSuccess && registeredOrderId > 0);
        ReturnToListCommand = new RelayCommand(async () => await RequestCloseAsync(), () => !isBusy && isSuccess);
        StartNextOrderCommand = new RelayCommand(StartNextOrder, () => !isBusy && isSuccess);
        SelectOfficeCommand = new RelayCommand(row => { if (row is MSalesOffice office) SelectedOffice = office; }, _ => !isBusy && !isSuccess);
        SelectClientCommand = new RelayCommand(row => { if (row is MClient client) SelectedClient = client; }, _ => !isBusy && !isSuccess);
        SelectProductCommand = new RelayCommand(row => { if (row is RegistrationProduct product) SelectedProduct = product; }, _ => !isBusy && !isSuccess);
        Lines.CollectionChanged += (_, _) => LinesChanged();
        savedForm = CaptureForm();
    }

    public ObservableCollection<RegistrationLine> Lines { get; } = [];
    public IReadOnlyList<MSalesOffice> Offices => offices;
    public IReadOnlyList<MClient> Clients => clients;
    public IReadOnlyList<RegistrationProduct> Products => products;
    public IReadOnlyList<ProductMasterOption> MakerOptions => makerOptions;
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
    public string CurrentStockDisplay => !IsProductValid ? "—" : selectedProduct!.StockState switch
    {
        InventoryStockState.Available when selectedProduct.Stock is int count => $"{count:N0} 個",
        InventoryStockState.MissingRecord => "未登録",
        _ => "取得不可"
    };
    public bool HasSufficientStock => IsProductValid && QuantityError.Length == 0 && StockStatus.Tone == OrderStockTone.Normal;
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
    public RelayCommand SaveDraftCommand { get; }
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
    public bool IsDraft => draftToken.Version != null && !isSuccess;
    public bool IsDraftSaveFeedback => isDraftSaveFeedback;
    public int RegisteredOrderId => registeredOrderId;
    public string RegisteredOrderNumber => registeredOrderId > 0 ? $"ORD-{registeredOrderId:000000}" : "";
    public string CompletionMessage => alreadyRegistered ? "この下書きは登録済みです。" : "受注を登録しました。";
    public string RegisterButtonText => "受注を登録";
    public string CompletionError => completionError;
    public string Feedback => feedback;
    public string EmployeeName => AppSession.Employee?.EmName ?? "未設定";
    public bool IsCartEmpty => Lines.Count == 0;
    public bool HasItems => Lines.Count > 0;
    public string QuantitySummary => $"{Lines.Count} 品目 / {Lines.Sum(line => (long)line.Quantity):N0} 点";
    public decimal Subtotal => preview.Subtotal;
    public decimal Tax => preview.Tax;
    public string TaxLabel => $"消費税（{OrderTax.Rate * 100:0.##}%）";
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
    public int SelectedMakerId
    {
        get => selectedMakerId;
        set
        {
            if (!Set(ref selectedMakerId, value)) return;
            if (selectedProduct != null && !MatchesMaker(selectedProduct)) SelectedProduct = null;
            else FilterProducts();
        }
    }
    public RegistrationProduct? SelectedProduct
    {
        get => selectedProduct;
        set
        {
            var next = availableProducts.FirstOrDefault(product => product.PrId == value?.PrId && product.IsActive && MatchesMaker(product));
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
            FilterProducts(); StateChanged();
        }
    }
    public string Quantity { get => quantity; set { if (Set(ref quantity, value)) StateChanged(); } }

    internal void RefreshSelectedProductStock(InventoryStockLookup lookup)
    {
        if (selectedProduct == null) return;
        selectedProduct.ApplyStockLookup(lookup);
        Notify(nameof(StockStatus));
        Notify(nameof(CurrentStockDisplay));
        Notify(nameof(HasSufficientStock));
    }

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
    internal string OrderDateInputText => orderDateText;
    internal string DeliveryDateInputText => deliveryDateText;
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
    public bool CanRegister => !isSuccess && !isBusy && !isProductDetailsVisible && isLoaded && AppSession.CanManageOrders && IsClientValid
        && EffectiveOrderDate.HasValue && EffectiveDeliveryDate.HasValue && Remarks.Trim().Length <= OrderUpdateRules.RemarksMaxLength && Lines.Count > 0
        && (ProductSearch.Length == 0 || IsProductValid) && TryQuantity(out _) && QuantityError.Length == 0
        && Lines.All(line => line.QuantityError.Length == 0 && FindProduct(line.ProductId)?.UnitPrice == line.UnitPrice && line.UnitPrice >= 0);
    private RegistrationProduct? FindProduct(int id) => availableProducts.FirstOrDefault(product => product.PrId == id);
    private bool CanSaveDraft => !disposed && !isBusy && !isSuccess && (isLoaded || draftToRestore == null)
        && AppSession.CanManageOrders && HasUnsavedChanges;
    public string QuantityError => !TryQuantity(out var count) ? "数量は1以上の整数で入力してください。"
        : IsProductValid && !OrderUpdateRules.TrySubtotal(count, selectedProduct!.UnitPrice, out _)
            ? "行合計は9,999,999,999円以内で入力してください。" : "";
    public bool CustomerInputInvalid => CustomerSearch.Length > 0 && !IsClientValid;
    public bool OfficeInputInvalid => OfficeSearch.Length > 0 && !IsOfficeValid;
    public bool ProductInputInvalid => ProductSearch.Length > 0 && !IsProductValid;
    public string DateInputError => EffectiveOrderDate == null || EffectiveDeliveryDate == null ? "受注日・納品希望日を正しい日付で指定してください。" : "";
    public bool HasUnsavedChanges => !isSuccess && savedForm is { } saved
        && (CaptureHeader() != saved.Header || Lines.Count != saved.Lines.Count
            || Lines.Where((line, index) => CaptureLine(line) != saved.Lines[index]).Any());
    public string ValidationSummary => isBusy ? busyMessage
        : CanRegister ? "入力内容を確認して登録してください。"
        : !IsOfficeValid ? "受付営業所を候補から選択してください。"
        : !IsClientValid ? "受注元顧客を候補から選択してください。"
        : DateInputError.Length > 0 ? DateInputError
        : Remarks.Trim().Length > OrderUpdateRules.RemarksMaxLength ? "備考は50文字以内で入力してください。"
        : ProductInputInvalid ? "商品を候補から選択するか、商品欄をクリアしてください。"
        : !TryQuantity(out _) ? "数量は1以上の整数で入力してください。"
        : QuantityError.Length > 0 ? QuantityError
        : Lines.Count == 0 ? "商品を1件以上追加してください。"
        : Lines.FirstOrDefault(line => line.QuantityError.Length > 0) is { } invalid ? $"{invalid.ProductCode}：{invalid.QuantityError}"
        : Lines.FirstOrDefault(line => FindProduct(line.ProductId) == null) is { } unavailable ? $"{unavailable.ProductName}は現在利用できません。"
        : Lines.FirstOrDefault(line => FindProduct(line.ProductId)?.UnitPrice != line.UnitPrice) is { } repriced
            ? $"{repriced.ProductName}の単価が変更されました。商品を追加し直してください。"
        : "明細の数量・単価を確認してください。";

    public void Load(bool includeOptionStock = true)
    {
        if (!AppSession.CanManageOrders) return;
        try
        {
            availableOffices = source.LoadOffices().Where(office => office.SoFlag == 0
                && (AppSession.IsAdmin || office.SoId == AppSession.Employee?.SoId)).ToArray();
            availableProducts = source.LoadProducts().Where(product => product.IsActive).ToArray();
            makerOptions = [AllMakers, .. availableProducts.Where(product => product.MakerId > 0)
                .DistinctBy(product => product.MakerId)
                .OrderBy(product => product.MakerDisplayName, StringComparer.CurrentCulture)
                .ThenBy(product => product.MakerId)
                .Select(product => new ProductMasterOption(product.MakerId, product.MakerDisplayName))];
            Notify(nameof(MakerOptions));
            if (includeOptionStock) LoadOptionStock();
            if (draftToRestore is { } draft) RestoreDraft(draft);
            else SelectedOffice = availableOffices.FirstOrDefault(office => office.SoId == AppSession.Employee?.SoId) ?? availableOffices.FirstOrDefault();
            draftToRestore = null;
            savedForm = CaptureForm();
            FilterProducts(); isLoaded = true; RefreshPreview(); StateChanged();
        }
        catch { ShowFeedback("入力候補を取得できませんでした。接続を確認し、画面を開き直してください。"); }
    }

    private void LoadOptionStock()
    {
        IReadOnlyDictionary<int, InventoryStockLookup> stocks;
        try { stocks = source.LoadStocks(availableProducts.Select(product => product.PrId).ToArray()); }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("受注登録の商品候補の在庫照会に失敗しました: {0}", exception);
            stocks = new Dictionary<int, InventoryStockLookup>();
        }
        foreach (var product in availableProducts)
            product.ApplyStockLookup(stocks.GetValueOrDefault(product.PrId, new InventoryStockLookup(InventoryStockState.QueryFailed)));
    }

    private OrderDraftHeader CaptureHeader() => new(
        new OrderDraftSelection(selectedOffice?.SoId, OfficeSearch), new OrderDraftSelection(selectedClient?.ClId, CustomerSearch),
        CaptureDate(EffectiveOrderDate, orderDateText), CaptureDate(EffectiveDeliveryDate, deliveryDateText), Remarks,
        new OrderDraftSelection(selectedProduct?.PrId, ProductSearch), selectedProduct?.UnitPrice, Quantity);

    private static OrderDraftDate CaptureDate(DateTime? value, string text) => new(value, value.HasValue ? "" : text);

    private static OrderDraftLine CaptureLine(RegistrationLine line) =>
        new(line.ProductId, line.ProductCode, line.ProductName, line.UnitPrice, line.Quantity, line.QuantityText);

    private OrderDraftForm CaptureForm() => new(CaptureHeader(), Lines.Select(CaptureLine).ToArray());

    private void RestoreDraft(OrderDraftSnapshot draft)
    {
        var header = draft.Form.Header;
        SelectedOffice = availableOffices.FirstOrDefault(office => office.SoId == header.Office.Id);
        if (SelectedOffice == null) OfficeSearch = header.Office.Text;
        SelectedClient = availableClients.FirstOrDefault(client => client.ClId == header.Customer.Id);
        if (SelectedClient == null) CustomerSearch = header.Customer.Text;
        OrderDate = header.OrderDate.Value;
        DeliveryDate = header.DeliveryDate.Value;
        SetDateInputText(false, OrderDate?.ToString("d") ?? header.OrderDate.UnparsedText);
        SetDateInputText(true, DeliveryDate?.ToString("d") ?? header.DeliveryDate.UnparsedText);
        Remarks = header.Remarks;
        SelectedProduct = availableProducts.FirstOrDefault(product => product.PrId == header.PendingProduct.Id);
        if (SelectedProduct == null) ProductSearch = header.PendingProduct.Text;
        Quantity = header.PendingQuantity;
        foreach (var line in draft.Form.Lines)
        {
            var product = FindProduct(line.ProductId);
            Lines.Add(new RegistrationLine
            {
                ProductId = line.ProductId, ProductCode = line.ProductCode, ProductName = line.ProductName,
                UnitPrice = line.UnitPrice, Quantity = line.Quantity, QuantityText = line.QuantityText, Stock = product?.Stock,
                StockState = product?.StockState ?? InventoryStockState.NotRequested
            });
        }
        draftToken = draft.Token;
        Notify(nameof(IsDraft));
        if (SelectedProduct is { } pending && pending.UnitPrice != header.PendingUnitPrice)
            ShowFeedback("選択中の商品の単価が変更されました。追加前に単価を確認してください。");
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
        products = availableProducts.Where(product => MatchesMaker(product)
            && (query.Length == 0 || Matches(product.ProductName, query) || Matches(product.ProductCode, query)
                || Matches(product.Barcode, query) || Matches(product.ModelNumber, query) || Matches(product.MakerName, query)))
            .ToArray();
        Notify(nameof(Products));
    }
    private bool MatchesMaker(RegistrationProduct product) => selectedMakerId == AllMakers.Id || product.MakerId == selectedMakerId;
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
        SelectedProduct = null; Quantity = "1";
        ShowFeedback(""); StateChanged();
    }

    private void LinesChanged()
    {
        foreach (var line in observedLines.Where(line => !Lines.Contains(line)).ToArray())
        { line.PropertyChanged -= LineChanged; observedLines.Remove(line); }
        foreach (var line in Lines.Where(line => !observedLines.Contains(line)))
        { line.PropertyChanged += LineChanged; observedLines.Add(line); }
        RefreshPreview(); StateChanged();
    }
    private void LineChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(RegistrationLine.QuantityText)) return;
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

    public async Task<bool> SaveDraftAsync()
    {
        if (!CanSaveDraft) return false;
        var form = CaptureForm();
        var token = draftToken;
        SetBusy(true, "下書きを保存しています…");
        ShowFeedback("");
        draftSaveAttempted = true;
        try
        {
            draftToken = await Task.Run(() => draftStore.Save(token, form));
            savedForm = form;
            ShowFeedback("下書きを保存しました。", draftSaved: true);
            return true;
        }
        catch (OrderDraftException exception) { ShowFeedback(exception.Message); }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("受注の下書き保存に失敗しました: {0}", exception);
            ShowFeedback("下書きを保存できませんでした。接続を確認して、もう一度お試しください。");
        }
        finally { SetBusy(false); }
        return false;
    }

    private async Task RegisterAsync()
    {
        if (!CanRegister || isBusy) return;
        var draftForm = IsDraft || draftSaveAttempted ? CaptureForm() : null;
        var token = draftToken;
        SetBusy(true, "受注を登録しています…");
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
                if (draftForm != null)
                {
                    var registration = draftStore.Register(token, draftForm);
                    return (succeeded: true, orderId: registration.OrderId, message: "", alreadyRegistered: registration.AlreadyRegistered);
                }
                var succeeded = source.Register(employeeId, customerId, officeId, date, delivery, note, details, out var orderId, out var message);
                return (succeeded, orderId, message, alreadyRegistered: false);
            });
            if (!result.succeeded) { ShowFeedback(result.message); return; }
            ClearUndo();
            registeredOrderId = result.orderId;
            alreadyRegistered = result.alreadyRegistered;
            Notify(nameof(RegisteredOrderId)); Notify(nameof(RegisteredOrderNumber)); Notify(nameof(CompletionMessage));
            ShowFeedback(CompletionMessage, true);
            RegistrationCompleted?.Invoke($"{CompletionMessage}\n受注番号：{RegisteredOrderNumber}");
        }
        catch (OrderDraftException exception) { ShowFeedback(exception.Message); }
        catch
        {
            if (registeredOrderId > 0)
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
                if (!disposed) ResetForNextOrder();
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
        if (isDraftSaveFeedback && HasUnsavedChanges) ShowFeedback("");
        foreach (var name in new[] { nameof(CanRegister), nameof(CanAddProduct), nameof(ValidationSummary), nameof(QuantityError), nameof(MaximumQuantity), nameof(HasSelectedProduct), nameof(StockStatus),
            nameof(CurrentStockDisplay), nameof(HasSufficientStock), nameof(QuantityLabel), nameof(DateInputError), nameof(UnitPriceDisplay),
            nameof(CustomerInputInvalid), nameof(OfficeInputInvalid), nameof(ProductInputInvalid), nameof(HasUnsavedChanges) }) Notify(name);
        AddLineCommand.Refresh(); RemoveLineCommand.Refresh(); RegisterCommand.Refresh(); SaveDraftCommand.Refresh();
        ShowProductDetailsCommand.Refresh(); CloseProductDetailsCommand.Refresh(); UndoRemoveLineCommand.Refresh();
    }
    private void ShowFeedback(string message, bool success = false, bool draftSaved = false)
    {
        var successChanged = isSuccess != success;
        var completionChanged = isCompletionDialogVisible != success;
        feedback = message; isSuccess = success; isCompletionDialogVisible = success;
        isDraftSaveFeedback = draftSaved;
        if (!success) { completionError = ""; Notify(nameof(CompletionError)); }
        Notify(nameof(Feedback)); Notify(nameof(IsDraft)); Notify(nameof(IsDraftSaveFeedback));
        if (successChanged) { Notify(nameof(IsSuccess)); Notify(nameof(IsEditing)); }
        if (completionChanged) Notify(nameof(IsCompletionDialogVisible));
        EscapeCommand.Refresh(); DismissCompletionCommand.Refresh();
    }
    private async Task HandleEscapeAsync()
    {
        if (isProductDetailsVisible) CloseProductDetails();
        else if (isCompletionDialogVisible) DismissCompletionDialog();
        else await RequestCloseAsync();
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
            isSuccess = false; isLoaded = false; draftSaveAttempted = false; isDraftSaveFeedback = false;
            draftToken = new OrderDraftToken(Guid.NewGuid());
            registeredOrderId = 0; alreadyRegistered = false; feedback = ""; completionError = "";
            isProductDetailsVisible = false; productDetails = null;
            selectedOffice = null; selectedClient = null; selectedProduct = null;
            availableOffices = []; availableClients = []; availableProducts = [];
            offices = []; clients = []; products = [];
            makerOptions = [AllMakers]; selectedMakerId = AllMakers.Id;
            officeSearch = ""; customerSearch = ""; productSearch = "";
            quantity = "1"; remarks = "";
            initialOrderDate = DateTime.Today;
            orderDate = initialOrderDate; deliveryDate = initialOrderDate.AddDays(7);
            orderDateText = orderDate.Value.ToString("d"); deliveryDateText = deliveryDate.Value.ToString("d");
            Lines.Clear();
            savedForm = CaptureForm();
            foreach (var name in new[] { nameof(IsSuccess), nameof(IsEditing), nameof(IsDraft), nameof(IsDraftSaveFeedback), nameof(RegisteredOrderId), nameof(RegisteredOrderNumber),
                nameof(Feedback), nameof(CompletionError), nameof(CompletionMessage), nameof(IsProductDetailsVisible), nameof(ProductDetails),
                nameof(SelectedOffice), nameof(SelectedClient), nameof(SelectedProduct), nameof(OfficeSearch), nameof(CustomerSearch),
                nameof(ProductSearch), nameof(Offices), nameof(Clients), nameof(Products), nameof(MakerOptions), nameof(SelectedMakerId), nameof(Quantity), nameof(Remarks),
                nameof(OrderDate), nameof(DeliveryDate), nameof(PreviewOrderDate), nameof(PreviewDeliveryDate), nameof(PreviewRemarks) }) Notify(name);
            Load();
            RefreshPreview(); StateChanged();
        }
        finally { SetBusy(false); }
    }
    private void SetBusy(bool busy, string message = "")
    {
        isBusy = busy; busyMessage = message;
        Notify(nameof(IsBusy)); Notify(nameof(CanRegister)); Notify(nameof(RegisterButtonText)); Notify(nameof(ValidationSummary));
        Notify(nameof(HasUnsavedChanges));
        foreach (var command in new[] { CloseCommand, AddLineCommand, RemoveLineCommand, RegisterCommand, SaveDraftCommand, UndoRemoveLineCommand, ShowProductDetailsCommand, CloseProductDetailsCommand,
            SelectOfficeCommand, SelectClientCommand, SelectProductCommand, ShowRegisteredOrderCommand, ReturnToListCommand, StartNextOrderCommand,
            EscapeCommand, DismissCompletionCommand }) command.Refresh();
    }
    public async Task<bool> TryApproveCloseAsync()
    {
        if (isBusy || approvingClose || disposed) return false;
        if (!HasUnsavedChanges) return true;
        approvingClose = true;
        try
        {
            return confirmNavigation() switch
            {
                OrderDraftNavigationChoice.SaveAndLeave => await SaveDraftAsync(),
                OrderDraftNavigationChoice.DiscardAndLeave => true,
                _ => false
            };
        }
        finally { approvingClose = false; }
    }
    private async Task RequestCloseAsync()
    {
        if (!await TryApproveCloseAsync()) return;
        HideCompletionDialog();
        close();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        HideCompletionDialog();
        undoTimer.Stop();
        foreach (var line in observedLines) line.PropertyChanged -= LineChanged;
        observedLines.Clear(); source.Dispose();
    }
}
