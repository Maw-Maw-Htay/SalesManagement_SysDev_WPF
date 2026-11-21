using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using SalesManagement_SysDev;

internal static partial class Program
{
    private const string OrderListFixture = "一覧検証";

    private static void CheckOrderListWorkspace(SalesManagementContext db)
    {
        var admin = AppSession.Employee!;
        var sales = ReadEmployee(3);
        var customers = new[]
        {
            new MClient { ClName = OrderListFixture + " あさひ電機" + new string('長', 20), SoId = admin.SoId },
            new MClient { ClName = OrderListFixture + " きた商会", SoId = admin.SoId },
            new MClient { ClName = OrderListFixture + " さくら商会", SoId = sales.SoId }
        };
        db.MClients.AddRange(customers);
        foreach (var entry in db.ChangeTracker.Entries<MClient>().Where(entry => entry.State == EntityState.Added))
            foreach (var property in entry.Properties.Where(property => property.Metadata.ClrType == typeof(string)
                && !property.Metadata.IsNullable && property.CurrentValue == null)) property.CurrentValue = "test";
        db.SaveChanges();

        var productIds = db.MProducts.Select(product => product.PrId).OrderBy(id => id).ToArray();
        var dates = new DateTime(2026, 11, 1);
        var orders = Enumerable.Range(1, 38).Select(index =>
        {
            var customer = customers[(index - 1) % customers.Length];
            var date = dates.AddDays(index % 7);
            return new TOrder
            {
                SoId = customer.SoId, ClId = customer.ClId, EmId = customer.SoId == admin.SoId ? (index % 2 == 0 ? 2 : admin.EmId) : sales.EmId,
                OrDate = date, OrDeliveryDate = index % 4 == 0 ? null : date.AddDays(7),
                OrStateFlag = index % 3 == 0 ? 1 : 0, OrFlag = index % 11 == 0 ? 2 : 0,
                TOrderDetails = Enumerable.Range(0, index % 3 + 1).Select(line => new TOrderDetail
                {
                    PrId = productIds[line % productIds.Length], OrQuantity = 5 + line, OrTotalPrice = index * 77 + 5 + line
                }).Append(new TOrderDetail { PrId = productIds[0], OrQuantity = 0, OrTotalPrice = 0 }).ToList()
            };
        }).ToArray();
        orders[4].OrStateFlag = null;
        db.TOrders.AddRange(orders);
        db.TOrders.Add(new TOrder
        {
            SoId = customers[0].SoId, ClId = customers[0].ClId, EmId = admin.EmId, OrDate = dates,
            OrFlag = 0, OrStateFlag = 9
        });
        db.SaveChanges();
        var linked = orders[1];
        db.TChumons.Add(new TChumon
        {
            OrId = linked.OrId, ClId = linked.ClId, SoId = linked.SoId, ChDate = linked.OrDate, ChStateFlag = 0, ChFlag = 0
        });
        db.SaveChanges();
        var draftStore = new OrderDraftService();
        var offices = db.MSalesOffices.AsNoTracking().ToDictionary(office => office.SoId, office => office.SoName);
        var drafts = Enumerable.Range(1, 13).Select(index =>
        {
            var customer = customers[(index - 1) % customers.Length];
            var orderDate = dates.AddDays(index);
            var form = new OrderDraftForm(new OrderDraftHeader(new(customer.SoId, offices[customer.SoId]),
                new(customer.ClId, customer.ClName), new(orderDate, ""), new(index % 2 == 0 ? orderDate.AddDays(7) : null, ""),
                $"下書き一覧検証 {index}", new(null, ""), null, "1"),
                productIds.Take(index % 3).Select(id => new OrderDraftLine(id, $"確認用型番-{index}", "商品", 100 + index, 3, "3")).ToArray());
            return (Token: draftStore.Save(new OrderDraftToken(Guid.NewGuid()), form), Form: form);
        }).ToArray();
        AppSession.Employee = ReadEmployee(2);
        try { draftStore.Save(new OrderDraftToken(Guid.NewGuid()), drafts[0].Form); }
        finally { AppSession.Employee = admin; }

        var shell = NewOrderListShell();
        try
        {
            var (root, model, navigate) = OpenOrderList(shell);
            T Find<T>(string name) where T : FrameworkElement => Descendants<T>(root).Single(element => element.Name == name);
            var grid = Find<DataGrid>("grid");
            var search = Find<TextBox>("search");
            var pagination = Find<Border>("orderPagination");
            var visible = orders.Where(order => order.OrFlag != 2).ToArray();
            var employeeNames = db.MEmployees.AsNoTracking().ToDictionary(employee => employee.EmId, employee => employee.EmName);
            var officeNames = db.MSalesOffices.AsNoTracking().ToDictionary(office => office.SoId, office => office.SoName);

            void Search(string text)
            {
                search.SetCurrentValue(TextBox.TextProperty, text);
                FlushBindings();
                WaitUntil(() => !model.IsLoading);
            }
            void SelectTab(string name)
            {
                Find<MenuItem>(name).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                WaitUntil(() => !model.IsLoading);
            }
            void FirstPage()
            {
                model.GoToPageCommand.Execute(new OrderPageLink(1, false));
                WaitUntil(() => !model.IsLoading);
            }
            void CheckBadge(string status, Color background)
            {
                var row = RealizeOrderRow(grid, model.Rows[0]);
                var badge = Descendants<Border>(row).Single(border => border.Child is StackPanel panel
                    && panel.Children.OfType<TextBlock>().Any(text => text.Text == status));
                Check(badge.Background is SolidColorBrush brush && brush.Color == background && Descendants<LucideIcon>(badge).Single().Data != null,
                    $"{status} displays its configured badge color and icon");
            }

            Search(OrderListFixture);
            Check(model.TotalCount == visible.Length && model.Rows.Count == 20 && model.PageNumber == 1
                && model.PageRange == $"{visible.Length}件中 1～20件を表示"
                && model.Rows.All(row => row.IsRegisteredOrder && !row.IsHidden && row.StateFlag is 0 or 1),
                "All paginates the registered pending and confirmed orders, excluding hidden and unknown states");
            Check(Find<TextBlock>("allCount").Text == visible.Length.ToString()
                && Find<TextBlock>("pendingCount").Text == visible.Count(order => (order.OrStateFlag ?? 0) == 0).ToString()
                && Find<TextBlock>("confirmedCount").Text == visible.Count(order => order.OrStateFlag == 1).ToString()
                && Find<TextBlock>("hiddenCount").Text == orders.Count(order => order.OrFlag == 2).ToString()
                && Find<TextBlock>("draftCount").Text == drafts.Length.ToString(),
                "All is the sum of pending and confirmed, and the filtered draft count includes only the current employee's drafts");
            var tabs = Find<Menu>("statusMenu").Items.OfType<MenuItem>().ToArray();
            Check(tabs.Select(tab => System.Windows.Automation.AutomationProperties.GetName(tab))
                .SequenceEqual(new[] { "すべて", "未確定", "確定済み", "下書き", "非表示", "受注履歴" })
                && tabs.All(tab => tab.ActualHeight == 32), "the six status tabs appear in the requested order with equal height");
            Check(model.Rows.Select(row => row.OrderId).SequenceEqual(visible.OrderByDescending(order => order.OrDate)
                .ThenByDescending(order => order.OrId).Take(20).Select(order => order.OrId)), "default list order is newest with a stable ID tie-break");
            var sample = model.Rows.First();
            var persisted = orders.Single(order => order.OrId == sample.OrderId);
            var subtotal = persisted.TOrderDetails.Sum(line => line.OrTotalPrice);
            Check(sample.LineCount == persisted.TOrderDetails.Count - 1 && sample.Total == subtotal + Math.Round(subtotal / 10, 0, MidpointRounding.AwayFromZero)
                && sample.EmployeeName == employeeNames[persisted.EmId] && sample.OfficeName == officeNames[persisted.SoId],
                "list uses active detail records, document-level tax, and each order's assigned employee and branch");
            CheckOrderListLayouts(shell, root, grid, pagination);
            CheckOrderRowMenus(root, grid, model);

            var headers = Descendants<DataGridColumnHeader>(grid).ToArray();
            var dateHeader = headers.Single(header => Equals(header.Content, "受注日 / 納期"));
            var down = Descendants<Button>(dateHeader).Single(button => Equals(button.Content, "▼"));
            Check(dateHeader.SortDirection == ListSortDirection.Descending && down.Foreground is SolidColorBrush { Color: var color }
                && color == Color.FromRgb(0x1D, 0x4E, 0xD8), $"default header shows both arrows and highlights descending order ({dateHeader.SortDirection}, {down.Foreground})");
            // Header clicks invoke the DataGrid's virtual OnClick, which also handles real mouse input.
            typeof(DataGridColumnHeader).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dateHeader, null);
            WaitUntil(() => !model.IsLoading);
            Check(model.SortColumn == OrderListSort.OrderDate && model.SortDirection == ListSortDirection.Ascending
                && dateHeader.SortDirection == ListSortDirection.Ascending, "clicking an active column header reverses its sort");

            var japanese = StringComparer.Create(CultureInfo.GetCultureInfo("ja-JP"), ignoreCase: true);
            var sortChecks = new Dictionary<OrderListSort, Comparison<TOrder>>
            {
                [OrderListSort.OrderId] = (left, right) => left.OrId.CompareTo(right.OrId),
                [OrderListSort.OrderDate] = (left, right) => left.OrDate.CompareTo(right.OrDate),
                [OrderListSort.CustomerName] = (left, right) => japanese.Compare(customers.Single(customer => customer.ClId == left.ClId).ClName,
                    customers.Single(customer => customer.ClId == right.ClId).ClName),
                [OrderListSort.LineCount] = (left, right) => left.TOrderDetails.Count.CompareTo(right.TOrderDetails.Count),
                [OrderListSort.EmployeeName] = (left, right) =>
                {
                    var names = japanese.Compare(employeeNames[left.EmId], employeeNames[right.EmId]);
                    return names != 0 ? names : japanese.Compare(officeNames[left.SoId], officeNames[right.SoId]);
                },
                [OrderListSort.Total] = (left, right) => left.TOrderDetails.Sum(detail => detail.OrTotalPrice)
                    .CompareTo(right.TOrderDetails.Sum(detail => detail.OrTotalPrice)),
                [OrderListSort.Status] = (left, right) => (left.OrStateFlag ?? 0).CompareTo(right.OrStateFlag ?? 0)
            };
            foreach (var (column, compare) in sortChecks)
            foreach (var descending in new[] { false, true })
            {
                var command = descending ? model.SortDescendingCommand : model.SortAscendingCommand;
                command.Execute(column.ToString());
                WaitUntil(() => !model.IsLoading);
                var first = model.Rows.Select(row => row.OrderId).ToArray();
                Check(model.PageNumber == 1 && model.NextPageCommand.CanExecute(null), "changing sort returns to page one");
                model.NextPageCommand.Execute(null);
                WaitUntil(() => !model.IsLoading);
                var sorted = visible.OrderBy(order => order, Comparer<TOrder>.Create((left, right) => descending ? compare(right, left) : compare(left, right)))
                    .ThenByDescending(order => order.OrId).Select(order => order.OrId);
                Check(first.Concat(model.Rows.Select(row => row.OrderId)).SequenceEqual(sorted)
                    && model.PageNumber == 2 && !model.NextPageCommand.CanExecute(null), $"complete SQL results sorted across pages for {column} ({descending})");
            }
            FirstPage();
            var sizePicker = Descendants<ComboBox>(pagination).Single();
            foreach (var size in new[] { 10, 50, 100, 20 })
            {
                sizePicker.SetCurrentValue(Selector.SelectedItemProperty, size);
                FlushBindings();
                WaitUntil(() => !model.IsLoading);
                Check(model.PageSize == size && model.PageNumber == 1 && model.Rows.Count == Math.Min(size, visible.Length),
                    $"page size {size} updates the visible range and retains the current sort");
            }

            model.Rows[0].IsChecked = true;
            Check(model.SelectionText == "1件選択中" && model.AllPageSelected == null, "individual checkbox updates selection and indeterminate header");
            model.TogglePageSelectionCommand.Execute(null);
            Check(model.SelectionText == "20件選択中" && model.AllPageSelected == true, "header selects the current page");
            model.NextPageCommand.Execute(null);
            WaitUntil(() => !model.IsLoading);
            Check(model.Rows.All(row => !row.IsChecked) && model.SelectionText == "20件選択中", "selection count includes checked orders on other pages");
            model.ClearSelectionCommand.Execute(null);
            Check(!model.HasSelection && !model.ConfirmSelectedCommand.CanExecute(null) && !model.HideSelectedCommand.CanExecute(null),
                "clearing all pages disables the bulk commands");

            Search($"ORD-{orders[0].OrId:000000}");
            Check(model.Rows.Single().OrderId == orders[0].OrId, "quick search accepts a formatted order number");
            Search(orders[0].OrId.ToString());
            Check(model.Rows.Any(row => row.OrderId == orders[0].OrId), "quick search accepts a numeric order number");
            Search(customers[0].ClId.ToString());
            Check(model.Rows.Any(row => row.CustomerName == customers[0].ClName), "quick search matches customer ID");
            Search(OrderListFixture);
            model.ToggleFiltersCommand.Execute(null);
            root.UpdateLayout();
            FlushBindings();
            Find<TextBox>("orderIdFilter").SetCurrentValue(TextBox.TextProperty, $"ORD-{orders[0].OrId:000000}");
            WaitUntil(() => !model.IsLoading);
            Check(model.Rows.Single().OrderId == orders[0].OrId, "detailed order-number search uses the same ID");
            Find<TextBox>("orderIdFilter").SetCurrentValue(TextBox.TextProperty, "");
            WaitUntil(() => !model.IsLoading);
            Find<TextBox>("customerNameFilter").SetCurrentValue(TextBox.TextProperty, customers[1].ClId.ToString());
            WaitUntil(() => !model.IsLoading);
            Check(model.Rows.Count > 0 && model.Rows.All(row => row.CustomerName == customers[1].ClName), "detailed customer ID filters the list");
            Find<TextBox>("customerNameFilter").SetCurrentValue(TextBox.TextProperty, "");
            WaitUntil(() => !model.IsLoading);
            Find<TextBox>("employeeNameFilter").SetCurrentValue(TextBox.TextProperty, sales.EmName);
            WaitUntil(() => !model.IsLoading);
            model.SelectedOfficeId = sales.SoId;
            WaitUntil(() => !model.IsLoading);
            Check(model.Rows.Count > 0 && model.Rows.All(row => row.EmployeeName == sales.EmName && row.OfficeId == sales.SoId),
                "detailed employee and branch filters combine");
            Find<DatePicker>("fromDate").SelectedDate = dates.AddDays(3);
            Find<DatePicker>("toDate").SelectedDate = dates.AddDays(5);
            Find<CheckBox>("deliveryUnsetOnly").SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            WaitUntil(() => !model.IsLoading);
            Check(model.Rows.Count > 0 && model.Rows.All(row => row.DeliveryDate == null && row.OrderDate >= dates.AddDays(3)
                && row.OrderDate <= dates.AddDays(5)), "detailed order-date range and unset due-date filter combine");
            Find<CheckBox>("deliveryUnsetOnly").SetCurrentValue(ToggleButton.IsCheckedProperty, false);
            Find<DatePicker>("deliveryFromDate").SelectedDate = dates.AddDays(10);
            Find<DatePicker>("deliveryToDate").SelectedDate = dates.AddDays(12);
            WaitUntil(() => !model.IsLoading);
            Check(model.Rows.Count > 0 && model.Rows.All(row => row.DeliveryDate >= dates.AddDays(10) && row.DeliveryDate <= dates.AddDays(12)),
                "detailed due-date range is applied in SQL");
            model.ClearFiltersCommand.Execute(null);
            WaitUntil(() => !model.IsLoading);
            model.ToggleFiltersCommand.Execute(null);
            Search(OrderListFixture);

            SelectTab("pendingMenuItem");
            Check(model.Rows.All(row => row.StatusText == "未確定") && model.PageNumber == 1, "pending tab shows only pending orders");
            CheckBadge("未確定", Color.FromRgb(0xFD, 0xE6, 0x8A));
            SelectTab("confirmedMenuItem");
            Check(model.Rows.All(row => row.StatusText == "確定"), "confirmed tab uses the confirmed state");
            CheckBadge("確定", Color.FromRgb(0xBB, 0xF7, 0xD0));
            var confirmed = model.Rows[0];
            confirmed.IsChecked = true;
            CheckBulkButtons(root, [false, true, true]);
            confirmed.IsChecked = false;
            var confirmedMenu = OpenOrderMenu(grid, confirmed);
            var disabledActions = confirmedMenu.Items.OfType<MenuItem>().Where(item => !item.IsEnabled && item.Visibility == Visibility.Visible).ToArray();
            var muted = Color.FromRgb(0x94, 0xA3, 0xB8);
            Render(confirmedMenu, "order-row-menu-confirmed");
            Check(disabledActions.Select(item => item.Header).SequenceEqual(new[] { "編集する", "受注を確定する" })
                && disabledActions.All(item => HasColor(item.Foreground, muted) && HasColor(Descendants<LucideIcon>(item).Single().Stroke, muted) && item.Cursor == Cursors.No),
                "confirmed orders show the disabled edit and confirm actions with gray text, muted icons and disabled cursor. "
                + string.Join(", ", disabledActions.Select(item => $"{item.Header} ({item.Foreground}, {Descendants<LucideIcon>(item).Single().Stroke}, {item.Cursor})")));
            foreach (var item in disabledActions) item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(confirmedMenu.IsOpen && shell.Title == "受注管理" && !model.IsBusy, "disabled row actions cannot run even when invoked programmatically");
            confirmedMenu.IsOpen = false;
            SelectTab("hiddenMenuItem");
            Check(model.Rows.Count == 3 && model.Rows.All(row => row.StatusText == "非表示"), "hidden orders have a separate tab and status");
            CheckBadge("非表示", Color.FromRgb(0xE2, 0xE8, 0xF0));
            var hiddenMenu = OpenOrderMenu(grid, model.Rows[0]);
            Check(VisibleMenuItems(hiddenMenu).Select(item => item.Header).SequenceEqual(new[] { "詳細を見る", "復元する" }),
                "hidden orders show details and the existing admin restore action");
            SelectTab("historyMenuItem");
            Check(!hiddenMenu.IsOpen && Find<Grid>("historyPanel").IsVisible && !pagination.IsVisible, "switching to history closes the menu and keeps the independent audit view");
            SelectTab("allOrdersMenuItem");

            model.SortDescendingCommand.Execute(nameof(OrderListSort.OrderId));
            WaitUntil(() => !model.IsLoading);
            model.NextPageCommand.Execute(null);
            WaitUntil(() => !model.IsLoading);
            InteractWithModal(shell, () => model.PrintCommand.Execute(null), modal =>
            {
                var printRows = (OrderPrintRow[])typeof(OrderListPrintPreviewWindow)
                    .GetField("rows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(modal)!;
                Check(printRows.Select(row => row.OrderId).SequenceEqual(visible.OrderByDescending(order => order.OrId).Select(order => order.OrId))
                    && printRows.Length > model.PageSize, "print from page two previews the entire sorted and filtered list");
                modal.Close();
            });
            WaitUntil(() => !model.IsBusy);

            Search($"ORD-{orders[0].OrId:000000}");
            var pending = model.Rows.Single();
            var actions = OpenOrderMenu(grid, pending);
            var details = actions.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "詳細を見る"));
            InteractWithModal(shell, () => details.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)), modal =>
            {
                Check(!actions.IsOpen && modal is F_OrderDetail && modal.Title.Contains(pending.OrderNumber), "row menu closes and opens the matching order detail");
                modal.Close();
            });
            actions.IsOpen = false;
            WaitUntil(() => !model.IsLoading);
            var rowContainer = RealizeOrderRow(grid, model.Rows.Single());
            InteractWithModal(shell, () => grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = Control.PreviewMouseDoubleClickEvent, Source = rowContainer
            }), modal =>
            {
                Check(modal is F_OrderDetail && modal.Title.Contains(pending.OrderNumber), "row double-click still opens the matching order detail");
                modal.Close();
            });
            WaitUntil(() => !model.IsLoading);
            actions = OpenOrderMenu(grid, model.Rows.Single());
            var edit = actions.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "編集する"));
            Check(edit.IsEnabled, "pending row action permits editing when eligible");
            edit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            actions.IsOpen = false;
            WaitUntil(() => shell.Title == "受注更新");
            Check(shell.Title == "受注更新", "edit menu reuses the existing order editor");
            navigate();
            WaitUntil(() => !model.IsLoading);
            Search($"ORD-{linked.OrId:000000}");
            Check(!model.Rows.Single().CanEdit && !model.Rows.Single().CanConfirm, "pending rows already linked to an order cannot be edited or confirmed");

            Search($"ORD-{orders[6].OrId:000000}");
            CheckOrderMenuWrites(db, shell, grid, model, orders[6].OrId);

            CheckOrderBulkActions(db, shell, model, Search, orders[0], orders[3]);
            model.PageSize = 20;
            WaitUntil(() => !model.IsLoading);
            var checkedRow = model.Rows[0];
            checkedRow.IsChecked = true;
            Check(BusinessService.SetOrderVisibility(checkedRow.OrderId, admin.EmId, true, "別画面での非表示", out var hideMessage),
                $"another screen can hide a selected order ({hideMessage})");
            model.NextPageCommand.Execute(null);
            WaitUntil(() => !model.IsLoading);
            Check(model.PageNumber == 2 && !model.HasSelection, "page changes drop checked orders that no longer match the filtered list");
            Check(BusinessService.SetOrderVisibility(checkedRow.OrderId, admin.EmId, false, "一覧検証の復帰", out var restoreMessage),
                $"admin can restore the order ({restoreMessage})");
            CheckOrderCsv(db, customers[0], productIds[0], admin.EmId);

            SelectTab("allOrdersMenuItem");
            model.NextPageCommand.Execute(null);
            WaitUntil(() => !model.IsLoading);
            var allCount = int.Parse(Find<TextBlock>("allCount").Text);
            SelectTab("draftsMenuItem");
            Check(model.PageNumber == 1 && model.TotalCount == drafts.Length && model.Rows.All(row => row.IsDraft)
                && model.QuickSearchText == OrderListFixture && Find<TextBlock>("allCount").Text == allCount.ToString(),
                "switching from page two to drafts resets the page, preserves the filter, and keeps the official count separate");
            CheckBadge("下書き", Color.FromRgb(0xDB, 0xEA, 0xFE));
            var draftRow = model.Rows[0];
            var draftCheckBox = Descendants<CheckBox>(RealizeOrderRow(grid, draftRow)).Single();
            Check(draftCheckBox.IsEnabled, "the draft row checkbox is enabled");
            draftCheckBox.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            FlushBindings();
            var draftForm = drafts.Single(sample => sample.Token.Id == draftRow.DraftToken!.Id).Form;
            var draftSubtotal = draftForm.Lines.Sum(line => line.Quantity * line.UnitPrice);
            Check(draftRow.IsChecked && model.SelectionText == "1件選択中" && !model.ConfirmSelectedCommand.CanExecute(null)
                && !model.HideSelectedCommand.CanExecute(null) && !model.PrintCommand.CanExecute(null) && !draftRow.CanConfirm
                && draftRow.LineCount == draftForm.Lines.Count && draftRow.Subtotal == draftSubtotal
                && draftRow.OfficeName == offices[draftForm.Header.Office.Id!.Value] && draftRow.EmployeeName == admin.EmName,
                "checking a draft updates the count without enabling registered-order actions");
            Render((FrameworkElement)shell.Content, "order-list-drafts");
            model.PageSize = 10;
            WaitUntil(() => !model.IsLoading);
            model.SortAscendingCommand.Execute(nameof(OrderListSort.OrderDate));
            WaitUntil(() => !model.IsLoading);
            var firstDraftPage = model.Rows.Select(row => row.DraftToken!.Id).ToArray();
            Check(model.Rows.All(row => !row.IsChecked) && model.SelectionText == "1件選択中", "draft selection survives sorting onto a different page");
            model.TogglePageSelectionCommand.Execute(null);
            Check(model.Rows.All(row => row.IsChecked) && model.SelectionText == "11件選択中", "select-all checks just the visible draft page");
            model.NextPageCommand.Execute(null);
            WaitUntil(() => !model.IsLoading);
            Check(model.PageRange == "13件中 11～13件を表示" && firstDraftPage.Concat(model.Rows.Select(row => row.DraftToken!.Id))
                .SequenceEqual(drafts.OrderBy(draft => draft.Form.Header.OrderDate.Value).Select(draft => draft.Token.Id)),
                "draft dates are sorted by SQL Server before limits for both pages");
            Check(model.Rows.Single(row => row.IsChecked).DraftToken!.Id == draftRow.DraftToken!.Id && model.SelectionText == "11件選択中",
                "the next page restores the selected draft by its own ID");
            model.ClearSelectionCommand.Execute(null);
            Check(!model.HasSelection && model.Rows.All(row => !row.IsChecked), "clear-selection clears drafts on all pages");
            Search(customers[0].ClId.ToString());
            Check(drafts.Where(draft => draft.Form.Header.Customer.Id == customers[0].ClId)
                .All(draft => model.Rows.Any(row => row.DraftToken!.Id == draft.Token.Id)), "draft quick search matches the customer ID");
            Search("確認用型番-13");
            Check(model.Rows.Single().DraftToken!.Id == drafts[^1].Token.Id, "draft quick search matches product codes in the saved lines");
            Search(OrderListFixture);
            model.ToggleFiltersCommand.Execute(null);
            root.UpdateLayout();
            FlushBindings();
            model.SelectedOfficeId = admin.SoId;
            Find<TextBox>("employeeNameFilter").SetCurrentValue(TextBox.TextProperty, admin.EmName);
            Find<DatePicker>("fromDate").SelectedDate = dates.AddDays(2);
            Find<CheckBox>("deliveryUnsetOnly").SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            WaitUntil(() => !model.IsLoading);
            var expectedDrafts = drafts.Where(sample => sample.Form.Header.Office.Id == admin.SoId
                && sample.Form.Header.OrderDate.Value >= dates.AddDays(2) && sample.Form.Header.DeliveryDate.Value == null);
            Check(model.Rows.Select(row => row.DraftToken!.Id).SequenceEqual(expectedDrafts.Select(draft => draft.Token.Id)),
                "draft filtering combines employee, office, order date and unset delivery date");
            SelectTab("allOrdersMenuItem");
            Check(model.Rows.All(row => row.IsRegisteredOrder) && model.SelectedOfficeId == admin.SoId && model.DeliveryDateUnsetOnly,
                "returning to All retains advanced filters and shows only registered orders");
        }
        finally { if (shell.IsVisible) shell.Close(); }

        AppSession.Employee = sales;
        var salesShell = NewOrderListShell();
        try
        {
            var (root, model, _) = OpenOrderList(salesShell);
            Check(Descendants<TextBlock>(root).Single(text => text.Name == "draftCount").Text == "0",
                "draft count excludes other employees and the salesperson's former office");
            Descendants<TextBox>(root).Single(box => box.Name == "search").SetCurrentValue(TextBox.TextProperty, OrderListFixture);
            FlushBindings();
            WaitUntil(() => !model.IsLoading);
            Check(model.Rows.Count > 0 && model.Rows.All(row => row.OfficeId == sales.SoId)
                && model.TotalCount == orders.Count(order => order.SoId == sales.SoId && order.OrFlag != 2),
                "SQL list search and counts respect the salesperson's office scope");
            Descendants<MenuItem>(root).Single(tab => tab.Name == "hiddenMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            WaitUntil(() => !model.IsLoading);
            var grid = Descendants<DataGrid>(root).Single(element => element.Name == "grid");
            var hiddenMenu = OpenOrderMenu(grid, model.Rows.Single());
            Check(VisibleMenuItems(hiddenMenu).Select(item => item.Header).SequenceEqual(new[] { "詳細を見る" }),
                "the hidden-order menu omits restore for a salesperson");
            hiddenMenu.IsOpen = false;
        }
        finally { if (salesShell.IsVisible) salesShell.Close(); AppSession.Employee = admin; }
        if (interactiveOrderMenu) ShowInteractiveOrderMenu();
    }

    private static void CheckDraftSelectionState()
    {
        var confirmInvoked = false;
        var hideInvoked = false;
        var model = new OrderManagementViewModel(() => { }, () => confirmInvoked = true, () => hideInvoked = true,
            false, () => { }, () => { }, _ => { });
        var showPage = typeof(OrderManagementViewModel).GetMethod("ShowPage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void ShowPage(OrderListRow[] rows, OrderListRow[] checkedOffPage, int total, int page) =>
            showPage.Invoke(model, [rows, checkedOffPage, total, page]);

        var first = new OrderListRow { DraftToken = new OrderDraftToken(Guid.NewGuid()) };
        var second = new OrderListRow { DraftToken = new OrderDraftToken(Guid.NewGuid()) };
        var third = new OrderListRow { DraftToken = new OrderDraftToken(Guid.NewGuid()) };
        model.SelectTab(OrderStatusTab.Draft);
        ShowPage([first, second], [], 3, 1);
        first.IsChecked = true;
        Check(model.SelectionText == "1件選択中" && model.AllPageSelected is null && !second.IsChecked,
            "checking one draft selects just that draft even though both are unnumbered");
        model.ConfirmSelectedCommand.Execute(null);
        model.HideSelectedCommand.Execute(null);
        Check(!confirmInvoked && !hideInvoked && !model.PrintCommand.CanExecute(null), "selected drafts cannot enter registered-order actions");
        model.TogglePageSelectionCommand.Execute(null);
        Check(first.IsChecked && second.IsChecked && model.AllPageSelected == true && model.SelectionText == "2件選択中",
            "the header checks every draft on the page");

        ShowPage([third], [first, second], 3, 2);
        Check(!third.IsChecked && model.AllPageSelected == false && model.SelectionText == "2件選択中",
            "another draft page keeps earlier checks without checking unrelated rows");
        var reloaded = new OrderListRow { DraftToken = new OrderDraftToken(first.DraftToken!.Id, [1]) };
        ShowPage([reloaded], [], 1, 1);
        Check(reloaded.IsChecked && model.SelectionText == "1件選択中", "selection follows the draft ID across reloads and drops rows removed from the list");
        model.ClearSelectionCommand.Execute(null);
        Check(!reloaded.IsChecked && !model.HasSelection && !model.ClearSelectionCommand.CanExecute(null), "draft selection can be cleared");

        model.SelectTab(OrderStatusTab.All);
        var order = new OrderListRow { OrderId = 1, OfficeId = 1, EmployeeInOffice = true, LineCount = 1 };
        ShowPage([order], [], 1, 1);
        order.IsChecked = true;
        Check(model.ConfirmSelectedCommand.CanExecute(null) && model.HideSelectedCommand.CanExecute(null)
            && model.SelectionText == "1件選択中", "registered-order selection still enables eligible actions");
    }

    private static F_Main NewOrderListShell() => new()
    {
        WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000,
        Width = 1280, Height = 720, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Opacity = 0
    };

    private static (FrameworkElement Root, OrderManagementViewModel Model, Action Navigate) OpenOrderList(F_Main shell)
    {
        shell.Show();
        var sidebar = (SidebarViewModel)((FrameworkElement)shell.FindName("mainSidebar")).DataContext;
        var entry = sidebar.MenuItems.Single(item => item.Label == "受注管理");
        void Navigate() => entry.NavigateCommand.Execute(null);
        Navigate();
        var root = (FrameworkElement)((ContentControl)shell.FindName("pageHost")).Content;
        var model = (OrderManagementViewModel)Descendants<Grid>(root).Single(grid => grid.Name == "orderRoot").DataContext;
        WaitUntil(() => !model.IsLoading && model.Rows.Count > 0);
        return (root, model, Navigate);
    }

    private static DataGridRow RealizeOrderRow(DataGrid grid, OrderListRow item)
    {
        grid.ScrollIntoView(item);
        grid.UpdateLayout();
        return (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(item);
    }

    private static ContextMenu OpenOrderMenu(DataGrid grid, OrderListRow item)
    {
        var row = RealizeOrderRow(grid, item);
        var button = Descendants<Button>(row).Single(button => Equals(button.Content, "⋯"));
        FlushBindings();
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        FlushBindings();
        Check(button.ContextMenu is { IsOpen: true, Placement: PlacementMode.Custom } menu
            && ReferenceEquals(menu.DataContext, item) && ReferenceEquals(menu.PlacementTarget, button),
            "three-dot menu opens for the clicked order");
        return button.ContextMenu;
    }

    private static void CheckOrderListLayouts(F_Main shell, FrameworkElement root, DataGrid grid, Border pagination)
    {
        var shellRoot = (FrameworkElement)shell.Content;
        var header = (FrameworkElement)shell.FindName("mainHeader");
        var tabs = Descendants<Menu>(root).Single(menu => menu.Name == "statusMenu").Items.OfType<MenuItem>().ToArray();
        var statusActions = Descendants<StackPanel>(root).Single(panel => panel.Name == "statusActions");
        try
        {
            foreach (var (width, height) in new[] { (1280, 720), (1366, 768), (1920, 1080) })
            {
                // Size the WPF layout surface directly so the current monitor cannot cap the test window.
                shellRoot.Width = width;
                shellRoot.Height = height;
                shell.UpdateLayout();
                FlushBindings();
                shellRoot.Arrange(new Rect(0, 0, width, height));
                Rect Bounds(FrameworkElement element) => element.TransformToAncestor(shellRoot).TransformBounds(new Rect(element.RenderSize));
                var scroller = Descendants<ScrollViewer>(grid).First();
                Check(Math.Abs(shellRoot.ActualWidth - width) < 1 && Math.Abs(shellRoot.ActualHeight - height) < 1
                    && header.IsVisible && Bounds(header).Bottom <= Bounds(grid).Top && Bounds(grid).Bottom <= Bounds(pagination).Top + 1
                    && Bounds(pagination).Bottom <= shellRoot.ActualHeight + 1 && scroller.ScrollableHeight > 0
                    && scroller.ScrollableWidth < 1 && grid.Columns.Count == 9,
                    $"header and pager stay fixed while the full grid fits and scrolls at {width}×{height}");
                Check(tabs.All(tab => Math.Abs(Bounds(tab).Top - Bounds(statusActions).Top) < 1)
                    && Bounds(tabs[^1]).Right <= Bounds(statusActions).Left,
                    $"the status tabs and file actions share one row without overlap at {width}×{height}");
                foreach (var label in new[] { "CSV取込", "印刷" })
                    Check(Descendants<Button>(root).Any(button => button.IsVisible && System.Windows.Automation.AutomationProperties.GetName(button) == label),
                        $"{label} remains visible at {width}×{height}");
                Render(shellRoot, $"order-list-{width}x{height}");
            }
        }
        finally
        {
            shellRoot.ClearValue(FrameworkElement.WidthProperty);
            shellRoot.ClearValue(FrameworkElement.HeightProperty);
            shell.UpdateLayout();
            FlushBindings();
        }
    }

    private static void CheckOrderBulkActions(SalesManagementContext db, F_Main owner, OrderManagementViewModel model,
        Action<string> search, TOrder first, TOrder second)
    {
        search(OrderListFixture);
        model.PageSize = 100;
        WaitUntil(() => !model.IsLoading);
        model.Rows.Single(row => row.OrderId == first.OrId).IsChecked = true;
        model.Rows.Single(row => row.OrderId == second.OrId).IsChecked = true;
        var originalStock = db.TStocks.AsNoTracking().Sum(stock => stock.StQuantity);
        var (confirmations, locked) = AcceptOrderConfirmation(owner, model, () => model.ConfirmSelectedCommand.Execute(null));
        var ids = new[] { first.OrId, second.OrId };
        Check(confirmations == 2 && locked && !model.HasSelection
            && db.TOrders.AsNoTracking().Count(order => ids.Contains(order.OrId) && order.OrStateFlag == 1) == 2
            && db.TChumons.Count(order => ids.Contains(order.OrId)) == 2 && db.TStocks.AsNoTracking().Sum(stock => stock.StQuantity) == originalStock,
            "bulk confirmation uses the existing dialog, prevents resubmission, and refreshes both committed rows");
        foreach (var row in model.Rows.Where(row => ids.Contains(row.OrderId))) row.IsChecked = true;
        InteractWithModal(owner, () => model.HideSelectedCommand.Execute(null), modal =>
        {
            var hide = (OrderHideViewModel)modal.DataContext;
            hide.Reason = "一覧検証の非表示";
            hide.HideCommand.Execute(null);
            WaitUntil(() => hide.HasChanges && hide.CanClose);
            modal.Close();
        });
        WaitUntil(() => !model.IsBusy);
        Check(!model.HasSelection && model.Rows.All(row => !ids.Contains(row.OrderId))
            && db.TOrders.AsNoTracking().Count(order => ids.Contains(order.OrId) && order.OrFlag == 2) == 2,
            "bulk hide keeps the reason dialog and removes the hidden rows from the active list");
    }

    private static void CheckOrderCsv(SalesManagementContext db, MClient customer, int productId, int employeeId)
    {
        var path = Path.Combine(output, $"{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path, $"外部受注番号,受注日,納期,営業所ID,担当者ID,顧客ID,商品ID,数量,単価\n" +
                $"LIST-CSV,2026-11-20,2026-11-30,{customer.SoId},{employeeId},{customer.ClId},{productId},2,105\n");
            var preview = CsvOrderImportService.BuildPreview(path);
            Check(preview.Errors.Count == 0 && CsvOrderImportService.ImportApproved(preview.Orders) == 1
                && db.TOrders.Any(order => order.ClId == customer.ClId && order.OrDate == new DateTime(2026, 11, 20)
                    && order.OrDeliveryDate == new DateTime(2026, 11, 30)), "CSV service validates and imports an order with a due date in the isolated SQL database");
        }
        finally { File.Delete(path); }
    }
}
