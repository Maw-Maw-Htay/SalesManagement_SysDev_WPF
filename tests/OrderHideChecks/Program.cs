using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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
    private static readonly string output = Path.GetFullPath("build_verify/order-hide-preview");
    private static string testConnection = "";
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
            Directory.CreateDirectory(output);
            var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingListener());
            SetRole("管理者");
            var detailsOnly=args.Contains("--details-only");
            var readOnlyDatabase=args.Contains("--database-read-only");
            if(readOnlyDatabase) CheckExistingDatabaseRead();
            else if(detailsOnly) { CheckOrderDetails(); CheckDetailModalClosing(); }
            else { CheckViewModel(); CheckHistoryPresentation(); CheckOrderDetails(); CheckDetailModalClosing(); CheckCloseRoutes(); CheckLayouts(); }
            if (!readOnlyDatabase && !detailsOnly && !args.Contains("--skip-database")) CheckDatabase();
            else Console.WriteLine("SKIPPED: database integration checks.");
            Check(bindingErrors.Count == 0, "no XAML binding errors: " + string.Join(" | ", bindingErrors));
            Console.WriteLine($"PASS: {checks} checks. Renders: {output}");
            app.Shutdown(); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Check(bool passed, string name)
    { if (!passed) throw new InvalidOperationException("FAIL: " + name); checks++; Console.WriteLine("PASS " + name); }
    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T found) return found;
            var nested = FindVisualChild<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T found) yield return found;
            foreach (var nested in FindVisualChildren<T>(child)) yield return nested;
        }
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Wait(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(45);
        while (!task.IsCompleted && DateTime.UtcNow < until) { Pump(); Thread.Sleep(10); }
        if (!task.IsCompleted) throw new TimeoutException("Test task did not complete.");
        task.GetAwaiter().GetResult(); Pump();
    }
    private static void SetRole(string role, int office = 1) => AppSession.Employee = new MEmployee
    {
        EmId=1, EmName="検証担当者", SoId=office, Po=new MPosition { PoName=role },
        So=new MSalesOffice { SoId=office, SoName="北大阪営業所" }
    };
    private static void CheckViewModel()
    {
        var source = new ControlledSource(); var closed = false;
        var vm = new OrderHideViewModel([165], () => closed = true, source);
        Check(vm.TargetText == "対象受注：ORD-000165" && !vm.CanHide, "single target and disabled initial confirmation");
        Check(vm.ConfirmationMessage=="この受注を非表示にしますか？", "single-order hide confirmation uses the existing reason dialog");
        Wait(vm.HideAsync());
        Check(source.Calls.Count == 0 && vm.ReasonError == "非表示理由を入力してください。", "empty reason rejected inline");
        foreach (var text in new[] { " ", "\r\n\t", "　　" })
        { vm.Reason = text; Wait(vm.HideAsync()); Check(!vm.CanHide && source.Calls.Count == 0, "whitespace reason rejected"); }
        vm.Reason = "　 重複登録のため\r\n確認済みです。 \r\n";
        Check(vm.CanHide && vm.HideCommand.CanExecute(null) && vm.ReasonError == "", "Japanese multiline reason enables confirmation");
        source.Pending = new TaskCompletionSource<OrderHideResult>();
        var saving = vm.HideAsync();
        Check(vm.IsBusy && !vm.CanEdit && !vm.CanClose && !vm.CanHide && vm.ConfirmLabel == "処理中…", "processing disables input, close and confirmation");
        Check(!vm.CloseCommand.CanExecute(null), "cancel disabled while processing");
        vm.CloseCommand.Execute(null); vm.HideCommand.Execute(null); Wait(vm.HideAsync());
        Check(!closed && source.Calls.Count == 1, "duplicate submission and close prevented");
        vm.Reason = "書き換え";
        source.Pending.SetResult(new(165, true, "success")); Wait(saving);
        Check(source.Calls.Single().Reason == "重複登録のため\r\n確認済みです。", "submitted reason trimmed once, multiline preserved");
        Check(vm.IsComplete && !vm.CanHide && vm.Feedback == "受注を非表示にしました。", "single success only after awaited result");
        vm.CloseCommand.Execute(null); Check(closed, "close enabled after completion");

        source = new ControlledSource { Handler = id => new(id, id != 166, "対象外です。") };
        vm = new OrderHideViewModel([165,166,167,165], () => { }, source) { Reason = "重複登録" };
        Check(vm.TargetText == "対象：選択した受注 3件", "bulk target count and duplicate target normalization");
        Wait(vm.HideAsync());
        Check(vm.SuccessfulOrderIds.Order().SequenceEqual(new[] {165,167}) && vm.Feedback.Contains("成功 2件 / 失敗 1件")
            && vm.FailureDetails.Contains("ORD-000166"), "partial result reports actual successes and failed target");
        source.Handler = id => new(id, true, "success"); Wait(vm.HideAsync());
        Check(source.Calls.Count == 4 && source.Calls.Last().Id == 166 && vm.Feedback == "3件の受注を非表示にしました。", "retry only failed orders, cumulative truthful summary");
        source = new ControlledSource { Handler = _ => throw new IOException("simulated database failure") };
        vm = new OrderHideViewModel([165], () => { }, source) { Reason = "入力を保持" }; Wait(vm.HideAsync());
        Check(!vm.IsBusy && vm.CanHide && vm.Reason == "入力を保持" && !vm.HasChanges && vm.HasFailures, "failure preserves reason, unlocks retry, no false success");
        vm = new OrderHideViewModel([], () => { }, source) { Reason = "対象なし" };
        Check(!vm.CanHide, "empty selection rejected");
    }

    private static void CheckHistoryPresentation()
    {
        var recorded = "【受注管理更新】保存時の記録\n品目数：2 → 3\n総数量：11 → 10\n金額：¥828,000 → ¥685,400"
            + "\n変更前明細：イヤホンA（商品 1）：1 点、¥40,000 / iphone8（商品 2）：10 点、¥788,000"
            + "\n明細：イヤホンA（商品 1）：1 点、¥45,000 / iphone8（商品 2）：8 点、¥630,400 / ケース（商品 3）：1 点、¥10,000"
            + "\n備考・担当連絡先：記録済みの追記";
        var entry = new OrderOperationHistory
        {
            Id = 20, RecordedAt = new DateTimeOffset(2026, 10, 25, 12, 30, 0, TimeSpan.Zero),
            Operation = OrderHistoryOperations.Updated, OrderId = 173, CustomerName = "INATUGI",
            BeforeState = 0, AfterState = 0, BeforeManagement = 0, AfterManagement = 0,
            ActorName = "検証担当者", ActorOfficeName = "北大阪営業所", Details = recorded
        };
        var row = new OrderHistoryDisplayRow(entry);
        Check(row.ChangeSummary.Contains("数量：11 → 10 点") && row.ChangeSummary.Contains("合計金額：¥828,000 → ¥685,400")
            && row.ChangeSummary.Length < recorded.Length, "history list uses concise recorded change summaries");
        Check(row.Comparisons.Single(value => value.Field == "受注状態").Changed == false
            && row.Comparisons.Single(value => value.Field == "管理状態").Changed == false,
            "unchanged recorded state and management flags are not represented as changes");
        Check(row.ProductChanges.Count == 3
            && row.ProductChanges.Single(value => value.Product == "iphone8").BeforeQuantity == "10 点"
            && row.ProductChanges.Single(value => value.Product == "iphone8").AfterQuantity == "8 点"
            && row.ProductChanges.Single(value => value.Product == "iphone8").UnitPrice == "¥78,800"
            && row.ProductChanges.Single(value => value.Product == "イヤホンA").UnitPrice == "¥40,000 → ¥45,000"
            && row.ProductChanges.Single(value => value.Product == "ケース").ChangeKind == "追加",
            "history detail shows recorded product quantity, amount, price and addition changes");
        Check(row.Details == recorded && new OrderHistoryDetailViewModel(entry, () => { }, () => { }).OriginalDetails == recorded,
            "history detail preserves the original audit text exactly");

        var timeline = new OrderDetailHistoryRow(entry);
        Check(!timeline.IsExpanded && timeline.RecordedDetails == recorded, "timeline starts collapsed and retains the recorded description");
        timeline.ToggleDetailsCommand.Execute(null);
        Check(timeline.IsExpanded && timeline.ExpandedDetailsVisibility == Visibility.Visible, "timeline expands a single history item");
        timeline.ToggleDetailsCommand.Execute(null);
        Check(!timeline.IsExpanded && timeline.ExpandedDetailsVisibility == Visibility.Collapsed, "timeline collapses without rebuilding history");

        var older = new OrderOperationHistory { Id = 21, OrderId = 173, OfficeId = 1, RecordedAt = entry.RecordedAt };
        var newest = new OrderOperationHistory { Id = 22, OrderId = 173, OfficeId = 1, RecordedAt = entry.RecordedAt.AddSeconds(1) };
        var detail = new OrderDetailViewModel(new OrderDetailSnapshot { OrderId = 173, OfficeId = 1 }, () => { }, () => { }, () => { }, () => { }, () => { });
        detail.SetHistory([older, entry, newest]);
        Check(detail.History.Select(value => value.Display.Entry.Id).SequenceEqual(new long[] { 22, 21, 20 }),
            "timeline sorts recorded timestamp descending with stable event ID ordering");
    }

    private static OrderDetailSnapshot DetailSample(int state=0,int hidden=0) => new()
    {
        OrderId=173, OfficeId=1, CustomerId=1, CustomerName="検証用顧客", CustomerAddress="大阪市・記録済みの顧客住所",
        CustomerPhone="06-0000-0000", OfficeName="北大阪営業所", EmployeeName="検証担当者", StateFlag=state, ManagementFlag=hidden,
        HiddenReason=hidden==2 ? "記録された非表示理由" : null, OrderDate=new DateTime(2026,11,2), DeliveryDate=new DateTime(2026,11,9),
        Lines=Enumerable.Range(1,12).Select(index=>new OrderDetailLine
        {
            ProductId=index, ProductCode=$"PR-{index:0000}", ProductName="業務用ワイヤレスキーボード・日本語の商品名を省略せず表示 "+index,
            Quantity=2, UnitPrice=1234, Subtotal=index==1 ? 9999999999m : 2468
        }).ToList()
    };

    private static void CheckOrderDetails()
    {
        var prints=0; var edits=0; var confirmations=0; var hides=0;
        OrderDetailViewModel Model(OrderDetailSnapshot sample)=>new(sample,()=>edits++,()=>confirmations++,()=>hides++,()=>prints++,()=>{});
        var draft=Model(DetailSample()); var confirmed=Model(DetailSample(1)); var hidden=Model(DetailSample(1,2));
        Check(draft.HeaderStatus==new OrderDetailBadge("未確定","Warning") && draft.CanEdit && draft.CanConfirm && !draft.CanPrint,
            "draft details expose confirmation/editing and an orange status while printing is unavailable");
        draft.PrintCommand.Execute(null);
        Check(prints==0 && draft.ShowPrintHint && draft.PrintHint=="受注確定後に印刷できます", "printing an unconfirmed order cannot invoke the existing print workflow");
        Check(confirmed.HeaderStatus==new OrderDetailBadge("確定済み","Success") && confirmed.ConfirmButtonText=="確定済み"
            && !confirmed.EditCommand.CanExecute(null) && !confirmed.ConfirmCommand.CanExecute(null) && confirmed.PrintCommand.CanExecute(null),
            "confirmed details retain the existing edit/confirmation denial and enable printing");
        confirmed.PrintCommand.Execute(null); confirmed.EditCommand.Execute(null); confirmed.ConfirmCommand.Execute(null);
        Check(prints==1 && edits==0 && confirmations==0, "confirmed print action delegates once without editing or confirming again");
        Check(hidden.HeaderStatus==new OrderDetailBadge("非表示","Neutral") && hidden.ManagementStatus.Tone=="Neutral" && !hidden.CanHide,
            "hidden status remains distinct and neutral without changing the recorded state");
        SetRole("営業",2);
        Check(!confirmed.PrintCommand.CanExecute(null) && !confirmed.HideCommand.CanExecute(null), "detail actions enforce office scope");
        SetRole("管理者");
        confirmed.SetHistory([]);
        Check(confirmed.History.Count==0 && confirmed.ConfirmedAt=="未記録" && confirmed.RegisteredAt=="未記録", "missing audit timestamps are never inferred from current status");
        Check(confirmed.HistoryCountText=="0件", "empty retrieved history reports an actual zero count");
        confirmed.SetHistory([],"検証用の取得失敗");
        Check(confirmed.HistoryCountText=="取得失敗" && confirmed.ShowHistoryNotice,
            "failed audit retrieval is distinguished from an empty recorded history");
        draft.EditCommand.Execute(null); draft.ConfirmCommand.Execute(null); draft.HideCommand.Execute(null);
        Check(edits==1 && confirmations==1 && hides==1, "permitted actions delegate once to the existing workflows");

        foreach(var state in new[] {0,1})
        {
            var sample=DetailSample(state);
            var owner=new Window { Width=1000,Height=720,Left=-30000,Top=-30000,ShowInTaskbar=false,ShowActivated=false };
            owner.Show(); Pump();
            var view=new F_OrderDetail(sample,owner);
            var closed=0;
            var vm=new OrderDetailViewModel(sample,()=>{},()=>{},()=>{},()=>{},()=>closed++);
            vm.SetHistory(Enumerable.Range(1,24).Select(index=>new OrderOperationHistory
            {
                Id=index,OrderId=173,OfficeId=1,RecordedAt=new DateTimeOffset(2026,11,2,10,0,0,TimeSpan.FromHours(9)).AddMinutes(index),
                Operation=OrderHistoryOperations.Updated,ActorName="記録済み担当者",ActorOfficeName="北大阪営業所",
                BeforeState=0,AfterState=0,BeforeManagement=0,AfterManagement=0,Details="保存された備考の更新記録"
            }));
            var root=(FrameworkElement)view.Content; view.Content=null;
            var host=new Window { Content=root,DataContext=vm,Width=760,Height=520,Left=-30000,Top=-30000,
                WindowStyle=WindowStyle.None,ShowInTaskbar=false,ShowActivated=false };
            host.Show(); Pump(); host.UpdateLayout();
            var body=(ScrollViewer)view.FindName("DetailBodyScroll");
            var header=(FrameworkElement)view.FindName("DetailHeader");
            var footer=(FrameworkElement)view.FindName("DetailFooter");
            var edit=(Button)view.FindName("EditOrderButton");
            var print=(Button)view.FindName("PrintOrderButton");
            var expander=(Expander)view.FindName("DetailHistoryExpander");
            var historyGrid=(DataGrid)view.FindName("DetailHistoryGrid");
            var itemsGrid=(DataGrid)view.FindName("DetailItemsGrid");
            Check(((TextBlock)view.FindName("DetailHeaderOrderNumber")).Text==vm.OrderNumber
                && ((TextBlock)view.FindName("DetailHeaderSummary")).Text==vm.HeaderSummary
                && vm.HeaderSummary.Contains("2026/11/02") && vm.HeaderSummary.Contains(sample.OfficeName),
                "fixed header uses the selected order number, date and office");
            Check(((TextBlock)view.FindName("DetailHistoryCount")).Text=="24件", "collapsed history shows the actual retrieved count");
            Check(edit.Visibility==Visibility.Visible && edit.IsEnabled==(state==0) && print.IsEnabled==(state==1)
                && !expander.IsExpanded && ((Button)view.FindName("ConfirmOrderButton")).Visibility==(state==1 ? Visibility.Collapsed : Visibility.Visible),
                "compact action bar respects order rules and initially collapses history, state "+state);
            foreach(var (width,height,label) in DetailLayoutSizes())
            {
                host.Width=width; host.Height=height; host.UpdateLayout(); Pump();
                var headerTop=header.TransformToAncestor(root).Transform(new Point()).Y;
                var footerTop=footer.TransformToAncestor(root).Transform(new Point()).Y;
                body.ScrollToBottom(); host.UpdateLayout(); Pump();
                Check(body.ScrollableWidth<1 && header.TransformToAncestor(root).Transform(new Point()).Y==headerTop
                    && footer.TransformToAncestor(root).Transform(new Point()).Y==footerTop
                    && footerTop+footer.ActualHeight<=root.ActualHeight+1, $"detail header/footer remain accessible with vertical-only body scrolling {label}, state {state}");
                var itemScroll=FindVisualChild<ScrollViewer>(itemsGrid)!;
                Check(itemsGrid.Columns.All(column=>column.ActualWidth>=28) && itemScroll.ScrollableWidth<1,
                    "all item columns fit without horizontal scrolling at width "+width);
                var actions=(Grid)view.FindName("DetailActionBar");
                var badge=(FrameworkElement)view.FindName("HeaderStatusBadge");
                var confirm=(Button)view.FindName("ConfirmOrderButton");
                var first=state==0 ? confirm : edit;
                Check(badge.TransformToAncestor(actions).Transform(new Point(badge.ActualWidth,0)).X
                    <=first.TransformToAncestor(actions).Transform(new Point()).X,
                    "status badge and existing actions do not overlap at width "+width);
            }
            Check(itemsGrid.Columns.Select(column=>column.Header.ToString())
                .SequenceEqual(new[] {"No.","商品コード","商品名","単価","数量","税抜小計","消費税","税込小計"}),
                "reference item table keeps separate code and product columns with all monetary information");
            var orderCard=(FrameworkElement)view.FindName("OrderCard");
            var customerCard=(FrameworkElement)view.FindName("CustomerCard");
            Check(orderCard.TransformToAncestor(root).Transform(new Point()).X<customerCard.TransformToAncestor(root).Transform(new Point()).X,
                "reference information cards place order information on the left and customer information on the right");
            host.Width=900;host.Height=640;host.UpdateLayout();body.ScrollToTop();Pump();
            Render(root,"order-detail-"+(state==0 ? "draft" : "confirmed"));
            var itemsScroll=FindVisualChild<ScrollViewer>(itemsGrid)!;
            Check(itemsGrid.IsReadOnly && itemsGrid.ActualHeight<=220 && itemsScroll.ScrollableHeight>0,
                "long read-only item list has bounded internal scrolling");
            itemsGrid.ScrollIntoView(vm.Items[^1]);host.UpdateLayout();Pump();
            Check(itemsGrid.ItemContainerGenerator.ContainerFromItem(vm.Items[^1]) is DataGridRow,
                "the final item remains accessible without dropping any products");
            Render(root,"order-detail-items-"+state);
            expander.IsExpanded=true;host.UpdateLayout();body.ScrollToBottom();Pump();
            var historyScroll=FindVisualChild<ScrollViewer>(historyGrid)!;
            Check(historyGrid.ActualHeight<=180 && historyScroll.ScrollableHeight>0 && vm.History.Count==24,
                "expanded detail history is bounded and keeps all recorded entries accessible");
            Check(historyGrid.Columns.Select(column=>column.Header.ToString()).SequenceEqual(new[] {"日時","操作者","操作内容","変更内容"}),
                "recorded history is displayed in the reference four-column table");
            historyGrid.SelectedItem=vm.History[0];host.UpdateLayout();Pump();
            var selectedRow=historyGrid.ItemContainerGenerator.ContainerFromItem(vm.History[0]) as DataGridRow;
            Check(selectedRow?.DetailsVisibility==Visibility.Visible && vm.History[0].RecordedDetails=="保存された備考の更新記録",
                "selecting a history row retains access to the original detailed audit presentation");
            historyGrid.SelectedItem=null;host.UpdateLayout();
            Render(root,"order-detail-history-"+state);
            expander.IsExpanded=false;host.UpdateLayout();
            Check(!expander.IsExpanded && vm.History.Count==24, "collapsing history preserves recorded entries");
            if(state==1)
            {
                var slipData = new OrderSlipPrintData
                {
                    OrderId = 173, OrderDate = new DateTime(2026,11,2), DeliveryDate = new DateTime(2026,11,9),
                    CustomerId = 5, CustomerName = "検証用顧客", CustomerAddress = "大阪市・記録済み住所", CustomerPhone = "06-0000-0000",
                    OfficeId = 1, OfficeName = "北大阪営業所", EmployeeId = 310, EmployeeName = "検証担当者", OrderStatus = "確定済み",
                    Remarks = "検証用備考", Lines = [new OrderSlipPrintLine
                    {
                        ProductCode = "4902602600083", ProductName = "24型フルHD液晶ディスプレイ",
                        UnitPrice = 25800, Quantity = 1, Subtotal = 25800
                    }],
                    Subtotal = 25800, Tax = 2580, Total = 28380
                };
                var createSlip = typeof(OrderSlipPrintPreviewWindow).GetMethod("CreateDocument", BindingFlags.Static | BindingFlags.NonPublic)!;
                var document = (System.Windows.Documents.FlowDocument)createSlip.Invoke(null, [slipData])!;
                var text = new System.Windows.Documents.TextRange(document.ContentStart, document.ContentEnd).Text;
                Check(text.Contains("ORD-000173") && text.Contains("検証用顧客") && text.Contains("大阪市・記録済み住所")
                    && text.Contains("06-0000-0000") && text.Contains("北大阪営業所") && text.Contains("検証担当者")
                    && text.Contains("4902602600083") && text.Contains("24型フルHD液晶ディスプレイ")
                    && text.Contains("¥25,800") && text.Contains("¥2,580") && text.Contains("¥28,380") && text.Contains("検証用備考"),
                    "individual slip includes the selected order, customer, employee, product, tax, total, and remarks");
                Check(Math.Abs(document.PageWidth - 793.7) < 0.1 && Math.Abs(document.PageHeight - 1122.5) < 0.1,
                    "individual slip document uses an A4 portrait page size");
                var slipPreview = new OrderSlipPrintPreviewWindow(slipData)
                {
                    Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual,
                    ShowInTaskbar = false, ShowActivated = false
                };
                slipPreview.Show(); Pump(); slipPreview.UpdateLayout();
                var slipReader = FindVisualChild<FlowDocumentReader>(slipPreview);
                var previewButtons = FindVisualChildren<Button>(slipPreview).ToArray();
                Check(slipReader?.ViewingMode == FlowDocumentReaderViewingMode.Page && slipReader.IsPrintEnabled == false
                    && previewButtons.Any(button => button.Content?.ToString() == "印刷")
                    && previewButtons.Any(button => button.Content?.ToString() == "閉じる"),
                    "custom preview shows the A4 document with explicit print and close actions");
                Render(slipPreview, "order-slip-preview");
                slipPreview.Close();
                var manyLines = Enumerable.Range(1, 100).Select(index => new OrderSlipPrintLine
                {
                    ProductCode = $"CODE-{index:000}", ProductName = $"複数ページ検証商品 {index:000}",
                    UnitPrice = 100, Quantity = 1, Subtotal = 100
                }).ToArray();
                var multiPageSlip = new OrderSlipPrintData
                {
                    OrderId = slipData.OrderId, OrderDate = slipData.OrderDate, CustomerId = slipData.CustomerId,
                    CustomerName = slipData.CustomerName, CustomerAddress = slipData.CustomerAddress, CustomerPhone = slipData.CustomerPhone,
                    OfficeId = slipData.OfficeId, OfficeName = slipData.OfficeName, EmployeeId = slipData.EmployeeId,
                    EmployeeName = slipData.EmployeeName, OrderStatus = slipData.OrderStatus, Lines = manyLines,
                    Subtotal = 10000, Tax = 1000, Total = 11000
                };
                var multiPageDocument = (System.Windows.Documents.FlowDocument)createSlip.Invoke(null, [multiPageSlip])!;
                var paginator = ((System.Windows.Documents.IDocumentPaginatorSource)multiPageDocument).DocumentPaginator;
                paginator.ComputePageCount();
                Check(paginator.PageCount > 1, "individual slip paginates an order with many product rows");
                var previewData=new OrderDetailSnapshot
                {
                    OrderId=173,OfficeId=1,CustomerId=5,CustomerName="検証用顧客",CustomerPhone="06-0000-0000",CustomerAddress="大阪市・記録済み住所",
                    OfficeName="北大阪営業所",EmployeeName="検証担当者",StateFlag=1,OrderDate=new DateTime(2026,11,2),DeliveryDate=new DateTime(2026,11,9),
                    Lines=[new OrderDetailLine {ProductId=1,ProductCode="4902602600083",ProductName="24型フルHD液晶ディスプレイ",Quantity=1,UnitPrice=25800,Subtotal=25800}]
                };
                var preview=new OrderDetailViewModel(previewData,()=>{},()=>{},()=>{},()=>{},()=>{});
                preview.SetHistory(vm.History.Take(3).Select(row=>row.Display.Entry));
                host.DataContext=preview;expander.IsExpanded=false;host.UpdateLayout();body.ScrollToTop();Pump();
                Render(root,"order-detail-reference");
                Check(body.ScrollableHeight<1 && preview.FooterSummary.Contains("通常表示"),
                    $"compact order layout fits without hiding management information (overflow={body.ScrollableHeight}, body={body.ActualHeight}, history={historyGrid.ActualHeight}, footer={preview.FooterSummary})");
                expander.IsExpanded=true;host.UpdateLayout();body.ScrollToBottom();Pump();
                Render(root,"order-detail-reference-expanded");
                host.DataContext=vm;host.UpdateLayout();Pump();
            }
            ((Button)view.FindName("DetailCloseButton")).Command.Execute(null);
            Check(closed==1, "detail header close delegates to the existing close command");
            host.Close();view.Close();owner.Close();
        }
    }

    private static IEnumerable<(double Width,double Height,string Label)> DetailLayoutSizes()
    {
        yield return (760,460,"minimum 760x460");
        var calculate=typeof(F_OrderDetail).Assembly.GetType("SalesManagement_SysDev.RegistrationWindowPlacement")!
            .GetMethod("Calculate",BindingFlags.Static|BindingFlags.NonPublic)!;
        foreach(var (width,height) in new[] {(1280,720),(1366,768),(1600,900),(1920,1080)})
        foreach(var scale in new[] {1d,1.25,1.5})
        {
            var work=new Rect(0,0,width,height-48);
            var placement=calculate.Invoke(null,[work,work,scale,scale,900d,640d,true,760d,460d])!;
            var bounds=(Rect)placement.GetType().GetProperty("Pixels")!.GetValue(placement)!;
            Check(work.Contains(bounds),$"detail placement fits {width}x{height} work area at {scale*100}%");
            yield return (bounds.Width/scale,bounds.Height/scale,$"{width}x{height} at {scale*100}% (DPI layout units)");
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);

    private static void InvokeButton(Button button)
    {
        var peer=new ButtonAutomationPeer(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
    }

    private static void CheckDetailModalClosing()
    {
        foreach(var route in new[] {"DetailCloseButton","DetailFooterCloseButton","Escape","NativeClose"})
        {
            var clicks=0;
            var navigation=new Button {Content="受注一覧",Width=140,Height=40};
            var restricted=new Button {Content="権限による無効操作",IsEnabled=false};
            navigation.Click+=(_,_)=>clicks++;
            var panel=new StackPanel();panel.Children.Add(navigation);panel.Children.Add(restricted);
            var owner=new Window {Content=panel,Width=1000,Height=720,Left=-30000,Top=-30000,ShowInTaskbar=false,ShowActivated=false};
            owner.Show();Pump();
            var handle=new WindowInteropHelper(owner).Handle;
            var view=new F_OrderDetail(DetailSample(),owner) {ShowInTaskbar=false};
            // Exercise real WPF ShowDialog/Close without connecting to or modifying the database.
            typeof(F_OrderDetail).GetField("historyRequested",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(view,true);
            ((OrderDetailViewModel)view.DataContext).SetHistory([]);
            Exception? failure=null;
            view.ContentRendered+=(_,_)=>view.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
            {
                try
                {
                    Check(!IsWindowEnabled(handle),"actual modal details disable native owner interaction while open: "+route);
                    if(route=="Escape") view.InputBindings.OfType<KeyBinding>().Single(key=>key.Key==Key.Escape).Command.Execute(null);
                    else if(route=="NativeClose") SystemCommands.CloseWindow(view);
                    else InvokeButton((Button)view.FindName(route));
                }
                catch(Exception ex) {failure=ex;view.Close();}
            }));
            view.ShowDialog();Pump();
            if(failure!=null) throw failure;
            Check(!view.IsVisible && owner.OwnedWindows.Count==0 && IsWindowEnabled(handle)
                && owner.IsEnabled && owner.IsHitTestVisible && !restricted.IsEnabled,
                "modal close releases ownership and preserves intentionally disabled controls: "+route);
            Check(owner.InputHitTest(navigation.TranslatePoint(new Point(navigation.ActualWidth/2,navigation.ActualHeight/2),owner))!=null,
                "owner navigation remains reachable by hit testing: "+route);
            InvokeButton(navigation);Pump();
            Check(clicks==1,"owner navigation button accepts an action after modal closure: "+route);
            owner.Close();
        }
    }

    private static void CheckCloseRoutes()
    {
        foreach (var route in new[] {"CancelButton", "CloseButton", "Escape", "NativeClose"})
        {
            var source = new ControlledSource(); var dialog = new F_OrderHide([165], source);
            Exception? failure = null;
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                try
                {
                    var input = (TextBox)dialog.FindName("ReasonInput");
                    Check(input.IsKeyboardFocused && input.AcceptsReturn && input.TextWrapping == TextWrapping.Wrap
                        && InputMethod.GetIsInputMethodEnabled(input), "automatic focus and Japanese multiline input " + route);
                    input.Text = "キャンセル確認";
                    if (route == "NativeClose") dialog.Close();
                    else if (route == "Escape") dialog.InputBindings.OfType<KeyBinding>().Single(key => key.Key == Key.Escape).Command.Execute(null);
                    else ((Button)dialog.FindName(route)).Command.Execute(null);
                }
                catch (Exception ex) { failure = ex; dialog.Close(); }
            }));
            dialog.ShowDialog(); if (failure != null) throw failure;
            Check(source.Calls.Count == 0 && dialog.SuccessfulOrderIds.Count == 0, "no data operation on " + route);
        }
        var pending = new TaskCompletionSource<OrderHideResult>();
        var busySource = new ControlledSource { Pending = pending };
        var busyDialog = new F_OrderHide([165], busySource);
        Exception? busyFailure = null;
        busyDialog.Loaded += (_, _) => busyDialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            try
            {
                var vm = (OrderHideViewModel)busyDialog.DataContext; vm.Reason = "処理中の確認";
                var task = vm.HideAsync(); busyDialog.Close();
                Check(busyDialog.IsVisible && !((Button)busyDialog.FindName("CloseButton")).IsEnabled, "native close blocked during in-flight operation");
                pending.SetResult(new(165, true, "success")); await task;
                vm.CloseCommand.Execute(null);
            }
            catch (Exception ex) { busyFailure = ex; pending.TrySetResult(new(165, false, "test cleanup")); busyDialog.Close(); }
        }));
        busyDialog.ShowDialog(); if (busyFailure != null) throw busyFailure;
        Check(busySource.Calls.Count == 1 && busyDialog.SuccessfulOrderIds.Contains(165), "successful result survives dialog close");
    }

    private static void CheckLayouts()
    {
        var owner = new Window { Width=900, Height=650, WindowStartupLocation=WindowStartupLocation.CenterScreen };
        owner.Show(); Pump();
        var dialog = new F_OrderHide([165], new ControlledSource()) { Owner=owner }; dialog.Show(); Pump(); dialog.UpdateLayout();
        Check(dialog.Width == 460 && dialog.ActualHeight is >= 340 and <= 380, "compact automatic dialog dimensions " + dialog.ActualHeight);
        Check(Math.Abs(dialog.Left + dialog.ActualWidth/2 - owner.Left - owner.ActualWidth/2) < 3
            && Math.Abs(dialog.Top + dialog.ActualHeight/2 - owner.Top - owner.ActualHeight/2) < 3, "dialog centered on owner");
        Render((FrameworkElement)dialog.Content, "single"); dialog.Close(); owner.Close();

        var historyOwner = new Window { Width=1000, Height=680, WindowStartupLocation=WindowStartupLocation.CenterScreen };
        historyOwner.Show(); Pump();
        var history = new OrderOperationHistory
        {
            Id=1, OrderId=173, CustomerName="INATUGI", Operation=OrderHistoryOperations.Updated,
            RecordedAt=DateTimeOffset.UtcNow, ActorName="検証担当者", ActorOfficeName="北大阪営業所",
            BeforeState=0, AfterState=0, BeforeManagement=0, AfterManagement=0,
            Details=string.Join("\n", Enumerable.Repeat("記録済みの監査本文。数量と金額の変更理由を含みます。", 80))
        };
        var historyDialog = new F_OrderHistoryDetail(history, historyOwner, () => { });
        historyDialog.Show(); Pump(); historyDialog.UpdateLayout();
        Check(historyDialog.Width <= 760 && historyDialog.ActualHeight <= historyDialog.MaxHeight + 1,
            "history detail respects compact width and monitor work-area height");
        Check(Math.Abs(historyDialog.Left + historyDialog.ActualWidth/2 - historyOwner.Left - historyOwner.ActualWidth/2) < 3,
            "history detail is centered on its owner");
        Check(FindVisualChild<ScrollViewer>((DependencyObject)historyDialog.Content) is { ActualHeight: > 0 }
            && FindVisualChild<TextBlock>((DependencyObject)historyDialog.Content) is not null,
            "long history text is hosted in a scrollable content area with a visible dialog header");
        historyDialog.Close(); historyOwner.Close();

        var noticeOwner = new Window { Width=900, Height=650, WindowStartupLocation=WindowStartupLocation.CenterScreen };
        noticeOwner.Show(); Pump();
        var notice = new F_OrderNotice("受注を確定", "選択した受注 2 件を確定します。よろしいですか？", OrderNoticeKind.Confirmation,
            "受注を確定", "キャンセル", showCancel: true) { Owner=noticeOwner };
        notice.Loaded += (_, _) => notice.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            ((OrderNoticeViewModel)notice.DataContext).CancelCommand.Execute(null)));
        Check(notice.ShowDialog() == false, "custom confirmation cancel keeps the operation unconfirmed");
        var successNotice = new F_OrderNotice("受注更新", "受注を更新しました。", OrderNoticeKind.Success) { Owner=noticeOwner };
        successNotice.Loaded += (_, _) => successNotice.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            ((OrderNoticeViewModel)successNotice.DataContext).ConfirmCommand.Execute(null)));
        Check(successNotice.ShowDialog() == true, "custom notice primary action closes with a successful result");
        noticeOwner.Close();

        var calculate = typeof(F_OrderHide).Assembly.GetType("SalesManagement_SysDev.RegistrationWindowPlacement")!
            .GetMethod("Calculate", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var (width, height) in new[] {(1920,1080),(1600,900),(1366,768),(1280,720)})
        foreach (var scale in new[] {1d,1.25,1.5})
        {
            var work = new Rect(0,0,width,height-48);
            var placement = calculate.Invoke(null, [work,work,scale,scale,460d,378d,true,420d,0d])!;
            var bounds = (Rect)placement.GetType().GetProperty("Pixels")!.GetValue(placement)!;
            Check(work.Contains(bounds) && bounds.Width <= work.Width*.95+1 && bounds.Height <= work.Height*.9+1,
                $"work area bounds {width}x{height}@{scale*100}%");
            var editor = new F_OrderHide([165,166,167], new ControlledSource());
            var root = (FrameworkElement)editor.Content; editor.Content = null;
            var host = new Window { Content=root, Width=bounds.Width/scale, Height=bounds.Height/scale,
                WindowStyle=WindowStyle.None, ResizeMode=ResizeMode.NoResize };
            root.DataContext = editor.DataContext;
            host.Show(); Pump(); host.UpdateLayout();
            var footer = (FrameworkElement)editor.FindName("Footer"); var header = (FrameworkElement)editor.FindName("Header");
            var cancel = (FrameworkElement)editor.FindName("CancelButton"); var confirm = (FrameworkElement)editor.FindName("HideButton");
            var cancelPos = cancel.TransformToAncestor(root).Transform(new Point()); var confirmPos = confirm.TransformToAncestor(root).Transform(new Point());
            Check(Math.Abs(cancelPos.Y-confirmPos.Y)<1 && confirmPos.X+confirm.ActualWidth <= root.ActualWidth
                && header.ActualHeight == 52 && footer.ActualHeight >= 58, $"visible header and horizontal actions @{scale*100}% {width}");
            var vm = (OrderHideViewModel)root.DataContext;
            vm.Reason = " "; host.UpdateLayout();
            Check(((TextBlock)editor.FindName("ReasonValidation")).ActualHeight > 0, "inline validation visible in constrained layout");
            ((FrameworkElement)editor.FindName("ReasonValidation")).BringIntoView(); Pump(); host.UpdateLayout();
            var validation=(FrameworkElement)editor.FindName("ReasonValidation"); var body=(ScrollViewer)editor.FindName("BodyScroller");
            var validationPoint=validation.TransformToAncestor(body).Transform(new Point());
            Check(validationPoint.Y >= -1 && validationPoint.Y+validation.ActualHeight <= body.ActualHeight+1, "validation accessible by scrolling in constrained viewport");
            if (width == 1280) Render(root, "bulk-invalid-" + scale.ToString(CultureInfo.InvariantCulture));
            host.Close(); editor.Close();
        }
        var partial = new F_OrderHide([165,166,167], new ControlledSource { Handler=id => new(id,id==165,"所属営業所の範囲外です。") });
        var partialVm = (OrderHideViewModel)partial.DataContext; partialVm.Reason="重複登録のため非表示にする";
        Wait(partialVm.HideAsync()); partial.Show(); Pump(); partial.UpdateLayout(); Render((FrameworkElement)partial.Content,"partial-result");
        var scroll = (ScrollViewer)partial.FindName("BodyScroller"); scroll.ScrollToBottom(); partial.UpdateLayout();
        Check(partialVm.HasFailures && ((Button)partial.FindName("HideButton")).IsEnabled, "partial summary permits failed-order retry");
        partial.Close();
    }
    private static void Render(FrameworkElement root, string name)
    {
        var image = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);
        image.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(output,name+".png")); encoder.Save(file);
    }

    private static SalesManagementContext Context()
    { var db = new SalesManagementContext(); db.Database.SetConnectionString(testConnection); return db; }
    private static void CheckExistingDatabaseRead()
    {
        // This opt-in check uses SELECT queries only against the configured existing database.
        using var db=new SalesManagementContext();
        var orderId=db.TOrders.AsNoTracking().Where(order=>order.OrFlag==0 && order.TOrderDetails.Any())
            .OrderByDescending(order=>order.OrId).Select(order=>(int?)order.OrId).FirstOrDefault();
        Check(orderId.HasValue,"existing SQL Server database returns a saved visible order with details");
        using var source=(IOrderRegistrationDataSource)Activator.CreateInstance(typeof(F_OrderDetail).Assembly.GetType("SalesManagement_SysDev.OrderRegistrationDataSource")!,true)!;
        var snapshot=source.LoadRegisteredOrder(orderId!.Value);
        Check(snapshot!=null && snapshot.OrderId==orderId && !string.IsNullOrWhiteSpace(snapshot.CustomerName)
            && !string.IsNullOrWhiteSpace(snapshot.OfficeName) && snapshot.Lines.Count>0
            && snapshot.Lines.All(line=>!string.IsNullOrWhiteSpace(line.ProductName)),
            "existing snapshot query loads actual order, customer, office and product records");
        var confirmedOrder=db.TOrders.AsNoTracking()
            .Where(order=>order.OrFlag==0 && order.OrStateFlag==1
                && order.TOrderDetails.Any(line=>line.OrQuantity!=0 || line.OrTotalPrice!=0m))
            .OrderByDescending(order=>order.OrId)
            .Include(order=>order.Cl).Include(order=>order.So).Include(order=>order.Em)
            .Include(order=>order.TOrderDetails).ThenInclude(line=>line.Pr)
            .FirstOrDefault();
        Check(confirmedOrder!=null,"existing SQL Server database returns a confirmed order for individual slip read verification");
        if(confirmedOrder==null) throw new InvalidOperationException("A confirmed order is required for the individual slip read check.");
        Check(OrderSlipPrintService.TryLoadConfirmedOrder(confirmedOrder.OrId,out var slip,out var printMessage) && slip!=null,
            "individual slip service reloads the confirmed order by its persisted primary key ("+printMessage+")");
        var actualLines=confirmedOrder.TOrderDetails.Where(line=>line.OrQuantity!=0 || line.OrTotalPrice!=0m)
            .OrderBy(line=>line.OrDetailId).ToArray();
        Check(slip!.OrderId==confirmedOrder.OrId && slip.CustomerId==confirmedOrder.ClId
            && slip.CustomerName==confirmedOrder.Cl.ClName && slip.CustomerAddress==confirmedOrder.Cl.ClAddress
            && slip.CustomerPhone==confirmedOrder.Cl.ClPhone && slip.OrderDate==confirmedOrder.OrDate
            && slip.DeliveryDate==confirmedOrder.OrDeliveryDate && slip.OfficeName==confirmedOrder.So.SoName
            && slip.EmployeeName==confirmedOrder.Em.EmName && slip.Lines.Count==actualLines.Length
            && slip.Lines.Zip(actualLines).All(pair=>pair.First.ProductName==pair.Second.Pr.PrName
                && pair.First.Quantity==pair.Second.OrQuantity && pair.First.Subtotal==pair.Second.OrTotalPrice),
            "individual slip data matches persisted customer, order, office, employee and every product detail");
        var persistedSubtotal=actualLines.Sum(line=>line.OrTotalPrice);
        Check(slip.Subtotal==persistedSubtotal && slip.Tax==Math.Round(persistedSubtotal*.1m,0,MidpointRounding.AwayFromZero)
            && slip.Total==slip.Subtotal+slip.Tax,
            "individual slip tax and total match the saved line amounts and existing order-detail rounding");
        var slipDocument=(System.Windows.Documents.FlowDocument)typeof(OrderSlipPrintPreviewWindow)
            .GetMethod("CreateDocument",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[slip])!;
        var slipText=new System.Windows.Documents.TextRange(slipDocument.ContentStart,slipDocument.ContentEnd).Text;
        Check(slipText.Contains($"ORD-{confirmedOrder.OrId:000000}") && slipText.Contains(slip.CustomerName)
            && actualLines.All(line=>slipText.Contains(line.Pr.PrName)),
            "individual slip print document contains only the selected order and its persisted products");
        var records=OrderHistoryService.Read(db,new OrderHistoryFilter {OrderId=orderId.Value}).Rows;
        var model=new OrderDetailViewModel(snapshot!,()=>{},()=>{},()=>{},()=>{},()=>{});
        model.SetHistory(records);
        Check(model.History.Count==records.Count && records.All(record=>record.OrderId==orderId),
            "existing audit query displays only recorded events for the selected real order");
        Check(model.Subtotal==snapshot!.Lines.Sum(line=>line.Subtotal)
            && model.Total==model.Subtotal+Math.Round(model.Subtotal*.1m,0,MidpointRounding.AwayFromZero),
            "actual saved amounts use the existing document tax and rounding");
        var owner=new Window {Width=1000,Height=720,Left=-30000,Top=-30000,ShowInTaskbar=false,ShowActivated=false};
        owner.Show();Pump();
        var view=new F_OrderDetail(snapshot,owner);
        var root=(FrameworkElement)view.Content;view.Content=null;
        var host=new Window {Content=root,DataContext=model,Width=900,Height=640,Left=-30000,Top=-30000,
            WindowStyle=WindowStyle.None,ShowInTaskbar=false,ShowActivated=false};
        host.Show();host.UpdateLayout();Pump();
        var expander=(Expander)view.FindName("DetailHistoryExpander");
        Check(!expander.IsExpanded && ((TextBlock)view.FindName("DetailHeaderOrderNumber")).Text==model.OrderNumber
            && ((TextBlock)view.FindName("DetailHistoryCount")).Text==$"{records.Count:N0}件"
            && ((DataGrid)view.FindName("DetailItemsGrid")).Items.Count==snapshot.Lines.Count,
            "real saved order header, item count and collapsed audit count bind correctly");
        Render(root,"order-detail-actual");
        expander.IsExpanded=true;host.UpdateLayout();Pump();
        Check(((DataGrid)view.FindName("DetailHistoryGrid")).Items.Count==records.Count,
            "expanding real saved order history preserves every retrieved record");
        ((ScrollViewer)view.FindName("DetailBodyScroll")).ScrollToBottom();host.UpdateLayout();Pump();
        Render(root,"order-detail-actual-expanded");
        Console.WriteLine($"READ ONLY {model.OrderNumber}: {snapshot.Lines.Count} detail lines, {records.Count} audit records, total ¥{model.Total:N0}.");
        host.Close();view.Close();owner.Close();
        Check(!db.ChangeTracker.HasChanges(),"read-only verification leaves the existing database unchanged");
        var login=new LoginViewModel(()=>throw new InvalidOperationException("Connection checks must not authenticate."),()=>{});
        Check(login.PrepareDatabase() && login.DatabaseReady && login.LoginLabel=="ログイン",
            "the login screen's actual connection check reaches Ready without authenticating or initializing data");
    }
    private static OrderHideResult Hide(int id, string? reason=" 重複登録\r\n確認済み ", int? employee=null)
    {
        using var db=Context(); object?[] values=[db,id,employee ?? AppSession.Employee?.EmId ?? 0,reason,null];
        var success=(bool)typeof(BusinessService).GetMethod("HideOrderCore",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,values)!;
        return new(id,success,(string)values[^1]!);
    }
    private static void CheckDatabase()
    {
        using var config=new SalesManagementContext();
        var catalog="SalesManagement_OrderHideChecks_"+Guid.NewGuid().ToString("N");
        var connection=new SqlConnectionStringBuilder(config.Database.GetDbConnection().ConnectionString) { InitialCatalog=catalog };
        var server=Environment.GetEnvironmentVariable("ORDER_HIDE_TEST_SERVER");
        if (!string.IsNullOrWhiteSpace(server)) connection.DataSource=server;
        testConnection=connection.ConnectionString; var created=false;
        try
        {
            using (var db=Context()) { db.Database.EnsureCreated(); created=true; Seed(db); }
            Check(!Hide(1,"　\r\n").Succeeded && !Hide(0).Succeeded && !Hide(99999).Succeeded, "service rejects whitespace, invalid and missing targets");
            SetRole("物流"); Check(!Hide(1).Succeeded,"service role denial");
            AppSession.Employee=null; Check(!Hide(1).Succeeded,"service unauthenticated denial");
            SetRole("営業",2); Check(!Hide(1).Succeeded,"service branch denial");
            SetRole("営業"); Check(!Hide(1,employee:999).Succeeded,"service actor identity denial");
            Check(Hide(1).Succeeded,"single hide transaction commits");
            using (var db=Context())
            {
                var order=db.TOrders.Single(x=>x.OrId==1); var audit=db.OrderOperationHistories.Single();
                Check(order.OrFlag==2 && order.OrHidden=="重複登録\r\n確認済み" && db.TOrders.Count()==5,"logical hide preserves records and trimmed multiline reason");
                Check(audit.OrderId==1 && audit.ActorEmployeeId==1 && audit.OfficeId==1 && audit.RecordedAt!=default
                    && audit.ActorOfficeName=="北大阪営業所" && audit.Details.Contains("重複登録") && audit.BeforeManagement==0
                    && audit.AfterManagement==2 && audit.Operation==OrderHistoryOperations.Hidden,"audit actor, time, branch, target, reason and transition");
                Check(db.TOrderDetails.Count()==5 && db.TStocks.Single().StQuantity==50 && db.TSales.Count()==0 && order.OrStateFlag==0,"hide preserves details, stock, sales and confirmation state");
            }
            Check(!Hide(1).Succeeded,"already hidden service rejection");
            using (var db=Context()) Check(db.OrderOperationHistories.Count()==1,"duplicate hide creates no duplicate audit");
            var source=new DatabaseSource(); var vm=new OrderHideViewModel([2,3,99999],()=>{},source) {Reason="一括処理"}; Wait(vm.HideAsync());
            Check(vm.SuccessfulOrderIds.Count==2 && vm.HasFailures && vm.Feedback.Contains("失敗 1件"),"bulk database partial-success policy");
            using (var db=Context())
            {
                Check(db.TOrders.Count(x=>x.OrFlag!=2)==2 && db.TOrders.Count(x=>x.OrFlag==2)==3
                    && db.TOrders.Count(x=>x.OrFlag!=2 && (x.OrStateFlag??0)==0)==1
                    && db.TOrders.Count(x=>x.OrFlag!=2 && x.OrStateFlag==1)==1,"all, hidden, pending and confirmed visibility counts");
                Check(OrderHistoryService.Read(db,new OrderHistoryFilter {OrderId=2}).Rows.Count==1,"hidden orders retain authorized audit access");
            }
            using (var db=Context()) db.Database.ExecuteSqlRaw("CREATE TRIGGER OrderHideChecksFailure ON T_OrderOperationHistory AFTER INSERT AS THROW 51000, N'OrderHideChecks rollback', 1;");
            try
            {
                Check(!Hide(4).Succeeded,"actual SQL audit failure reported");
                using var db=Context(); Check(db.TOrders.Single(x=>x.OrId==4).OrFlag==0 && db.OrderOperationHistories.Count()==3,"SQL failure rolls back visibility and audit together");
            }
            finally {using var db=Context();db.Database.ExecuteSqlRaw("DROP TRIGGER OrderHideChecksFailure");}
            var concurrent=Task.WhenAll(Task.Run(()=>Hide(4)),Task.Run(()=>Hide(4))); Wait(concurrent);
            Check(concurrent.Result.Count(result=>result.Succeeded)==1,"concurrent submissions succeed exactly once");
            using (var db=Context()) Check(db.OrderOperationHistories.Count(x=>x.OrderId==4)==1,"concurrent submissions audit exactly once");
            SetRole("管理者",2); Check(Hide(5).Succeeded,"existing admin cross-office permission retained");
            SetRole("営業",2);
            using (var db=Context()) Check(OrderHistoryService.Read(db,new OrderHistoryFilter()).Rows.Count==0,"audit queries enforce branch scope");
            SetRole("管理者");
        }
        finally
        {
            if (!catalog.StartsWith("SalesManagement_OrderHideChecks_",StringComparison.Ordinal) || connection.InitialCatalog!=catalog || catalog=="SalesManagement")
                throw new InvalidOperationException("Unsafe cleanup target");
            if (created) {using var db=Context();db.Database.EnsureDeleted();Console.WriteLine("Disposable database removed: "+catalog);}
        }
    }
    private static void Seed(SalesManagementContext db)
    {
        var office=new MSalesOffice {SoName="北大阪営業所"};
        var employee=new MEmployee {EmId=1,EmName="検証担当者",EmHiredate=DateTime.Today,So=office,Po=new MPosition {PoName="営業"},EmPassword="test"};
        var customer=new MClient {ClName="検証顧客",So=office};
        var product=new MProduct {PrName="キーボード",Price=1000,PrReleaseDate=DateTime.Today,
            Sc=new MSmallClassification {ScName="入力機器",Mc=new MMajorClassification {McName="電子機器"}},Ma=new MMaker {MaName="検証メーカー"}};
        foreach (var index in Enumerable.Range(1,5)) db.Add(new TOrder {So=office,Em=employee,Cl=customer,OrDate=DateTime.Today,
            OrStateFlag=index==5?1:0,TOrderDetails=[new TOrderDetail {Pr=product,OrQuantity=2,OrTotalPrice=2000}]});
        db.Add(new TStock {Pr=product,StQuantity=50});
        foreach (var entry in db.ChangeTracker.Entries())
        foreach (var property in entry.Properties.Where(p=>p.Metadata.ClrType==typeof(string) && !p.Metadata.IsNullable && p.CurrentValue==null)) property.CurrentValue="test";
        db.SaveChanges();
    }
    private sealed class DatabaseSource : IOrderHideDataSource
    {
        public Task<OrderHideResult> HideAsync(int orderId,int employeeId,string reason)=>Task.Run(()=>Hide(orderId,reason,employeeId));
    }
    private sealed class ControlledSource : IOrderHideDataSource
    {
        public readonly List<(int Id,string Reason)> Calls=[];
        public TaskCompletionSource<OrderHideResult>? Pending;
        public Func<int,OrderHideResult> Handler=id=>new(id,true,"success");
        public Task<OrderHideResult> HideAsync(int orderId,int employeeId,string reason)
        {Calls.Add((orderId,reason));return Pending?.Task ?? Task.FromResult(Handler(orderId));}
    }
    private sealed class BindingListener : TraceListener
    {
        public override void Write(string? message) {if (!string.IsNullOrWhiteSpace(message)) bindingErrors.Add(message);}
        public override void WriteLine(string? message)=>Write(message);
    }
}
