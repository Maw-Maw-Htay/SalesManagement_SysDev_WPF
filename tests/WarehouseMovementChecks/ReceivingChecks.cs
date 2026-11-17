using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using SalesManagement_SysDev;

internal static partial class Program
{
    private static WarehouseMovementRecord Receipt(int id, int state, int flag = 0, string? reason = null) =>
        new(WarehouseMovementKind.Inbound, id, id + 100, 1, id == 3 ? "試験部品メーカー" : "試験メーカー",
            "", null, 1, "試験担当者", new DateTime(2026, 10, 1).AddDays(id), state, flag, reason, 1, 8, "P-0003 試験部品")
        {
            PurchaseDate = new DateTime(2026, 9, 1).AddDays(id),
            ReceiptProducts = [new(3, "4900000000003", "試験部品"), new(4, "4900000000004", "別の商品")]
        };

    private static WarehouseMovementDetail ReceiptDetail(WarehouseMovementRecord record)
    {
        var line = new WarehouseMovementLine(3, "P-0003", "試験部品", 8, 12) { SafetyStock = 10 };
        var purchase = new PurchaseOrderRecord(record.ReferenceId, record.PurchaseDate!.Value, 1, record.PartnerName, 1, "発注担当",
            0, null, 1, 10, 8);
        return new(record, [line]) { RelatedPurchase = new PurchaseOrderDetail(purchase,
            [new PurchaseOrderLineRecord(1, 3, "P-0003", "試験部品", 10, 8, 12, 10)]) { OfficeName = "試験営業所" } };
    }

    private static void CheckReceivingUi(bool render)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AppSession.Employee = Employee("管理者");
        try
        {
            var assembly = typeof(F_Main).Assembly;
            foreach (var dictionary in new[] { "Colors", "Typography", "Controls", "IconResources", "Layouts" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    { Source = new Uri($"/{assembly.GetName().Name};component/Resources/{dictionary}.xaml", UriKind.Relative) });
            var records = Enumerable.Range(1, 47).Reverse().Select(i => i == 46 ? Receipt(i, 0, 2)
                : i == 47 ? Receipt(i, 1, 2, "試験の非表示理由") : Receipt(i, i % 2)).ToArray();
            var directory = new WarehouseMovementDirectory(records, [new(1, "試験メーカー")], [new(1, "試験担当者")], new(45, 22, 23));
            var vm = (WarehouseMovementViewModel)NewReceiving(typeof(WarehouseMovementViewModel), WarehouseMovementKind.Inbound,
                (Func<WarehouseMovementDirectory>)(() => directory));
            var view = (WarehouseMovementManagementView)NewReceiving(typeof(WarehouseMovementManagementView), vm);
            var host = new Window
            {
                Width = 920, Height = 540, ShowActivated = false, ShowInTaskbar = false,
                Left = -2000, WindowStartupLocation = WindowStartupLocation.Manual, Content = view
            };
            host.Show();
            ReceivingWait(() => !vm.IsLoading && vm.Rows.Count > 0, "receipt data is ready");
            host.UpdateLayout();
            if (render) SaveReceivingPreview(view, "receiving-list");
            var grid = (DataGrid)view.FindName("MovementsGrid");
            var scroll = ReceivingVisuals(grid).OfType<ScrollViewer>().First();
            Check(grid.IsReadOnly && scroll.ScrollableHeight > 0 && scroll.ScrollableWidth < 1 && !vm.IsFiltersExpanded,
                "compact read-only list fits typical width and starts with filters collapsed");
            Check(vm.Rows.Count == 20 && vm.VisibleCount == 45 && vm.PendingCount == 22 && vm.ConfirmedCount == 23 && vm.HiddenCount == 2,
                "the four tabs count actual records within scope");
            vm.SetPage(3);
            Check(vm.Rows.Count == 5 && !vm.CanNext, "paging shows the final five visible records");
            foreach (var tabName in new[] { "PendingTab", "ConfirmedTab", "HiddenTab", "AllTab" })
            {
                var tab = (MenuItem)view.FindName(tabName);
                tab.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                host.UpdateLayout();
                var icon = ReceivingVisuals(tab).OfType<LucideIcon>().Single();
                var labels = ReceivingVisuals(tab).OfType<TextBlock>().ToArray();
                Check(tab.IsChecked && ((SolidColorBrush)tab.Background).Color == ((SolidColorBrush)view.FindResource("OrderHeader")).Color
                    && ((SolidColorBrush)icon.Stroke).Color == Colors.White && labels.Length == 2
                    && labels.All(x => ((SolidColorBrush)x.Foreground).Color == Colors.White), $"{tabName} has dark navy and white label, count and icon");
            }
            vm.IsFiltersExpanded = true;
            host.UpdateLayout();
            if (render) SaveReceivingPreview(view, "receiving-filters");
            Check(grid.ActualHeight > 80 && ((ScrollViewer)view.FindName("AdvancedScroll")).ScrollableHeight > 0,
                "expanded filters scroll separately without hiding the results area");
            vm.IsFiltersExpanded = false;
            var quick = (TextBox)view.FindName("QuickSearch");
            quick.Text = "部品メーカー";
            vm.PurchaseNumber = "PO-00103";
            vm.ReceiptProductCode = "P-000003";
            vm.ReceiptProductName = "試験";
            ReceivingWait(() => vm.Rows.Count == 1 && vm.Rows[0].Data.Id == 3, "supplier quick search combines with purchase ID and the same product line");
            vm.ReceiptProductName = "別の商品";
            ReceivingWait(() => vm.Rows.Count == 0, "product code and name cannot match different receipt lines");
            vm.ReceiptNumber = "PO-123";
            ReceivingWait(() => vm.SearchError.Contains("入庫番号"), "identifier validation rejects the wrong prefix");
            vm.ReceiptNumber = "";
            vm.PurchaseFrom = new DateTime(2026, 10, 1); vm.PurchaseTo = new DateTime(2026, 9, 2);
            ReceivingWait(() => vm.SearchError.Contains("発注日"), "reversed purchase date range is rejected");
            vm.ClearConditions();
            ReceivingWait(() => vm.Rows.Count == 20 && vm.Page == 1 && quick.Text == "", "clear restores the first page");
            vm.SelectReceipt(vm.Rows.First(x => x.Data.IsUnconfirmed));
            Check(vm.CanConfirmReceipt && vm.Rows.Count(x => x.SelectedForConfirmation) == 1, "an eligible pending receipt is selectable for single confirmation");
            var historyButton = (Button)view.FindName("HistoryButton");
            historyButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            ReceivingWait(() => vm.Rows.Count == 20 && vm.Rows.All(x => x.Data.IsConfirmed), "history includes only completed records");
            Check(vm.Rows[0].Data.Id == 47 && !vm.CanConfirmReceipt && vm.InboundListTitle == "入庫履歴", "completed history is newest first and has no confirmation action");
            if (render) SaveReceivingPreview(view, "receiving-history");

            var snapshot = ReceiptDetail(records[0]);
            var detail = new F_WarehouseMovementDetail(snapshot) { Owner = host };
            detail.Show(); detail.UpdateLayout();
            var tabs = ReceivingVisuals(detail).OfType<TabControl>().Single();
            Check(tabs.Items.Cast<TabItem>().Select(x => x.Header).SequenceEqual(new[] { "基本情報", "関連発注情報", "在庫情報" }),
                "receiving detail displays only its supported related tabs");
            var detailVm = (WarehouseMovementDetailViewModel)detail.DataContext;
            Check(detailVm.ReceiptLines.Single().DifferenceText == "-2" && detailVm.StockLines.Single().StockConditionText == "正常"
                && detailVm.CanRestore && !detailVm.CanConfirm, "detail compares stored quantities and allows safe restore only");
            if (render) SaveReceivingPreview(detail, "receiving-detail");
            detail.Close();
            Check(!new WarehouseMovementDetailViewModel(snapshot, true).CanRestore
                && !new WarehouseMovementDetailViewModel(ReceiptDetail(Receipt(5, 1, 2))).CanRestore,
                "history and legacy reasonless records do not expose restore");
            var purchase = (Window)NewReceiving(typeof(F_HattyuDetail), snapshot.RelatedPurchase!, true);
            Check(!((PurchaseOrderDetailViewModel)purchase.DataContext).CanHide, "related purchase detail opens without write actions");
            purchase.Close();
            var confirm = new F_WarehouseMovementConfirm(ReceiptDetail(Receipt(2, 0))) { Owner = host };
            confirm.Show(); confirm.UpdateLayout();
            var confirmationVm = (WarehouseMovementConfirmViewModel)confirm.DataContext;
            Check(confirmationVm.CanConfirm && confirmationVm.ImpactText.Contains("在庫数量が更新")
                && confirmationVm.ExpectedLines.Single().Quantity == 8, "confirmation uses recorded lines and documents the actual stock update");
            if (render) SaveReceivingPreview(confirm, "receiving-confirmation");
            confirmationVm.IsProcessing = true;
            Check(!confirmationVm.CanConfirm, "confirmation cannot be submitted again while processing");
            confirmationVm.IsProcessing = false;
            confirm.Close();
            host.Close();
        }
        finally { AppSession.Employee = null; app.Shutdown(); }
    }

    private static void CheckReceivingRead()
    {
        using var db = new SalesManagementContext();
        var admin = db.MEmployees.AsNoTracking().Where(x => x.EmFlag == 0 && x.Po.PoFlag == 0 && x.Po.PoName == "管理者")
            .Select(x => new { x.EmId, x.SoId }).First();
        AppSession.Employee = Employee("管理者");
        AppSession.Employee.EmId = admin.EmId;
        AppSession.Employee.SoId = admin.SoId;
        var directory = WarehousingService.Load();
        Check(directory.Records.Count == db.TWarehousings.Count() && directory.Summary.Confirmed ==
            db.TWarehousings.Count(x => x.WaFlag == 0 && x.WaShelfFlag == 1),
            "live receiving list and completed totals match scoped records");
        var record = directory.Records.First(x => x.ManagementFlag == 0 && x.LineCount > 0);
        var original = db.TWarehousings.AsNoTracking().Single(x => x.WaId == record.Id);
        var details = WarehousingService.LoadDetail(record.Id)!;
        Check(details.RelatedPurchase?.Order.Id == original.HaId && details.Record.ReferenceId == original.HaId,
            "live purchase details use the stored purchase ID");
        var realLines = db.TWarehousingDetails.AsNoTracking().Where(x => x.WaId == record.Id)
            .Select(x => new { x.PrId, x.WaQuantity }).ToList();
        Check(details.Lines.Select(x => (x.ProductId, x.Quantity)).Order().SequenceEqual(realLines.Select(x => (x.PrId, x.WaQuantity)).Order()),
            "live receiving detail shows the recorded quantities");
        foreach (var line in details.Lines)
        {
            var balances = db.TStocks.AsNoTracking().Where(x => x.PrId == line.ProductId && x.StFlag == 0)
                .Select(x => x.StQuantity).ToArray();
            Check(line.CurrentStock == (balances.Length == 1 ? balances[0] : (int?)null), "live inventory panel matches the valid product balance");
        }
        Check(directory.Records.Where(x => x.ManagementFlag == 2 && string.IsNullOrWhiteSpace(x.HiddenReason))
            .All(x => !WarehousingService.IsRestorable(x)), "legacy reasonless records are kept hidden");
        Check(db.TWarehousings.AsNoTracking().Single(x => x.WaId == record.Id).WaShelfFlag == original.WaShelfFlag,
            "viewing receipt information preserves its processing state");
    }

    private static object NewReceiving(Type type, params object[] args) =>
        Activator.CreateInstance(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, args, null)!;

    private static IEnumerable<DependencyObject> ReceivingVisuals(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in ReceivingVisuals(child)) yield return descendant;
        }
    }

    private static void ReceivingWait(Func<bool> assertion, string message)
    {
        var clock = Stopwatch.StartNew();
        while (!assertion() && clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }
        Check(assertion(), message);
    }

    private static void SaveReceivingPreview(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(AppContext.BaseDirectory, name + ".png"));
        encoder.Save(stream);
    }
}
