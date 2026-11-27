using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SalesManagement_SysDev;

internal static partial class Program
{
    private static void CheckHorizontalStepper(QuantityUpDown stepper)
    {
        stepper.ApplyTemplate();
        Point Position(string name) => ((FrameworkElement)stepper.Template.FindName(name, stepper))
            .TransformToAncestor(stepper).Transform(new Point());
        var minus = Position("PART_Decrease");
        var input = Position("PART_Input");
        var plus = Position("PART_Increase");
        Check(minus.X < input.X && input.X < plus.X && Math.Abs(minus.Y - plus.Y) < 1,
            "quantity stepper puts minus, input and plus on one line");
    }

    private static void CheckManufacturerFiltering(OrderRegistrationViewModel model, QuantityRegistrationSource source,
        RegistrationLookup manufacturer, RegistrationLookup lookup, Button details)
    {
        var makerInput = (TextBox)manufacturer.FindName("QueryInput");
        var makerPopup = (Popup)manufacturer.FindName("SuggestionsPopup");

        Check(makerInput.IsEnabled && !makerInput.IsReadOnly && makerInput.Text == "" && manufacturer.Placeholder == "すべてのメーカー"
            && model.SelectedMakerId == 0
            && model.MakerOptions.Count == 4 && model.Products.Count == 35,
            "manufacturer defaults to all and includes products beyond the former 30-option limit");
        TypeManufacturerName(manufacturer, "pPl");
        Check(model.MakerSuggestions is [{ Name: "Apple" }] && model.SelectedMakerId == 0 && model.Products.Count == 35,
            "typing a partial manufacturer name filters the choices before applying the product filter");
        RaiseLookupKey(makerInput, Key.Enter);
        FlushBindings();
        Check(model.SelectedMakerId == 1 && model.Products.Count == 12 && model.Products.All(product => product.MakerName == "Apple")
            && makerInput.Text == "Apple" && !makerPopup.IsOpen && !model.HasUnsavedChanges,
            "Enter chooses the matching manufacturer and filters products without changing draft inputs");
        TypeManufacturerName(manufacturer, "該当しないメーカー");
        Check(model.MakerSuggestions.Count == 0 && model.SelectedMakerId == 1, "unmatched manufacturer text preserves the active filter");
        RaiseLookupKey(makerInput, Key.Escape);
        FlushBindings();
        Check(makerInput.Text == "Apple" && !makerPopup.IsOpen, "Escape restores the displayed manufacturer from the active filter");
        foreach (var (query, expected) in new[] { ("キーボード", 1), ("PR-0004", 4), ("4900000000004", 4) })
        {
            model.ProductSearch = query;
            Check(model.Products.Single().PrId == expected, "manufacturer-scoped name, code or barcode search: " + query);
        }
        model.ProductSearch = "PR-0002";
        Check(model.Products.Count == 0, "product code search excludes other manufacturers");
        ChooseManufacturer(manufacturer, "Samsung");
        Check(model.ProductSearch == "PR-0002" && model.Products.Single().PrId == 2
            && source.ProductLoads == 1 && source.StockBatches == 1 && source.StockSelections == 0,
            "changing manufacturer retains a typed search and reuses the loaded catalog and stock");
        var search = (TextBox)lookup.FindName("QueryInput");
        var suggestions = (ListBox)lookup.FindName("SuggestionList");
        RaiseLookupKey(search, Key.Down);
        FlushBindings();
        Check(suggestions.SelectedItem is RegistrationProduct { PrId: 2 }, "Down selects the first manufacturer-scoped product suggestion");
        RaiseLookupKey(suggestions, Key.Enter);
        FlushBindings();
        var selected = model.SelectedProduct;
        Check(selected is { PrId: 2 } && !((Popup)lookup.FindName("SuggestionsPopup")).IsOpen,
            "Enter commits the selected suggestion and closes the popup");
        TypeManufacturerName(manufacturer, "");
        RaiseLookupKey(makerInput, Key.Down);
        var makerList = (ListBox)manufacturer.FindName("SuggestionList");
        Check(makerList.SelectedItem is ProductMasterOption { Id: 0 }, "Down highlights the all-manufacturers option after clearing the search");
        RaiseLookupKey(makerList, Key.Enter);
        FlushBindings();
        Check(ReferenceEquals(model.SelectedProduct, selected) && model.Products.Count == 35 && details.IsEnabled,
            "showing all manufacturers keeps the current valid selection");
        ChooseManufacturer(manufacturer, "Samsung");
        Check(ReferenceEquals(model.SelectedProduct, selected) && source.StockSelections == 1 && details.IsEnabled,
            "switching to a matching manufacturer keeps the selection without reloading stock");
        ChooseManufacturer(manufacturer, "Dell");
        Check(model.SelectedProduct == null && model.ProductSearch == "" && model.Products.All(product => product.MakerName == "Dell")
            && !details.IsEnabled && !model.HasUnsavedChanges, "switching to a different manufacturer clears only the pending selection");
        ChooseManufacturer(manufacturer, "すべてのメーカー");
    }

    private static void TypeManufacturerName(RegistrationLookup manufacturer, string query)
    {
        ((TextBox)manufacturer.FindName("QueryInput")).SetCurrentValue(TextBox.TextProperty, query);
        ((Popup)manufacturer.FindName("SuggestionsPopup")).IsOpen = true;
        FlushBindings();
    }

    private static void ChooseManufacturer(RegistrationLookup manufacturer, string query)
    {
        TypeManufacturerName(manufacturer, query);
        RaiseLookupKey((TextBox)manufacturer.FindName("QueryInput"), Key.Enter);
        FlushBindings();
    }

    private static void RaiseLookupKey(FrameworkElement element, Key key) =>
        element.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(element),
            Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    private static void CheckManufacturerLayout(FrameworkElement view)
    {
        var productInput = (OrderProductInput)view.FindName("ProductInput");
        var fields = (FrameworkElement)productInput.FindName("ProductSearchFields");
        var maker = (FrameworkElement)productInput.FindName("ManufacturerFilter");
        var search = (FrameworkElement)productInput.FindName("ProductLookup");
        var details = (FrameworkElement)productInput.FindName("ProductDetailsButton");
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(fields).TransformBounds(new Rect(element.RenderSize));
        var makerBounds = Bounds(maker);
        var searchBounds = Bounds(search);
        var detailBounds = Bounds(details);
        Check(makerBounds.Bottom < searchBounds.Top && Math.Abs(makerBounds.Left - searchBounds.Left) < 1
            && Math.Abs(makerBounds.Width - fields.ActualWidth) < 1
            && Math.Abs(searchBounds.Top - detailBounds.Top) < 1 && searchBounds.Right < detailBounds.Left,
            "the manufacturer field spans the card above the aligned product lookup and details button");
    }

    private static void CheckRegistrationRow(FrameworkElement root, DataGrid grid, OrderRegistrationViewModel vm)
    {
        var missing = vm.Lines.Single(line => line.ProductId == 3);
        grid.ScrollIntoView(missing);
        root.UpdateLayout();
        FlushBindings();
        var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(missing);
        var product = Descendants<TextBlock>(row).Single(text => text.Text == missing.ProductName);
        var code = Descendants<TextBlock>(row).Single(text => text.Text == missing.ProductCode);
        var warning = Descendants<TextBlock>(row).Single(text => text.Text == "在庫情報が未登録です");
        var icon = Descendants<LucideIcon>((DependencyObject)warning.Parent).Single();
        var stepper = Descendants<QuantityUpDown>(row).Single();
        var delete = Descendants<Button>(row).Single(button => Equals(button.ToolTip, "この商品行を削除"));
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(row).TransformBounds(new Rect(element.RenderSize));
        DataGridCell Cell(string heading) => Descendants<DataGridCell>(row).Single(cell => Equals(cell.Column.Header, heading));
        var productCell = Cell("商品コード・商品名");
        var thumbnail = Descendants<Image>(productCell).Single();
        Check(thumbnail.Stretch == Stretch.Uniform && thumbnail.IsVisible == (thumbnail.Source != null)
            && (thumbnail.IsVisible ? Bounds(thumbnail).Right < Bounds(product).Left : Bounds(product).Left - Bounds(productCell).Left < 16)
            && !Descendants<LucideIcon>(productCell).Any(element => Equals(element.Data, Application.Current.FindResource("IconProduct"))),
            "wide registration rows show available product photos and collapse the thumbnail space when a photo is missing");
        var unit = Descendants<TextBlock>(Cell("単価")).Single();
        var subtotal = Descendants<TextBlock>(Cell("小計")).Single();
        Check(warning.Foreground is SolidColorBrush { Color: var warningColor } && warningColor == Color.FromRgb(0xB4, 0x53, 0x09)
            && icon.IsVisible && icon.Stroke is SolidColorBrush { Color: var iconColor } && iconColor == Color.FromRgb(0xD9, 0x77, 0x06)
            && warning.FontSize == 12 && warning.FontWeight == FontWeights.SemiBold,
            $"missing stock uses 12-DIP semibold amber text and an orange icon ({warning.Foreground}, {icon.Stroke}, {warning.FontSize}, {warning.FontWeight})");
        var verticalGap = Bounds(warning).Top - Bounds(code).Bottom;
        var iconGap = Bounds(warning).Left - Bounds(icon).Right;
        var productCellEnd = Bounds(productCell).Right;
        Check(Math.Abs(verticalGap - 3) < 1 && Math.Abs(iconGap - 4) < 1 && Bounds(warning).Right <= productCellEnd + 0.5
            && Bounds(product).Bottom <= Bounds(code).Top + 0.5 && product.FontWeight == FontWeights.Bold
            && product.TextTrimming == TextTrimming.CharacterEllipsis && Equals(product.ToolTip, product.Text),
            $"warning stays under the product name and code ({verticalGap:F1} vertical gap, {iconGap:F1} icon gap, {productCellEnd - Bounds(warning).Right:F1} remaining width)");
        Check(Math.Abs(CenterY(Bounds(unit)) - CenterY(Bounds(stepper))) < 1
            && Math.Abs(CenterY(Bounds(subtotal)) - CenterY(Bounds(stepper))) < 1
            && unit.TextAlignment == TextAlignment.Right && subtotal.TextAlignment == TextAlignment.Right
            && Math.Abs((Bounds(Cell("操作")).Left + Bounds(Cell("操作")).Right) - (Bounds(delete).Left + Bounds(delete).Right)) < 2,
            "prices and subtotals are right-aligned, and delete is centered");
        foreach (var (productId, message, isWarning) in new[] { (1, "在庫不足：7点", true), (2, "在庫がありません", true), (4, "在庫：5点", false) })
        {
            var line = vm.Lines.Single(line => line.ProductId == productId);
            grid.ScrollIntoView(line);
            root.UpdateLayout();
            FlushBindings();
            var stockRow = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(line);
            var stockText = Descendants<TextBlock>(stockRow).Single(text => text.Text == message);
            var stockIcon = Descendants<LucideIcon>((DependencyObject)stockText.Parent).Single();
            Check(stockIcon.IsVisible == isWarning && Equals(stockText.Foreground,
                Application.Current.FindResource(isWarning ? "ThemeDangerText" : "ThemeSecondaryText")),
                "existing row style is preserved for " + message);
        }
    }

    private static void CheckRegistrationFooter(F_OrderRegistration view, string layout)
    {
        var footer = (Border)view.FindName("RegistrationFooter");
        var cancel = (Button)view.FindName("CancelButton");
        var save = (Button)view.FindName("SaveDraftButton");
        var register = (Button)view.FindName("RegisterButton");
        var buttons = Descendants<Button>(footer).ToArray();
        Rect Bounds(Button button) => button.TransformToAncestor(footer).TransformBounds(new Rect(button.RenderSize));
        Check(buttons.SequenceEqual(new[] { cancel, save, register })
            && buttons.Select(button => button.Content?.ToString()).SequenceEqual(new[] { "キャンセル", "下書き保存", "受注を登録" })
            && buttons.All(button =>
            {
                var bounds = Bounds(button);
                return button.IsVisible && Math.Abs(bounds.Height - 42) < 1 && bounds.Left >= 0 && bounds.Top >= 0
                    && bounds.Right <= footer.ActualWidth + 1 && bounds.Bottom <= footer.ActualHeight + 1;
            }),
            "exactly three unclipped footer actions at " + layout);
        var cancelBounds = Bounds(cancel);
        var saveBounds = Bounds(save);
        var registerBounds = Bounds(register);
        var gap = footer.ActualWidth < 480 ? 10 : 12;
        Check(Math.Abs(footer.ActualWidth - registerBounds.Right - footer.Padding.Right) < 1
            && Math.Abs(saveBounds.Left - cancelBounds.Right - gap) < 1 && Math.Abs(registerBounds.Left - saveBounds.Right - gap) < 1
            && Math.Abs(cancelBounds.Top - saveBounds.Top) < 1 && Math.Abs(saveBounds.Top - registerBounds.Top) < 1,
            "footer keeps Cancel, Draft and Register together at the right at " + layout);
    }

    private static double CenterY(Rect rect) => rect.Top + rect.Height / 2;

    private static void CheckDraftNavigation()
    {
        var store = new DraftStoreStub();
        var source = new QuantityRegistrationSource();
        var choice = OrderDraftNavigationChoice.ContinueEditing;
        var prompts = 0;
        var navigations = 0;
        using var model = new OrderRegistrationViewModel(() => navigations++, () => { prompts++; return choice; },
            source: source, draftStore: store);
        model.Load();
        Check(WaitFor(model.TryApproveCloseAsync()) && prompts == 0, "pristine registration leaves without a warning");
        model.CustomerSearch = "入力中の顧客";
        model.SetDateInputText(false, "2026/11/");
        model.DeliveryDate = null;
        model.SelectedProduct = model.Products[2];
        model.Quantity = "3";
        model.Remarks = "入力途中の備考";
        model.CloseCommand.Execute(null);
        Check(prompts == 1 && navigations == 0 && store.Saves == 0, "continue editing cancels navigation");

        choice = OrderDraftNavigationChoice.SaveAndLeave;
        store.FailSave = true;
        model.CloseCommand.Execute(null);
        WaitUntil(() => !model.IsBusy);
        Check(navigations == 0 && model.HasUnsavedChanges && model.Remarks == "入力途中の備考"
            && model.Quantity == "3" && model.SelectedProduct?.PrId == 3 && model.DateInputError.Length > 0
            && model.Feedback.Contains("保存できませんでした"), "failed save-and-leave retains form and stays on the same page");

        store.FailSave = false;
        Wait(model.SaveDraftAsync());
        Check(model.IsDraft && !model.HasUnsavedChanges && model.IsDraftSaveFeedback && !model.CanRegister
            && store.Saved?.Form.Header is { Customer.Id: null, Office.Id: 1, OrderDate.UnparsedText: "2026/11/",
                PendingProduct.Id: 3, PendingQuantity: "3", PendingUnitPrice: 2000m },
            "explicit draft save accepts partial data and marks it clean");
        model.CloseCommand.Execute(null);
        Check(navigations == 1 && prompts == 2, "leaving an unchanged saved draft skips the confirmation");

        using var reopened = new OrderRegistrationViewModel(() => navigations++, () => { prompts++; return choice; },
            source: source, draftStore: store, draft: store.Saved);
        reopened.Load();
        Check(reopened.IsDraft && !reopened.HasUnsavedChanges && reopened.OfficeSearch == model.OfficeSearch
            && reopened.CustomerSearch == model.CustomerSearch && reopened.Quantity == "3" && reopened.DeliveryDate == null
            && reopened.Remarks == "入力途中の備考" && reopened.DateInputError.Length > 0,
            "the same registration form restores unfinished inputs and defaults");
        reopened.Remarks = "更新した備考";
        reopened.CloseCommand.Execute(null);
        WaitUntil(() => navigations == 2);
        Check(store.Saved?.Form.Header.Remarks == "更新した備考" && !reopened.HasUnsavedChanges,
            "save-and-leave updates the saved draft before running the originally selected callback");
        reopened.Remarks = "破棄する変更";
        choice = OrderDraftNavigationChoice.DiscardAndLeave;
        reopened.CloseCommand.Execute(null);
        Check(navigations == 3 && store.Saved?.Form.Header.Remarks == "更新した備考",
            "discard-and-leave preserves the previous draft");
        CheckDraftNavigationDialog();
    }

    private static void CheckDraftNavigationDialog()
    {
        var host = new Window { Width = 1100, Height = 560, Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false };
        var store = new DraftStoreStub();
        var navigations = 0;
        using var model = new OrderRegistrationViewModel(() => navigations++, () => F_OrderNotice.ConfirmDraftNavigation(host),
            source: new QuantityRegistrationSource(), draftStore: store);
        model.Load();
        using var view = new F_OrderRegistration();
        var root = (FrameworkElement)view.Content;
        view.Content = null;
        root.DataContext = model;
        host.Content = root;
        var remarks = (TextBox)view.FindName("RemarksInput");
        var cancel = (Button)view.FindName("CancelButton");
        var click = typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        host.Show();
        try
        {
            FlushBindings();
            remarks.Text = "移動前に保持する入力";
            FocusManager.SetFocusedElement(host, remarks);
            FlushBindings();
            var layer = AdornerLayer.GetAdornerLayer(root) ?? throw new InvalidOperationException("Expected an owner adorner layer.");
            var originalAdorners = layer.GetAdorners(root)?.Length ?? 0;

            void Interact(Action<Window, Button, Button, Button> interact)
            {
                InteractWithModal(host, () => click.Invoke(cancel, null), modal =>
                {
                    modal.UpdateLayout();
                    FlushBindings();
                    var buttons = Descendants<Button>(modal).Where(button => button.IsVisible).ToArray();
                    Button Find(string caption) => buttons.Single(button => Equals(button.Content, caption));
                    Check(modal.Owner == host && modal.WindowStartupLocation == WindowStartupLocation.CenterOwner
                        && !IsWindowEnabled(new WindowInteropHelper(host).Handle)
                        && layer.GetAdorners(root)?.Length == originalAdorners + 1,
                        "the unsaved notice is centered, modal and dims the owner");
                    interact(modal, Find("破棄して移動"), Find("入力を続ける"), Find("一時保存して移動"));
                });
                Check(IsWindowEnabled(new WindowInteropHelper(host).Handle) && (layer.GetAdorners(root)?.Length ?? 0) == originalAdorners,
                    "closing the notice restores the owner and removes the overlay");
            }

            Interact((modal, discard, stay, save) =>
            {
                Check(modal.Title == "未保存の変更があります"
                    && Descendants<TextBlock>(modal).Count(text => text.IsVisible && text.Text is
                        "受注の入力内容がまだ保存されていません。" or "移動すると、入力内容が失われる可能性があります。" or "どのようにしますか？") == 3,
                    "the modal shows the requested title and three messages");
                Check(stay.IsDefault && !discard.IsDefault && !save.IsDefault, "the default action continues editing");
                CheckFooter(modal, discard, stay, save, stacked: false);
                Render((FrameworkElement)modal.Content, "unsaved-changes-dialog");
                click.Invoke(stay, null);
            });
            Check(navigations == 0 && store.Saves == 0 && model.Remarks == "移動前に保持する入力"
                && ReferenceEquals(FocusManager.GetFocusedElement(host), remarks), "continue preserves the input and restores editing focus");

            host.Width = 560;
            host.UpdateLayout();
            Interact((modal, discard, stay, save) =>
            {
                CheckFooter(modal, discard, stay, save, stacked: false);
                var close = Descendants<Button>(modal).Single(button => button.IsVisible && Equals(button.ToolTip, "閉じる"));
                click.Invoke(close, null);
            });
            host.Width = 430;
            host.UpdateLayout();
            Interact((modal, discard, stay, save) =>
            {
                CheckFooter(modal, discard, stay, save, stacked: true);
                stay.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(stay), Environment.TickCount, Key.Escape)
                    { RoutedEvent = Keyboard.KeyDownEvent });
            });
            Check(navigations == 0 && store.Saves == 0 && model.Remarks == "移動前に保持する入力",
                "the X button and Escape cancel navigation");

            store.FailSave = true;
            Interact((_, _, _, save) => click.Invoke(save, null));
            WaitUntil(() => !model.IsBusy);
            Check(navigations == 0 && model.Remarks == "移動前に保持する入力" && model.Feedback.Contains("保存できませんでした"),
                "the save action leaves the editor and input available on failure");
            store.FailSave = false;
            Interact((_, _, _, save) => click.Invoke(save, null));
            WaitUntil(() => navigations == 1);
            Check(store.Saved?.Form.Header.Remarks == "移動前に保持する入力" && !model.HasUnsavedChanges,
                "the save action completes before navigation");

            model.Remarks = "今回の変更だけ破棄";
            Interact((_, discard, _, _) => click.Invoke(discard, null));
            Check(navigations == 2 && store.Saves == 2 && store.Saved?.Form.Header.Remarks == "移動前に保持する入力",
                "the discard action navigates without replacing or deleting the saved draft");
        }
        finally { host.Close(); }

        static void CheckFooter(Window modal, Button discard, Button stay, Button save, bool stacked)
        {
            var surface = (FrameworkElement)modal.Content;
            Rect Bounds(FrameworkElement element) => element.TransformToAncestor(surface).TransformBounds(new Rect(element.RenderSize));
            var destructive = Bounds(discard);
            var keep = Bounds(stay);
            var temporary = Bounds(save);
            Check((stacked ? destructive.Bottom < keep.Top : keep.Left - destructive.Right >= 47.5 && Math.Abs(destructive.Top - keep.Top) < 1)
                && temporary.Left - keep.Right >= 11.5 && Math.Abs(keep.Top - temporary.Top) < 1 && temporary.Right <= surface.ActualWidth,
                $"the footer separates destructive and safe actions without overlap ({surface.ActualWidth:F1} wide, {destructive}, {keep}, {temporary})");
            foreach (var button in new[] { discard, stay, save })
            {
                var caption = Descendants<TextBlock>(button).Single(text => Equals(text.Text, button.Content));
                var text = new FormattedText(caption.Text, CultureInfo.CurrentCulture, caption.FlowDirection,
                    new Typeface(caption.FontFamily, caption.FontStyle, caption.FontWeight, caption.FontStretch),
                    caption.FontSize, caption.Foreground, VisualTreeHelper.GetDpi(caption).PixelsPerDip);
                Check(caption.ActualWidth + 1 >= text.Width && Math.Abs(button.ActualHeight - 42) < 1,
                    "the action caption fits its button");
            }
        }
    }

    private static void WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < deadline) Wait(Task.Delay(10));
        if (!condition()) throw new TimeoutException("Timed out waiting for the WPF operation.");
    }

    private sealed class DraftStoreStub : IOrderDraftStore
    {
        public OrderDraftSnapshot? Saved { get; private set; }
        public int Saves { get; private set; }
        public bool FailSave { get; set; }
        public OrderDraftToken Save(OrderDraftToken token, OrderDraftForm form)
        {
            Saves++;
            if (FailSave) throw new InvalidOperationException("Simulated connection failure.");
            var committed = new OrderDraftToken(token.Id, BitConverter.GetBytes((long)Saves));
            Saved = new OrderDraftSnapshot(committed, DateTimeOffset.UtcNow, form);
            return committed;
        }
        public IReadOnlyList<OrderDraftSnapshot> List() => Saved is { } saved ? [saved] : [];
        public OrderDraftSnapshot Open(Guid id) => throw new NotSupportedException();
        public void Delete(OrderDraftToken token) => throw new NotSupportedException();
        public OrderDraftRegistration Register(OrderDraftToken token, OrderDraftForm form) => throw new NotSupportedException();
    }

    private static void CheckDraftDatabase()
    {
        const string connectionVariable = "SALESMANAGEMENT_CONNECTION_STRING";
        var previousConnection = Environment.GetEnvironmentVariable(connectionVariable);
        var previousEmployee = AppSession.Employee;
        using var configuration = new SalesManagementContext();
        var catalog = "SalesManagement_OrderDraftChecks_" + Guid.NewGuid().ToString("N");
        var connection = new SqlConnectionStringBuilder(configuration.Database.GetDbConnection().ConnectionString) { InitialCatalog = catalog };
        var testServer = Environment.GetEnvironmentVariable("ORDER_UPDATE_TEST_SERVER");
        if (!string.IsNullOrWhiteSpace(testServer)) connection.DataSource = testServer;
        Environment.SetEnvironmentVariable(connectionVariable, connection.ConnectionString);
        var created = false;
        try
        {
            using var db = new SalesManagementContext();
            created = db.Database.EnsureCreated();
            if (!created) throw new InvalidOperationException("Expected a new disposable database.");
            Seed(db);
            // Exercise first-use provisioning on a database that already has orders.
            db.Database.ExecuteSqlRaw("DROP TABLE dbo.T_OrderDraft");
            var owner = ReadEmployee(1);
            AppSession.Employee = owner;
            var store = new OrderDraftService();
            var noDraftRows = ExportCsv(new OrderListRequest(new OrderSearchCriteria(), OrderStatusTab.Draft,
                OrderListSort.OrderDate, ListSortDirection.Descending));
            Check(noDraftRows.Length == 1 && db.Database.SqlQueryRaw<int>("SELECT CASE WHEN OBJECT_ID(N'dbo.T_OrderDraft', N'U') IS NULL THEN 0 ELSE 1 END AS [Value]").Single() == 0,
                "draft export works against the old schema without creating the draft table");
            var legacyShell = NewOrderListShell();
            try
            {
                var (root, model, _) = OpenOrderList(legacyShell);
                Check(model.TotalCount == 1 && Descendants<TextBlock>(root).Single(text => text.Name == "draftCount").Text == "0"
                    && db.Database.SqlQueryRaw<int>("SELECT CASE WHEN OBJECT_ID(N'dbo.T_OrderDraft', N'U') IS NULL THEN 0 ELSE 1 END AS [Value]").Single() == 0,
                    "registered orders load and report zero drafts on a database without the draft table, with no schema write");
            }
            finally { legacyShell.Close(); }
            var firstList = store.List();
            Check(firstList.Count == 0 && store.List().Count == 0 && db.TOrders.Count() == 1 && db.TOrderDetails.Count() == 1,
                "first draft use upgrades the schema idempotently and preserves existing orders");
            var employeeRole = db.MPositions.Single();
            var secondOffice = new MSalesOffice { SoName = "第二営業所" };
            var salesRole = new MPosition { PoName = "営業" };
            var coworker = new MEmployee { EmId = 2, EmName = "別の管理者", SoId = owner.SoId, Po = employeeRole, EmHiredate = DateTime.Today };
            var salesperson = new MEmployee { EmId = 3, EmName = "営業担当者", SoId = owner.SoId, Po = salesRole, EmHiredate = DateTime.Today };
            var product = db.MProducts.Single();
            var missing = new MProduct { PrName = "在庫未登録の商品", Price = 125, Ma = new MMaker { MaName = "Dell" },
                ScId = product.ScId, PrReleaseDate = DateTime.Today };
            db.AddRange(coworker, salesperson, secondOffice, missing);
            foreach (var entry in db.ChangeTracker.Entries().Where(entry => entry.State == EntityState.Added))
                foreach (var property in entry.Properties.Where(property => property.Metadata.ClrType == typeof(string)
                    && !property.Metadata.IsNullable && property.CurrentValue == null)) property.CurrentValue = "test";
            db.SaveChanges();

            var customer = db.MClients.Single();
            var partial = new OrderDraftForm(new OrderDraftHeader(new(owner.SoId, owner.So.SoName), new(null, "IN"),
                new(null, "2026/11/"), new(null, ""), "日本語の備考", new(product.PrId, product.PrName), product.Price, "2"), []);
            var token = new OrderDraftToken(Guid.NewGuid());
            var initial = store.Save(token, partial);
            var duplicateSave = store.Save(token, partial);
            var snapshot = store.Open(token.Id);
            Check(initial.Version is { Length: 8 } && duplicateSave.Version!.SequenceEqual(initial.Version)
                && store.List().Count == 1 && snapshot.Form.Header == partial.Header && snapshot.Form.Lines.Count == 0,
                "partially entered draft persists across service instances and an identical retry resolves to the same record");
            Check(db.TOrders.Count() == 1 && db.TOrderDetails.Count() == 1 && db.OrderOperationHistories.Count() == 0
                && db.TChumons.Count() == 0 && db.TShipments.Count() == 0 && db.TSales.Count() == 0
                && db.TStocks.Single().StQuantity == 50, "draft save has no write to orders, audit, stock, shipments or sales");

            AppSession.Employee = ReadEmployee(2);
            Check(store.List().Count == 0, "another administrator sees only their own drafts");
            Reject(() => store.Open(token.Id), "other employee cannot open the draft by ID");
            Reject(() => store.Save(initial, partial), "other employee cannot update the draft");
            Reject(() => store.Delete(initial), "other employee cannot delete the draft");
            Reject(() => store.Register(initial, partial), "other employee cannot register the draft");

            AppSession.Employee = ReadEmployee(3);
            var foreignOffice = partial with { Header = partial.Header with { Office = new(secondOffice.SoId, secondOffice.SoName) } };
            Reject(() => store.Save(new OrderDraftToken(Guid.NewGuid()), foreignOffice), "sales cannot save a draft for another office");
            var previousOfficeDraft = store.Save(new OrderDraftToken(Guid.NewGuid()), partial);
            salesperson.SoId = secondOffice.SoId;
            db.SaveChanges();
            Reject(() => store.List(), "stale session office is rejected after reassignment");
            AppSession.Employee = ReadEmployee(3);
            Check(store.List().Count == 0, "sales cannot list drafts outside their current scope");
            Reject(() => store.Open(previousOfficeDraft.Id), "sales cannot reopen a draft outside their current scope");

            AppSession.Employee = owner;
            var orderDate = new DateTime(2026, 11, 21);
            var complete = new OrderDraftForm(partial.Header with
            {
                Customer = new(customer.ClId, customer.ClName), OrderDate = new(orderDate, ""), DeliveryDate = new(orderDate.AddDays(7), ""),
                Remarks = "登録時の備考", PendingProduct = new(null, ""), PendingUnitPrice = null, PendingQuantity = "1"
            }, [new(product.PrId, "PR-1", product.PrName, product.Price, 2, "2"),
                new(missing.PrId, "PR-2", missing.PrName, missing.Price, 3, "3")]);
            var edited = store.Save(initial, complete);
            Check(store.Open(token.Id).Form.Lines.SequenceEqual(complete.Lines) && !initial.Version!.SequenceEqual(edited.Version!),
                "reopened draft updates rows, quantities and prices on the same ID");
            Reject(() => store.Save(initial, partial), "stale editor cannot overwrite a newer draft");
            Reject(() => store.Delete(initial), "stale editor cannot delete a newer draft");
            Reject(() => store.Register(initial, complete), "stale editor cannot register a newer draft");
            var stalePrice = complete with { Lines = [complete.Lines[0] with { UnitPrice = 1m }] };
            Reject(() => store.Register(edited, stalePrice), "registration revalidates current catalog prices");
            var invalidQuantity = complete with { Lines = [complete.Lines[0] with { QuantityText = "" }] };
            Reject(() => store.Register(edited, invalidQuantity), "registration rejects unfinished quantity");

            var toDelete = store.Save(new OrderDraftToken(Guid.NewGuid()), partial);
            store.Delete(toDelete);
            Check(store.List().Single().Token.Id == token.Id, "deletion removes the draft from the list");
            Reject(() => store.Open(toDelete.Id), "deleted draft cannot be reopened");
            Reject(() => store.Save(toDelete, partial), "stale save cannot recreate a deleted draft");
            Reject(() => store.Register(toDelete, complete), "deleted draft cannot be registered");

            db.Database.ExecuteSqlRaw("ALTER TABLE dbo.T_OrderDraft ADD CONSTRAINT CK_DraftChecksFailure CHECK (RegisteredOrderId IS NULL)");
            try
            {
                try { store.Register(edited, complete); throw new InvalidOperationException("Expected database failure during draft completion."); }
                catch (DbUpdateException) { }
                Check(db.TOrders.Count() == 1 && db.TOrderDetails.Count() == 1 && db.OrderOperationHistories.Count() == 0
                    && store.Open(token.Id).Form.Lines.Count == 2, "final write failure rolls back order, details and audit and keeps the draft");
            }
            finally { db.Database.ExecuteSqlRaw("ALTER TABLE dbo.T_OrderDraft DROP CONSTRAINT CK_DraftChecksFailure"); }

            var first = Task.Run(() => store.Register(edited, complete));
            var second = Task.Run(() => store.Register(edited, complete));
            Wait(Task.WhenAll(first, second));
            var orderId = first.Result.OrderId;
            var order = db.TOrders.AsNoTracking().Include(order => order.TOrderDetails).Single(order => order.OrId == orderId);
            Check(second.Result.OrderId == orderId && first.Result.AlreadyRegistered != second.Result.AlreadyRegistered
                && db.TOrders.Count() == 2 && db.OrderOperationHistories.Count() == 1,
                "two concurrent registration attempts return one order and distinguish the replay");
            Check(order.OrStateFlag == 0 && order.OrFlag == 0 && order.EmId == owner.EmId && order.ClId == customer.ClId
                && order.SoId == owner.SoId && order.OrDate == orderDate && order.OrDeliveryDate == orderDate.AddDays(7)
                && order.ClCharge == complete.Header.Remarks && order.TOrderDetails.Count == 2
                && order.TOrderDetails.Sum(line => line.OrTotalPrice) == 2 * product.Price + 3 * missing.Price
                && db.TChumons.Count() == 0 && db.TShipments.Count() == 0 && db.TSales.Count() == 0 && db.TStocks.Single().StQuantity == 50,
                "final registration creates a standard unconfirmed order with saved data and no stock or downstream effect");
            Check(store.List().Count == 0, "a converted draft disappears from the active list");
            Reject(() => store.Open(token.Id), "converted draft cannot be reopened");
            Reject(() => store.Save(edited, partial), "converted draft cannot be recreated by a stale save");
            CheckDraftShell(db, store, partial, customer.ClId, missing.PrId);
            CheckOrderListWorkspace(db);
        }
        finally
        {
            AppSession.Employee = previousEmployee;
            try
            {
                if (created && connection.InitialCatalog == catalog && catalog.StartsWith("SalesManagement_OrderDraftChecks_", StringComparison.Ordinal))
                {
                    using var cleanup = new SalesManagementContext();
                    cleanup.Database.EnsureDeleted();
                    Console.WriteLine("Disposable draft database removed: " + catalog);
                }
            }
            finally { Environment.SetEnvironmentVariable(connectionVariable, previousConnection); }
        }
    }

    private static void CheckDraftShell(SalesManagementContext db, OrderDraftService store, OrderDraftForm partial,
        int customerId, int productId)
    {
        var shell = new F_Main { WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000,
            ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        var host = (ContentControl)shell.FindName("pageHost");
        var sidebar = ((FrameworkElement)shell.FindName("mainSidebar")).DataContext as SidebarViewModel;
        var orders = sidebar!.MenuItems.Single(entry => entry.Label == "受注管理");
        var products = sidebar.MenuItems.Single(entry => entry.Label == "商品管理");
        Button HeaderButton(string name) => (Button)shell.FindName(name);
        void OpenOrders() => orders.NavigateCommand.Execute(null);
        void NewRegistration() => HeaderButton("orderHeaderRegisterButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        FrameworkElement OrdersRoot() => (FrameworkElement)host.Content;
        OrderManagementViewModel OrderList() => (OrderManagementViewModel)Descendants<Grid>(OrdersRoot()).Single(grid => grid.Name == "orderRoot").DataContext;
        string TabCount(string name) => Descendants<TextBlock>(OrdersRoot()).Single(text => text.Name == name).Text;
        void OpenTab(string name)
        {
            Descendants<MenuItem>(OrdersRoot()).Single(tab => tab.Name == name).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            WaitUntil(() => !OrderList().IsLoading);
        }
        ContextMenu DraftActions()
        {
            var grid = Descendants<DataGrid>(OrdersRoot()).Single(table => table.Name == "grid");
            return OpenOrderMenu(grid, OrderList().Rows.Single());
        }
        void NavigateToProducts() => products.NavigateCommand.Execute(null);
        OrderRegistrationViewModel Registration() => (OrderRegistrationViewModel)((F_OrderRegistration)host.Content).DataContext;

        shell.Show();
        try
        {
            OpenOrders();
            WaitUntil(() => !OrderList().IsLoading && OrderList().TotalCount > 0);
            var allBeforeSave = OrderList().TotalCount;
            var pendingBeforeSave = int.Parse(TabCount("pendingCount"));
            Check(shell.FindName("orderHeaderDraftsButton") == null && HeaderButton("orderHeaderRegisterButton").IsVisible,
                "order header keeps registration as its only visible action");
            NewRegistration();
            FlushBindings();
            var model = Registration();
            var date = OrderDateBox((F_OrderRegistration)host.Content);
            date.Text = "20xx";
            date.RaiseEvent(new RoutedEventArgs(FrameworkElement.LostFocusEvent));
            FlushBindings();
            Check(date.Text == "20xx" && model.DateInputError.Length > 0, "unfinished date survives validation and loss of focus");
            model.Remarks = "DraftNavigationFailure";

            InteractWithModal(shell, NavigateToProducts, modal =>
            {
                var notice = (OrderNoticeViewModel)modal.DataContext;
                var captions = Descendants<Button>(modal).Where(button => button.IsVisible).Select(button => button.Content).ToArray();
                Check(captions.Contains("入力を続ける") && captions.Contains("一時保存して移動") && captions.Contains("破棄して移動"),
                    "navigation dialog exposes all three requested actions");
                notice.CancelCommand.Execute(null);
            });
            Check(ReferenceEquals(Registration(), model) && store.List().Count == 0, "continue from the shell leaves input in place");

            db.Database.ExecuteSqlRaw("ALTER TABLE dbo.T_OrderDraft ADD CONSTRAINT CK_DraftChecksSaveFailure CHECK (FormJson NOT LIKE N'%DraftNavigationFailure%')");
            try
            {
                InteractWithModal(shell, NavigateToProducts, modal => ((OrderNoticeViewModel)modal.DataContext).ConfirmCommand.Execute(null));
                WaitUntil(() => !model.IsBusy);
                Check(ReferenceEquals(Registration(), model) && model.Remarks == "DraftNavigationFailure" && date.Text == "20xx"
                    && !products.IsActive && store.List().Count == 0, "failed database save stays on the registration page with input intact");
            }
            finally { db.Database.ExecuteSqlRaw("ALTER TABLE dbo.T_OrderDraft DROP CONSTRAINT CK_DraftChecksSaveFailure"); }

            InteractWithModal(shell, NavigateToProducts, modal => ((OrderNoticeViewModel)modal.DataContext).ConfirmCommand.Execute(null));
            WaitUntil(() => products.IsActive);
            Check(shell.Title == "商品管理" && store.List().Single().Form.Header.OrderDate.UnparsedText == "20xx",
                "successful save keeps partial input and reaches the originally clicked sidebar destination");

            OpenOrders();
            WaitUntil(() => !OrderList().IsLoading);
            Check(OrderList().TotalCount == allBeforeSave && TabCount("pendingCount") == pendingBeforeSave.ToString()
                && TabCount("draftCount") == "1" && OrderList().Rows.All(row => row.IsRegisteredOrder),
                "saving a draft increases only the draft count and leaves the All list free of drafts");
            OpenTab("draftsMenuItem");
            var draftRow = OrderList().Rows.Single();
            Check(OrderList().TotalCount == 1 && draftRow.IsDraft && draftRow.OrderNumber == "未採番"
                && draftRow.StatusText == "下書き" && draftRow.OrderDate is null, "the draft tab shows the saved incomplete record without an official ID or date");
            var actions = DraftActions();
            Check(actions.Items.OfType<MenuItem>().Where(item => item.Visibility == Visibility.Visible).Select(item => item.Header)
                .SequenceEqual(new[] { "詳細を見る", "編集を続ける", "下書きを削除する" }), "draft row menu offers only draft actions");
            actions.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "詳細を見る")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(!actions.IsOpen, "selecting draft details closes the popup before loading the draft");
            WaitUntil(() => host.Content is F_OrderRegistration);
            FlushBindings();
            var reopened = Registration();
            Check(reopened.IsDraft && !reopened.HasUnsavedChanges && OrderDateBox((F_OrderRegistration)host.Content).Text == "20xx"
                && reopened.Remarks == "DraftNavigationFailure", "draft list reopens the same registration form with the raw date and notes");

            var draftView = (F_OrderRegistration)host.Content;
            var productInput = (OrderProductInput)draftView.FindName("ProductInput");
            var manufacturer = (RegistrationLookup)productInput.FindName("ManufacturerFilter");
            Check(reopened.SelectedMakerId == 0 && manufacturer.Text == "", "reopened draft starts with all manufacturers");
            ChooseManufacturer(manufacturer, "ell");
            Check(reopened.Products.Single().PrId == productId && reopened.Products[0].MakerAndStockLabel == "メーカー：Dell | 在庫：未登録"
                && !reopened.HasUnsavedChanges, "manufacturer filtering uses the actual SQL master relationship and stock while reopening a draft");
            reopened.SelectedClient = reopened.Clients.Single(client => client.ClId == customerId);
            reopened.OrderDate = new DateTime(2026, 11, 21);
            reopened.SelectedProduct = reopened.Products.Single(product => product.PrId == productId);
            reopened.AddLineCommand.Execute(null);
            reopened.Remarks = "画面から更新";
            ((Button)draftView.FindName("SaveDraftButton")).Command.Execute(null);
            WaitUntil(() => !reopened.IsBusy);
            Check(!reopened.HasUnsavedChanges && store.List().Single().Form.Header.Remarks == "画面から更新",
                "fixed footer saves edits to the reopened draft");
            var orderCount = db.TOrders.Count();
            ((Button)draftView.FindName("RegisterButton")).Command.Execute(null);
            WaitUntil(() => !reopened.IsBusy);
            Check(reopened.IsSuccess && reopened.IsCompletionDialogVisible && store.List().Count == 0 && db.TOrders.Count() == orderCount + 1,
                "registration button converts a reopened draft and shows the existing completion dialog");
            reopened.ReturnToListCommand.Execute(null);
            WaitUntil(() => !OrderList().IsLoading);
            Check(shell.Title == "受注管理" && OrderList().TotalCount == allBeforeSave + 1 && TabCount("draftCount") == "0"
                && TabCount("pendingCount") == (pendingBeforeSave + 1).ToString(),
                "the converted draft leaves the draft count and appears once in the pending and All counts");
            OpenTab("pendingMenuItem");
            var orderId = db.TOrders.Single(order => order.ClCharge == "画面から更新").OrId;
            Check(OrderList().Rows.Count(row => row.OrderId == orderId && row.StatusText == "未確定") == 1,
                "the converted draft is visible as a single pending order");

            store.Save(new OrderDraftToken(Guid.NewGuid()), partial);
            OpenTab("draftsMenuItem");
            var draftGrid = Descendants<DataGrid>(OrdersRoot()).Single(table => table.Name == "grid");
            var container = RealizeOrderRow(draftGrid, OrderList().Rows.Single());
            draftGrid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = Control.PreviewMouseDoubleClickEvent, Source = container
            });
            WaitUntil(() => host.Content is F_OrderRegistration);
            FlushBindings();
            Check(Registration().IsDraft && Registration().Remarks == partial.Header.Remarks, "double-clicking a draft opens its saved editor");
            Registration().CloseCommand.Execute(null);
            WaitUntil(() => host.Content is not F_OrderRegistration && !OrderList().IsLoading);
            OpenTab("draftsMenuItem");
            actions = DraftActions();
            var delete = actions.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "下書きを削除する"));
            Check(delete.IsEnabled, "draft deletion is enabled for an owned draft");
            var dangerColor = Color.FromRgb(0xDC, 0x26, 0x26);
            Check(HasColor(delete.Foreground, dangerColor) && HasColor(Descendants<LucideIcon>(delete).Single().Stroke, dangerColor),
                "draft deletion displays its label and trash icon in red");
            InteractWithModal(shell, () => delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)), modal =>
            {
                var notice = (OrderNoticeViewModel)modal.DataContext;
                Check(notice.ConfirmLabel == "削除する" && store.List().Count == 1, "draft deletion waits for confirmation");
                notice.ConfirmCommand.Execute(null);
            });
            actions.IsOpen = false;
            WaitUntil(() => !OrderList().IsBusy && !OrderList().IsLoading);
            Check(OrderList().Rows.Count == 0 && TabCount("draftCount") == "0" && store.List().Count == 0,
                "confirming deletion removes the draft from the tab and updates its count");

            NewRegistration();
            FlushBindings();
            Registration().Remarks = "終了前の下書き";
            InteractWithModal(shell, shell.Close, modal => ((OrderNoticeViewModel)modal.DataContext).ConfirmCommand.Execute(null));
            WaitUntil(() => !shell.IsVisible);
            Check(store.List().Single().Form.Header.Remarks == "終了前の下書き", "window close waits for a successful draft save");
        }
        finally
        {
            if (shell.IsVisible && host.Content is F_OrderRegistration { DataContext: OrderRegistrationViewModel model } && model.HasUnsavedChanges)
                InteractWithModal(shell, shell.Close, modal => ((OrderNoticeViewModel)modal.DataContext).DiscardCommand.Execute(null));
            else if (shell.IsVisible) shell.Close();
        }
    }

    private static TextBox OrderDateBox(F_OrderRegistration registration)
    {
        var picker = (DatePicker)registration.FindName("OrderDateInput");
        picker.ApplyTemplate();
        return (TextBox)picker.Template.FindName("PART_TextBox", picker);
    }

    private static void InteractWithModal(Window owner, Action show, Action<Window> interact)
    {
        Exception? error = null;
        var interacted = false;
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        void FindModal(object? sender, EventArgs args)
        {
            var modal = owner.OwnedWindows.OfType<Window>().SingleOrDefault(window => window.IsVisible);
            if (modal == null) return;
            timer.Stop();
            interacted = true;
            modal.Opacity = 0;
            try { interact(modal); }
            catch (Exception exception) { error = exception; modal.Close(); }
        }
        timer.Tick += FindModal;
        timer.Start();
        try
        {
            show();
            WaitUntil(() => interacted);
        }
        catch (TimeoutException exception) when (!interacted)
        {
            throw new InvalidOperationException($"Expected a modal dialog owned by {owner.Title}.", exception);
        }
        finally { timer.Stop(); }
        if (error != null) throw new InvalidOperationException("Modal interaction failed.", error);
    }

    private static void Reject(Action action, string description)
    {
        try { action(); }
        catch (OrderDraftException) { Check(true, description); return; }
        Check(false, description);
    }

    private static MEmployee ReadEmployee(int id)
    {
        using var db = new SalesManagementContext();
        return db.MEmployees.AsNoTracking().Include(employee => employee.So).Include(employee => employee.Po)
            .Single(employee => employee.EmId == id);
    }
}
