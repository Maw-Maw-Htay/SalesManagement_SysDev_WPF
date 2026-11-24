using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SalesManagement_SysDev;


public sealed class OrderUpdateLine : ViewModelBase, IDataErrorInfo
{
    private readonly OrderDetailLine original;
    internal RegistrationLine CartLine { get; }
    public OrderUpdateLine(OrderDetailLine line) : this(line, new RegistrationLine
    {
        SourceDetailId=line.DetailId > 0 ? line.DetailId : null, ProductId=line.ProductId, ProductCode=line.ProductCode,
        ProductName=line.ProductName, UnitPrice=line.UnitPrice, Quantity=line.Quantity
    }) { }
    internal OrderUpdateLine(OrderDetailLine line, RegistrationLine item)
    {
        original=line; CartLine=item; Preview=line;
        ConfirmQuantityCommand=new RelayCommand(Changed,()=>QuantityError.Length==0);
        CartLine.PropertyChanged+=ItemChanged;
        Changed();
    }
    private void ItemChanged(object? sender, PropertyChangedEventArgs args)
    {
        if(args.PropertyName==nameof(RegistrationLine.QuantityText)) { Notify(nameof(Quantity)); Changed(); }
        if(args.PropertyName==nameof(RegistrationLine.Number)) Notify(nameof(Number));
        if(args.PropertyName==nameof(RegistrationLine.StockStatus)) Notify(nameof(StockStatus));
    }
    internal void Detach() => CartLine.PropertyChanged-=ItemChanged;
    public int Number => CartLine.Number;
    public bool IsNew => DetailId<=0;
    public OrderStockStatus StockStatus => CartLine.StockStatus;
    public string RemoveHint => IsNew ? "追加した商品を削除" : "保存済み明細を受注から削除（変更を保存で反映）";
    public int DetailId => original.DetailId;
    public int ProductId => original.ProductId;
    public string ProductCode => original.ProductCode;
    public string ProductName => original.ProductName;
    public string Quantity
    {
        get=>CartLine.QuantityText;
        set
        {
            // Keep the same integer entry rules; the shared cart stores a canonical valid quantity.
            CartLine.QuantityText=IsNumber(value) && int.TryParse(value,NumberStyles.Integer|NumberStyles.AllowThousands,
                CultureInfo.CurrentCulture,out var count) && count>0 ? count.ToString(CultureInfo.InvariantCulture) : value;
        }
    }
    public decimal UnitPrice => original.UnitPrice;
    public int MaximumQuantity => RegistrationQuantity.Maximum(UnitPrice);
    public RelayCommand ConfirmQuantityCommand { get; }
    public bool IsQuantityChanged => !TryQuantity(out var count) || count != original.Quantity;
    public bool HasPendingQuantity => !TryQuantity(out var count) || count != Preview.Quantity;
    public bool HasChanges => IsNew || IsQuantityChanged || Preview.Quantity != original.Quantity;
    public string QuantityConfirmationHint => "";
    public string QuantityError => !TryQuantity(out var count) ? "数量は1以上の整数で入力してください。"
        : UnitPrice < 0 ? "登録済みの単価を確認してください。"
        : !OrderUpdateRules.TrySubtotal(count, UnitPrice, out _) ? "行合計は9,999,999,999円以内で入力してください。" : "";
    public string Error => QuantityError;
    public string this[string name] => name == nameof(Quantity) ? QuantityError : "";
    public OrderDetailLine Preview { get; private set; }
    public string SubtotalText => Error.Length == 0 ? $"¥{Preview.Subtotal:N0}" : "—";

    private static bool IsNumber(string text)
    {
        if (text.Length > 64) return false;
        var format = CultureInfo.CurrentCulture.NumberFormat;
        var group = Regex.Escape(format.NumberGroupSeparator);
        var number = group.Length == 0 ? "[0-9]+" : $"(?:[0-9]+|[0-9]{{1,3}}(?:{group}[0-9]{{3}})+)";
        return Regex.IsMatch(text.Trim(), $"^[+\\-]?{number}$");
    }
    private bool TryQuantity(out int count)
    {
        count = 0;
        return IsNumber(Quantity) && int.TryParse(Quantity.Trim(), NumberStyles.Integer | NumberStyles.AllowThousands,
            CultureInfo.CurrentCulture, out count) && count > 0;
    }
    public bool TryRead(out int count, out decimal price, out decimal subtotal)
    {
        var validCount = TryQuantity(out count);
        price = UnitPrice;
        subtotal = 0;
        if (!validCount || !OrderUpdateRules.TrySubtotal(count, price, out subtotal)) return false;
        // Legacy stored line totals may not divide evenly into a whole-yen unit price.
        if (!IsNew && count == original.Quantity) subtotal = original.Subtotal;
        return true;
    }
    private void Changed()
    {
        if (TryRead(out var count, out var price, out var subtotal))
            Preview = new OrderDetailLine { DetailId = DetailId, ProductId = ProductId, ProductCode = ProductCode,
                ProductName = ProductName, Quantity = count, UnitPrice = price, Subtotal = subtotal };
        foreach (var name in new[] { nameof(IsQuantityChanged), nameof(HasChanges), nameof(HasPendingQuantity), nameof(QuantityConfirmationHint),
            nameof(QuantityError), nameof(Error), nameof(SubtotalText), nameof(Preview) }) Notify(name);
        ConfirmQuantityCommand.Refresh();
    }
}

public sealed class OrderUpdateViewModel : ViewModelBase, IDisposable
{
    private readonly OrderDetailSnapshot data;
    private readonly Action<bool> close;
    private readonly IOrderUpdateDataSource source;
    private readonly IOrderRegistrationDataSource lookups;
    private readonly Func<MClient,bool> confirmCustomerChange;
    private readonly Dictionary<RegistrationLine,OrderUpdateLine> rows=[];
    private string remarks, saveError="";
    private bool isBusy, hasSaved, loaded, customerSelectionVisible, leaveApproved;
    private bool refreshingInventory, disposed;
    private int productSelectionVersion;
    private DateTime? orderDate, deliveryDate;
    private bool orderDateInvalid, deliveryDateInvalid;
    private MClient customer;
    private OrderDetailViewModel preview;

    public OrderUpdateViewModel(OrderDetailSnapshot data, Action<bool> close, IOrderUpdateDataSource? source=null,
        IOrderRegistrationDataSource? lookups=null, Func<MClient,bool>? confirmCustomerChange=null)
    {
        this.data=data; this.close=close; this.source=source??new OrderUpdateDataSource();
        this.lookups=lookups??new OrderRegistrationDataSource();
        this.confirmCustomerChange=confirmCustomerChange??(_=>false);
        remarks=data.CustomerContact??""; orderDate=data.OrderDate; deliveryDate=data.DeliveryDate;
        customer=new MClient {ClId=data.CustomerId, ClName=data.CustomerName, SoId=data.OfficeId,
            ClAddress=data.CustomerAddress, ClPhone=data.CustomerPhone};
        Detail=CreatePreview(data); preview=Detail;
        Entry=new OrderRegistrationViewModel(()=>{},source:this.lookups);
        foreach(var line in data.Lines)
        {
            var row=new OrderUpdateLine(line); rows.Add(row.CartLine,row); Lines.Add(row); Entry.Lines.Add(row.CartLine);
            row.PropertyChanged+=LineChanged;
        }
        CloseCommand=new RelayCommand(()=>close(false),()=>!isBusy);
        EscapeCommand=new RelayCommand(()=>
        {
            if(Entry.IsProductDetailsVisible) Entry.CloseProductDetailsCommand.Execute(null);
            else CloseCommand.Execute(null);
        },()=>!isBusy);
        SaveCommand=new RelayCommand(async()=>await SaveAsync(),()=>CanSave);
        AddLineCommand=new RelayCommand(()=>Entry.AddLineCommand.Execute(null),()=>CanEdit && Lines.All(line=>line.Error.Length==0) && Entry.CanAddProduct);
        RemoveLineCommand=new RelayCommand(row=>
        {
            if(row is OrderUpdateLine line) Entry.RemoveLineCommand.Execute(line.CartLine);
        },row=>CanEdit && row is OrderUpdateLine line && Lines.Contains(line));
        UndoRemoveLineCommand=new RelayCommand(()=>Entry.UndoRemoveLineCommand.Execute(null),()=>CanEdit && Entry.HasUndo);
        ChangeCustomerCommand=new RelayCommand(()=>{customerSelectionVisible=!customerSelectionVisible;Notify(nameof(IsCustomerSelectionVisible));},()=>CanEdit);
        SelectCustomerCommand=new RelayCommand(row=>SelectCustomer(row as MClient),_=>CanEdit);
        Entry.Lines.CollectionChanged+=(_,_)=>SynchronizeLines();
        Entry.PropertyChanged+=EntryChanged;
    }
    public OrderDetailViewModel Detail {get;}
    public OrderRegistrationViewModel Entry {get;}
    public OrderStockStatus EntryStockStatus => Entry.StockStatus;
    public ObservableCollection<OrderUpdateLine> Lines {get;}=[];
    public RelayCommand CloseCommand {get;}
    public RelayCommand EscapeCommand {get;}
    public RelayCommand SaveCommand {get;}
    public RelayCommand AddLineCommand {get;}
    public RelayCommand RemoveLineCommand {get;}
    public RelayCommand UndoRemoveLineCommand {get;}
    public RelayCommand ChangeCustomerCommand {get;}
    public RelayCommand SelectCustomerCommand {get;}
    public bool IsCustomerSelectionVisible=>customerSelectionVisible;
    public string CustomerDisplay=>$"C-{customer.ClId:0000}　{customer.ClName}";
    public string CustomerName => customer.ClName;
    public string SessionHeader => $"役職：{AppSession.Role}　営業所：{AppSession.Employee?.So?.SoName}";
    public DateTime? OrderDate {get=>orderDate;set {if(Set(ref orderDate,value)) {orderDateInvalid=!value.HasValue;StateChanged();}}}
    public DateTime? DeliveryDate {get=>deliveryDate;set {if(Set(ref deliveryDate,value)) {deliveryDateInvalid=false;StateChanged();}}}
    public string DateInputError=>orderDateInvalid || deliveryDateInvalid || !OrderDate.HasValue ? "受注日・納品希望日を正しい日付で指定してください。" : "";
    public void SetDateInputText(bool delivery,string text)
    {
        var invalid=!DateTime.TryParse(text,CultureInfo.CurrentCulture,DateTimeStyles.None,out _);
        if(delivery) deliveryDateInvalid=text.Length>0 && invalid; else orderDateInvalid=invalid;
        StateChanged();
    }
    public bool IsBusy=>isBusy;
    public bool HasSaved=>hasSaved;
    public bool CanEdit=>!isBusy && !hasSaved && Detail.CanEdit && !Entry.IsProductDetailsVisible;
    public bool HasUndo=>Entry.HasUndo;
    public bool HasPendingQuantity=>Lines.Any(line=>line.HasPendingQuantity);
    private bool HasOrderChanges=>Remarks.Trim()!=(data.CustomerContact??"").Trim() || customer.ClId!=data.CustomerId
        || OrderDate?.Date!=data.OrderDate.Date || DeliveryDate?.Date!=data.DeliveryDate?.Date
        || Lines.Count!=data.Lines.Count || Lines.Any(line=>line.HasChanges);
    public bool HasUnsavedChanges=>!hasSaved && (HasOrderChanges || DateInputError.Length>0 || Entry.ProductSearch.Length>0 || Entry.Quantity!="1");
    public string Remarks {get=>remarks;set {if(Set(ref remarks,value)) StateChanged();}}
    public int RemarksMaxLength=>OrderUpdateRules.RemarksMaxLength;
    public string RemarksError=>Remarks.Trim().Length>RemarksMaxLength ? $"備考は{RemarksMaxLength}文字以内で入力してください。" : "";
    public string ValidationMessage=>!Detail.CanEdit ? "未確定・表示中で、更新権限のある受注だけを編集できます。"
        : DateInputError.Length>0 ? DateInputError : Lines.Count==0 ? "受注明細がありません。"
        : Lines.FirstOrDefault(line=>line.Error.Length>0) is {} invalid ? $"{invalid.ProductCode}：{invalid.Error}"
        : Entry.ProductSearch.Length>0 ? "入力中の商品を追加するか、商品欄をクリアしてください。" : RemarksError;
    public bool CanSave=>CanEdit && HasOrderChanges && !HasPendingQuantity && ValidationMessage.Length==0;
    public bool IsError=>!isBusy && (ValidationMessage.Length>0 || saveError.Length>0);
    public string Feedback=>isBusy ? "保存中…" : hasSaved ? "受注を更新しました。"
        : ValidationMessage.Length>0 ? ValidationMessage : saveError.Length>0 ? saveError
        : HasUnsavedChanges ? "未保存の変更があります。" : "変更はありません。";
    public decimal Subtotal=>preview.Subtotal;
    public decimal Tax=>preview.Tax;
    public string TaxLabel => Entry.TaxLabel;
    public decimal Total=>preview.Total;
    public string RoundingNotice=>preview.RoundingNotice;
    public string QuantitySummary=>$"{Lines.Count} 品目 / {Lines.Sum(line=>(long)line.Preview.Quantity):N0} 点";
    public bool TryApproveClose(Func<bool> confirmDiscard)=>!isBusy && !Entry.IsProductDetailsVisible
        && (leaveApproved || !HasUnsavedChanges || (leaveApproved=confirmDiscard()));
    public void Load()
    {
        if(loaded) return; loaded=true;
        Entry.Load(includeOptionStock: false);
        Entry.SelectedOffice=Entry.Offices.FirstOrDefault(office=>office.SoId==data.OfficeId);
        Entry.SelectedClient=Entry.Clients.FirstOrDefault(client=>client.ClId==data.CustomerId);
        var productIds = GetInventoryProductIds();
        if (productIds.Length != 0) ApplyInventory(ReadInventory(productIds));
        RefreshState();
    }

    public async Task RefreshInventoryAsync()
    {
        if (!loaded || disposed || isBusy || hasSaved || refreshingInventory) return;
        var productIds = GetInventoryProductIds();
        if (productIds.Length == 0) return;
        var selectionVersion = productSelectionVersion;
        refreshingInventory = true;
        try
        {
            var stock = await Task.Run(() => ReadInventory(productIds));
            // Product selection performs its own current lookup; discard an older refresh after a selection change.
            if (!disposed && !isBusy && !hasSaved && selectionVersion == productSelectionVersion) ApplyInventory(stock);
        }
        finally { refreshingInventory = false; }
    }

    private int[] GetInventoryProductIds() => Entry.Lines.Select(line => line.ProductId)
        .Concat(Entry.SelectedProduct is { } product ? [product.PrId] : Array.Empty<int>()).Distinct().ToArray();

    private IReadOnlyDictionary<int, InventoryStockLookup> ReadInventory(int[] productIds)
    {
        try { return lookups.LoadStocks(productIds); }
        catch (Exception exception)
        {
            Trace.TraceError("受注更新の在庫照会に失敗しました: {0}", exception);
            return productIds.ToDictionary(id => id, _ => new InventoryStockLookup(InventoryStockState.QueryFailed));
        }
    }

    private void ApplyInventory(IReadOnlyDictionary<int, InventoryStockLookup> stock)
    {
        foreach (var line in Entry.Lines)
            if (stock.TryGetValue(line.ProductId, out var lookup)) line.ApplyStockLookup(lookup);
        if (Entry.SelectedProduct is { } product && stock.TryGetValue(product.PrId, out var selected))
            Entry.RefreshSelectedProductStock(selected);
    }

    private void SelectCustomer(MClient? candidate)
    {
        if(candidate==null || candidate.SoId!=data.OfficeId || candidate.ClFlag!=0 || !Entry.Clients.Contains(candidate)) return;
        if(candidate.ClId!=customer.ClId && !confirmCustomerChange(candidate)) return;
        customer=candidate; Entry.SelectedClient=candidate; customerSelectionVisible=false;
        Notify(nameof(CustomerName));Notify(nameof(CustomerDisplay));Notify(nameof(IsCustomerSelectionVisible));StateChanged();
    }
    private void EntryChanged(object? sender,PropertyChangedEventArgs args)
    {
        leaveApproved=false;
        if(args.PropertyName==nameof(OrderRegistrationViewModel.SelectedProduct))
        {
            productSelectionVersion++;
            if(Entry.SelectedProduct!=null) Entry.Quantity="1";
        }
        if(args.PropertyName is nameof(OrderRegistrationViewModel.SelectedProduct) or nameof(OrderRegistrationViewModel.StockStatus))
            Notify(nameof(EntryStockStatus));
        Notify(nameof(HasUndo));RefreshState();
    }
    private void SynchronizeLines()
    {
        foreach(var item in rows.Keys.Where(item=>!Entry.Lines.Contains(item)).ToArray())
        {
            var row=rows[item];row.PropertyChanged-=LineChanged;row.Detach();rows.Remove(item);Lines.Remove(row);
        }
        foreach(var item in Entry.Lines.Where(item=>!rows.ContainsKey(item)))
        {
            var original=data.Lines.FirstOrDefault(line=>line.DetailId==item.SourceDetailId)
                ??new OrderDetailLine {ProductId=item.ProductId,ProductCode=item.ProductCode,ProductName=item.ProductName,
                    Quantity=item.Quantity,UnitPrice=item.UnitPrice,Subtotal=decimal.Round(item.Quantity*item.UnitPrice,0,MidpointRounding.AwayFromZero)};
            var row=new OrderUpdateLine(original,item);rows.Add(item,row);row.PropertyChanged+=LineChanged;
            Lines.Insert(Math.Min(Entry.Lines.IndexOf(item),Lines.Count),row);
        }
        RefreshPreview();StateChanged();
    }
    private void LineChanged(object? sender,PropertyChangedEventArgs args)
    {if(args.PropertyName==nameof(OrderUpdateLine.Preview)) {RefreshPreview();StateChanged();}}
    public async Task SaveAsync()
    {
        if(!CanSave) return;
        var updates=new List<OrderDetailUpdate>();
        foreach(var line in Lines)
        {
            if(!line.TryRead(out var count,out var price,out _)) return;
            updates.Add(new OrderDetailUpdate(line.DetailId>0 ? line.DetailId : null,line.ProductId,count,price));
        }
        var edited=new OrderDetailSnapshot {OrderId=data.OrderId,CustomerId=customer.ClId,OfficeId=data.OfficeId,
            EmployeeId=data.EmployeeId,OrderDate=OrderDate!.Value,DeliveryDate=DeliveryDate,
            StateFlag=data.StateFlag,ManagementFlag=data.ManagementFlag,CustomerContact=Remarks,
            Lines=Lines.Select(line=>line.Preview).ToList()};
        isBusy=true;saveError="";RefreshState();
        try
        {
            var result=await source.SaveAsync(data,edited,Remarks,updates);
            if(!result.Succeeded) {saveError=result.Message;return;}
            hasSaved=true;
        }
        catch {saveError="受注を更新できませんでした。接続を確認して、もう一度お試しください。入力内容は保持されています。";}
        finally {isBusy=false;RefreshState();}
        if(hasSaved) close(true);
    }
    public OrderDetailSnapshot? LoadSavedOrder()=>hasSaved ? lookups.LoadRegisteredOrder(data.OrderId) : null;
    private static OrderDetailViewModel CreatePreview(OrderDetailSnapshot snapshot)=>new(snapshot,()=>{},()=>{},()=>{},()=>{},()=>{});
    private void RefreshPreview()
    {
        preview=CreatePreview(new OrderDetailSnapshot {Lines=Lines.Select(line=>line.Preview).ToList()});
        foreach(var name in new[] {nameof(Subtotal),nameof(Tax),nameof(Total),nameof(QuantitySummary),nameof(RoundingNotice)}) Notify(name);
    }
    private void StateChanged() {leaveApproved=false;saveError="";RefreshState();}
    private void RefreshState()
    {
        foreach(var name in new[] {nameof(IsBusy),nameof(HasSaved),nameof(CanEdit),nameof(HasUnsavedChanges),nameof(HasPendingQuantity),
            nameof(CanSave),nameof(RemarksError),nameof(DateInputError),nameof(ValidationMessage),nameof(IsError),nameof(Feedback)}) Notify(name);
        foreach(var command in new[] {SaveCommand,CloseCommand,EscapeCommand,AddLineCommand,RemoveLineCommand,UndoRemoveLineCommand,ChangeCustomerCommand,SelectCustomerCommand}) command.Refresh();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Entry.PropertyChanged-=EntryChanged;
        foreach(var row in rows.Values) {row.PropertyChanged-=LineChanged;row.Detach();}
        Entry.Dispose();
    }
}
