using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
            if (args.Contains("--export-only"))
            {
                CheckOrderExportFiles(args.Contains("--benchmark"));
                Console.WriteLine($"PASS: {checks} order export file checks. Output: {output}");
                app.Shutdown();
                return 0;
            }
            var updateDatabaseOnly = args.Contains("--update-database-only");
            if(args.Contains("--draft-only")) CheckDraftNavigation();
            else if(args.Contains("--preview-only")) RenderLayout(1150,720,"update-ux-preview",true,count:2);
            else if(args.Contains("--layout-only")) CheckLayouts();
            else if(args.Contains("--database-only") || updateDatabaseOnly) { }
            else { CheckViewModel(); CheckQuantityInputs(); CheckDraftNavigation(); CheckUpdateWorkflow(); CheckUpdateDeletion(); CheckInventoryStatus(); CheckUpdateNavigation(); CheckCompletionRoutes(); CheckConcurrencySnapshot(); CheckLayouts(); }
            if (!args.Contains("--skip-database"))
            {
                if (!args.Contains("--draft-only")) CheckDatabase();
                if (!updateDatabaseOnly) CheckDraftDatabase();
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
    private static OrderDetailSnapshot Sample(int count = 2, int state = 0, int hidden = 0, int employeeId = 1,
        int officeId = 1, string officeName = "北大阪営業所") => new()
    {
        OrderId = 165, CustomerId = 1, CustomerName = "INATUGI", CustomerAddress = "大阪府茨木市横江2-5-60 本館3階 商品受入窓口",
        CustomerPhone = "072-02-5171", OfficeId = officeId, OfficeName = officeName, EmployeeId = employeeId, EmployeeName = "佐藤一郎",
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
            Check(!locked.CanEdit && !locked.CanSave && !locked.RemoveAllLinesCommand.CanExecute(null)
                && !locked.UndoRemoveLineCommand.CanExecute(null), "confirmed/hidden read-only, including detail actions");
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
        var saved=Sample(employeeId: 17); var source=new ControlledSource(); var lookups=new UpdateLookupSource();
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
        vm.RemoveLineCommand.Execute(added);Check(vm.Lines.Count==2 && vm.HasUndo,"new draft row removal enables update Undo");
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
            && source.Edited.OrderId==saved.OrderId && source.Edited.EmployeeId==saved.EmployeeId
            && source.Edited.EmployeeId!=AppSession.Employee!.EmId && source.Expected.OrderDate==saved.OrderDate
            && source.SubmittedLines!.Single(line=>line.ProductId==3).DetailId==null,
            "save submits original concurrency snapshot separately from edited header and newly added detail");
        Wait(vm.SaveAsync());Check(source.Calls==1 && !vm.CanEdit && !vm.HasUnsavedChanges,"successful save retains read-only contents and prevents a second save");
    }

    private static void CheckUpdateDeletion()
    {
        var original = Sample(3, employeeId: 17);
        var source = new ControlledSource();
        var confirm = false;
        using var editor = new OrderUpdateViewModel(original, _ => { }, source, new UpdateLookupSource(), confirmDeleteAll: () => confirm);
        editor.Load();
        var originalRows = editor.Lines.ToArray();
        Check(editor.RemoveAllLinesCommand.CanExecute(null) && !editor.UndoRemoveLineCommand.CanExecute(null),
            "update starts with Delete All available and Undo disabled");

        editor.Remarks = "編集中の備考";
        editor.OrderDate = original.OrderDate.AddDays(1);
        editor.DeliveryDate = null;
        editor.RemoveLineCommand.Execute(originalRows[1]);
        originalRows[0].Quantity = "3";
        editor.UndoRemoveLineCommand.Execute(null);
        Check(editor.Lines.SequenceEqual(originalRows) && editor.QuantitySummary == "3 品目 / 7 点"
            && editor.Subtotal == 8638 && editor.Tax == 864 && editor.Total == 9502 && !editor.HasUndo,
            "individual Undo restores the same row in its original position and retains edits on other rows");

        originalRows[2].Quantity = "5";
        originalRows[2].Quantity = "abc";
        editor.RemoveAllLinesCommand.Execute(null);
        Check(editor.Lines.SequenceEqual(originalRows) && !editor.HasUndo, "declining Delete All preserves every row");
        confirm = true;
        editor.RemoveAllLinesCommand.Execute(null);
        Wait(editor.SaveAsync());
        Check(editor.Lines.Count == 0 && editor.QuantitySummary == "0 品目 / 0 点"
            && editor.Subtotal == 0 && editor.Tax == 0 && editor.Total == 0
            && !editor.CanSave && !editor.RemoveAllLinesCommand.CanExecute(null) && editor.UndoRemoveLineCommand.CanExecute(null)
            && editor.HasUnsavedChanges && source.Calls == 0, "Delete All clears local details and blocks saving an empty order");
        Check(editor.OrderDate == original.OrderDate.AddDays(1) && editor.DeliveryDate == null
            && editor.Remarks == "編集中の備考" && editor.Detail.EmployeeName == original.EmployeeName
            && editor.Entry.SelectedOffice?.SoId == original.OfficeId && editor.CustomerName == original.CustomerName,
            "Delete All keeps the customer, receiving office, employee, edited dates and remarks");
        editor.Entry.SelectedProduct = editor.Entry.Products.Single(product => product.PrId == 4);
        editor.AddLineCommand.Execute(null);
        editor.UndoRemoveLineCommand.Execute(null);
        Check(editor.Lines.Take(3).SequenceEqual(originalRows) && editor.Lines[3].ProductId == 4
            && editor.Lines[2].Quantity == "abc" && editor.Lines[2].Preview.Quantity == 5 && !editor.CanSave
            && editor.Subtotal == 14340 && editor.Tax == 1434 && editor.Total == 15774 && !editor.HasUndo,
            "bulk Undo restores order and pending quantities, keeps newly added rows and revalidates all details");

        originalRows[2].Quantity = "5";
        editor.RemoveAllLinesCommand.Execute(null);
        editor.Entry.SelectedProduct = editor.Entry.Products.Single(product => product.PrId == originalRows[2].ProductId);
        editor.Entry.Quantity = "2";
        editor.AddLineCommand.Execute(null);
        editor.UndoRemoveLineCommand.Execute(null);
        Check(editor.Lines.Count == 4 && ReferenceEquals(editor.Lines[2], originalRows[2]) && editor.Lines[2].Quantity == "7"
            && editor.Lines[2].UnitPrice == original.Lines[2].UnitPrice && editor.CanSave,
            "bulk Undo merges quantities for a re-added product while retaining the saved detail ID and price");

        editor.RemoveAllLinesCommand.Execute(null);
        editor.Entry.SelectedProduct = editor.Entry.Products.Single(product => product.PrId == originalRows[0].ProductId);
        editor.Entry.Quantity = editor.Entry.MaximumQuantity.ToString(CultureInfo.InvariantCulture);
        editor.AddLineCommand.Execute(null);
        var readded = editor.Lines.Single();
        editor.UndoRemoveLineCommand.Execute(null);
        Check(editor.Lines.SequenceEqual(new[] { readded }) && editor.HasUndo && editor.Feedback.Contains("行合計"),
            "Undo validates a merged amount before changing any rows and keeps the snapshot available on failure");
        readded.Quantity = "1";
        editor.UndoRemoveLineCommand.Execute(null);
        Check(editor.Lines.Take(3).SequenceEqual(originalRows) && originalRows[0].Quantity == "4" && !editor.HasUndo,
            "Undo can be retried after adjusting an amount that exceeded the existing limit");

        editor.RemoveLineCommand.Execute(originalRows[1]);
        source.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = editor.SaveAsync();
        Check(editor.IsBusy && !editor.RemoveAllLinesCommand.CanExecute(null) && !editor.UndoRemoveLineCommand.CanExecute(null),
            "detail actions are disabled while saving");
        source.Pending.SetResult(new(false, "検証用の保存失敗"));
        Wait(failed);
        Check(editor.HasUndo && editor.UndoRemoveLineCommand.CanExecute(null) && editor.Lines.Count == 3
            && editor.Feedback == "検証用の保存失敗", "a failed save leaves the unsaved deletion and Undo available");
        source.Pending = null;
        Wait(editor.SaveAsync());
        Check(editor.HasSaved && !editor.HasUndo && !editor.UndoRemoveLineCommand.CanExecute(null)
            && !editor.RemoveAllLinesCommand.CanExecute(null) && source.Calls == 2
            && source.Expected == original && source.Edited!.EmployeeId == original.EmployeeId
            && source.SubmittedLines!.All(line => line.DetailId != originalRows[1].DetailId),
            "successful save clears Undo and submits the edited detail set through the existing update path");
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

    private static void CheckUpdateNavigation()
    {
        var source = new ControlledSource();
        var returns = 0;
        var order = Sample(6, employeeId: 17, officeId: 2, officeName: "南営業所");
        using var editor = new F_OrderUpdate(order, () => returns++,
            updateSource: source, lookupSource: new UpdateLookupSource());
        var root = (FrameworkElement)editor.DetachView();
        var model = (OrderUpdateViewModel)root.DataContext;
        var shell = new F_Main
        {
            Width = 1100, Height = 760, Left = -30000, Top = -30000, WindowStyle = WindowStyle.None,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false, ShowActivated = false
        };
        try
        {
            var sidebar = (Border)shell.FindName("mainSidebar");
            var sidebarModel = (SidebarViewModel)sidebar.DataContext;
            ((Button)shell.FindName("homeButton")).Tag = null;
            sidebarModel.MenuItems.Single(entry => entry.Label == "受注管理").IsActive = true;
            typeof(F_Main).GetMethod("ShowBusinessView", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(shell, ["受注更新", root]);
            shell.Show(); shell.UpdateLayout(); FlushBindings();
            var status = (TextBlock)shell.FindName("status");
            var sessionPanel = (FrameworkElement)shell.FindName("orderEntrySession");
            var sessionText = Descendants<TextBlock>(sessionPanel).Where(text => text.IsVisible).Select(text => text.Text).ToArray();
            var employee = AppSession.Employee!;
            Check(((TextBlock)shell.FindName("mainHeaderTitle")).Text == "受注更新"
                && ((LucideIcon)shell.FindName("orderHeaderIcon")).IsVisible
                && sessionPanel.IsVisible && !status.IsVisible
                && sessionText is [var name, var role, var office] && name.EndsWith(employee.EmName, StringComparison.Ordinal)
                && role == employee.Po.PoName && office == order.OfficeName && office != employee.So.SoName
                && model.Entry.SelectedOffice?.SoId == order.OfficeId
                && Equals(sidebar.Background, Application.Current.FindResource("ThemeOrderEntryNavy")),
                "embedded update shows the signed-in employee and the order's own branch in the shared registration header");
            Check(Descendants<TextBlock>(root).Any(text => text.Text == model.Detail.OrderNumber && text.IsVisible)
                && !Descendants<TextBlock>(root).Any(text => text.IsVisible
                    && (text.Text is "受注受付担当者" or "受付営業所" || text.Text == model.Detail.EmployeeName || text.Text == order.OfficeName)),
                "the update form keeps the order number and uses the header for the branch");
            model.Remarks = "入力中の変更内容";
            shell.UpdateLayout(); FlushBindings();
            Render((FrameworkElement)shell.Content, "update-shell-desktop");

            var (deleteAll, undo) = DetailActionButtons((FrameworkElement)editor.FindName("DetailActions"));
            Check(Descendants<TextBlock>(root).Count(text => text.IsVisible && text.Text == model.QuantitySummary) == 1,
                "the update editor displays its quantity summary once in the detail heading");
            foreach (var confirm in new[] { false, true })
            {
                CheckDeleteAllPrompt(shell, deleteAll, confirm, render: confirm);
                FlushBindings();
                Check(model.Lines.Count == (confirm ? 0 : 6) && model.Remarks == "入力中の変更内容" && source.Calls == 0,
                    "Delete All confirmation affects local details only");
            }
            Check(!deleteAll.IsVisible && undo.IsVisible && undo.IsEnabled && !((Button)editor.FindName("SaveButton")).IsEnabled
                && Descendants<TextBlock>(root).Any(text => text.Text == "商品が追加されていません。" && text.IsVisible)
                && Descendants<TextBlock>(root).Any(text => text.Text == "左側から商品を選択して追加してください。" && text.IsVisible),
                "empty update details show guidance and Undo, hide Delete All and disable Save");
            undo.Command.Execute(null); FlushBindings();
            Check(model.Lines.Count == 6 && deleteAll.IsEnabled && !undo.IsEnabled, "the fixed Undo button restores all rows and updates the action state");

            shell.Width = shell.MinWidth; shell.Height = shell.MinHeight;
            shell.UpdateLayout(); FlushBindings();
            CheckUpdateFooter(editor, "minimum shell with expanded sidebar");
            sidebarModel.ToggleSidebarCommand.Execute(null);
            shell.UpdateLayout(); FlushBindings();
            CheckUpdateFooter(editor, "minimum shell with collapsed sidebar");
            model.RemoveLineCommand.Execute(model.Lines[0]);
            foreach (var route in new[] { "Continue", "Escape", "Discard" })
            {
                Exception? failure = null;
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    var notice = Application.Current.Windows.OfType<F_OrderNotice>().Single();
                    try
                    {
                        var viewModel = (OrderNoticeViewModel)notice.DataContext;
                        var actions = Descendants<Button>(notice).Where(button => button.IsVisible).Select(button => button.Content).OfType<string>();
                        Check(viewModel.Title == "未保存の変更があります" && !viewModel.ConfirmCommand.CanExecute(null)
                            && actions.SequenceEqual(new[] { "破棄して移動", "入力を続ける" })
                            && !IsWindowEnabled(new WindowInteropHelper(shell).Handle),
                            "unsaved update uses the shared modal with discard and continue actions: " + route);
                        if (route == "Continue") viewModel.CancelCommand.Execute(null);
                        else if (route == "Escape") notice.InputBindings.OfType<KeyBinding>().Single().Command.Execute(null);
                        else viewModel.DiscardCommand.Execute(null);
                    }
                    catch (Exception ex) { failure = ex; notice.Close(); }
                }));
                ((Button)editor.FindName("CancelButton")).Command.Execute(null);
                if (failure != null) throw failure;
                Check(returns == (route == "Discard" ? 1 : 0) && source.Calls == 0 && model.Remarks == "入力中の変更内容"
                    && model.Lines.Count == 5 && model.HasUndo,
                    "Cancel protects unsaved edits and performs no write: " + route);
            }
            editor.Dispose();
            Check(!model.HasUndo && !model.UndoRemoveLineCommand.CanExecute(null), "leaving the editor clears the local undo snapshot");
        }
        finally { shell.Close(); }
    }

    private static void CheckDeleteAllPrompt(Window owner, Button deleteAll, bool confirm, bool render = false)
    {
        InteractWithModal(owner, () => deleteAll.Command.Execute(null), modal =>
        {
            modal.UpdateLayout(); FlushBindings();
            var notice = (OrderNoticeViewModel)modal.DataContext;
            var buttons = Descendants<Button>(modal).Where(button => button.IsVisible).ToArray();
            var delete = buttons.Single(button => Equals(button.Content, "すべて削除"));
            var cancel = buttons.Single(button => Equals(button.Content, "キャンセル"));
            Check(notice.Title == "すべての商品を削除しますか？"
                && notice.Message == "受注明細のすべての商品を削除します。受注情報や備考は削除されません。"
                && delete.TranslatePoint(new Point(delete.ActualWidth, 0), modal).X < cancel.TranslatePoint(new Point(), modal).X
                && Equals(Descendants<LucideIcon>(delete).Single().Data, Application.Current.FindResource("IconTrash"))
                && Equals(Descendants<LucideIcon>(cancel).Single().Data, Application.Current.FindResource("IconX"))
                && cancel.IsDefault && cancel.IsCancel && !delete.IsDefault,
                "the confirmation places the trash action to the left of Cancel and shows the X icon on Cancel");
            if (render) Render((FrameworkElement)modal.Content, "delete-all-confirmation");
            (confirm ? delete : cancel).Command.Execute(null);
        });
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
        Check(grid.Columns.Select(column => column.Header).SequenceEqual(new[] { "商品コード・商品名", "単価", "数量", "小計", "操作" }),
            "update uses the same five detail columns as registration");
        CheckManufacturerLayout(editor);
        var productInput = (OrderProductInput)editor.FindName("ProductInput");
        var maker = (RegistrationLookup)productInput.FindName("ManufacturerFilter");
        var originalLines = vm.Lines.ToArray();
        ChooseManufacturer(maker, "ell");
        Check(vm.Entry.Products.All(product => product.MakerName == "Dell") && vm.Lines.SequenceEqual(originalLines)
            && !vm.HasUnsavedChanges && !vm.CanSave, "the manufacturer filter changes suggestions only and retains existing order rows");
        ChooseManufacturer(maker, "すべてのメーカー");
        lookups.TestedProductId = 3;
        vm.Entry.SelectedProduct = vm.Entry.Products.Single(product => product.PrId == 3);
        FlushBindings();
        var stockPanel = (Border)productInput.FindName("ProductInputStockPanel");
        var stockHint = (TextBlock)productInput.FindName("ProductInputStockHint");
        Check(vm.Entry.HasSufficientStock && stockPanel.Visibility == Visibility.Collapsed,
            "update collapses the stock notice when stock is sufficient");
        lookups.StockState = InventoryStockState.MissingRecord;
        Wait(vm.RefreshInventoryAsync()); FlushBindings();
        Check(stockHint.IsVisible && stockHint.Text == "在庫情報が未登録です"
            && Descendants<TextBlock>(productInput).Any(text => text.Text == "未登録" && text.IsVisible)
            && vm.Lines.SequenceEqual(originalLines), "update shows a refreshed missing-stock warning and badge for an unadded product");
        lookups.StockState = InventoryStockState.Available;
        Wait(vm.RefreshInventoryAsync()); FlushBindings();
        Check(stockPanel.Visibility == Visibility.Collapsed && vm.Entry.HasSufficientStock,
            "update collapses the stock notice when a refresh restores sufficient stock");
        vm.Entry.SelectedProduct = null;
        lookups.TestedProductId = 1;
        lookups.StockState = InventoryStockState.Available;
        var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(vm.Lines[0]);
        var number = Descendants<QuantityUpDown>(row).Single(); number.ApplyTemplate();
        var stock = Descendants<TextBlock>(row).Single(text=>text.Text=="在庫：5点");
        var code = Descendants<TextBlock>(row).Single(text => text.Text == vm.Lines[0].ProductCode);
        Check(stock.TranslatePoint(new Point(), row).Y >= code.TranslatePoint(new Point(0, code.ActualHeight), row).Y,
            "actual stock appears under the product name and code");
        var quantityText = (TextBox)number.Template.FindName("PART_Input", number);
        var plus=(Button)number.Template.FindName("PART_Increase",number);
        var minus=(Button)number.Template.FindName("PART_Decrease",number);
        Check(number.Text=="2" && plus.IsEnabled && minus.IsEnabled, "the saved quantity loads into the shared stepper");
        CheckHorizontalStepper(number);
        plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FlushBindings();
        Check(vm.Lines[0].Quantity=="3" && vm.Subtotal==6170 && vm.Tax==617 && vm.Total==6787 && vm.CanSave
            && vm.HasUnsavedChanges && !vm.HasPendingQuantity && !vm.TryApproveClose(() => false) && saveSource.Calls==0,
            "plus immediately updates quantity, tax, totals and save eligibility without writing to the database");
        minus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FlushBindings();
        Check(vm.Lines[0].Quantity=="2" && vm.Subtotal==4936 && !vm.CanSave && !vm.HasUnsavedChanges,
            "minus immediately restores the original quantity and amounts");
        quantityText.Text="37"; FlushBindings();
        Check(vm.Lines[0].Quantity=="37" && vm.CanSave && vm.Subtotal==39*1234 && vm.Tax==4813 && vm.Total==52939
            && vm.Lines[0].SubtotalText=="¥45,658" && vm.Lines[0].UnitPrice==1234 && saveSource.Calls==0,
            "direct numeric input immediately recalculates row and document totals while preserving the saved price");
        Check(!Descendants<TextBlock>(row).Any(text=>text.Text.Contains("Enter")) && !vm.Feedback.Contains("Enter"),
            "automatic quantity entry displays no Enter confirmation hint");
        row.IsSelected=true;FlushBindings();
        Check(stock.Text=="在庫不足：32点" && Equals(stock.Foreground,stock.FindResource("ThemeDangerText"))
            && Equals(row.Background,row.FindResource("ThemePaleBlue")),"the selected row retains red shortage text on a subtle background");
        lookups.StockState=InventoryStockState.MissingRecord;
        Wait(vm.RefreshInventoryAsync());FlushBindings();
        Check(stock.Text=="在庫情報が未登録です" && Equals(stock.Foreground,editor.FindResource("RegistrationMissingStockText")),
            "a fresh missing lookup uses the shared amber detail style");
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
        var host = new Window { Width=1224, Height=626, Left=-30000, Top=-30000,
            WindowStyle=WindowStyle.None, ShowInTaskbar=false, ShowActivated=false };
        using var vm = new OrderRegistrationViewModel(() => { }, source: source,
            confirmDeleteAll: () => F_OrderNotice.ConfirmDeleteAll(host));
        vm.Load();
        Check(source.StockBatches == 1 && source.StockSelections == 0
            && vm.Products[0].StockLabel == "在庫：5点" && vm.Products[1].StockLabel == "在庫：0点"
            && vm.Products[2].StockLabel == "在庫：未登録", "registration stock options use one batch with available, zero and missing stock");
        Check(vm.QuantityLabel=="数量", "unit is hidden until a product is selected");
        Check(!vm.CanRegister && !vm.AddLineCommand.CanExecute(null) && !vm.ShowProductDetailsCommand.CanExecute(null), "empty registration commands disabled");
        var view = new F_OrderRegistration { DataContext=vm };
        var root = (FrameworkElement)view.Content; view.Content=null;
        host.Content = root;
        host.DataContext = vm;
        host.Show(); host.UpdateLayout(); FlushBindings();
        var detailActions = (ContentControl)view.FindName("DetailActions");
        detailActions.ApplyTemplate();
        var (deleteAll, undo) = DetailActionButtons(detailActions);
        Check(detailActions.Visibility == Visibility.Collapsed && detailActions.DesiredSize.Height == 0
            && !deleteAll.IsVisible && !undo.IsVisible
            && Descendants<TextBlock>(root).Any(label => label.IsVisible && label.Text == "商品が追加されていません。"),
            "empty registration shows guidance and reserves no space for detail actions");
        Render(root, "registration-empty");
        CheckRegistrationLabels(root);
        var productInput = (OrderProductInput)view.FindName("ProductInput");
        var editor=(QuantityUpDown)productInput.FindName("QuantityInput"); editor.ApplyTemplate();
        var inputScroller=(ScrollViewer)view.FindName("InputScroll");
        var addButton=Descendants<Button>(root).Single(button=>System.Windows.Automation.AutomationProperties.GetName(button)=="商品を追加");
        var addBounds=addButton.TransformToAncestor(inputScroller).TransformBounds(new Rect(new Point(),addButton.RenderSize));
        Check(addBounds.Top>=0 && addBounds.Bottom<=inputScroller.ViewportHeight, "product add action is visible at 1280x720 without scrolling");
        var input=(TextBox)editor.Template.FindName("PART_Input",editor);
        var plus=(Button)editor.Template.FindName("PART_Increase",editor);
        var minus=(Button)editor.Template.FindName("PART_Decrease",editor);
        var details=(Button)productInput.FindName("ProductDetailsButton");
        Check(!details.IsEnabled && Equals(details.Background, Application.Current.FindResource("ThemeBorder")),
            "product details is disabled and gray before selecting a product");
        var manufacturer = (RegistrationLookup)productInput.FindName("ManufacturerFilter");
        CheckManufacturerFiltering(vm, source, manufacturer, (RegistrationLookup)productInput.FindName("ProductLookup"), details);
        vm.SelectedClient=source.LoadClients(1)[0]; vm.Remarks="入力内容を保持";
        var option = (FrameworkElement)((DataTemplate)view.Resources["RegistrationProductSuggestion"]).LoadContent();
        var missingProduct = vm.Products.Single(product => product.PrId == 3);
        option.DataContext = missingProduct;
        FlushBindings();
        var optionText = Descendants<TextBlock>(option).Select(text => text.Text).ToArray();
        var remarksCounter = (TextBlock)view.FindName("RemarksCounter");
        Check(remarksCounter.Text == $"{vm.Remarks.Length} / {OrderUpdateRules.RemarksMaxLength}",
            "the remarks counter tracks the actual text and keeps the existing limit");
        Check(optionText.Contains(missingProduct.ProductCode) && optionText.Contains(missingProduct.ProductName)
            && optionText.Contains("メーカー：Dell | 在庫：未登録") && optionText.Contains("¥2,000"),
            "product option renders code, name, manufacturer, stock and price");
        Check(editor.Text=="1" && !minus.IsEnabled && plus.IsEnabled, "quantity default/minimum state");
        CheckHorizontalStepper(editor);
        plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FlushBindings();
        Check(vm.Quantity=="2" && minus.IsEnabled, "plus updates quantity without Enter");
        minus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FlushBindings();
        Check(vm.Quantity=="1" && !minus.IsEnabled, "minus returns to minimum");
        vm.SelectedProduct=source.LoadProducts()[0]; FlushBindings();
        var stockPanel = (Border)productInput.FindName("ProductInputStockPanel");
        var stockHint = (TextBlock)productInput.FindName("ProductInputStockHint");
        host.UpdateLayout();
        var visibleProductText = Descendants<TextBlock>(productInput).Where(text => text.IsVisible).Select(text => text.Text).ToArray();
        Check(vm.CurrentStockDisplay == "5 個" && vm.HasSufficientStock && stockPanel.Visibility == Visibility.Collapsed
            && stockPanel.DesiredSize.Height == 0
            && visibleProductText.Count(text => text == vm.CurrentStockDisplay) == 1
            && visibleProductText.Count(text => text == vm.UnitPriceDisplay) == 1,
            "registration displays the stock badge and unit price once without reserving notice space");
        input.Text="37"; FlushBindings();
        Check(details.IsEnabled, "product details is enabled for a valid selection");
        Check(vm.QuantityLabel=="数量（個）", "selected product displays the existing item-count unit");
        Check(vm.CanAddProduct && !vm.HasSufficientStock && stockHint.IsVisible && stockHint.Text == "在庫不足：32点"
            && vm.StockStatus.Tooltip.Contains("受注数量：37点"),
            "direct input shows a shortage without stock blocking");
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
            Check(!vm.CanAddProduct && !vm.HasSufficientStock && vm.QuantityError.Length>0, "invalid quantity blocks product addition: "+value);
        }
        input.Text="37"; FlushBindings();
        vm.AddLineCommand.Execute(null); FlushBindings();
        Check(!details.IsEnabled, "product details is disabled again after clearing selection");
        Check(vm.Lines.Single().Quantity==37 && vm.Lines.Single().UnitName=="個" && vm.Quantity=="1" && vm.SelectedProduct==null && vm.UnitPriceDisplay=="商品選択後に表示", "addition retains unit and resets only product input");
        Check(detailActions.IsVisible && deleteAll.IsVisible && deleteAll.IsEnabled && undo.IsVisible && !undo.IsEnabled
            && Descendants<TextBlock>(root).Count(text => text.IsVisible && text.Text == vm.QuantitySummary) == 1,
            "adding the first product displays the detail actions and a single quantity summary in the heading");
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
        Check(grid.Columns.Select(column => column.Header).SequenceEqual(new[] { "商品コード・商品名", "単価", "数量", "小計", "操作" })
            && grid.Columns[3] is DataGridTextColumn { Binding: Binding subtotalBinding } && subtotalBinding.Path.Path == nameof(RegistrationLine.Amount),
            "detail columns present a pre-tax subtotal using the existing line amount");
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
        vm.SelectedProduct=source.LoadProducts()[1]; FlushBindings();
        Check(stockHint.IsVisible && stockHint.Text == "在庫がありません" && vm.CurrentStockDisplay == "0 個"
            && vm.CanAddProduct, "zero stock shows a warning without blocking addition");
        vm.SelectedProduct=source.LoadProducts()[2]; FlushBindings();
        Check(stockHint.IsVisible && stockHint.Text == "在庫情報が未登録です" && vm.CurrentStockDisplay == "未登録"
            && vm.CanAddProduct, "unregistered stock shows a warning without blocking addition");
        CheckOrderProductDetails(view, root, vm);
        vm.SelectedProduct=source.LoadProducts()[0]; vm.Quantity=vm.MaximumQuantity.ToString(CultureInfo.InvariantCulture); FlushBindings();
        Check(!plus.IsEnabled && minus.IsEnabled, "plus disables at existing amount-derived maximum");
        vm.Quantity="1"; vm.SelectedProduct=null;
        Check(!WaitFor(vm.TryApproveCloseAsync()), "unsaved changes confirmation preserves input when cancelled");
        Check(!Descendants<TextBlock>(root).Any(text => text.Text == "受注受付担当者" || text.Text == AppSession.Employee!.EmName)
            && !Descendants<TextBox>(root).Any(box => System.Windows.Automation.AutomationProperties.GetName(box) == "単価（参照専用）"),
            "the registration form omits the employee row and keeps the unit price read-only");
        foreach(var product in source.LoadProducts().Skip(1)) { vm.SelectedProduct=product; vm.AddLineCommand.Execute(null); }
        FlushBindings();
        var originalLines = vm.Lines.Select(line => (line.ProductId, line.Quantity, line.UnitPrice)).ToArray();
        var originalTotals = (vm.Subtotal, vm.Tax, vm.Total, vm.QuantitySummary);
        var originalHeader = (vm.SelectedClient, vm.SelectedOffice, vm.OrderDate, vm.DeliveryDate, vm.Remarks);
        CheckDeleteAllPrompt(host, deleteAll, confirm: false);
        Check(vm.Lines.Count == originalLines.Length && (vm.Subtotal, vm.Tax, vm.Total, vm.QuantitySummary) == originalTotals && !undo.IsEnabled,
            "Cancel in the registration confirmation leaves the details and undo state intact");
        CheckDeleteAllPrompt(host, deleteAll, confirm: true);
        FlushBindings();
        Check(vm.Lines.Count == 0 && vm.Subtotal == 0 && vm.Tax == 0 && vm.Total == 0 && vm.QuantitySummary == "0 品目 / 0 点"
            && (vm.SelectedClient, vm.SelectedOffice, vm.OrderDate, vm.DeliveryDate, vm.Remarks) == originalHeader
            && !vm.CanRegister && !deleteAll.IsVisible && undo.IsVisible && undo.IsEnabled,
            "confirmed registration removal clears only the editable lines and recalculates the totals");
        undo.Command.Execute(null); FlushBindings();
        Check(vm.Lines.Select(line => (line.ProductId, line.Quantity, line.UnitPrice)).SequenceEqual(originalLines)
            && (vm.Subtotal, vm.Tax, vm.Total, vm.QuantitySummary) == originalTotals && vm.CanRegister && deleteAll.IsEnabled && !undo.IsEnabled,
            "registration Undo restores the original line order, quantities, prices and totals");
        var aboveMaximum = (vm.Lines[0].MaximumQuantity + 1).ToString(CultureInfo.InvariantCulture);
        vm.Lines[0].QuantityText = aboveMaximum;
        vm.Lines[1].QuantityText = "0";
        CheckDeleteAllPrompt(host, deleteAll, confirm: true);
        undo.Command.Execute(null); FlushBindings();
        Check(vm.Lines.Count == originalLines.Length && vm.Lines[0].QuantityText == aboveMaximum && vm.Lines[1].QuantityText == "0"
            && !vm.CanRegister && !vm.HasUndo, "registration Undo preserves unfinished quantity edits and their validation");
        vm.Lines[0].Quantity = originalLines[0].Quantity;
        vm.Lines[1].Quantity = originalLines[1].Quantity;
        foreach(var (width,height) in new[] { (1280,720),(1366,768),(1600,900),(1920,1080) })
        {
            host.Width=width-56; host.Height=height-94; host.UpdateLayout(); FlushBindings();
            CheckManufacturerLayout(view);
            var footer=(FrameworkElement)view.FindName("RegistrationFooter");
            var totals=(FrameworkElement)view.FindName("OrderTotalsCard");
            var footerTop=footer.TransformToAncestor(root).Transform(new Point()).Y;
            var totalTop=totals.TransformToAncestor(root).Transform(new Point()).Y;
            Check(footerTop+footer.ActualHeight<=root.ActualHeight+1 && totalTop+totals.ActualHeight<=footerTop+1, "fixed footer and totals fit "+width+"x"+height);
            CheckDetailActionLayout(detailActions, grid, totals, root, $"registration {width}x{height}");
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
                CheckRegistrationRow(root, grid, vm);
                CheckRegistrationFooter(view, "desktop");
                var helper = (TextBlock)view.FindName("RegistrationFeedbackText");
                var cancel = (Button)view.FindName("CancelButton");
                Check(helper.IsVisible && helper.Text == "入力内容を確認して登録してください。"
                    && helper.TranslatePoint(new Point(helper.ActualWidth, 0), root).X < cancel.TranslatePoint(new Point(), root).X,
                    "the ready helper stays to the left of the grouped actions");
                Check(Descendants<TextBlock>(totals).Any(text => text.Text == vm.TaxLabel),
                    "the fixed totals card displays the tax label from the calculation rate");
            }
            Render(root,"registration-"+width+"x"+height);
        }
        host.Width=420; host.Height=420; host.UpdateLayout(); FlushBindings();
        CheckRegistrationFooter(view, "420-DIP pane");
        Check(!((TextBlock)view.FindName("RegistrationFeedbackText")).IsVisible, "the passive helper yields space on a narrow pane");
        var detailScroll = Descendants<ScrollViewer>(grid).Single(scroll => scroll.Name == "DG_ScrollViewer");
        Check(detailScroll.ScrollableWidth > 0 && grid.Columns[2].ActualWidth >= 108 && grid.Columns[4].ActualWidth >= 44,
            "the narrow detail table scrolls horizontally while preserving the quantity and remove control widths");
        var fixedFooter = (FrameworkElement)view.FindName("RegistrationFooter");
        var fixedTotals = (FrameworkElement)view.FindName("OrderTotalsCard");
        CheckDetailActionLayout(detailActions, grid, fixedTotals, root, "narrow registration");
        var actionsPosition = detailActions.TranslatePoint(new Point(), root);
        var footerPosition = fixedFooter.TranslatePoint(new Point(), root);
        var totalsPosition = fixedTotals.TranslatePoint(new Point(), root);
        inputScroller.ScrollToBottom(); grid.ScrollIntoView(vm.Lines[^1]); host.UpdateLayout(); FlushBindings();
        Check(inputScroller.VerticalOffset > 0 && grid.ItemContainerGenerator.ContainerFromItem(vm.Lines[^1]) != null
            && fixedTotals.TranslatePoint(new Point(), root) == totalsPosition && fixedFooter.TranslatePoint(new Point(), root) == footerPosition
            && detailActions.TranslatePoint(new Point(), root) == actionsPosition
            && footerPosition.Y + fixedFooter.ActualHeight <= root.ActualHeight + 1,
            "scrolling either pane at short height keeps the deletion bar, totals and footer fixed");
        Render(root, "registration-narrow-actions");
        CheckDeleteAllPrompt(host, deleteAll, confirm: true);
        Wait(Task.Delay(6500));
        Check(vm.Lines.Count == 0 && !vm.HasUndo && detailActions.Visibility == Visibility.Collapsed && !undo.IsVisible,
            "the empty detail footer collapses when the last deletion can no longer be undone");
        var previewProduct = vm.Products[0];
        vm.SelectedProduct = previewProduct;
        vm.Quantity = "2";
        vm.AddLineCommand.Execute(null);
        vm.SelectedProduct = previewProduct;
        host.Content = null; host.Close();
        CheckRegistrationShell(view, root, vm);
        view.Dispose();
        CheckRegistrationSubmission();
    }

    private static void CheckRegistrationLabels(FrameworkElement root)
    {
        var indicators = Descendants<TextBlock>(root).Where(text => text.Name == "RequiredFieldMarker").ToArray();
        var captions = indicators.Select(star => Descendants<TextBlock>(star.Parent).Single(text => text.Name == "RequiredFieldCaption").Text).ToArray();
        Check(indicators.Length == 5
            && captions.ToHashSet().SetEquals(new[] { "受注元顧客", "受注日", "納品希望日", "商品コード・商品名", "数量" })
            && indicators.All(star => star.Text == "*" && star.IsVisible && star.Foreground is SolidColorBrush { Color: var color }
                && color == Color.FromRgb(0xDC, 0x26, 0x26)),
            "all five required labels use one red asterisk and optional labels have none (" + string.Join(", ", captions) + ")");
        Check(!Descendants<TextBlock>(root).Any(text => text.IsVisible && text.Text == "受付営業所"),
            "the registration form omits the office field");
    }

    private static void CheckRegistrationShell(F_OrderRegistration view, FrameworkElement root, OrderRegistrationViewModel model)
    {
        root.DataContext = model;
        var shell = new F_Main
        {
            Width = 1440, Height = 960, Left = -30000, Top = -30000, WindowStyle = WindowStyle.None,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false, ShowActivated = false
        };
        var showBusinessView = typeof(F_Main).GetMethod("ShowBusinessView", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var status = (TextBlock)shell.FindName("status");
        var standardStatus = status.Text;
        var sidebar = (Border)shell.FindName("mainSidebar");
        var sidebarModel = (SidebarViewModel)sidebar.DataContext;
        var title = (TextBlock)shell.FindName("mainHeaderTitle");
        var icon = (LucideIcon)shell.FindName("orderHeaderIcon");
        var sessionPanel = (FrameworkElement)shell.FindName("orderEntrySession");
        var employee = AppSession.Employee!;
        try
        {
            ((Button)shell.FindName("homeButton")).Tag = null;
            sidebarModel.MenuItems.Single(entry => entry.Label == "受注管理").IsActive = true;
            showBusinessView.Invoke(shell, ["受注登録", root]);
            shell.Show(); shell.UpdateLayout(); FlushBindings();
            var sessionText = Descendants<TextBlock>(sessionPanel).Where(text => text.IsVisible).Select(text => text.Text).ToArray();
            Check(title.Text == "受注登録" && icon.IsVisible
                && sessionPanel.IsVisible && !status.IsVisible
                && sessionText is [var name, var role, var office] && name.EndsWith(employee.EmName, StringComparison.Ordinal)
                && role == employee.Po.PoName && office == employee.So.SoName
                && Equals(sidebar.Background, Application.Current.FindResource("ThemeOrderEntryNavy")),
                "registration header shows the signed-in employee, role and office");
            Check(model.SelectedOffice?.SoId == employee.SoId && !((TextBlock)view.FindName("OfficeErrorText")).IsVisible,
                "the session office is assigned automatically without adding a field to the registration form");
            ((ScrollViewer)view.FindName("InputScroll")).ScrollToTop();
            var grid = (DataGrid)view.FindName("OrderItemsGrid");
            grid.ScrollIntoView(model.Lines[0], grid.Columns[0]);
            shell.UpdateLayout(); FlushBindings();
            Render((FrameworkElement)shell.Content, "registration-shell-desktop");
            shell.Width = shell.MinWidth; shell.Height = shell.MinHeight;
            shell.UpdateLayout(); FlushBindings();
            CheckRegistrationFooter(view, "minimum shell size with sidebar expanded");
            Render((FrameworkElement)shell.Content, "registration-shell-minimum");
            sidebarModel.ToggleSidebarCommand.Execute(null);
            shell.UpdateLayout(); FlushBindings();
            CheckRegistrationFooter(view, "minimum shell size with sidebar collapsed");
            showBusinessView.Invoke(shell, ["受注管理", new Grid()]);
            Check(status.Text == standardStatus && status.IsVisible && !sessionPanel.IsVisible,
                "other pages restore the existing session header");
        }
        finally { shell.Close(); }
    }

    private static void CheckOrderProductDetails(F_OrderRegistration view, FrameworkElement root, OrderRegistrationViewModel vm)
    {
        var productInput = (OrderProductInput)view.FindName("ProductInput");
        var open = (Button)productInput.FindName("ProductDetailsButton");
        var overlay = (OrderProductDetailsOverlay)view.FindName("ProductDetailsOverlay");
        var card = (Border)overlay.FindName("ProductDetailsCard");
        var scroller = (ScrollViewer)overlay.FindName("ProductDetailsScroller");
        var close = (Button)overlay.FindName("CloseProductDetailsButton");
        var dismiss = (Button)overlay.FindName("DismissProductDetailsButton");
        var stock = (TextBlock)overlay.FindName("ProductDetailStock");
        var price = (TextBlock)overlay.FindName("ProductDetailPrice");
        var thumbnail = (Image)overlay.FindName("ProductThumbnail");
        var placeholder = (StackPanel)overlay.FindName("ProductThumbnailPlaceholder");
        var click = typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var before = (vm.SelectedClient, vm.Remarks, vm.Quantity, vm.Lines.Count);
        foreach (var (id, stockText, color) in new[]
        {
            (1, "5 個", Color.FromRgb(0x15, 0x80, 0x3D)),
            (4, "5 個", Color.FromRgb(0xB4, 0x53, 0x09)),
            (2, "0 個", Color.FromRgb(0xDC, 0x26, 0x26)),
            (3, "取得できません", Color.FromRgb(0x64, 0x74, 0x8B))
        })
        {
            vm.SelectedProduct = null;
            var product = vm.Products.Single(item => item.PrId == id);
            vm.SelectedProduct = product;
            FlushBindings();
            click.Invoke(open, null);
            FlushBindings();
            root.UpdateLayout();
            var text = Descendants<TextBlock>(card).Select(element => element.Text).ToArray();
            Check(overlay.IsVisible && ReferenceEquals(vm.ProductDetails, product) && !vm.CanRegister
                && !Descendants<TextBox>(card).Any()
                && new[] { product.ProductCode, product.ProductName, product.MakerDisplayName, product.CategoryName }.All(text.Contains)
                && price.Text == $"¥{product.UnitPrice:N0}" && price.FontSize == 24,
                "product dialog displays the selected read-only catalog data and price");
            Check(stock.Text == stockText && stock.Foreground is SolidColorBrush brush && brush.Color == color
                && placeholder.IsVisible == (thumbnail.Source == null) && thumbnail.Stretch == Stretch.Uniform,
                "product dialog shows the correct stock color and preserves image proportions or shows the placeholder");

            if (id == 1)
            {
                Check(Math.Abs(card.ActualWidth - 440) < 1 && scroller.ScrollableHeight < 1,
                    "product dialog fits normal content without scrolling");
                Render(root, "registration-product-details");
            }

            if (id == 2)
                close.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(close), Environment.TickCount, Key.Escape)
                    { RoutedEvent = Keyboard.KeyDownEvent });
            else
                click.Invoke(id == 4 ? dismiss : close, null);
            FlushBindings();
            Check(!overlay.IsVisible && before == (vm.SelectedClient, vm.Remarks, vm.Quantity, vm.Lines.Count),
                "closing the dialog preserves the order entry");
        }
    }

    private static void CheckRegistrationSubmission()
    {
        var source=new RegistrationSubmissionSource();
        var listReturns=0; var nextOrderStarts=0;
        using var vm=new OrderRegistrationViewModel(()=>listReturns++,source:source,startNextOrder:()=>nextOrderStarts++);
        vm.Load();
        var otherOffice = source.LoadOffices().First();
        Check(otherOffice.SoId != AppSession.Employee!.SoId && !vm.SelectOfficeCommand.CanExecute(otherOffice),
            "the registration model disables office selection even for administrators");
        vm.SelectedOffice = otherOffice;
        vm.OfficeSearch = otherOffice.SoName;
        vm.SelectedClient=source.LoadClients(1)[0]; vm.SelectedProduct=source.LoadProducts()[0]; vm.AddLineCommand.Execute(null);
        vm.Remarks="最初の受注";
        var view=new F_OrderRegistration { DataContext=vm };
        var root=(FrameworkElement)view.Content; view.Content=null;
        var host=new Window { Content=root, DataContext=vm, Width=1224, Height=626, Left=-30000, Top=-30000,
            WindowStyle=WindowStyle.None, ShowInTaskbar=false, ShowActivated=false };
        host.Show(); host.UpdateLayout(); FlushBindings();
        var registerButton=(Button)view.FindName("RegisterButton");
        var footerFeedback = (TextBlock)view.FindName("RegistrationFeedbackText");
        host.Width=420; host.UpdateLayout(); FlushBindings();
        vm.RegisterCommand.Execute(null);
        for(var i=0;i<100 && source.Calls==0;i++) Wait(Task.Delay(10));
        FlushBindings();
        Check(vm.IsBusy && !vm.RegisterCommand.CanExecute(null) && !WaitFor(vm.TryApproveCloseAsync())
            && registerButton.IsVisible && registerButton.Content?.ToString()=="受注を登録" && !registerButton.IsEnabled
            && footerFeedback.IsVisible && footerFeedback.Text=="受注を登録しています…",
            "registration request keeps the register label while disabling duplicate submit and navigation");
        host.Width=1224; host.UpdateLayout(); FlushBindings();
        vm.RegisterCommand.Execute(null); source.AllowCompletion.Set();
        for(var i=0;i<200 && vm.IsBusy;i++) Wait(Task.Delay(10));
        Check(source.Calls==1 && vm.IsSuccess && vm.IsCompletionDialogVisible && vm.RegisteredOrderNumber=="ORD-000237", "single successful submission preserves existing completion and numbering");
        Check(source.EmployeeId == AppSession.Employee!.EmId && source.OfficeId == AppSession.Employee.SoId,
            "registration sends the signed-in employee and assigned office IDs to the data source");
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
        var productInput = (OrderProductInput)view.FindName("ProductInput");
        var quantityInput=(QuantityUpDown)productInput.FindName("QuantityInput");
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
        var failedSource=new RegistrationSubmissionSource { Fail=true }; failedSource.AllowCompletion.Set();
        using var failed=new OrderRegistrationViewModel(()=>{},source:failedSource);
        failed.Load(); failed.SelectedClient=failedSource.LoadClients(1)[0]; failed.SelectedProduct=failedSource.LoadProducts()[0]; failed.AddLineCommand.Execute(null);
        host.DataContext=failed; host.Width=520; host.UpdateLayout(); FlushBindings();
        failed.RegisterCommand.Execute(null);
        for(var i=0;i<200 && failed.IsBusy;i++) Wait(Task.Delay(10));
        Check(!failed.IsSuccess && failed.Lines.Count==1 && failed.RegisterCommand.CanExecute(null)
            && footerFeedback.IsVisible && footerFeedback.Text==failed.Feedback && footerFeedback.Text=="検証用の登録失敗",
            "registration failure remains visible on a narrow footer and restores retry state");
        CheckRegistrationSessionOffice(view, host);
        host.Close(); view.Dispose();
    }

    private static void CheckRegistrationSessionOffice(F_OrderRegistration view, Window host)
    {
        var previous = AppSession.Employee;
        try
        {
            foreach (var officeId in new[] { 0, 8, 99 })
            {
                SetRole("管理者", office: officeId);
                var source = new RegistrationSubmissionSource();
                using var model = new OrderRegistrationViewModel(() => { }, source: source);
                model.Load();
                model.SelectedOffice = source.LoadOffices()[0];
                model.SelectedProduct = source.LoadProducts()[0];
                model.AddLineCommand.Execute(null);
                host.DataContext = model;
                host.UpdateLayout(); FlushBindings();
                model.RegisterCommand.Execute(null);
                var error = (TextBlock)view.FindName("OfficeErrorText");
                Check(model.SelectedOffice == null && model.OfficeDisplay == "未設定" && error.IsVisible
                    && error.Text == "所属営業所を確認できません。" && !((Button)view.FindName("RegisterButton")).IsEnabled && source.Calls == 0,
                    "missing, inactive and unknown session offices block submission without a fallback: " + officeId);
            }

            AppSession.Employee = previous;
            var header = new OrderDraftHeader(new(9, "以前の営業所"), new(77, "以前の顧客"), new(DateTime.Today, ""),
                new(DateTime.Today.AddDays(7), ""), "以前の備考", new(null, ""), null, "1");
            var token = new OrderDraftToken(Guid.NewGuid(), BitConverter.GetBytes(1L));
            var draft = new OrderDraftSnapshot(token, DateTimeOffset.Now, new(header, [new(1, "PR-0001", "キーボード", 2000, 2, "2")]));
            using var reopened = new OrderRegistrationViewModel(() => { }, source: new RegistrationSubmissionSource(), draft: draft);
            reopened.Load();
            Check(reopened.IsDraft && reopened.SelectedOffice?.SoId == previous!.SoId && reopened.SelectedClient == null
                && reopened.CustomerSearch == "以前の顧客" && reopened.Remarks == "以前の備考" && reopened.Lines.Single().Quantity == 2 && !reopened.CanRegister,
                "an older draft keeps the inputs and uses the assigned office with customer validation");

            SetRole("管理者", office: 9);
            using var update = new OrderUpdateViewModel(Sample(), _ => { }, source: new ControlledSource(), lookups: new RegistrationSubmissionSource());
            update.Load();
            Check(update.CanEdit && update.Entry.SelectedOffice?.SoId == 1,
                "the update model continues to use the existing order office when the administrator belongs to a different office");
        }
        finally { AppSession.Employee = previous; }
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
        RenderLayout(420, 400, "update-narrow-footer", true, embedded: true);
    }

    private static void CheckUpdateFooter(F_OrderUpdate editor, string layout)
    {
        var footer = (Border)editor.FindName("Footer");
        var cancel = (Button)editor.FindName("CancelButton");
        var save = (Button)editor.FindName("SaveButton");
        Rect Bounds(Button button) => button.TransformToAncestor(footer).TransformBounds(new Rect(button.RenderSize));
        var cancelBounds = Bounds(cancel);
        var saveBounds = Bounds(save);
        var gap = footer.ActualWidth < 480 ? 10 : 12;
        Check(Descendants<Button>(footer).SequenceEqual(new[] { cancel, save })
            && Equals(cancel.Content, "キャンセル") && Equals(save.Content, "変更を保存")
            && Math.Abs(cancelBounds.Height - 42) < 1 && Math.Abs(saveBounds.Height - 42) < 1
            && cancel.IsVisible && save.IsVisible && cancelBounds.Left >= footer.Padding.Left - 1
            && Math.Abs(footer.ActualWidth - saveBounds.Right - footer.Padding.Right) < 1
            && cancelBounds.Top >= 0 && saveBounds.Bottom <= footer.ActualHeight + 1
            && Math.Abs(saveBounds.Left - cancelBounds.Right - gap) < 1 && Math.Abs(cancelBounds.Top - saveBounds.Top) < 1,
            "Cancel and Save stay fixed, fully visible and grouped on the right at " + layout);
    }

    private static void RenderLayout(double width, double height, string name, bool saveImage, bool invalid = false,int count=30, bool embedded = false)
    {
        var editor = new F_OrderUpdate(Sample(count),updateSource:new ControlledSource(),lookupSource:new UpdateLookupSource());
        var vm = (OrderUpdateViewModel)editor.DataContext;
        if (invalid) vm.Lines[0].Quantity = "-1";
        if(count==2) vm.Load();
        var root = (FrameworkElement)editor.DetachView();
        var header = (FrameworkElement)editor.FindName("Header");
        if (!embedded) header.Visibility = Visibility.Visible;
        var host = new Window { Content = root, DataContext = vm, Width = width, Height = height, Left = -30000, Top = -30000,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false };
        host.Show(); host.UpdateLayout();
        host.UpdateLayout(); Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        var footer = (FrameworkElement)editor.FindName("Footer");
        var save = (Button)editor.FindName("SaveButton");
        var grid = (DataGrid)editor.FindName("ItemsGrid");
        var scroller = (ScrollViewer)editor.FindName("ContentScroller");
        Check((embedded ? !header.IsVisible : Math.Abs(header.ActualHeight-64)<1)
            && footer.ActualHeight>=64 && (width<800 || footer.ActualHeight<=65),
            "fixed header and footer use the shared dimensions " + name);
        if (!embedded)
        {
            var sessionText = Descendants<TextBlock>(header).Where(text => text.IsVisible && text.Name.StartsWith("Session", StringComparison.Ordinal));
            Check(sessionText.Select(text => text.Text).SequenceEqual(new[] { vm.HeaderSession.EmployeeName, vm.HeaderSession.Role, vm.HeaderSession.OfficeName }),
                "standalone update displays the same employee, role and branch header " + name);
        }
        CheckUpdateFooter(editor, name);
        var buttonPoint = save.TransformToAncestor(root).Transform(new Point());
        Check(buttonPoint.X >= 0 && buttonPoint.X + save.ActualWidth <= root.ActualWidth + 1
            && buttonPoint.Y + save.ActualHeight <= root.ActualHeight + 1, "save button visible " + name);
        var overflow=Descendants<ScrollViewer>(root).Where(scroll=>scroll.ScrollableWidth>=1).ToArray();
        Check(overflow.All(scroll => scroll.Name == "DG_ScrollViewer" && IsWithin(scroll, grid)),
            "only the detail grid can scroll horizontally " + name + " "
            + string.Join(", ", overflow.Select(scroll => $"{scroll.Name} ({scroll.ViewportWidth:F0}/{scroll.ExtentWidth:F0})")));
        Check(Descendants<ScrollViewer>(grid).Single(scroll => scroll.Name == "DG_ScrollViewer").ViewportHeight > 0,
            "the detail list keeps visible space below its header " + name);
        var verticalScrollers = Descendants<ScrollViewer>(root)
            .Where(scroll => scroll.IsVisible && scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible).ToArray();
        Check(verticalScrollers.Length <= 2, "only the independent input and item panes show scrollbars " + name + " "
            + string.Join(", ", verticalScrollers.Select(scroll => $"{scroll.Name} ({scroll.ViewportHeight:F0}/{scroll.ExtentHeight:F0})")));
        var firstRow = grid.ItemContainerGenerator.ContainerFromItem(vm.Lines[0]) as DataGridRow;
        if (firstRow != null)
        {
            Check(Descendants<QuantityUpDown>(firstRow).All(input => input.ActualHeight >= 34
                && input.TransformToAncestor(firstRow).Transform(new Point()).Y + input.ActualHeight <= firstRow.ActualHeight + 1), "numeric inputs fit row " + name);
            Check(Descendants<QuantityUpDown>(firstRow).Count() == 1
                && Descendants<TextBlock>(firstRow).Any(text => System.Windows.Automation.AutomationProperties.GetName(text) == "単価"
                    && text.Text == "¥1,234"), "unit price displayed without an input control " + name);
        }
        if (saveImage) Render(root, name);
        var totals = (FrameworkElement)editor.FindName("AmountSummary");
        var itemsHeading = (FrameworkElement)editor.FindName("ItemsHeading");
        var detailActions = (FrameworkElement)editor.FindName("DetailActions");
        var footerPosition = footer.TranslatePoint(new Point(), root);
        var totalsPosition = totals.TranslatePoint(new Point(), root);
        var headingPosition = itemsHeading.TranslatePoint(new Point(), root);
        var actionsPosition = detailActions.TranslatePoint(new Point(), root);
        CheckDetailActionLayout(detailActions, grid, totals, root, name);
        var panels = (FrameworkElement)editor.FindName("RegistrationPanels");
        Check(panels.ActualHeight >= 420 || Descendants<TextBlock>(totals).All(text => text.Text != vm.RoundingNotice || !text.IsVisible),
            "the rounding explanation leaves more detail-list room at short heights " + name);
        grid.ScrollIntoView(vm.Lines[^1]); host.UpdateLayout();
        Check(grid.ItemContainerGenerator.ContainerFromItem(vm.Lines[^1])!=null,"last order item accessible "+name);
        scroller.ScrollToBottom();host.UpdateLayout();
        var remarks=(FrameworkElement)editor.FindName("RemarksInput");
        var point=remarks.TransformToAncestor(scroller).Transform(new Point());
        Check(point.Y>=-1 && point.Y+remarks.ActualHeight<=scroller.ActualHeight+1,"remarks accessible in independent input pane "+name);
        Check(totalsPosition.Y + totals.ActualHeight <= footerPosition.Y + 1
            && totals.TranslatePoint(new Point(), root) == totalsPosition && footer.TranslatePoint(new Point(), root) == footerPosition
            && itemsHeading.TranslatePoint(new Point(), root) == headingPosition && detailActions.TranslatePoint(new Point(), root) == actionsPosition,
            "detail heading, deletion bar, totals and footer remain fixed while scrolling " + name);
        Check(editor.InputBindings.OfType<KeyBinding>().Any(key => key.Key == Key.Escape), "Escape close route " + name);
        if (invalid) vm.Lines[0].Quantity = "2";
        host.Close(); editor.Dispose();
    }

    private static (Button DeleteAll, Button Undo) DetailActionButtons(FrameworkElement actions)
    {
        var buttons = Descendants<Button>(actions).ToArray();
        return (buttons.Single(button => Equals(button.Content, "すべて削除")),
            buttons.Single(button => Equals(button.Content, "元に戻す")));
    }

    private static void CheckDetailActionLayout(FrameworkElement actions, DataGrid grid, FrameworkElement totals, FrameworkElement root, string name)
    {
        var (deleteAll, undo) = DetailActionButtons(actions);
        var actionsPosition = actions.TranslatePoint(new Point(), root);
        var totalsPosition = totals.TranslatePoint(new Point(), root);
        var gridBottom = grid.TranslatePoint(new Point(0, grid.ActualHeight), root).Y;
        Check(actions.IsVisible && Math.Abs(deleteAll.ActualHeight - undo.ActualHeight) < 1
            && Math.Abs(undo.TranslatePoint(new Point(), actions).X - deleteAll.ActualWidth - 10) < 1
            && undo.TranslatePoint(new Point(undo.ActualWidth, 0), actions).X <= actions.ActualWidth + 1
            && actionsPosition.Y >= gridBottom - 0.5
            && actionsPosition.Y + actions.ActualHeight <= totalsPosition.Y + 0.5
            && actionsPosition.X + actions.ActualWidth <= totalsPosition.X + totals.ActualWidth + 0.5,
            "the Delete All and Undo bar fits below the detail grid and above the totals " + name);
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
            Check(!Save(original,"全商品削除後の差し替えをロールバック",edits.Where(line => !line.DetailId.HasValue).ToArray(),
                customerId, orderDate, deliveryDate).Succeeded, "SQL failure rejects replacing all saved details");
            using var db=TestContext();var order=db.TOrders.Include(order=>order.TOrderDetails).Single();
            Check(order.ClId==original.CustomerId && order.OrDate==original.OrderDate && order.TOrderDetails.Count==original.Lines.Count
                && original.Lines.All(line => order.TOrderDetails.Any(detail => detail.OrDetailId == line.DetailId
                    && detail.OrQuantity == line.Quantity && detail.OrTotalPrice == line.Subtotal))
                && db.OrderOperationHistories.Count()==historyCount,
                "failed combined update rolls back deleted and added rows, customer, dates and audit atomically");
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
                UnitPrice = id == 1 ? 1234 : 2000, CategoryName = "入力機器", Stock = id == 3 ? null : id == 2 ? 0 : 5,
                SafetyStock = id == 4 ? 6 : 5
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
        public IReadOnlyList<MSalesOffice> LoadOffices() => [.. catalog.LoadOffices(), new MSalesOffice { SoId = 2, SoName = "南営業所" }];
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
        public int EmployeeId { get; private set; }
        public int OfficeId { get; private set; }
        public bool Fail;
        public IReadOnlyList<MSalesOffice> LoadOffices() =>
        [
            new MSalesOffice { SoId = 9, SoName = "南営業所" },
            new MSalesOffice { SoId = 1, SoName = "北大阪営業所" },
            new MSalesOffice { SoId = 8, SoName = "休止中の営業所", SoFlag = 1 }
        ];
        public IReadOnlyList<MClient> LoadClients(int officeId)=>[new MClient { ClId=1,ClName="INATUGI",SoId=officeId }];
        public IReadOnlyList<RegistrationProduct> LoadProducts()=>[new RegistrationProduct { PrId=1,ProductCode="PR-0001",ProductName="キーボード",UnitPrice=2000,Stock=0 }];
        public bool Register(int employeeId,int customerId,int officeId,DateTime orderDate,DateTime deliveryDate,
            string remarks,IReadOnlyCollection<OrderDetailUpdate> details,out int orderId,out string message)
        {
            EmployeeId = employeeId;
            OfficeId = officeId;
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
