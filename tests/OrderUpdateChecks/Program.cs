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

internal static class Program
{
    private static int checks;
    private static readonly List<string> bindingErrors = [];
    private static string output = "";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
            output = Path.GetFullPath(args.FirstOrDefault(arg => !arg.StartsWith("--")) ?? "build_verify/order-update-preview");
            Directory.CreateDirectory(output);
            var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingListener());
            SetRole("管理者");
            CheckSavedOrderPolicy();
            CheckChumonStockDisplay();
            if (!args.Contains("--database-only")) CheckRegistrationInputs();
            if (!args.Contains("--skip-database")) CheckDatabase();
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

    private static void CheckSavedOrderPolicy()
    {
        foreach (var role in new[] { "管理者", "営業", "物流" })
        {
            SetRole(role);
            var saved = Sample();
            Check(!BusinessService.UpdateOrder(saved.OrderId, 1, 1, 1, 1, saved.OrderDate, null, 0, 0, null,
                "変更", [], saved, out var reason) && reason.Contains("保存済み"), role + " cannot edit a saved order");
        }
        SetRole("管理者");
    }

    private static void CheckChumonStockDisplay()
    {
        var line = new OrderLineView(1, "P-000001", "商品", 5, 1, 5,
            new InventoryStockLookup(InventoryStockState.Available, 6), RequiredStockQuantity: 10, Number: 1);
        Check(line.CurrentStockText == "6" && line.Availability == "在庫不足",
            "order detail checks the total quantity for duplicate product lines");
        Check((line with { RequiredStockQuantity = 5 }).Availability == "在庫あり",
            "order detail labels sufficient stock");
        Check((line with { Stock = new(InventoryStockState.Available, 0) }).Availability == "在庫0",
            "order detail distinguishes an empty shelf from an unavailable balance");
        var duplicate = line with { Stock = new(InventoryStockState.DuplicateActiveRecords) };
        Check(duplicate.CurrentStockText == "—" && duplicate.Availability == "在庫行重複",
            "order detail does not show a summed balance for duplicate active stock rows");
        Check((line with { Stock = new(InventoryStockState.MissingRecord) }).Availability == "在庫未登録"
            && (line with { Stock = new(InventoryStockState.InactiveRecord) }).Availability == "有効在庫なし"
            && (line with { Stock = new(InventoryStockState.InvalidQuantity) }).Availability == "在庫数不正"
            && (line with { Stock = new(InventoryStockState.QueryFailed) }).Availability == "照会失敗",
            "order detail distinguishes missing, inactive, invalid and failed stock lookups");
    }

    private static void Wait(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        if (!task.IsCompleted) throw new TimeoutException();
        task.GetAwaiter().GetResult();
    }

    private static void CheckRegistrationInputs()
    {
        var source = new QuantityRegistrationSource();
        using var vm = new OrderRegistrationViewModel(() => { }, () => false, source);
        vm.Load(); vm.SelectedClient=source.LoadClients(1)[0]; vm.Remarks="入力内容を保持";
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
        Check(editor.Text=="1" && !minus.IsEnabled && plus.IsEnabled, "quantity default/minimum state");
        Check(plus.TransformToAncestor(editor).Transform(new Point()).Y < minus.TransformToAncestor(editor).Transform(new Point()).Y, "plus is above minus");
        plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FlushBindings();
        Check(vm.Quantity=="2" && minus.IsEnabled, "plus updates quantity without Enter");
        minus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FlushBindings();
        Check(vm.Quantity=="1" && !minus.IsEnabled, "minus returns to minimum");
        vm.SelectedProduct=source.LoadProducts()[0]; input.Text="37"; FlushBindings();
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
        Check(vm.Lines.Single().Quantity==37 && vm.Lines.Single().UnitName=="個" && vm.Quantity=="1" && vm.SelectedProduct==null && vm.UnitPriceDisplay=="商品選択後に表示", "addition retains unit and resets only product input");
        Check(vm.SelectedClient!=null && vm.Remarks=="入力内容を保持" && vm.CanRegister, "header and remarks retained; stock shortage can register");
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
        var before=(vm.SelectedClient,vm.Remarks,vm.Quantity,vm.Lines.Count);
        vm.ShowProductDetailsCommand.Execute(null); FlushBindings();
        Check(vm.IsProductDetailsVisible && vm.ProductDetails?.PrId==3 && !vm.CanRegister, "read-only product details opens for selected product");
        Render(root,"registration-product-details");
        vm.EscapeCommand.Execute(null); FlushBindings();
        Check(!vm.IsProductDetailsVisible && before==(vm.SelectedClient,vm.Remarks,vm.Quantity,vm.Lines.Count), "closing product details preserves entry data");
        vm.SelectedProduct=source.LoadProducts()[0]; vm.Quantity=vm.MaximumQuantity.ToString(CultureInfo.InvariantCulture); FlushBindings();
        Check(!plus.IsEnabled && minus.IsEnabled, "plus disables at existing amount-derived maximum");
        vm.Quantity="1"; vm.SelectedProduct=null;
        Check(!vm.TryApproveClose(), "unsaved changes confirmation preserves input when cancelled");
        Check(!Descendants<TextBox>(root).Any(box => System.Windows.Automation.AutomationProperties.GetName(box) is "受注受付担当者" or "単価（参照専用）"), "employee and unit price are borderless text displays");
        foreach(var product in source.LoadProducts().Skip(1)) { vm.SelectedProduct=product; vm.AddLineCommand.Execute(null); }
        foreach(var (width,height) in new[] { (1280,720),(1366,768),(1600,900),(1920,1080) })
        {
            host.Width=width-56; host.Height=height-94; host.UpdateLayout(); FlushBindings();
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
        using var vm=new OrderRegistrationViewModel(()=>listReturns++,()=>false,source,startNextOrder:()=>nextOrderStarts++);
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
        Check(vm.IsBusy && !vm.RegisterCommand.CanExecute(null) && !vm.TryApproveClose()
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
        using var failed=new OrderRegistrationViewModel(()=>{},()=>false,failedSource);
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

    private static void FlushBindings() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));

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
            int orderId;
            int customerOffice;
            using (var db = TestContext())
            {
                var actor = db.MEmployees.Include(x => x.Po).Include(x => x.So).Single();
                AppSession.Employee = actor;
                var customer = db.MClients.Single();
                customerOffice = customer.SoId;
                Check(actor.SoId != customerOffice, "administrator belongs to another office");
                var line = new OrderDetailUpdate(null, db.MProducts.Single().PrId, 2, 30);
                Check(BusinessService.CreateOrderCore(db, actor.EmId, customer.ClId, customerOffice, DateTime.Today,
                    " 山田様 ", [line], out orderId, out var reason), "cross-office registration: " + reason);
            }
            using (var db = TestContext())
            {
                Check(db.TOrders.Single(x => x.OrId == orderId).ClCharge == "山田様", "customer contact stored");
                Check(BusinessService.ConfirmOrderCore(db, orderId, 1, out var reason), "cross-office confirmation: " + reason);
            }
            using (var db = TestContext())
            {
                Check(!BusinessService.ConfirmOrderCore(db, orderId, 1, out _), "duplicate confirmation denied");
                var saved = db.TOrders.Single(x => x.OrId == orderId);
                var next = db.TChumons.Include(x => x.TChumonDetails).Single(x => x.OrId == orderId);
                Check(saved.EmId == 1 && saved.OrStateFlag == 1 && next.SoId == customerOffice && next.EmId == null
                    && next.TChumonDetails.Single().ChQuantity == 2, "one pending order retains registered office and lines");
            }
            using (var db = TestContext())
            {
                var employee = db.MEmployees.Single();
                employee.EmFlag = 2;
                db.SaveChanges();
            }
            using (var db = TestContext())
            {
                var customer = db.MClients.Single();
                var line = new OrderDetailUpdate(null, db.MProducts.Single().PrId, 1, 30);
                Check(!BusinessService.CreateOrderCore(db, 1, customer.ClId, customer.SoId, DateTime.Today,
                    null, [line], out _, out var reason) && reason.Contains("権限"),
                    "deactivated employee cannot register an order using their existing session");
                db.MEmployees.Single().EmFlag = 0;
                db.SaveChanges();
            }

            int inboundId;
            using (var db = TestContext())
            {
                var product = db.MProducts.Single();
                var receipt = new TWarehousing
                {
                    Ha = new THattyu
                    {
                        MaId = product.MaId,
                        EmId = 1,
                        RegisteredOfficeId = customerOffice,
                        HaDate = DateTime.Today,
                        WaWarehouseFlag = 1,
                        THattyuDetails = [new THattyuDetail { PrId = product.PrId, HaQuantity = 5 }]
                    },
                    WaShelfFlag = 0,
                    TWarehousingDetails = [new TWarehousingDetail { PrId = product.PrId, WaQuantity = 5 }]
                };
                db.TWarehousings.Add(receipt);
                db.SaveChanges();
                inboundId = receipt.WaId;
            }

            using (var db = TestContext())
            {
                var client = db.MClients.Single();
                client.SoId = AppSession.Employee!.SoId;
                var product = db.MProducts.Include(x => x.Ma).Single();
                var originalMaker = product.Ma;
                product.Ma = new MMaker
                {
                    MaName = "変更後メーカー",
                    MaAddress = originalMaker.MaAddress,
                    MaPhone = originalMaker.MaPhone,
                    MaPostal = originalMaker.MaPostal,
                    MaFax = originalMaker.MaFax
                };
                db.SaveChanges();
            }

            int outboundId;
            using (var db = TestContext())
            {
                var productId = db.MProducts.Single().PrId;
                var hidden = ProductService.Hide(db, 1, productId, "新規受注での使用を停止");
                Check(hidden.Succeeded, "hide a product after order acceptance and warehouse arrival: " + hidden.Message);
                var customerId = db.MClients.Single().ClId;
                var hiddenCustomer = CustomerService.Hide(db, 1, customerId, "新規受注での使用を停止");
                Check(hiddenCustomer.Succeeded, "hide a customer after order acceptance: " + hiddenCustomer.Message);
            }
            using (var db = TestContext())
            {
                var order = db.TChumons.Single(x => x.OrId == orderId);
                Check(BusinessService.ConfirmChumonCore(db, order.ChId, 1, out var reason, out var generatedId),
                    "confirm a linked order after product visibility changes: " + reason);
                outboundId = generatedId!.Value;
            }
            using (var db = TestContext())
            {
                var confirmed = OutboundService.ConfirmInContext(db, 1, outboundId);
                Check(confirmed.Succeeded && db.TStocks.Single().StQuantity == 48
                    && db.TArrivals.Count(x => x.OrId == orderId && x.SoId == customerOffice) == 1,
                    "outbound retains the order office after the customer moves without deducting stock twice: " + confirmed.Message);
            }
            using (var db = TestContext())
            {
                var received = WarehousingService.ConfirmShelfInContext(db, 1, inboundId, expected: null);
                Check(received.Succeeded && db.TStocks.Single().StQuantity == 53
                    && db.TWarehousings.Any(x => x.WaId == inboundId && x.WaShelfFlag == 1
                        && x.Ha.MaId != db.MProducts.Select(product => product.MaId).First()),
                    "shelving uses the purchase maker after the product maker changes: " + received.Message);
            }
            using (var db = TestContext())
            {
                Check(!WarehousingService.ConfirmShelfInContext(db, 1, inboundId, expected: null).Succeeded
                    && db.TStocks.Single().StQuantity == 53,
                    "repeating shelf confirmation does not add stock again");
            }
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
    private static void Seed(SalesManagementContext db)
    {
        var office = new MSalesOffice { SoName="北大阪営業所" };
        var position = new MPosition { PoName="管理者" };
        var employee = new MEmployee { EmId=1, EmName="高谷春男", EmHiredate=DateTime.Today, So=new MSalesOffice { SoName="本社" }, Po=position, EmPassword="test" };
        var customer = new MClient { ClName="INATUGI", So=office };
        var major = new MMajorClassification { McName="電子機器" };
        var small = new MSmallClassification { ScName="入力機器", Mc=major };
        var maker = new MMaker { MaName="テストメーカー" };
        var product = new MProduct { PrName="キーボード", Price=9999, Sc=small, Ma=maker, PrReleaseDate=DateTime.Today };
        var order = new TOrder { So=office, Em=employee, Cl=customer, OrDate=DateTime.Today, OrStateFlag=1,
            TOrderDetails=[new TOrderDetail { Pr=product, OrQuantity=3, OrTotalPrice=100 }] };
        db.Add(order); db.Add(new TStock { Pr=product, StQuantity=50 });
        foreach (var entry in db.ChangeTracker.Entries())
        foreach (var property in entry.Properties.Where(property => property.Metadata.ClrType == typeof(string) && !property.Metadata.IsNullable && property.CurrentValue == null))
            property.CurrentValue = "test";
        db.SaveChanges();
    }

    private sealed class QuantityRegistrationSource : IOrderRegistrationDataSource
    {
        public IReadOnlyList<MSalesOffice> LoadOffices() => [new MSalesOffice { SoId=1, SoName="北大阪営業所" }];
        public IReadOnlyList<MClient> LoadClients(int officeId) => [new MClient { ClId=1, ClName="INATUGI", SoId=officeId }];
        public IReadOnlyList<RegistrationProduct> LoadProducts() => Enumerable.Range(1,35).Select(id=>new RegistrationProduct
        { PrId=id, ProductCode=$"PR-{id:0000}", ProductName=id==1 ? "キーボード" : $"検証用商品 {id:00}", UnitPrice=id==1 ? 1234 : 2000,
          CategoryName="入力機器", Stock=id==3 ? null : id==2 ? 0 : 5 }).ToArray();
        public bool Register(int employeeId, int customerId, int officeId, DateTime orderDate, DateTime deliveryDate,
            string remarks, IReadOnlyCollection<OrderDetailUpdate> details, out int orderId, out string message) => throw new InvalidOperationException("UI checks must not write to a database.");
        public OrderDetailSnapshot? LoadRegisteredOrder(int orderId) => null;
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
