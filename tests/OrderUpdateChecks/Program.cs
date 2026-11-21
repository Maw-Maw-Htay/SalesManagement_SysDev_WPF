using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SalesManagement_SysDev;

internal static partial class Program
{
    private static int checks;
    private static readonly List<string> bindingErrors = [];
    private static readonly Assembly assembly = typeof(F_OrderUpdate).Assembly;
    private static string output = "";
    private static bool interactiveOrderMenu;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
            if (args.Contains("--draft-selection-only"))
            {
                SetRole("管理者");
                CheckDraftSelectionState();
                Console.WriteLine($"PASS: {checks} selection checks.");
                return 0;
            }
            interactiveOrderMenu = args.Contains("--interactive-order-menu");
            output = Path.GetFullPath(args.FirstOrDefault(arg => !arg.StartsWith("--")) ?? "build_verify/order-update-preview");
            Directory.CreateDirectory(output);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var dictionary in new[] { "Colors", "Typography", "Controls", "IconResources", "Layouts" })
            {
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"/SalesManagement_SysDev;component/Resources/{dictionary}.xaml", UriKind.Relative)
                });
            }
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingListener());
            SetRole("管理者");
            if(args.Contains("--draft-only")) CheckDraftNavigation();
            else if(args.Contains("--preview-only")) RenderLayout(1150,720,"update-ux-preview",true,count:2);
            else if(args.Contains("--layout-only")) CheckLayouts();
            else if(args.Contains("--database-only")) { }
            else { CheckViewModel(); CheckQuantityInputs(); CheckDraftNavigation(); CheckUpdateWorkflow(); CheckInventoryStatus(); CheckCompletionRoutes(); CheckConcurrencySnapshot(); CheckLayouts(); }
            if (!args.Contains("--skip-database"))
            {
                if (!args.Contains("--draft-only")) CheckDatabase();
                CheckDraftDatabase();
            }
            else Console.WriteLine("SKIPPED: database integration tests (--skip-database).");
            Check(bindingErrors.Count == 0, "XAML binding errors: " + string.Join(" | ", bindingErrors));
            Console.WriteLine($"PASS: {checks} checks. Layout renders: {output}");
            app.Shutdown();
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Check(bool success, string name)
    {
        if (!success) throw new InvalidOperationException("FAIL: " + name);
        checks++; Console.WriteLine("PASS " + name);
    }
    private static void SetRole(string role, int office = 1) => AppSession.Employee = new MEmployee
    {
        EmId = 1, EmName = "高谷春男", SoId = office, Po = new MPosition { PoName = role },
        So = new MSalesOffice { SoId = office, SoName = "北大阪営業所" }
    };
    private static OrderDetailSnapshot Sample(int count = 2, int state = 0, int hidden = 0) => new()
    {
        OrderId = 165, CustomerId = 1, CustomerName = "INATUGI", CustomerAddress = "大阪府茨木市横江2-5-60 本館3階 商品受入窓口",
        CustomerPhone = "072-02-5171", OfficeId = 1, OfficeName = "北大阪営業所", EmployeeId = 1, EmployeeName = "高谷春男",
        OrderDate = new DateTime(2026, 10, 20), DeliveryDate = new DateTime(2026, 10, 29), StateFlag = state, ManagementFlag = hidden,
        Lines = Enumerable.Range(1, count).Select(index => new OrderDetailLine
        {
            DetailId = index, ProductId = index, ProductCode = $"PR-{index:0000}", ProductName = "業務用ワイヤレスキーボード " + index,
            Quantity = 2, UnitPrice = 1234, Subtotal = 2468
        }).ToList()
    };

    private static void CheckViewModel()
    {
        var source = new ControlledSource(); var closed = false;
        var vm = new OrderUpdateViewModel(Sample(), saved => closed = saved, source);
        Check(!vm.CanSave && !vm.HasUnsavedChanges && vm.Feedback == "変更はありません。", "initial state / save disabled");
        var confirmations = 0;
        Check(vm.TryApproveClose(() => { confirmations++; return false; }) && confirmations == 0, "clean close needs no confirmation");
        vm.Lines[0].Quantity = "3";
        Check(vm.CanSave && vm.Subtotal == 6170 && !vm.HasPendingQuantity && vm.HasUnsavedChanges,
            "valid quantity immediately updates the preview and save state without Enter");
        Check(vm.CanSave && vm.Subtotal == 6170 && vm.Tax == 617 && vm.Total == 6787, "quantity / document totals");
        Check(vm.Lines[0].IsQuantityChanged && vm.HasUnsavedChanges, "quantity dirty marker");
        Check(!vm.TryApproveClose(() => { confirmations++; return false; }) && confirmations == 1, "continue editing cancels close");
        Check(vm.TryApproveClose(() => true), "discard accepts close");
        vm.Lines[0].Quantity = "2";
        Check(!vm.HasUnsavedChanges && !vm.CanSave && vm.Subtotal==4936, "reverting quantity immediately restores the clean original state");
        Check(typeof(OrderUpdateLine).GetProperty(nameof(OrderUpdateLine.UnitPrice))!.SetMethod == null
            && vm.Lines[0].UnitPrice == 1234, "unit price is an immutable display value");
        foreach (var value in new[] { "", "abc", "-1", "0", "1.5", "1,2", "2147483648" })
        {
            vm.Lines[0].Quantity = value;
            Check(!vm.CanSave && vm.IsError && vm.Lines[0].QuantityError.Length > 0, "invalid quantity: " + value);
        }
        vm.Lines[0].Quantity = "10000000";
        Check(!vm.CanSave && vm.Lines[0].QuantityError.Contains("行合計"), "quantity change exceeds stored line amount range");
        Check(vm.Subtotal == 4936, "invalid edits retain last valid totals");
        vm.Lines[0].Quantity = "2";
        var free = Sample(1);
        free.Lines[0] = new OrderDetailLine { DetailId=1, ProductId=1, Quantity=2, UnitPrice=0, Subtotal=0 };
        var freeVm = new OrderUpdateViewModel(free, _ => { }); freeVm.Lines[0].Quantity="3";
        Check(freeVm.CanSave && freeVm.Total == 0 && freeVm.Lines[0].UnitPrice == 0, "stored zero unit price remains zero");
        vm.Remarks = new string('あ', 51);
        Check(!vm.CanSave && vm.RemarksError.Length > 0, "remarks max length");
        vm.Remarks = "午前中に納品";
        Check(vm.CanSave && vm.HasUnsavedChanges, "remarks dirty detection");
        source.Pending = new TaskCompletionSource<OrderUpdateResult>();
        var saving = vm.SaveAsync();
        Check(vm.IsBusy && !vm.CanSave && !vm.CloseCommand.CanExecute(null) && !vm.CanEdit && vm.Feedback == "保存中…", "saving state locks edits / close");
        Check(!vm.TryApproveClose(() => true), "system close blocked while saving");
        Wait(vm.SaveAsync()); Check(source.Calls == 1, "duplicate save suppressed");
        source.Pending.SetResult(new(false, "保存できませんでした。再試行してください。")); Wait(saving);
        Check(!closed && vm.CanSave && vm.Remarks == "午前中に納品" && vm.IsError, "failed save preserves input and permits retry");
        source.Pending = null; source.Throw = true; Wait(vm.SaveAsync());
        Check(!closed && vm.CanSave && vm.Feedback.Contains("入力内容は保持"), "exception preserves input");
        source.Throw = false; Wait(vm.SaveAsync());
        Check(closed && vm.HasSaved && !vm.HasUnsavedChanges && !vm.CanSave && vm.Feedback == "受注を更新しました。", "success only after awaited save / clear dirty state");
        var legacy = Sample(1);
        legacy.Lines[0] = new OrderDetailLine { DetailId = 1, ProductId = 1, Quantity = 3, UnitPrice = 33, Subtotal = 100 };
        var legacyVm = new OrderUpdateViewModel(legacy, _ => { }); legacyVm.Remarks = "備考だけ";
        Check(legacyVm.Subtotal == 100 && legacyVm.Lines[0].TryRead(out _, out _, out var subtotal) && subtotal == 100, "remarks edit preserves legacy rounded stored amounts");
        legacyVm.Lines[0].Quantity="4";
        Check(legacyVm.Subtotal==132, "automatic quantity updates use the stored unit price");
        legacyVm.Lines[0].Quantity="3";
        Check(legacyVm.Subtotal==100, "automatic reversion preserves the original legacy line total");
        foreach (var sample in new[] { Sample(state: 1), Sample(hidden: 2) })
        {
            var locked = new OrderUpdateViewModel(sample, _ => { }); locked.Remarks = "変更";
            Check(!locked.CanEdit && !locked.CanSave, "confirmed/hidden read-only");
        }
        SetRole("営業"); Check(new OrderUpdateViewModel(Sample(), _ => { }).CanEdit, "sales can edit quantity and remarks");
        SetRole("物流"); Check(!new OrderUpdateViewModel(Sample(), _ => { }).CanEdit, "role deny by default");
        SetRole("営業", 2); Check(!new OrderUpdateViewModel(Sample(), _ => { }).CanEdit, "office scope in UI");
        SetRole("管理者");
    }

    private static void Wait(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        if (!task.IsCompleted) throw new TimeoutException();
        task.GetAwaiter().GetResult();
    }

    private static T WaitFor<T>(Task<T> task)
    {
        Wait(task);
        return task.Result;
    }

    private static void CheckUpdateWorkflow()
    {
        var saved=Sample(); var source=new ControlledSource(); var lookups=new UpdateLookupSource();
        var allowCustomer=false; var confirmations=0; var completed=0;
        using var vm=new OrderUpdateViewModel(saved,success=>{if(success) completed++;},source,lookups,
            _=>{confirmations++;return allowCustomer;});
        vm.Load();
        Check(vm.OrderDate==saved.OrderDate && vm.DeliveryDate==saved.DeliveryDate && vm.Detail.EmployeeName==saved.EmployeeName
            && vm.CustomerDisplay.Contains("C-0001") && !vm.HasUnsavedChanges,"update loads original customer, dates, employee and clean draft");
        foreach(var query in new[] {"8","0008","C-0008"})
        {vm.Entry.CustomerSearch=query;Check(vm.Entry.Clients.Any(client=>client.ClId==8),"shared customer search resolves "+query);}
        var candidate=vm.Entry.Clients.Single(client=>client.ClId==8);
        vm.SelectCustomerCommand.Execute(candidate);
        Check(confirmations==1 && vm.CustomerDisplay.Contains("C-0001") && source.Calls==0,"cancelled customer confirmation preserves original without writing");
        allowCustomer=true;vm.SelectCustomerCommand.Execute(candidate);
        Check(vm.CustomerDisplay.Contains("C-0008") && vm.HasUnsavedChanges && source.Calls==0,"confirmed customer change stays in draft until save");
        vm.OrderDate=saved.OrderDate.AddDays(1);vm.DeliveryDate=saved.DeliveryDate!.Value.AddDays(2);vm.Remarks="更新する備考";
        vm.Entry.SelectedProduct=vm.Entry.Products.Single(product=>product.PrId==2);vm.Entry.Quantity="2";vm.AddLineCommand.Execute(null);
        Check(vm.Lines.Count==2 && vm.Lines[1].Quantity=="4" && vm.Lines[1].UnitPrice==1234,
            "shared duplicate add merges into the saved row using its saved price");
        vm.Entry.SelectedProduct=vm.Entry.Products.Single(product=>product.PrId==3);vm.Entry.Quantity="4";vm.AddLineCommand.Execute(null);
        var added=vm.Lines.Single(line=>line.ProductId==3);var amount=vm.Subtotal;
        Check(vm.Lines.Count==3 && added.IsNew && added.UnitPrice==2000 && vm.Remarks=="更新する備考" && source.Calls==0,
            "new product uses catalog price and preserves remarks without a database write");
        Check(vm.RemoveLineCommand.CanExecute(vm.Lines[0]),"existing saved detail can be removed during an order update");
        vm.RemoveLineCommand.Execute(added);Check(vm.Lines.Count==2 && vm.HasUndo,"new draft row removal reuses registration Undo");
        vm.UndoRemoveLineCommand.Execute(null);added=vm.Lines.Single(line=>line.ProductId==3);
        Check(vm.Lines.Count==3 && added.Quantity=="4" && vm.Subtotal==amount && added.UnitPrice==2000,"shared Undo restores quantity, original price and totals");
        lookups.StockState=InventoryStockState.Available;
        vm.Entry.SelectedProduct=vm.Entry.Products.Single(product=>product.PrId==3);vm.Entry.SelectedProduct=null;
        added.Quantity="6";
        Check(added.StockStatus.Text=="在庫不足：1点" && added.StockStatus.Tooltip.Contains("現在庫：5点") && vm.CanSave,
            "quantity changes refresh actual-stock shortage wording without blocking update");
        lookups.StockState=InventoryStockState.QueryFailed;
        vm.Entry.SelectedProduct=vm.Entry.Products.Single(product=>product.PrId==3);
        Check(vm.EntryStockStatus.Text=="在庫情報の取得に失敗しました" && added.StockStatus==vm.EntryStockStatus,
            "a failed product lookup stays visible below the matching row's quantity");
        vm.Entry.ShowProductDetailsCommand.Execute(null);
        Check(vm.Entry.IsProductDetailsVisible && !vm.CanSave,"shared product details keeps the pending update and suspends save");
        vm.EscapeCommand.Execute(null);vm.Entry.SelectedProduct=null;
        Check(!vm.Entry.IsProductDetailsVisible && vm.Remarks=="更新する備考" && vm.Lines.Count==3,"Escape closes only product details and retains draft");
        vm.SetDateInputText(false,"invalid date");Check(!vm.CanSave,"invalid typed date blocks update without replacing saved date");
        vm.SetDateInputText(false,vm.OrderDate!.Value.ToString("d"));
        Wait(vm.SaveAsync());
        Check(completed==1 && source.Calls==1 && source.Expected==saved && source.Edited!.CustomerId==8
            && source.Edited.OrderId==saved.OrderId && source.Edited.EmployeeId==saved.EmployeeId && source.Expected.OrderDate==saved.OrderDate
            && source.SubmittedLines!.Single(line=>line.ProductId==3).DetailId==null,
            "save submits original concurrency snapshot separately from edited header and newly added detail");
        Wait(vm.SaveAsync());Check(source.Calls==1 && !vm.CanEdit && !vm.HasUnsavedChanges,"successful save retains read-only contents and prevents a second save");
    }

    private static void CheckInventoryStatus()
    {
        var saved=Sample();
        saved.Lines[0]=new OrderDetailLine {DetailId=1,ProductId=3,Quantity=9,UnitPrice=1234,Subtotal=11106};
        var source=new ControlledSource();
        var lookups=new UpdateLookupSource();
        using var vm=new OrderUpdateViewModel(saved,_=>{},source,lookups);
        vm.Load();
        var row=vm.Lines[0];
        Check(row.StockStatus.Text=="在庫情報が未登録です" && row.StockStatus.Tone==OrderStockTone.Warning && !vm.HasUnsavedChanges,
            "missing stock is amber and does not dirty the draft");
        vm.Remarks="受注更新の在庫表示を検証";
        foreach(var (state,quantity,text,tone) in new[]
        {
            (InventoryStockState.Available,15,"在庫：15点",OrderStockTone.Normal),
            (InventoryStockState.Available,0,"在庫がありません",OrderStockTone.Danger),
            (InventoryStockState.Available,5,"在庫不足：4点",OrderStockTone.Danger),
            (InventoryStockState.Available,-1,"在庫情報を確認できません",OrderStockTone.Warning),
            (InventoryStockState.InactiveRecord,0,"有効な在庫情報がありません",OrderStockTone.Warning),
            (InventoryStockState.DuplicateActiveRecords,0,"在庫情報が重複しています",OrderStockTone.Warning),
            (InventoryStockState.MissingRecord,0,"在庫情報が未登録です",OrderStockTone.Warning)
        })
        {
            lookups.StockState=state;lookups.StockQuantity=quantity;
            Wait(vm.RefreshInventoryAsync());
            Check(row.StockStatus.Text==text && row.StockStatus.Tone==tone && vm.CanSave && row.Quantity=="9" && source.Calls==0,
                "refresh keeps stock and draft separate: "+text);
        }
        lookups.StockState=InventoryStockState.Available;lookups.StockQuantity=5;
        Wait(vm.RefreshInventoryAsync());
        row.Quantity="5";
        Check(row.StockStatus.Text=="在庫：5点" && vm.Subtotal==7*1234 && vm.CanSave,
            "quantity edit immediately clears the shortage and recalculates totals");
        lookups.ThrowOnStock=true;
        Wait(vm.RefreshInventoryAsync());
        Check(row.StockStatus.Text=="在庫情報の取得に失敗しました" && row.StockStatus.Tone==OrderStockTone.Warning
            && vm.CanSave && source.Calls==0, "failed stock refresh is distinct from missing and zero stock");
        Wait(vm.SaveAsync());
        Check(vm.HasSaved && source.Calls==1 && source.Edited!.OrderId==saved.OrderId
            && source.Edited.EmployeeId==saved.EmployeeId && source.SubmittedLines!.Single(line=>line.ProductId==3).Quantity==5,
            "an unconfirmed order saves through the existing workflow with an unverified stock lookup");
        using var registration = new OrderRegistrationViewModel(() => { }, source: lookups);
        registration.Load();
        registration.SelectedOffice = registration.Offices.First();
        registration.SelectedClient = registration.Clients.First();
        registration.SelectedProduct = registration.Products.Single(product => product.PrId == 3);
        Check(registration.StockStatus.Text == "在庫情報の取得に失敗しました" && registration.StockStatus.Tone == OrderStockTone.Warning
            && registration.CanAddProduct, "registration shows stock lookup failures separately and still accepts the line");
    }

    [DllImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);

    private static void CheckCompletionRoutes()
    {
        foreach(var route in new[] {"Close","Escape","List","Details"})
        {
            var source=new ControlledSource();var lookups=new UpdateLookupSource();var lists=0;var details=0;
            using var editor=new F_OrderUpdate(Sample(),()=>lists++,_=>details++,source,lookups);
            var root=editor.DetachView();
            var host=new Window {Content=root,Width=1150,Height=760,Left=-30000,Top=-30000,ShowInTaskbar=false,ShowActivated=false};
            host.Show();host.UpdateLayout();
            var vm=(OrderUpdateViewModel)((FrameworkElement)root).DataContext;
            vm.Remarks="完了経路の検証";
            Exception? failure=null;
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
            {
                var notice=Application.Current.Windows.OfType<F_OrderNotice>().Single();
                try
                {
                    Check(!IsWindowEnabled(new WindowInteropHelper(host).Handle),"completion is actually modal: "+route);
                    var model=(OrderNoticeViewModel)notice.DataContext;
                    Check(model.Title=="受注更新完了" && model.Message.Contains(vm.Detail.OrderNumber),"completion displays the saved order number: "+route);
                    if(route=="Close") model.CloseCommand.Execute(null);
                    else if(route=="Escape") notice.InputBindings.OfType<KeyBinding>().Single().Command.Execute(null);
                    else if(route=="List") model.CancelCommand.Execute(null);
                    else model.ConfirmCommand.Execute(null);
                }
                catch(Exception ex) {failure=ex;notice.Close();}
            }));
            Wait(vm.SaveAsync());FlushBindings();if(failure!=null) throw failure;
            Check(vm.HasSaved && host.IsVisible && host.OwnedWindows.Count==0 && IsWindowEnabled(new WindowInteropHelper(host).Handle)
                && ((ScrollViewer)editor.FindName("ContentScroller")).IsEnabled,
                "completion closes fully and restores host interaction and scrolling: "+route);
            Check(lists==(route=="List" ? 1 : 0) && details==(route=="Details" ? 1 : 0)
                && !vm.SaveCommand.CanExecute(null) && source.Calls==1 && vm.Remarks=="完了経路の検証",
                "completion route preserves saved contents without duplicate persistence: "+route);
            if(route=="Close") Render((FrameworkElement)root,"update-completed-inline");
            host.Close();
        }
    }

    private static void CheckQuantityInputs()
    {
        var saveSource = new ControlledSource();
        var lookups = new UpdateLookupSource { TestedProductId=1, StockState=InventoryStockState.Available };
        using var editor = new F_OrderUpdate(Sample(), updateSource:saveSource, lookupSource:lookups);
        var vm = (OrderUpdateViewModel)editor.DataContext;
        var root = (FrameworkElement)editor.Content; editor.Content = null;
        var host = new Window { Content=root, DataContext=vm, Width=1050, Height=720, Left=-30000, Top=-30000,
            WindowStyle=WindowStyle.None, ShowInTaskbar=false, ShowActivated=false };
        host.Show(); host.UpdateLayout();
        var grid = (DataGrid)editor.FindName("ItemsGrid");
        var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(vm.Lines[0]);
        var number = Descendants<QuantityUpDown>(row).Single(); number.ApplyTemplate();
        var stock = Descendants<TextBlock>(row).Single(text=>text.Text=="在庫：5点");
        Check(stock.TextWrapping==TextWrapping.NoWrap && stock.TransformToAncestor(row).Transform(new Point()).Y
            >= number.TransformToAncestor(row).Transform(new Point()).Y+number.ActualHeight,
            "actual stock is visible on one line directly below the quantity control");
        var quantityText = (TextBox)number.Template.FindName("PART_Input", number);
        var plus=(Button)number.Template.FindName("PART_Increase",number);
        var minus=(Button)number.Template.FindName("PART_Decrease",number);
        Check(number.Text=="2" && plus.IsEnabled && minus.IsEnabled
            && plus.TransformToAncestor(number).Transform(new Point()).Y < minus.TransformToAncestor(number).Transform(new Point()).Y,
            "update quantity uses the same stacked plus/minus control as registration");
        plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FlushBindings();
        Check(vm.Lines[0].Quantity=="3" && vm.Subtotal==6170 && vm.Tax==617 && vm.Total==6787 && vm.CanSave
            && vm.HasUnsavedChanges && !vm.HasPendingQuantity && !vm.TryApproveClose(() => false) && saveSource.Calls==0,
            "plus immediately updates quantity, tax, totals and save eligibility without writing to the database");
        minus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FlushBindings();
        Check(vm.Lines[0].Quantity=="2" && vm.Subtotal==4936 && !vm.CanSave && !vm.HasUnsavedChanges,
            "minus immediately restores the original quantity and amounts");
        quantityText.Text="37"; FlushBindings();
        Check(vm.Lines[0].Quantity=="37" && vm.CanSave && vm.Subtotal==39*1234 && vm.Tax==4813 && vm.Total==52939
            && vm.Lines[0].TotalText=="¥50,224" && vm.Lines[0].UnitPrice==1234 && saveSource.Calls==0,
            "direct numeric input immediately recalculates row and document totals while preserving the saved price");
        Check(!Descendants<TextBlock>(row).Any(text=>text.Text.Contains("Enter")) && !vm.Feedback.Contains("Enter"),
            "automatic quantity entry displays no Enter confirmation hint");
        row.IsSelected=true;FlushBindings();
        Check(stock.Text=="在庫不足：32点" && Equals(stock.Foreground,stock.FindResource("ThemeDangerText"))
            && Equals(row.Background,row.FindResource("ThemePaleBlue")),"the selected row retains red shortage text on a subtle background");
        lookups.StockState=InventoryStockState.MissingRecord;
        Wait(vm.RefreshInventoryAsync());FlushBindings();
        Check(stock.Text=="在庫情報が未登録です" && Equals(stock.Foreground,stock.FindResource("ThemeWarningText")),
            "a fresh missing lookup is amber in the existing quantity cell");
        Check(PressEnter(number) && vm.Subtotal==39*1234 && saveSource.Calls==0,
            "Enter is consumed without adding a confirmation step or saving the order");
        Render(root,"update-quantity-updown");
        foreach(var value in new[] { "0", "-1", "1.5", "abc", "" })
        {
            quantityText.Text=value; FlushBindings();
            Check(!vm.CanSave && vm.Lines[0].QuantityError.Length>0, "invalid automatic update input blocks saving: "+value);
            quantityText.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice,Environment.TickCount,quantityText,host)
                { RoutedEvent=Keyboard.LostKeyboardFocusEvent });
            Check(vm.Lines[0].Quantity==value && vm.Subtotal==39*1234 && saveSource.Calls==0, "invalid update text and the last valid amount survive focus loss: "+value);
        }
        quantityText.Text="1"; FlushBindings();
        Check(!minus.IsEnabled && plus.IsEnabled && vm.CanSave && vm.Subtotal==3*1234,
            "correcting invalid input immediately recovers the totals and save state; decrement stops at one");
        quantityText.SelectAll();
        foreach(var value in new[] { "0", "-1", "1.5", "abc", "2147483648" })
        {
            var paste=new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText,value),false,DataFormats.UnicodeText)
                { RoutedEvent=DataObject.PastingEvent };
            quantityText.RaiseEvent(paste);
            Check(paste.CommandCancelled && number.Text=="1", "update numeric paste rejects invalid or oversized input: "+value);
        }
        quantityText.Text=vm.Lines[0].MaximumQuantity.ToString(CultureInfo.InvariantCulture); FlushBindings();
        Check(!plus.IsEnabled && minus.IsEnabled && vm.CanSave, "automatic update quantity respects the existing line amount maximum");
        quantityText.Text="2"; FlushBindings();
        Check(!vm.HasUnsavedChanges && number.Text=="2", "update numeric control reflects reverted quantity");
        var large=Sample(1).Lines[0];
        large=new OrderDetailLine { DetailId=large.DetailId, ProductId=large.ProductId, Quantity=1234, UnitPrice=1, Subtotal=1234 };
        Check(new OrderUpdateLine(large).Quantity=="1234", "stored quantities above one thousand load as editable numeric text");
        number.IsEnabled=false; FlushBindings();
        Check(!quantityText.IsEnabled && !plus.IsEnabled && !minus.IsEnabled, "disabled update numeric control blocks text and step buttons");
        host.Close(); editor.Close();

        CheckRegistrationInputs();
    }

    private static void CheckRegistrationInputs()
    {
        var source = new QuantityRegistrationSource();
        using var vm = new OrderRegistrationViewModel(() => { }, source: source);
        vm.Load();
        Check(source.StockBatches == 1 && source.StockSelections == 0
            && vm.Products[0].StockLabel == "在庫：5点" && vm.Products[1].StockLabel == "在庫：0点"
            && vm.Products[2].StockLabel == "在庫：未登録", "registration stock options use one batch with available, zero and missing stock");
        Check(vm.QuantityLabel=="数量 *", "unit is hidden until a product is selected");
        Check(!vm.CanRegister && !vm.AddLineCommand.CanExecute(null) && !vm.ShowProductDetailsCommand.CanExecute(null), "empty registration commands disabled");
        var view = new F_OrderRegistration { DataContext=vm };
        var root = (FrameworkElement)view.Content; view.Content=null;
        var host = new Window { Content=root, DataContext=vm, Width=1224, Height=626, Left=-30000, Top=-30000,
            WindowStyle=WindowStyle.None, ShowInTaskbar=false, ShowActivated=false };
        host.Show(); host.UpdateLayout(); FlushBindings();
        var editor=(QuantityUpDown)view.FindName("QuantityInput"); editor.ApplyTemplate();
        var inputScroller=(ScrollViewer)view.FindName("InputScroll");
        var addButton=Descendants<Button>(root).Single(button=>System.Windows.Automation.AutomationProperties.GetName(button)=="商品を追加");
        var addBounds=addButton.TransformToAncestor(inputScroller).TransformBounds(new Rect(new Point(),addButton.RenderSize));
        Check(addBounds.Top>=0 && addBounds.Bottom<=inputScroller.ViewportHeight, "product add action is visible at 1280x720 without scrolling");
        var input=(TextBox)editor.Template.FindName("PART_Input",editor);
        var plus=(Button)editor.Template.FindName("PART_Increase",editor);
        var minus=(Button)editor.Template.FindName("PART_Decrease",editor);
        var details=(Button)view.FindName("ProductDetailsButton");
        Check(!details.IsEnabled && Equals(details.Background, Application.Current.FindResource("ThemeBorder")),
            "product details is disabled and gray before selecting a product");
        var manufacturer = (ComboBox)view.FindName("ManufacturerFilter");
        CheckManufacturerFiltering(vm, source, manufacturer, (RegistrationLookup)view.FindName("ProductLookup"), details);
        vm.SelectedClient=source.LoadClients(1)[0]; vm.Remarks="入力内容を保持";
        var option = (FrameworkElement)((DataTemplate)view.Resources["RegistrationProductSuggestion"]).LoadContent();
        var missingProduct = vm.Products.Single(product => product.PrId == 3);
        option.DataContext = missingProduct;
        FlushBindings();
        var optionText = Descendants<TextBlock>(option).Select(text => text.Text).ToArray();
        Check(optionText.Contains(missingProduct.ProductCode) && optionText.Contains(missingProduct.ProductName)
            && optionText.Contains("メーカー：Dell | 在庫：未登録") && optionText.Contains("¥2,000"),
            "product option renders code, name, manufacturer, stock and price");
        Check(editor.Text=="1" && !minus.IsEnabled && plus.IsEnabled, "quantity default/minimum state");
        CheckHorizontalStepper(editor);
        plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FlushBindings();
        Check(vm.Quantity=="2" && minus.IsEnabled, "plus updates quantity without Enter");
        minus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FlushBindings();
        Check(vm.Quantity=="1" && !minus.IsEnabled, "minus returns to minimum");
        vm.SelectedProduct=source.LoadProducts()[0]; input.Text="37"; FlushBindings();
        Check(details.IsEnabled, "product details is enabled for a valid selection");
        Check(vm.QuantityLabel=="数量 *（個）", "selected product displays the existing item-count unit");
        Check(vm.CanAddProduct && vm.StockStatus.Text == "在庫不足：32点" && vm.StockStatus.Tooltip.Contains("受注数量：37点"), "direct input updates warning without stock blocking");
        var enter=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(input),Environment.TickCount,Key.Enter) { RoutedEvent=Keyboard.PreviewKeyDownEvent };
        input.RaiseEvent(enter); FlushBindings();
        Check(enter.Handled && vm.Lines.Count==0, "Enter never adds a product");
        input.SelectAll();
        foreach(var value in new[] { "0", "-1", "1.5", "abc", "", "2147483648" })
        {
            var paste=new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText,value),false,DataFormats.UnicodeText) { RoutedEvent=DataObject.PastingEvent };
            input.RaiseEvent(paste);
            Check(paste.CommandCancelled && vm.Quantity=="37", "invalid paste rejected: "+value);
        }
        foreach(var value in new[] { "0", "-1", "1.5", "abc", "" })
        {
            input.Text=value; FlushBindings();
            Check(!vm.CanAddProduct && vm.QuantityError.Length>0, "invalid quantity blocks product addition: "+value);
        }
        input.Text="37"; FlushBindings();
        vm.AddLineCommand.Execute(null); FlushBindings();
        Check(!details.IsEnabled, "product details is disabled again after clearing selection");
        Check(vm.Lines.Single().Quantity==37 && vm.Lines.Single().UnitName=="個" && vm.Quantity=="1" && vm.SelectedProduct==null && vm.UnitPriceDisplay=="商品選択後に表示", "addition retains unit and resets only product input");
        Check(vm.SelectedClient!=null && vm.Remarks=="入力内容を保持" && vm.CanRegister, "header and remarks retained; stock shortage can register");
        var addedLine = vm.Lines.Single();
        var totalBeforeFilter = vm.Total;
        vm.SelectedMakerId = vm.MakerOptions.Single(maker => maker.Name == "Samsung").Id;
        Check(ReferenceEquals(addedLine, vm.Lines.Single()) && vm.Total == totalBeforeFilter && vm.CanRegister,
            "manufacturer browsing preserves existing order lines, totals and registration eligibility");
        vm.SelectedMakerId = 0;
        vm.Lines[0].QuantityText="5"; FlushBindings();
        Check(vm.Subtotal==6170 && vm.Tax==617 && vm.Total==6787 && vm.Lines[0].TotalIncludingTax==6787, "detail quantity recalculates row/net/tax/inclusive totals immediately");
        vm.Lines[0].QuantityText=""; FlushBindings();
        Check(!vm.CanRegister && vm.Subtotal==6170, "invalid detail retains last valid preview and blocks saving");
        vm.Lines[0].QuantityText="5";
        var grid=(DataGrid)view.FindName("OrderItemsGrid"); host.UpdateLayout();
        var unitColumn=grid.Columns.SingleOrDefault(column=>Equals(column.Header,"単位")) as DataGridTextColumn;
        Check(unitColumn?.Binding is Binding unitBinding && unitBinding.Path.Path=="UnitName", "order list includes the product unit column");
        var row=(DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(vm.Lines[0]);
        var rowEditor=Descendants<QuantityUpDown>(row).Single(); rowEditor.ApplyTemplate();
        CheckHorizontalStepper(rowEditor);
        ((TextBox)rowEditor.Template.FindName("PART_Input",rowEditor)).Text="7"; FlushBindings();
        Check(vm.Lines[0].Quantity==7 && vm.Subtotal==8638 && vm.Lines[0].StockStatus.Text=="在庫不足：2点", "DataGrid TwoWay binding updates calculations and stock warning");
        vm.SelectedProduct=source.LoadProducts()[0]; vm.Quantity="3"; vm.AddLineCommand.Execute(null);
        Check(vm.Lines.Count==1 && vm.Lines[0].Quantity==10, "duplicate addition merges quantity");
        var original=vm.Lines[0];
        vm.RemoveLineCommand.Execute(original); FlushBindings();
        Check(vm.HasUndo && vm.Lines.Count==0 && vm.Total==0 && !vm.CanRegister && vm.HasUnsavedChanges, "delete updates totals and unsaved tracking");
        vm.UndoRemoveLineCommand.Execute(null); FlushBindings();
        Check(vm.Lines.Single().Quantity==10 && vm.Lines[0].UnitPrice==1234 && !vm.HasUndo, "Undo restores quantity and original price");
        vm.RemoveLineCommand.Execute(vm.Lines[0]);
        vm.SelectedProduct=source.LoadProducts()[0]; vm.Quantity="2"; vm.AddLineCommand.Execute(null);
        vm.UndoRemoveLineCommand.Execute(null);
        Check(vm.Lines.Count==1 && vm.Lines[0].Quantity==12, "Undo after re-add merges without duplicate rows");
        vm.SelectedProduct=source.LoadProducts()[1];
        Check(vm.StockStatus.Text=="在庫がありません" && vm.CanAddProduct, "zero stock is explicit and nonblocking");
        vm.SelectedProduct=source.LoadProducts()[2];
        Check(vm.StockStatus.Text=="在庫情報が未登録です" && vm.CanAddProduct, "missing stock is explicit and nonblocking");
        FlushBindings();
        Check(((TextBlock)view.FindName("ProductInputStockHint")).Visibility == Visibility.Collapsed,
            "missing stock warning does not appear beneath the product input stepper");
        var before=(vm.SelectedClient,vm.Remarks,vm.Quantity,vm.Lines.Count);
        vm.ShowProductDetailsCommand.Execute(null); FlushBindings();
        Check(vm.IsProductDetailsVisible && vm.ProductDetails?.PrId==3 && !vm.CanRegister, "read-only product details opens for selected product");
        Check(Descendants<TextBlock>((FrameworkElement)view.FindName("ProductDetailsOverlay")).Any(text => text.Text == "Dell"),
            "product details displays the selected manufacturer's name");
        Render(root,"registration-product-details");
        vm.EscapeCommand.Execute(null); FlushBindings();
        Check(!vm.IsProductDetailsVisible && before==(vm.SelectedClient,vm.Remarks,vm.Quantity,vm.Lines.Count), "closing product details preserves entry data");
        vm.SelectedProduct=source.LoadProducts()[0]; vm.Quantity=vm.MaximumQuantity.ToString(CultureInfo.InvariantCulture); FlushBindings();
        Check(!plus.IsEnabled && minus.IsEnabled, "plus disables at existing amount-derived maximum");
        vm.Quantity="1"; vm.SelectedProduct=null;
        Check(!WaitFor(vm.TryApproveCloseAsync()), "unsaved changes confirmation preserves input when cancelled");
        Check(!Descendants<TextBox>(root).Any(box => System.Windows.Automation.AutomationProperties.GetName(box) is "受注受付担当者" or "単価（参照専用）"), "employee and unit price are borderless text displays");
        foreach(var product in source.LoadProducts().Skip(1)) { vm.SelectedProduct=product; vm.AddLineCommand.Execute(null); }
        foreach(var (width,height) in new[] { (1280,720),(1366,768),(1600,900),(1920,1080) })
        {
            host.Width=width-56; host.Height=height-94; host.UpdateLayout(); FlushBindings();
            CheckManufacturerLayout(view);
            var footer=(FrameworkElement)view.FindName("RegistrationFooter");
            var totals=(FrameworkElement)view.FindName("OrderTotalsCard");
            var footerTop=footer.TransformToAncestor(root).Transform(new Point()).Y;
            var totalTop=totals.TransformToAncestor(root).Transform(new Point()).Y;
            Check(footerTop+footer.ActualHeight<=root.ActualHeight+1 && totalTop+totals.ActualHeight<=footerTop+1, "fixed footer and totals fit "+width+"x"+height);
            Check(Descendants<ScrollViewer>(root).All(scroll=>scroll.ScrollableWidth<1), "registration has no horizontal scroll "+width+"x"+height);
            grid.ScrollIntoView(vm.Lines[^1]); host.UpdateLayout(); FlushBindings();
            Check(grid.ItemContainerGenerator.ContainerFromItem(vm.Lines[^1])!=null && Math.Abs(totals.TransformToAncestor(root).Transform(new Point()).Y-totalTop)<1, "only detail list scrolls above totals "+width+"x"+height);
            grid.ScrollIntoView(vm.Lines[0]); host.UpdateLayout(); FlushBindings();
            var first=(DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(vm.Lines[0]);
            Check(Descendants<QuantityUpDown>(first).All(control=>control.ActualHeight<=first.ActualHeight), "quantity controls fit rows "+width+"x"+height);
            if (width == 1920)
            {
                var remarks = (FrameworkElement)view.FindName("RemarksInput");
                Check(remarks.TransformToAncestor(inputScroller).TransformBounds(new Rect(remarks.RenderSize)).Bottom <= inputScroller.ViewportHeight,
                    "remarks are fully visible at 1920x1080 without scrolling");
                CheckRegistrationRowAndFooter(view, root, grid, vm);
            }
            Render(root,"registration-"+width+"x"+height);
        }
        vm.RemoveLineCommand.Execute(vm.Lines[0]);
        Wait(Task.Delay(6500));
        Check(!vm.HasUndo && !vm.UndoRemoveLineCommand.CanExecute(null), "Undo notification expires");
        host.Close(); view.Dispose();
        CheckRegistrationSubmission();
    }

    private static void CheckRegistrationSubmission()
    {
        var source=new RegistrationSubmissionSource();
        var listReturns=0; var nextOrderStarts=0;
        using var vm=new OrderRegistrationViewModel(()=>listReturns++,source:source,startNextOrder:()=>nextOrderStarts++);
        vm.Load(); vm.SelectedClient=source.LoadClients(1)[0]; vm.SelectedProduct=source.LoadProducts()[0]; vm.AddLineCommand.Execute(null);
        vm.Remarks="最初の受注";
        var view=new F_OrderRegistration { DataContext=vm };
        var root=(FrameworkElement)view.Content; view.Content=null;
        var host=new Window { Content=root, DataContext=vm, Width=1224, Height=626, Left=-30000, Top=-30000,
            WindowStyle=WindowStyle.None, ShowInTaskbar=false, ShowActivated=false };
        host.Show(); host.UpdateLayout(); FlushBindings();
        var registerButton=(Button)view.FindName("RegisterButton");
        vm.RegisterCommand.Execute(null);
        for(var i=0;i<100 && source.Calls==0;i++) Wait(Task.Delay(10));
        FlushBindings();
        Check(vm.IsBusy && !vm.RegisterCommand.CanExecute(null) && !WaitFor(vm.TryApproveCloseAsync())
            && registerButton.Visibility==Visibility.Visible && registerButton.Content?.ToString()=="受注を登録" && !registerButton.IsEnabled,
            "registration request keeps the register label while disabling duplicate submit and navigation");
        vm.RegisterCommand.Execute(null); source.AllowCompletion.Set();
        for(var i=0;i<200 && vm.IsBusy;i++) Wait(Task.Delay(10));
        Check(source.Calls==1 && vm.IsSuccess && vm.IsCompletionDialogVisible && vm.RegisteredOrderNumber=="ORD-000237", "single successful submission preserves existing completion and numbering");
        Check(vm.Lines.Count==1 && !vm.RegisterCommand.CanExecute(null), "completion stays on registration screen without duplicate submission");
        host.UpdateLayout(); FlushBindings();
        var dismissButton=(Button)view.FindName("DismissCompletionButton");
        var continueButton=(Button)view.FindName("ContinueOrderRegistrationButton");
        var showDetailsButton=(Button)view.FindName("ShowRegisteredOrderButton");
        var returnToListButton=(Button)view.FindName("CompletionReturnToListButton");
        var dialog=(Border)view.FindName("RegistrationCompletionDialog");
        var overlay=(Grid)view.FindName("RegistrationCompletionOverlay");
        var panels=(Grid)view.FindName("RegistrationPanels");
        var orderNumber=(TextBlock)view.FindName("CompletionOrderNumberText");
        var detailsGrid=(DataGrid)view.FindName("OrderItemsGrid");
        var detailsRow=(DataGridRow)detailsGrid.ItemContainerGenerator.ContainerFromItem(vm.Lines[0]);
        var detailQuantity=Descendants<QuantityUpDown>(detailsRow).Single();
        Check(overlay.Visibility==Visibility.Visible && overlay.IsHitTestVisible && !panels.IsEnabled
            && registerButton.Visibility==Visibility.Visible && registerButton.Content?.ToString()=="受注を登録" && !registerButton.IsEnabled
            && view.FindName("StartNextOrderButton")==null && dismissButton.IsEnabled && orderNumber.Text=="ORD-000237"
            && !detailQuantity.IsEnabled, "completion keeps the background register button and prevents edits or duplicate submission");
        Check(System.Windows.Automation.AutomationProperties.GetName(continueButton)=="続けて受注登録"
            && System.Windows.Automation.AutomationProperties.GetName(showDetailsButton)=="受注明細を表示"
            && System.Windows.Automation.AutomationProperties.GetName(returnToListButton)=="一覧に戻る"
            && continueButton.Command==vm.DismissCompletionCommand && showDetailsButton.Command==vm.ShowRegisteredOrderCommand
            && returnToListButton.Command==vm.ReturnToListCommand, "reference completion dialog exposes continue, saved details and return-to-list actions");
        Check(new[] {continueButton,showDetailsButton,returnToListButton}.All(button=>
        {
            var bounds=button.TransformToAncestor(dialog).TransformBounds(new Rect(new Point(),button.RenderSize));
            return button.IsEnabled && bounds.Left>=0 && bounds.Top>=0 && bounds.Right<=dialog.ActualWidth && bounds.Bottom<=dialog.ActualHeight;
        }), "all completion actions remain visible and usable inside the dialog");
        Render(root,"registration-success");
        vm.ShowRegisteredOrderCommand.Execute(null);
        for(var i=0;i<200 && vm.IsBusy;i++) Wait(Task.Delay(10));
        Check(vm.IsCompletionDialogVisible && vm.IsSuccess && vm.CompletionError.Length>0 && vm.Lines.Count==1,
            "detail lookup failure retains the completed order and a usable completion dialog");
        dismissButton.Command.Execute(null);
        host.UpdateLayout(); FlushBindings();
        var quantityInput=(QuantityUpDown)view.FindName("QuantityInput");
        var customerLookup=(RegistrationLookup)view.FindName("CustomerLookup");
        Check(ReferenceEquals(host.DataContext,vm) && ReferenceEquals(host.Content,root) && !vm.IsSuccess && vm.IsEditing
            && !vm.IsCompletionDialogVisible && overlay.Visibility==Visibility.Collapsed && !overlay.IsHitTestVisible
            && root.IsEnabled && panels.IsEnabled && customerLookup.IsEnabled && quantityInput.IsEnabled,
            "closing completion resets the same registration screen and releases all modal input blocking");
        Check(vm.Lines.Count==0 && vm.Subtotal==0 && vm.Tax==0 && vm.Total==0 && vm.SelectedClient==null && vm.SelectedProduct==null
            && vm.CustomerSearch=="" && vm.ProductSearch=="" && vm.Remarks=="" && vm.Quantity=="1"
            && vm.SelectedOffice?.SoId==AppSession.Employee!.SoId && vm.OrderDate==DateTime.Today && vm.DeliveryDate==DateTime.Today.AddDays(7)
            && vm.RegisteredOrderId==0 && vm.RegisteredOrderNumber=="" && vm.Feedback=="" && vm.CompletionError=="" && !vm.HasUnsavedChanges,
            "completion close clears every draft field, amount, saved identifier and old error, then restores defaults");
        Check(registerButton.Visibility==Visibility.Visible && !registerButton.IsEnabled && source.Calls==1
            && listReturns==0 && nextOrderStarts==0, "reset does not navigate or resubmit and requires a new valid order");
        var customerPoint=customerLookup.TransformToAncestor(root).Transform(new Point(customerLookup.ActualWidth/2,customerLookup.ActualHeight/2));
        var hit=root.InputHitTest(customerPoint) as DependencyObject;
        Check(hit!=null && IsWithin(hit,customerLookup), "customer input receives mouse hit testing after completion close");
        Render(root,"registration-after-completion-close");
        vm.SelectedClient=source.LoadClients(1)[0]; vm.SelectedProduct=source.LoadProducts()[0]; vm.Quantity="2"; vm.AddLineCommand.Execute(null);
        Check(vm.CanRegister && vm.Lines.Single().Quantity==2 && vm.Total==4400, "the reset screen accepts the next order with fresh quantities and totals");
        vm.RegisterCommand.Execute(null);
        for(var i=0;i<200 && vm.IsBusy;i++) Wait(Task.Delay(10));
        Check(source.Calls==2 && vm.IsSuccess && vm.RegisteredOrderNumber=="ORD-000238", "a second order registers once after completion reset");
        continueButton.Command.Execute(null); host.UpdateLayout(); FlushBindings();
        Check(!vm.IsSuccess && vm.IsCartEmpty && vm.Quantity=="1" && !vm.IsCompletionDialogVisible
            && source.Calls==2 && listReturns==0 && nextOrderStarts==0 && ReferenceEquals(host.DataContext,vm),
            "continue registration clears the same screen without navigation or resubmission");
        vm.SelectedClient=source.LoadClients(1)[0]; vm.SelectedProduct=source.LoadProducts()[0]; vm.AddLineCommand.Execute(null);
        vm.RegisterCommand.Execute(null);
        for(var i=0;i<200 && vm.IsBusy;i++) Wait(Task.Delay(10));
        vm.EscapeCommand.Execute(null); host.UpdateLayout(); FlushBindings();
        Check(!vm.IsSuccess && vm.IsCartEmpty && !vm.IsCompletionDialogVisible && !overlay.IsHitTestVisible && source.Calls==3,
            "Escape closes completion and resets the next draft without another registration");
        vm.SelectedClient=source.LoadClients(1)[0]; vm.SelectedProduct=source.LoadProducts()[0]; vm.AddLineCommand.Execute(null);
        vm.RegisterCommand.Execute(null);
        for(var i=0;i<200 && vm.IsBusy;i++) Wait(Task.Delay(10));
        ((Button)view.FindName("CompletionReturnToListButton")).Command.Execute(null);
        Check(listReturns==1 && source.Calls==4 && !vm.IsCompletionDialogVisible && vm.IsSuccess,
            "returning to the list hides completion without resetting or recursively navigating");
        host.Close(); view.Dispose();
        var failedSource=new RegistrationSubmissionSource { Fail=true }; failedSource.AllowCompletion.Set();
        using var failed=new OrderRegistrationViewModel(()=>{},source:failedSource);
        failed.Load(); failed.SelectedClient=failedSource.LoadClients(1)[0]; failed.SelectedProduct=failedSource.LoadProducts()[0]; failed.AddLineCommand.Execute(null);
        failed.RegisterCommand.Execute(null);
        for(var i=0;i<200 && failed.IsBusy;i++) Wait(Task.Delay(10));
        Check(!failed.IsSuccess && failed.Feedback.Length>0 && failed.Lines.Count==1 && failed.RegisterCommand.CanExecute(null), "registration failure retains inputs and restores retry state");
    }

    private static bool IsWithin(DependencyObject element, DependencyObject ancestor)
    {
        for(DependencyObject? current=element;current!=null;current=VisualTreeHelper.GetParent(current))
            if(ReferenceEquals(current,ancestor)) return true;
        return false;
    }

    private static bool PressEnter(QuantityUpDown number)
    {
        var input = (TextBox)number.Template.FindName("PART_Input", number);
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(input), Environment.TickCount, Key.Enter)
            { RoutedEvent=Keyboard.PreviewKeyDownEvent };
        input.RaiseEvent(key); FlushBindings();
        return key.Handled;
    }

    private static void FlushBindings() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));

    private static void CheckConcurrencySnapshot()
    {
        var snapshot = Sample(1);
        var matches = typeof(OrderUpdateRules).GetMethod("Matches", BindingFlags.Static | BindingFlags.NonPublic)!;
        TOrder Current() => new()
        {
            OrId=snapshot.OrderId, SoId=snapshot.OfficeId, ClId=snapshot.CustomerId, EmId=snapshot.EmployeeId,
            OrDate=snapshot.OrderDate, OrDeliveryDate=snapshot.DeliveryDate, OrStateFlag=0, OrFlag=0,
            TOrderDetails=[new TOrderDetail { OrDetailId=1, PrId=1, OrQuantity=2, OrTotalPrice=2468 }]
        };
        Check((bool)matches.Invoke(null, [snapshot, Current()])!, "matching concurrency snapshot");
        foreach (var change in new Action<TOrder>[]
        {
            order => order.ClCharge="変更済み", order => order.TOrderDetails.First().OrQuantity=3,
            order => order.TOrderDetails.First().OrTotalPrice=2500, order => order.OrStateFlag=1,
            order => order.OrFlag=2, order => order.ClId=2, order => order.SoId=2, order => order.EmId=2,
            order => order.OrDeliveryDate=null, order => order.TOrderDetails.Clear(),
            order => order.TOrderDetails.First().PrId=2
        })
        {
            var current = Current(); change(current);
            Check(!(bool)matches.Invoke(null, [snapshot, current])!, "changed header/detail concurrency snapshot rejected");
        }
    }

    private static void CheckLayouts()
    {
        var calculate = assembly.GetType("SalesManagement_SysDev.RegistrationWindowPlacement")!.GetMethod("Calculate", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var (width, height) in new[] { (1920,1080), (1600,900), (1366,768), (1280,720) })
        foreach (var scale in new[] { 1d, 1.25d, 1.5d })
        {
            var work = new Rect(-width, 80, width, height - 40 * scale);
            var placement = calculate.Invoke(null, [work, work, scale, scale, 1050d, 720d, true, 900d, 580d])!;
            var type = placement.GetType();
            var bounds = (Rect)type.GetProperty("Pixels")!.GetValue(placement)!;
            var label = $"{width}x{height}-{scale * 100:0}";
            Check(work.Contains(bounds) && bounds.Width <= width * .95 && bounds.Height <= work.Height * .90, "monitor/taskbar bounds " + label);
            Check(Math.Abs(bounds.Left - (work.Left + (work.Width - bounds.Width)/2)) <= 1, "owner centering / negative monitor origin " + label);
            RenderLayout(bounds.Width/scale, bounds.Height/scale, label, width == 1920 && scale == 1 || width == 1280 && scale == 1.5);
        }
        RenderLayout(1050, 720, "validation", true, invalid: true);
    }

    private static void RenderLayout(double width, double height, string name, bool saveImage, bool invalid = false,int count=30)
    {
        var editor = new F_OrderUpdate(Sample(count),updateSource:new ControlledSource(),lookupSource:new UpdateLookupSource());
        var vm = (OrderUpdateViewModel)editor.DataContext;
        if (invalid) vm.Lines[0].Quantity = "-1";
        if(count==2) vm.Load();
        var root = (FrameworkElement)editor.Content; editor.Content = null;
        var host = new Window { Content = root, DataContext = vm, Width = width, Height = height, Left = -30000, Top = -30000,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false };
        host.Show(); host.UpdateLayout();
        host.UpdateLayout(); Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        var header = (FrameworkElement)editor.FindName("Header");
        var footer = (FrameworkElement)editor.FindName("Footer");
        var save = (Button)editor.FindName("SaveButton");
        var grid = (DataGrid)editor.FindName("ItemsGrid");
        var scroller = (ScrollViewer)editor.FindName("ContentScroller");
        Check(Math.Abs(header.ActualHeight-68)<1 && Math.Abs(footer.ActualHeight-64)<1 && save.ActualHeight==40,
            "fixed header/footer/button " + name);
        var buttonPoint = save.TransformToAncestor(root).Transform(new Point());
        Check(buttonPoint.X >= 0 && buttonPoint.X + save.ActualWidth <= root.ActualWidth + 1
            && buttonPoint.Y + save.ActualHeight <= root.ActualHeight + 1, "save button visible " + name);
        var overflow=Descendants<ScrollViewer>(root).Where(scroll=>scroll.ScrollableWidth>=1).ToArray();
        if(overflow.Length>0) Render(root,"overflow-"+name);
        Check(overflow.Length==0,"no horizontal scrolling "+name+" "+string.Join(";",overflow.Select(scroll=>$"{scroll.Name}:{scroll.ScrollableWidth}/{scroll.ViewportWidth}/{scroll.ExtentWidth}")));
        var verticalScrollers = Descendants<ScrollViewer>(root)
            .Where(scroll => scroll.IsVisible && scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible).ToArray();
        Check(verticalScrollers.Length <= 2, "only the independent input and item panes show scrollbars " + name + " "
            + string.Join(", ", verticalScrollers.Select(scroll => $"{scroll.Name} ({scroll.ViewportHeight:F0}/{scroll.ExtentHeight:F0})")));
        var firstRow = grid.ItemContainerGenerator.ContainerFromItem(vm.Lines[0]) as DataGridRow;
        if (firstRow != null)
        {
            Check(Descendants<QuantityUpDown>(firstRow).All(input => input.ActualHeight >= 36
                && input.TransformToAncestor(firstRow).Transform(new Point()).Y + input.ActualHeight <= firstRow.ActualHeight + 1), "numeric inputs fit row " + name);
            Check(Descendants<QuantityUpDown>(firstRow).Count() == 1
                && Descendants<TextBlock>(firstRow).Any(text => System.Windows.Automation.AutomationProperties.GetName(text) == "単価"
                    && text.Text == "¥1,234"), "unit price displayed without an input control " + name);
        }
        if (saveImage) Render(root, name);
        grid.ScrollIntoView(vm.Lines[^1]); host.UpdateLayout();
        Check(grid.ItemContainerGenerator.ContainerFromItem(vm.Lines[^1])!=null,"last order item accessible "+name);
        scroller.ScrollToBottom();host.UpdateLayout();
        var remarks=(FrameworkElement)editor.FindName("RemarksInput");
        var point=remarks.TransformToAncestor(scroller).Transform(new Point());
        Check(point.Y>=-1 && point.Y+remarks.ActualHeight<=scroller.ActualHeight+1,"remarks accessible in independent input pane "+name);
        var totals=(FrameworkElement)editor.FindName("AmountSummary");
        var totalsPosition=totals.TransformToAncestor(root).Transform(new Point());
        var footerPosition=footer.TransformToAncestor(root).Transform(new Point());
        Check(totalsPosition.Y+totals.ActualHeight<=footerPosition.Y+1,"totals remain fixed above footer "+name);
        Check(editor.InputBindings.OfType<KeyBinding>().Any(key => key.Key == Key.Escape), "Escape close route " + name);
        if (invalid) vm.Lines[0].Quantity = "2";
        host.Close(); editor.Close();
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject item) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(item); index++)
        {
            var child = VisualTreeHelper.GetChild(item, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Render(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
    }

    private static string testConnection = "";
    private static SalesManagementContext TestContext()
    {
        var db = new SalesManagementContext(); db.Database.SetConnectionString(testConnection); return db;
    }
    private static void CheckDatabase()
    {
        using var configuration = new SalesManagementContext();
        var catalog = "SalesManagement_OrderUpdateChecks_" + Guid.NewGuid().ToString("N");
        var connection = new SqlConnectionStringBuilder(configuration.Database.GetDbConnection().ConnectionString) { InitialCatalog = catalog };
        var testServer = Environment.GetEnvironmentVariable("ORDER_UPDATE_TEST_SERVER");
        if (!string.IsNullOrWhiteSpace(testServer)) connection.DataSource = testServer;
        testConnection = connection.ConnectionString;
        var created = false;
        try
        {
            using (var db = TestContext()) { db.Database.EnsureCreated(); created = true; Seed(db); }
            var original = Snapshot();
            Check(Save(original, "備考だけ", Updates(original)).Succeeded, "database commit / remarks update");
            using (var db = TestContext())
            {
                Check(db.TOrderDetails.Single().OrTotalPrice == 100, "database preserves untouched legacy line amount");
                var audit = db.OrderOperationHistories.Single();
                Check(audit.ActorEmployeeId == 1 && audit.OfficeId == 1 && audit.RecordedAt != default
                    && audit.Operation == OrderHistoryOperations.Updated && audit.Details.Contains("備考"), "audit actor/time/office/change recorded");
            }
            var current = Snapshot();
            var updated = Updates(current, quantity: 4);
            Check(Save(current, "数量変更", updated).Succeeded, "database quantity change uses stored unit price");
            using (var db = TestContext())
                Check(db.TOrderDetails.Single().OrTotalPrice == 132 && db.TOrderDetails.Single().OrQuantity == 4
                    && db.MProducts.Single().Price == 9999
                    && db.TOrders.Single().OrStateFlag == 0 && db.TChumons.Count() == 0 && db.TSales.Count() == 0
                    && db.TStocks.Single().StQuantity == 50, "ordinary update leaves confirmation / stock / sales unchanged");
            Check(Snapshot().Lines[0].UnitPrice == 33, "stored unit price preserved after quantity update despite different catalog price");
            var conflict = Save(current, "古いウィンドウ", updated);
            Check(!conflict.Succeeded && conflict.Message.Contains("他の利用者"), "stale editor concurrency conflict");
            current = Snapshot();
            Check(!Save(current, "", Updates(current, quantity: 0)).Succeeded, "service rejects zero quantity");
            Check(!Save(current, "", Updates(current, quantity: int.MaxValue)).Succeeded, "service rejects line amount overflow");
            foreach (var price in new[] { -1m, 0m, 35m, decimal.MaxValue })
            {
                var rejected = Save(current, "単価変更", Updates(current, price: price));
                Check(!rejected.Succeeded && rejected.Message.Contains("単価を変更できません"), "service rejects manager price change: " + price);
            }
            var forged = Snapshot();
            forged.Lines[0] = new OrderDetailLine { DetailId=current.Lines[0].DetailId, ProductId=current.Lines[0].ProductId,
                Quantity=current.Lines[0].Quantity, Subtotal=current.Lines[0].Subtotal, UnitPrice=35 };
            Check(!Save(forged, "元の単価を偽装", Updates(forged)).Succeeded, "service derives price independently of forged snapshot");
            Check(Snapshot().CustomerContact == current.CustomerContact && Snapshot().Lines[0].UnitPrice == 33,
                "rejected price changes leave saved data unchanged");
            Check(!Save(current, new string('あ', 51), Updates(current)).Succeeded, "service rejects overlength remarks");
            Check(!Save(current, "", [new OrderDetailUpdate(current.Lines[0].DetailId, 99999, 1, 1)]).Succeeded, "service rejects product identity change");
            SetRole("物流"); Check(!Save(current, "権限なし", Updates(current)).Succeeded, "service role denial");
            SetRole("営業", 2); Check(!Save(current, "拠点外", Updates(current)).Succeeded, "service office scope denial");
            SetRole("営業");
            Check(!Save(current, "営業による価格変更", Updates(current, price: 36)).Succeeded, "service rejects sales price change");
            Check(Save(current, "営業による数量変更", Updates(current, quantity: 5)).Succeeded, "sales quantity editing remains permitted");
            SetRole("管理者");
            current = Snapshot();
            using (var db = TestContext()) db.Database.ExecuteSqlRaw("CREATE TRIGGER OrderUpdateChecksFailure ON T_OrderOperationHistory AFTER INSERT AS THROW 51000, N'OrderUpdateChecks rollback', 1;");
            try
            {
                Check(!Save(current, "保存失敗", Updates(current, quantity: 7)).Succeeded, "actual SQL save failure");
                var unchanged = Snapshot();
                Check(unchanged.CustomerContact == current.CustomerContact && unchanged.Lines[0].Subtotal == current.Lines[0].Subtotal,
                    "database rollback preserves original order");
            }
            finally { using var db = TestContext(); db.Database.ExecuteSqlRaw("DROP TRIGGER OrderUpdateChecksFailure"); }
            CheckExtendedDatabaseUpdate();
            current = Snapshot();
            foreach (var (state, flag) in new[] { (1,0), (0,2) })
            {
                using (var db = TestContext()) { var order = db.TOrders.Single(); order.OrStateFlag=state; order.OrFlag=flag; db.SaveChanges(); }
                Check(!Save(Snapshot(), "更新不可", Updates(current)).Succeeded, "service confirmed/hidden denial");
            }
            using (var db = TestContext())
            {
                var order = db.TOrders.Single(); order.OrFlag=0; order.OrStateFlag=0;
                db.TChumons.Add(new TChumon { OrId=order.OrId, ClId=order.ClId, SoId=order.SoId, ChDate=DateTime.Today }); db.SaveChanges();
            }
            Check(!Save(Snapshot(), "連携済み", Updates(current)).Succeeded, "service linked order denial");
        }
        finally
        {
            // Only the uniquely named database created by this run may be deleted.
            if (!catalog.StartsWith("SalesManagement_OrderUpdateChecks_", StringComparison.Ordinal)
                || connection.InitialCatalog != catalog || catalog == "SalesManagement") throw new InvalidOperationException("Unsafe cleanup target");
            if (created)
            {
                using var db = TestContext(); db.Database.EnsureDeleted();
                Console.WriteLine("Disposable database removed: " + catalog);
            }
        }
    }
    private static IReadOnlyCollection<OrderDetailUpdate> Updates(OrderDetailSnapshot snapshot, int? quantity = null, decimal? price = null) =>
        snapshot.Lines.Select(line => new OrderDetailUpdate(line.DetailId, line.ProductId, quantity ?? line.Quantity, price ?? line.UnitPrice)).ToArray();
    private static OrderUpdateResult Save(OrderDetailSnapshot expected, string remarks, IReadOnlyCollection<OrderDetailUpdate> updates,
        int? customerId=null,DateTime? orderDate=null,DateTime? deliveryDate=null,int? assignedEmployeeId=null,int? officeId=null)
    {
        using var db = TestContext();
        object?[] values = [db, expected.OrderId, AppSession.Employee!.EmId, customerId??expected.CustomerId, officeId??expected.OfficeId, assignedEmployeeId??expected.EmployeeId,
            orderDate??expected.OrderDate, deliveryDate??expected.DeliveryDate, expected.StateFlag, expected.ManagementFlag, expected.HiddenReason, remarks, updates, expected, null];
        var result = (bool)typeof(BusinessService).GetMethod("UpdateOrderCore", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, values)!;
        return new(result, (string)values[^1]!);
    }
    private static OrderDetailSnapshot Snapshot()
    {
        using var db = TestContext(); var order = db.TOrders.Include(x => x.TOrderDetails).Single();
        return new OrderDetailSnapshot
        {
            OrderId=order.OrId, CustomerId=order.ClId, OfficeId=order.SoId, EmployeeId=order.EmId, OrderDate=order.OrDate,
            DeliveryDate=order.OrDeliveryDate, CustomerContact=order.ClCharge, StateFlag=order.OrStateFlag ?? 0,
            ManagementFlag=order.OrFlag, HiddenReason=order.OrHidden,
            Lines=order.TOrderDetails.OrderBy(line=>line.OrDetailId).Select(line => new OrderDetailLine { DetailId=line.OrDetailId, ProductId=line.PrId,
                Quantity=line.OrQuantity, UnitPrice=decimal.Round(line.OrTotalPrice/line.OrQuantity, 0), Subtotal=line.OrTotalPrice }).ToList()
        };
    }
    private static void CheckExtendedDatabaseUpdate()
    {
        var original=Snapshot();int customerId,productId,editorId;
        using(var db=TestContext())
        {
            var savedProduct=db.MProducts.Single();var savedEmployee=db.MEmployees.Single();
            var customer=new MClient {ClName="変更先の実DB顧客",SoId=original.OfficeId};
            var product=new MProduct {PrName="追加商品の実DB検証",Price=2000,MaId=savedProduct.MaId,ScId=savedProduct.ScId,PrReleaseDate=DateTime.Today};
            var employee=new MEmployee {EmId=2,EmName="更新実行者",SoId=original.OfficeId,PoId=savedEmployee.PoId,EmHiredate=DateTime.Today};
            db.AddRange(customer,product,employee);
            foreach(var entry in db.ChangeTracker.Entries())
            foreach(var property in entry.Properties.Where(property=>property.Metadata.ClrType==typeof(string) && !property.Metadata.IsNullable && property.CurrentValue==null)) property.CurrentValue="test";
            db.SaveChanges();customerId=customer.ClId;productId=product.PrId;editorId=employee.EmId;
            AppSession.Employee=db.MEmployees.Include(employee=>employee.Po).Include(employee=>employee.So).Single(employee=>employee.EmId==editorId);
        }
        var edits=Updates(original).Append(new OrderDetailUpdate(null,productId,2,2000)).ToArray();
        Check(!Save(original,"無効顧客",edits,customerId:99999).Succeeded,"service rejects a missing or invalid replacement customer");
        Check(!Save(original,"営業所変更",edits,officeId:original.OfficeId+1).Succeeded
            && !Save(original,"担当者変更",edits,assignedEmployeeId:editorId).Succeeded,"service preserves receiving office and original employee");
        Check(!Save(original,"明細削除",[]).Succeeded,"service prevents saving an order with no active details");
        Check(!Save(original,"新規単価の偽装",Updates(original).Append(new OrderDetailUpdate(null,productId,2,1999)).ToArray()).Succeeded,
            "new product price is checked against the current catalog inside the transaction");
        var orderDate=original.OrderDate.AddDays(1);var deliveryDate=orderDate.AddDays(7);var historyCount=0;
        using(var db=TestContext())
        {
            historyCount=db.OrderOperationHistories.Count();
            db.Database.ExecuteSqlRaw("CREATE TRIGGER OrderUpdateChecksFailure ON T_OrderOperationHistory AFTER INSERT AS THROW 51000, N'OrderUpdateChecks rollback', 1;");
        }
        try
        {
            Check(!Save(original,"ヘッダー・追加行のロールバック",edits,customerId,orderDate,deliveryDate).Succeeded,
                "SQL failure rejects the combined header and added-product update");
            using var db=TestContext();var order=db.TOrders.Include(order=>order.TOrderDetails).Single();
            Check(order.ClId==original.CustomerId && order.OrDate==original.OrderDate && order.TOrderDetails.Count==original.Lines.Count
                && db.OrderOperationHistories.Count()==historyCount,"failed combined update rolls back customer, dates, added rows and audit atomically");
        }
        finally {using var db=TestContext();db.Database.ExecuteSqlRaw("DROP TRIGGER OrderUpdateChecksFailure");}
        Check(Save(original,"顧客・日付・商品を更新",edits,customerId,orderDate,deliveryDate).Succeeded,"existing service commits customer, dates and an additional product");
        var after=Snapshot();
        using(var db=TestContext())
        {
            var audit=db.OrderOperationHistories.OrderByDescending(row=>row.Id).First();
            Check(after.OrderId==original.OrderId && after.CustomerId==customerId && after.OrderDate==orderDate && after.DeliveryDate==deliveryDate
                && after.EmployeeId==original.EmployeeId && after.Lines.Single(line=>line.ProductId==productId).Subtotal==4000
                && db.TOrders.Count()==1 && db.TStocks.Single().StQuantity==50 && db.TChumons.Count()==0 && db.TSales.Count()==0,
                "combined update keeps order identity, original receiving employee, stock and unconfirmed state");
            Check(audit.ActorEmployeeId==editorId && audit.CustomerId==customerId && audit.Details.Contains("変更先の実DB顧客")
                && audit.Details.Contains("追加商品の実DB検証"),"existing audit records edited customer, added product and the distinct updating employee");
        }
        Check(!Save(original,"古い編集画面",edits,customerId,orderDate,deliveryDate).Succeeded,"combined update still rejects a stale original snapshot");
        var unchanged=Save(after,after.CustomerContact??"",Updates(after));
        Check(!unchanged.Succeeded && unchanged.Message=="変更はありません。","unchanged resubmission creates no second save");
        SetRole("管理者");
    }
    private static void Seed(SalesManagementContext db)
    {
        var office = new MSalesOffice { SoName="北大阪営業所" };
        var position = new MPosition { PoName="管理者" };
        var employee = new MEmployee { EmId=1, EmName="高谷春男", EmHiredate=DateTime.Today, So=office, Po=position, EmPassword="test" };
        var customer = new MClient { ClName="INATUGI", So=office };
        var major = new MMajorClassification { McName="電子機器" };
        var small = new MSmallClassification { ScName="入力機器", Mc=major };
        var maker = new MMaker { MaName="テストメーカー" };
        var product = new MProduct { PrName="キーボード", Price=9999, Sc=small, Ma=maker, PrReleaseDate=DateTime.Today };
        var order = new TOrder { So=office, Em=employee, Cl=customer, OrDate=DateTime.Today, OrStateFlag=0,
            TOrderDetails=[new TOrderDetail { Pr=product, OrQuantity=3, OrTotalPrice=100 }] };
        db.Add(order); db.Add(new TStock { Pr=product, StQuantity=50 });
        foreach (var entry in db.ChangeTracker.Entries())
        foreach (var property in entry.Properties.Where(property => property.Metadata.ClrType == typeof(string) && !property.Metadata.IsNullable && property.CurrentValue == null))
            property.CurrentValue = "test";
        db.SaveChanges();
    }

    private sealed class ControlledSource : IOrderUpdateDataSource
    {
        public OrderDetailSnapshot? Expected, Edited; public IReadOnlyCollection<OrderDetailUpdate>? SubmittedLines;
        public int Calls; public bool Throw; public TaskCompletionSource<OrderUpdateResult>? Pending;
        public Task<OrderUpdateResult> SaveAsync(OrderDetailSnapshot expected, OrderDetailSnapshot edited, string remarks, IReadOnlyCollection<OrderDetailUpdate> lines)
        {
            Expected=expected; Edited=edited; SubmittedLines=lines; Calls++; if (Throw) throw new InvalidOperationException("test failure");
            return Pending?.Task ?? Task.FromResult(new OrderUpdateResult(true, "受注を更新しました。"));
        }
    }
    private sealed class QuantityRegistrationSource : IOrderRegistrationDataSource
    {
        public int ProductLoads { get; private set; }
        public int StockBatches { get; private set; }
        public int StockSelections { get; private set; }
        public IReadOnlyList<MSalesOffice> LoadOffices() => [new MSalesOffice { SoId=1, SoName="北大阪営業所" }];
        public IReadOnlyList<MClient> LoadClients(int officeId) => [new MClient { ClId=1, ClName="INATUGI", SoId=officeId }];
        public IReadOnlyList<RegistrationProduct> LoadProducts()
        {
            ProductLoads++;
            return Enumerable.Range(1,35).Select(id=>new RegistrationProduct
            {
                PrId = id, ProductCode = $"PR-{id:0000}", Barcode = $"490000000{id:0000}",
                ProductName = id == 1 ? "キーボード" : id == 3 ? new string('長', 80) : $"検証用商品 {id:00}",
                MakerId = (id - 1) % 3 + 1, MakerName = ((id - 1) % 3) switch { 0 => "Apple", 1 => "Samsung", _ => "Dell" },
                UnitPrice = id == 1 ? 1234 : 2000, CategoryName = "入力機器", Stock = id == 3 ? null : id == 2 ? 0 : 5
            }).ToArray();
        }
        public InventoryStockLookup LoadStock(int id)
        {
            StockSelections++;
            return GetStock(id);
        }
        public IReadOnlyDictionary<int, InventoryStockLookup> LoadStocks(IReadOnlyCollection<int> ids)
        {
            StockBatches++;
            return ids.Distinct().ToDictionary(id => id, GetStock);
        }
        private static InventoryStockLookup GetStock(int id) => id == 3
            ? new(InventoryStockState.MissingRecord) : new(InventoryStockState.Available, id == 2 ? 0 : 5);
        public bool Register(int employeeId, int customerId, int officeId, DateTime orderDate, DateTime deliveryDate,
            string remarks, IReadOnlyCollection<OrderDetailUpdate> details, out int orderId, out string message) => throw new InvalidOperationException("UI checks must not write to a database.");
        public OrderDetailSnapshot? LoadRegisteredOrder(int orderId) => null;
        public void Dispose() { }
    }
    private sealed class UpdateLookupSource : IOrderRegistrationDataSource
    {
        private readonly QuantityRegistrationSource catalog=new();
        public int TestedProductId=3;
        public InventoryStockState StockState=InventoryStockState.MissingRecord;
        public int StockQuantity=5;
        public bool ThrowOnStock;
        public IReadOnlyList<MSalesOffice> LoadOffices()=>catalog.LoadOffices();
        public IReadOnlyList<MClient> LoadClients(int officeId)=>[..catalog.LoadClients(officeId),new MClient {ClId=8,ClName="変更先の顧客",SoId=officeId}];
        public IReadOnlyList<RegistrationProduct> LoadProducts()=>catalog.LoadProducts();
        public InventoryStockLookup LoadStock(int productId)=>productId==TestedProductId
            ? ThrowOnStock ? throw new InvalidOperationException("Simulated read failure.")
                : new(StockState,StockState==InventoryStockState.Available ? StockQuantity : null)
            : new(InventoryStockState.Available,5);
        public OrderDetailSnapshot? LoadRegisteredOrder(int orderId)=>Sample();
        public bool Register(int employeeId,int customerId,int officeId,DateTime orderDate,DateTime deliveryDate,
            string remarks,IReadOnlyCollection<OrderDetailUpdate> details,out int orderId,out string message)=>throw new InvalidOperationException("Update must never register a new order.");
        public void Dispose() { }
    }
    private sealed class RegistrationSubmissionSource : IOrderRegistrationDataSource
    {
        public readonly ManualResetEventSlim AllowCompletion=new(false);
        public int Calls;
        public bool Fail;
        public IReadOnlyList<MSalesOffice> LoadOffices()=>[new MSalesOffice { SoId=1,SoName="北大阪営業所" }];
        public IReadOnlyList<MClient> LoadClients(int officeId)=>[new MClient { ClId=1,ClName="INATUGI",SoId=officeId }];
        public IReadOnlyList<RegistrationProduct> LoadProducts()=>[new RegistrationProduct { PrId=1,ProductCode="PR-0001",ProductName="キーボード",UnitPrice=2000,Stock=0 }];
        public bool Register(int employeeId,int customerId,int officeId,DateTime orderDate,DateTime deliveryDate,
            string remarks,IReadOnlyCollection<OrderDetailUpdate> details,out int orderId,out string message)
        {
            Interlocked.Increment(ref Calls); AllowCompletion.Wait(TimeSpan.FromSeconds(10));
            orderId=Fail ? 0 : 236+Calls; message=Fail ? "検証用の登録失敗" : "登録完了"; return !Fail;
        }
        public OrderDetailSnapshot? LoadRegisteredOrder(int orderId)=>null;
        public void Dispose()=>AllowCompletion.Dispose();
    }
    private sealed class BindingListener : TraceListener
    {
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) bindingErrors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
