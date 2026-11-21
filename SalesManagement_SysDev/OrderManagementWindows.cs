using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public sealed partial class F_OrderManagement : Window
{
    private readonly DispatcherTimer keywordRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly HashSet<DatePicker> invalidDateInputs = [];
    private ScrollViewer? orderScrollViewer;
    private Point listDragStart;
    private double listDragStartOffset;
    private double listDragStartHorizontalOffset;
    private bool isListDragging;
    private readonly HashSet<ScrollViewer> configuredScrollViewers = new();
    private bool filterEventsEnabled;
    private CancellationTokenSource? activeOrderQuery;
    private long orderQueryVersion;
    private OrderListRequest? appliedOrderRequest;
    private readonly OrderDraftService draftService = new();

    private void ConfigureOrderGrid(DataGrid targetGrid)
    {
        targetGrid.PreviewMouseDoubleClick += OrderRowDoubleClick;
        targetGrid.Loaded += (_, _) => ConfigureGridScrollViewer(targetGrid);
        targetGrid.Unloaded += (_, _) => CloseRowMenu();
        targetGrid.Sorting += (_, args) =>
        {
            args.Handled = true;
            viewModel.SortBy(args.Column.SortMemberPath);
        };
        UpdateSortHeaders();
    }

    private void UpdateSortHeaders()
    {
        var active = viewModel.SortColumn.ToString();
        foreach (var column in grid.Columns)
        {
            column.SortDirection = column.SortMemberPath == active ? viewModel.SortDirection : null;
            if (column.SortMemberPath == nameof(OrderListSort.OrderId)) column.CanUserSort = !viewModel.IsDraftTab;
        }
    }

    private void ConfigureGridScrollViewer(DataGrid targetGrid)
    {
        var scrollViewer = FindVisualChild<ScrollViewer>(targetGrid);
        if (scrollViewer == null) return;
        if (!configuredScrollViewers.Add(scrollViewer)) return;
        scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        scrollViewer.CanContentScroll = true;
        scrollViewer.PreviewMouseLeftButtonDown += BeginListDrag;
        scrollViewer.PreviewMouseMove += MoveListDrag;
        scrollViewer.PreviewMouseLeftButtonUp += EndListDrag;
        scrollViewer.LostMouseCapture += EndListDrag;
        scrollViewer.ScrollChanged += (_, args) =>
        {
            if (args.HorizontalChange != 0 || args.VerticalChange != 0) CloseRowMenu();
        };
    }

    private void BeginListDrag(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left || e.ClickCount != 1
            || sender is not ScrollViewer viewer || IsInside<ScrollBar>(e.OriginalSource as DependencyObject)
            || IsInside<ButtonBase>(e.OriginalSource as DependencyObject)
            || IsInside<DataGridColumnHeader>(e.OriginalSource as DependencyObject)) return;
        orderScrollViewer = viewer;
        listDragStart = e.GetPosition(orderScrollViewer);
        listDragStartOffset = orderScrollViewer.VerticalOffset;
        listDragStartHorizontalOffset = orderScrollViewer.HorizontalOffset;
        isListDragging = true;
    }

    private void MoveListDrag(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!isListDragging || sender is not ScrollViewer viewer || !ReferenceEquals(orderScrollViewer, viewer)) return;
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            EndListDrag(sender, e);
            return;
        }
        var current = e.GetPosition(orderScrollViewer);
        var verticalDelta = current.Y - listDragStart.Y;
        var horizontalDelta = current.X - listDragStart.X;
        if (!viewer.IsMouseCaptured)
        {
            if (Math.Abs(verticalDelta) < SystemParameters.MinimumVerticalDragDistance
                && Math.Abs(horizontalDelta) < SystemParameters.MinimumHorizontalDragDistance) return;
            viewer.CaptureMouse();
        }
        orderScrollViewer.ScrollToVerticalOffset(listDragStartOffset - verticalDelta);
        orderScrollViewer.ScrollToHorizontalOffset(listDragStartHorizontalOffset - horizontalDelta);
        e.Handled = true;
    }

    private void EndListDrag(object? sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!isListDragging) return;
        isListDragging = false;
        if (orderScrollViewer?.IsMouseCaptured == true) orderScrollViewer.ReleaseMouseCapture();
        orderScrollViewer = null;
    }

    private static bool IsInside<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null)
        {
            if (element is T) return true;
            element = element is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var nested = FindVisualChild<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }

    private void FilterSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateClearFiltersButton();
        if (filterEventsEnabled) LoadRows();
    }

    private void FilterDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DatePicker picker) invalidDateInputs.Remove(picker);
        UpdateClearFiltersButton();
        if (filterEventsEnabled) LoadRows();
    }

    private void FilterDateValidationError(object? sender, DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        if (sender is DatePicker picker) invalidDateInputs.Add(picker);
        UpdateClearFiltersButton();
        if (filterEventsEnabled) LoadRows();
    }

    private void FilterDateLostFocus(object? sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (sender is DatePicker picker && invalidDateInputs.Contains(picker)
            && (string.IsNullOrWhiteSpace(picker.Text)
                || DateTime.TryParse(picker.Text, CultureInfo.CurrentCulture, DateTimeStyles.None, out _)))
        {
            invalidDateInputs.Remove(picker);
            UpdateClearFiltersButton();
            if (filterEventsEnabled) LoadRows();
        }
    }

    private void DeliveryUnsetChanged(object? sender, RoutedEventArgs e)
    {
        var shouldRefresh = filterEventsEnabled;
        var unsetOnly = deliveryUnsetOnly.IsChecked == true;
        filterEventsEnabled = false;
        try
        {
            if (unsetOnly)
            {
                invalidDateInputs.Remove(deliveryFromDate);
                invalidDateInputs.Remove(deliveryToDate);
                deliveryFromDate.SelectedDate = null;
                deliveryToDate.SelectedDate = null;
                deliveryFromDate.Text = "";
                deliveryToDate.Text = "";
            }
            deliveryFromDate.IsEnabled = !unsetOnly;
            deliveryToDate.IsEnabled = !unsetOnly;
        }
        finally
        {
            filterEventsEnabled = shouldRefresh;
        }
        if (!shouldRefresh) return;
        UpdateClearFiltersButton();
        LoadRows();
    }

    private void SearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateClearFiltersButton();
        if (!filterEventsEnabled) return;
        UpdateActiveFilterSummary();
        keywordRefreshTimer.Stop();
        if (sender is TextBox field && field.Text.Length == 0) LoadRows();
        else
        {
            activeOrderQuery?.Cancel();
            viewModel.IsLoading = true;
            resultCount.Text = "条件を反映中…";
            if (isHistoryView) historyResultCount.Text = "条件を反映中…";
            keywordRefreshTimer.Start();
        }
    }

    private void UpdateClearFiltersButton()
    {
        clearFiltersButton.Visibility = HasSearchConditions() ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatusMenuSelection();
    }

    private bool HasSearchConditions() => !string.IsNullOrWhiteSpace(viewModel.QuickSearchText)
            || !string.IsNullOrWhiteSpace(viewModel.OrderNumberFilter)
            || !string.IsNullOrWhiteSpace(viewModel.CustomerFilter)
            || !string.IsNullOrWhiteSpace(viewModel.EmployeeFilter)
            || (isHistoryView && (!string.IsNullOrWhiteSpace(historySearch.Text) || historyOperationFilter.SelectedIndex > 0))
            || viewModel.OrderDateFrom.HasValue
            || viewModel.OrderDateTo.HasValue
            || (!isHistoryView && (viewModel.DeliveryDateFrom.HasValue || viewModel.DeliveryDateTo.HasValue || viewModel.DeliveryDateUnsetOnly))
            || invalidDateInputs.Any(picker => !isHistoryView || (picker != deliveryFromDate && picker != deliveryToDate))
            || viewModel.SelectedOfficeId >= 0;

    private static bool IsSpecificFilterSelected(ComboBox comboBox)
    {
        return comboBox.SelectedItem is FilterItem item && item.Id >= 0;
    }
    private void UpdateActiveFilterSummary()
    {
        var summary = GetActiveFilterSummary();
        activeFilterSummary.Text = summary;
        activeFilterSummary.Visibility = summary.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private string GetActiveFilterSummary()
    {
        var conditions = new List<string>();
        if (isHistoryView) conditions.Add("対象：受注履歴");
        if (!string.IsNullOrWhiteSpace(viewModel.OrderNumberFilter))
            conditions.Add($"受注番号：{viewModel.OrderNumberFilter.Trim()}");
        if (!string.IsNullOrWhiteSpace(viewModel.CustomerFilter))
        {
            if (selectedCustomerNameId.HasValue)
                conditions.Add($"顧客：{viewModel.CustomerFilter.Trim()}（ID: {selectedCustomerNameId.Value}）");
            else if (int.TryParse(viewModel.CustomerFilter.Trim(), out var customerId))
                conditions.Add($"顧客ID：{customerId}");
            else
                conditions.Add($"顧客名：「{viewModel.CustomerFilter.Trim()}」を含む");
        }
        if (!string.IsNullOrWhiteSpace(viewModel.EmployeeFilter))
            conditions.Add(selectedEmployeeNameId.HasValue
                ? $"{(isHistoryView ? "実行担当者" : "担当者")}：{viewModel.EmployeeFilter.Trim()}（ID: {selectedEmployeeNameId.Value}）"
                : $"{(isHistoryView ? "実行担当者" : "担当者")}名：「{viewModel.EmployeeFilter.Trim()}」を含む");
        if (viewModel.OrderDateFrom.HasValue || viewModel.OrderDateTo.HasValue)
        {
            var from = viewModel.OrderDateFrom?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) ?? "指定なし";
            var to = viewModel.OrderDateTo?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) ?? "指定なし";
            conditions.Add($"{(isHistoryView ? "操作日時" : "受注日")}：{from} ～ {to}");
        }
        if (!isHistoryView && viewModel.DeliveryDateUnsetOnly)
            conditions.Add("納期：未設定のみ");
        else if (!isHistoryView && (viewModel.DeliveryDateFrom.HasValue || viewModel.DeliveryDateTo.HasValue))
        {
            var from = viewModel.DeliveryDateFrom?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) ?? "指定なし";
            var to = viewModel.DeliveryDateTo?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) ?? "指定なし";
            conditions.Add($"納期：{from} ～ {to}");
        }
        if (viewModel.SelectedOfficeId >= 0 && officeFilter.SelectedItem is FilterItem office)
            conditions.Add($"受付営業所：{office.Name}");
        if (!string.IsNullOrWhiteSpace(viewModel.QuickSearchText))
            conditions.Add($"キーワード：「{viewModel.QuickSearchText.Trim()}」");
        if (isHistoryView && !string.IsNullOrWhiteSpace(historySearch.Text))
            conditions.Add($"履歴検索：「{historySearch.Text.Trim()}」");
        if (isHistoryView && historyOperationFilter.SelectedIndex > 0)
            conditions.Add($"操作種別：{historyOperationFilter.SelectedItem}");
        return conditions.Count > 0 ? "検索条件：" + string.Join(" / ", conditions) : "";
    }
    private void SetDateRange(DateTime? from, DateTime? to)
    {
        filterEventsEnabled = false;
        try
        {
            invalidDateInputs.Remove(fromDate);
            invalidDateInputs.Remove(toDate);
            viewModel.OrderDateFrom = from;
            viewModel.OrderDateTo = to;
        }
        finally
        {
            filterEventsEnabled = true;
        }
        LoadRows();
    }
    private void ToggleFilters(object? sender, RoutedEventArgs e)
    {
        viewModel.ToggleFiltersCommand.Execute(null);
    }

    private bool TryReadFilterIds(out int? customerId, out string customerName, out string error)
    {
        customerId = null;
        customerName = "";
        error = "";
        var orderText = viewModel.OrderNumberFilter.Trim();
        if (orderText.Length > 0 && !OrderNumberSearch.TryGetDigits(orderText, out _))
        {
            error = "受注番号は数字または ORD-000123 形式で入力してください。";
            return false;
        }
        var customerText = viewModel.CustomerFilter.Trim();
        if (selectedCustomerNameId.HasValue)
        {
            customerId = selectedCustomerNameId.Value;
            return true;
        }
        if (customerText.Length == 0) return true;
        var looksLikeCustomerId = customerText.All(char.IsAsciiDigit)
            || ((customerText[0] is '+' or '-') && customerText.Length > 1 && customerText[1..].All(char.IsAsciiDigit));
        if (looksLikeCustomerId)
        {
            if (!int.TryParse(customerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCustomerId)
                || parsedCustomerId <= 0)
            {
                error = "顧客IDは正の整数で入力してください。";
                return false;
            }
            customerId = parsedCustomerId;
            return true;
        }
        customerName = customerText;
        return true;
    }
    private void LoadRows() => _ = LoadRowsAsync();

    private async Task<bool> LoadRowsAsync(int pageNumber = 1)
    {
        CloseRowMenu();
        keywordRefreshTimer.Stop();
        var requestVersion = ++orderQueryVersion;
        var cancellation = new CancellationTokenSource();
        var previous = activeOrderQuery;
        activeOrderQuery = cancellation;
        previous?.Cancel();
        var token = cancellation.Token;
        viewModel.IsLoading = true;

        try
        {
            var invalidRange = viewModel.OrderDateFrom.HasValue && viewModel.OrderDateTo.HasValue
                && viewModel.OrderDateFrom.Value.Date > viewModel.OrderDateTo.Value.Date;
            var invalidDeliveryRange = !isHistoryView && viewModel.DeliveryDateFrom.HasValue && viewModel.DeliveryDateTo.HasValue
                && viewModel.DeliveryDateFrom.Value.Date > viewModel.DeliveryDateTo.Value.Date;
            var invalidOrderDateText = invalidDateInputs.Contains(fromDate) || invalidDateInputs.Contains(toDate);
            var invalidDeliveryDateText = !isHistoryView && (invalidDateInputs.Contains(deliveryFromDate) || invalidDateInputs.Contains(deliveryToDate));
            var validIds = TryReadFilterIds(out var customerFilterId, out var customerName, out var idError);
            dateRangeError.Text = invalidOrderDateText ? "日付を正しい形式で入力してください。" : "開始日は終了日以前を指定してください。";
            dateRangeError.Visibility = invalidRange || invalidOrderDateText ? Visibility.Visible : Visibility.Collapsed;
            deliveryRangeError.Text = invalidDeliveryDateText ? "納期を正しい日付で入力してください。" : "納期の開始日は終了日以前を指定してください。";
            deliveryRangeError.Visibility = invalidDeliveryRange || invalidDeliveryDateText ? Visibility.Visible : Visibility.Collapsed;
            filterInputError.Text = idError;
            filterInputError.Visibility = validIds ? Visibility.Collapsed : Visibility.Visible;

            if (invalidRange || invalidDeliveryRange || invalidOrderDateText || invalidDeliveryDateText || !validIds)
            {
                var message = !validIds ? idError
                    : invalidOrderDateText || invalidDeliveryDateText ? "日付を正しい形式で入力してください。"
                    : invalidRange ? "期間の開始日と終了日を確認してください。"
                    : "納期の開始日と終了日を確認してください。";
                appliedOrderRequest = null;
                viewModel.ClearSelection();
                viewModel.ShowPage([], [], 0, 1);
                UpdateSortHeaders();
                historyGrid.ItemsSource = Array.Empty<OrderHistoryDisplayRow>();
                resultCount.Text = "検索条件を確認";
                pendingCount.Text = "0 件";
                confirmedCount.Text = "0 件";
                draftCount.Text = "0 件";
                hiddenCount.Text = "0 件";
                allCount.Text = "0 件";
                historyResultCount.Text = "検索条件を確認";
                historyEmptyState.Text = message;
                historyEmptyState.Visibility = isHistoryView ? Visibility.Visible : Visibility.Collapsed;
                orderEmptyState.Text = message;
                orderEmptyState.Visibility = isHistoryView ? Visibility.Collapsed : Visibility.Visible;
                UpdateClearFiltersButton();
                UpdateActiveFilterSummary();
                return false;
            }

            if (isHistoryView)
            {
                appliedOrderRequest = null;
                viewModel.ClearSelection();
                viewModel.ShowPage([], [], 0, 1);
                await LoadOperationHistoryAsync(customerFilterId, customerName, requestVersion, token);
                return false;
            }

            var criteria = new OrderSearchCriteria
            {
                OrderNumber = viewModel.OrderNumberFilter,
                CustomerId = customerFilterId,
                CustomerName = customerName,
                EmployeeId = selectedEmployeeNameId,
                EmployeeName = selectedEmployeeNameId.HasValue ? "" : viewModel.EmployeeFilter,
                OfficeId = viewModel.SelectedOfficeId,
                OrderDateFrom = viewModel.OrderDateFrom,
                OrderDateTo = viewModel.OrderDateTo,
                DeliveryDateFrom = viewModel.DeliveryDateFrom,
                DeliveryDateTo = viewModel.DeliveryDateTo,
                DeliveryDateUnsetOnly = viewModel.DeliveryDateUnsetOnly,
                QuickSearch = viewModel.QuickSearchText
            };

            var request = new OrderListRequest(criteria, viewModel.SelectedTab, viewModel.SortColumn, viewModel.SortDirection);
            if (appliedOrderRequest is not { } applied || applied.Criteria != criteria || applied.Tab != request.Tab)
            {
                viewModel.ClearSelection();
                pageNumber = 1;
            }
            using var db = new SalesManagementContext();
            var size = viewModel.PageSize;
            var drafts = await LoadDraftPageAsync(db, request, pageNumber, size, viewModel.SelectedDraftIds, token);
            var page = drafts != null && request.Tab == OrderStatusTab.Draft
                ? new OrderListPage(drafts.Rows, drafts.CheckedOffPage, await OrderListQuery.CountAsync(db, criteria, token), drafts.TotalCount, drafts.Number)
                : await OrderListQuery.ReadPageAsync(db, request, pageNumber, size, viewModel.SelectedOrderIds, token);

            if (requestVersion != orderQueryVersion || token.IsCancellationRequested) return false;
            appliedOrderRequest = request;
            viewModel.ShowPage(page.Rows, page.CheckedOffPage, page.TotalCount, page.Number);
            UpdateSortHeaders();
            FindVisualChild<ScrollViewer>(grid)?.ScrollToTop();
            orderEmptyState.Text = EmptyListMessage();
            UpdateClearFiltersButton();
            resultCount.Text = $"（{page.TotalCount:N0}件）";
            UpdateActiveFilterSummary();
            allCount.Text = $"{page.Counts.All:N0}";
            pendingCount.Text = $"{page.Counts.Pending:N0}";
            confirmedCount.Text = $"{page.Counts.Confirmed:N0}";
            draftCount.Text = drafts?.TotalCount.ToString("N0") ?? "—";
            hiddenCount.Text = $"{page.Counts.Hidden:N0}";
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (requestVersion != orderQueryVersion || token.IsCancellationRequested) return false;
            appliedOrderRequest = null;
            viewModel.ClearSelection();
            viewModel.ShowPage([], [], 0, 1);
            UpdateSortHeaders();
            var title = isHistoryView ? "受注履歴" : viewModel.IsDraftTab ? "下書き一覧" : "受注一覧";
            orderEmptyState.Text = $"{title}を取得できませんでした。";
            orderEmptyState.Visibility = Visibility.Visible;
            resultCount.Text = "取得失敗";
            pendingCount.Text = "—";
            confirmedCount.Text = "—";
            draftCount.Text = "—";
            hiddenCount.Text = "—";
            allCount.Text = "—";
            if (isHistoryView)
            {
                historyGrid.ItemsSource = Array.Empty<OrderHistoryDisplayRow>();
                historyResultCount.Text = "取得失敗";
                historyEmptyState.Text = "操作履歴を取得できませんでした。";
                historyEmptyState.Visibility = Visibility.Visible;
            }
            F_OrderNotice.Show(NavigationOwner ?? this, $"{title}の取得に失敗しました", ex.Message, OrderNoticeKind.Error);
        }
        finally
        {
            if (ReferenceEquals(activeOrderQuery, cancellation))
            {
                activeOrderQuery = null;
                viewModel.IsLoading = keywordRefreshTimer.IsEnabled;
            }
            cancellation.Dispose();
        }
        return false;
    }

    private string EmptyListMessage()
    {
        var target = viewModel.IsDraftTab ? "下書き" : "受注";
        return HasSearchConditions() ? $"該当する{target}はありません。検索条件を変更してください。"
            : viewModel.IsDraftTab ? "保存済みの下書きはありません。" : "受注データはまだありません。";
    }

    private static async Task<OrderDraftPage?> LoadDraftPageAsync(SalesManagementContext db, OrderListRequest request,
        int number, int size, IReadOnlyList<Guid> checkedIds, CancellationToken token)
    {
        try { return await OrderDraftService.ReadPageAsync(db, request, number, size, checkedIds, token); }
        catch (Exception ex) when (request.Tab != OrderStatusTab.Draft && !token.IsCancellationRequested)
        {
            Trace.TraceError("下書き件数を取得できませんでした: {0}", ex);
            return null;
        }
    }

    private void ImportCsv()
    {
        if (!AppSession.CanManageOrders)
        {
            MessageBox.Show(NavigationOwner ?? this, "CSV取込の権限がありません。", "権限エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "受注CSVを選択",
            Filter = "CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(NavigationOwner ?? this) != true) return;

        CsvOrderImportPreview preview;
        try { preview = CsvOrderImportService.BuildPreview(picker.FileName); }
        catch (Exception ex)
        {
            MessageBox.Show(NavigationOwner ?? this, $"CSVの検証に失敗しました。\n{ex.Message}", "CSV取込エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var previewWindow = new CsvOrderImportPreviewWindow(preview) { Owner = NavigationOwner ?? this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        if (previewWindow.ShowDialog() != true) return;
        try
        {
            var imported = CsvOrderImportService.ImportApproved(preview.Orders);
            MessageBox.Show(NavigationOwner ?? this, $"{imported}件の受注を取り込みました。", "CSV取込完了", MessageBoxButton.OK, MessageBoxImage.Information);
            _ = LoadRowsAsync(viewModel.PageNumber);
        }
        catch (Exception ex)
        {
            MessageBox.Show(NavigationOwner ?? this, $"受注の取込に失敗しました。保存前の検証またはDB保存でエラーになりました。\n{ex.Message}", "CSV取込エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void PrintFilteredOrders()
    {
        if (isHistoryView)
        {
            MessageBox.Show(NavigationOwner ?? this, "受注履歴ではなく受注一覧を表示してから印刷してください。", "印刷", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!viewModel.CanUseRegisteredOrders) return;
        var tab = viewModel.SelectedTab;
        var search = GetActiveFilterSummary();
        viewModel.IsBusy = true;
        try
        {
            if (!await LoadRowsAsync(viewModel.PageNumber) || isHistoryView || tab != viewModel.SelectedTab
                || search != GetActiveFilterSummary() || appliedOrderRequest is not { } request) return;
            using var db = new SalesManagementContext();
            var printRows = await OrderListQuery.ReadPrintRowsAsync(db, request, CancellationToken.None);
            if (isHistoryView || request != appliedOrderRequest || search != GetActiveFilterSummary()) return;
            var status = tab switch
            {
                OrderStatusTab.Pending => "未確定",
                OrderStatusTab.Confirmed => "確定済み",
                OrderStatusTab.Hidden => "非表示",
                _ => "すべて"
            };
            new OrderListPrintPreviewWindow(printRows, $"対象：{status}　　{search}")
            {
                Owner = NavigationOwner ?? this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            }.ShowDialog();
        }
        catch (Exception ex)
        {
            F_OrderNotice.Show(NavigationOwner ?? this, "受注一覧の印刷", "印刷データを取得できませんでした。\n" + ex.Message, OrderNoticeKind.Error);
        }
        finally { viewModel.IsBusy = false; }
    }

    private async Task LoadOperationHistoryAsync(int? customerId, string customerName, long requestVersion, CancellationToken cancellationToken)
    {
        using var db = new SalesManagementContext();
        var page = await OrderHistoryService.ReadAsync(db, new OrderHistoryFilter
        {
            From = viewModel.OrderDateFrom,
            To = viewModel.OrderDateTo,
            OrderNumberText = viewModel.OrderNumberFilter,
            CustomerId = customerId ?? -1,
            SelectedCustomerNameId = selectedCustomerNameId ?? -1,
            CustomerNamePrefix = selectedCustomerNameId.HasValue ? "" : customerName,
            ActorEmployeeId = selectedEmployeeNameId ?? -1,
            ActorNamePrefix = selectedEmployeeNameId.HasValue ? "" : viewModel.EmployeeFilter,
            OfficeId = viewModel.SelectedOfficeId,
            CommonKeyword = viewModel.QuickSearchText,
            Keyword = historySearch.Text,
            Operation = historyOperationFilter.SelectedIndex > 0 ? historyOperationFilter.SelectedItem as string ?? "" : ""
        }, cancellationToken);

        if (requestVersion != orderQueryVersion || cancellationToken.IsCancellationRequested) return;
        var rows = page.Rows.Select(x => new OrderHistoryDisplayRow(x)).ToList();
        historyGrid.ItemsSource = rows;
        historyResultCount.Text = $"該当件数：{rows.Count} / 全 {page.Total} 件";
        historyEmptyState.Text = "該当する操作履歴がありません。";
        historyEmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        resultCount.Text = $"履歴 {rows.Count} 件表示 / 全 {page.Total} 件";
        UpdateClearFiltersButton();
        UpdateActiveFilterSummary();
    }
    private async Task InitializeFilterDataAsync()
    {
        await LoadFilterMastersAsync();
        LoadRows();
    }

    private async Task LoadFilterMastersAsync()
    {
        var officeId = viewModel.SelectedOfficeId;
        filterEventsEnabled = false;
        try
        {
            using var db = new SalesManagementContext();
            var clients = db.MClients.Where(client => client.ClFlag == 0);
            var employees = db.MEmployees.Where(employee => employee.EmFlag == 0);
            var offices = db.MSalesOffices.Where(office => office.SoFlag == 0);
            if (!AppSession.IsAdmin)
            {
                var currentOfficeId = AppSession.Employee!.SoId;
                clients = clients.Where(client => client.SoId == currentOfficeId);
                employees = employees.Where(employee => employee.SoId == currentOfficeId);
                offices = offices.Where(office => office.SoId == currentOfficeId);
            }
            var clientRows = await clients.OrderBy(client => client.ClName)
                .Select(client => new { client.ClId, client.ClName }).ToListAsync();
            var employeeRows = await employees.OrderBy(employee => employee.EmName)
                .Select(employee => new { employee.EmId, employee.EmName }).ToListAsync();
            var officeRows = await offices.OrderBy(office => office.SoName)
                .Select(office => new { office.SoId, office.SoName }).ToListAsync();

            customerCandidates = clientRows.Select(client =>
                new FilterItem(client.ClId, client.ClName, $"{client.ClName}（ID: {client.ClId}）")).ToList();
            employeeCandidates = employeeRows.Select(employee =>
                new FilterItem(employee.EmId, employee.EmName, $"{employee.EmName}（ID: {employee.EmId}）")).ToList();
            officeFilter.ItemsSource = new[] { new FilterItem(-1, "すべて") }
                .Concat(officeRows.Select(office => new FilterItem(office.SoId, office.SoName, null))).ToList();
            viewModel.SelectedOfficeId = officeRows.Any(office => office.SoId == officeId) ? officeId : -1;
        }
        catch (Exception ex)
        {
            F_OrderNotice.Show(NavigationOwner ?? this, "検索条件の読込に失敗しました", ex.Message, OrderNoticeKind.Error);
        }
        finally
        {
            filterEventsEnabled = true;
            UpdateClearFiltersButton();
        }
    }
    private void ClearFilters(object? sender, RoutedEventArgs e)
    {
        filterEventsEnabled = false;
        keywordRefreshTimer.Stop();
        try
        {
            invalidDateInputs.Clear();
            viewModel.QuickSearchText = "";
            viewModel.OrderNumberFilter = "";
            viewModel.CustomerFilter = "";
            viewModel.EmployeeFilter = "";
            viewModel.SelectedOfficeId = -1;
            selectedCustomerNameId = null;
            selectedEmployeeNameId = null;
            customerSuggestionPopup.IsOpen = false;
            employeeSuggestionPopup.IsOpen = false;
            historySearch.Clear();
            historyOperationFilter.SelectedIndex = 0;
            viewModel.OrderDateFrom = null;
            viewModel.OrderDateTo = null;
            viewModel.DeliveryDateFrom = null;
            viewModel.DeliveryDateTo = null;
            viewModel.DeliveryDateUnsetOnly = false;
            fromDate.Text = "";
            toDate.Text = "";
            deliveryFromDate.Text = "";
            deliveryToDate.Text = "";
            officeFilter.SelectedIndex = 0;
        }
        finally
        {
            filterEventsEnabled = true;
            UpdateClearFiltersButton();
        }
        LoadRows();
    }
    private void ConfirmSelected(object? sender, RoutedEventArgs e)
    {
        ConfirmRows(viewModel.SelectedOrders);
    }

    private async void ConfirmRows(IReadOnlyList<OrderListRow> rows)
    {
        if (viewModel.IsBusy || viewModel.IsLoading || viewModel.IsDraftTab) return;
        if (!(AppSession.IsAdmin || AppSession.IsSales))
        {
            F_OrderNotice.Show(NavigationOwner ?? this, "権限エラー", "受注確定は管理者または営業担当者のみ実行できます。", OrderNoticeKind.Warning);
            return;
        }
        if (rows.Count == 0) { F_OrderNotice.Show(NavigationOwner ?? this, "受注確定", "確定する受注にチェックを付けてください。", OrderNoticeKind.Warning); return; }
        var eligibleRows = rows.Where(row => row.CanConfirm).ToList();
        if (eligibleRows.Count == 0)
        {
            F_OrderNotice.Show(NavigationOwner ?? this, "受注確定", "未確定かつ表示中の受注が選択されていません。", OrderNoticeKind.Warning);
            return;
        }
        var excludedCount = rows.Count - eligibleRows.Count;
        var prompt = $"選択した受注 {eligibleRows.Count} 件を確定します。よろしいですか？"
            + (excludedCount > 0 ? $"\n対象外の {excludedCount} 件は処理しません。" : "")
            + "\n\n受注が確定し、注文が作成されます。在庫はこの操作では減算しません。";
        viewModel.IsBusy = true;
        var attempted = false;
        try
        {
            if (!F_OrderNotice.Confirm(NavigationOwner ?? this, "受注を確定", prompt, "受注を確定")) return;
            attempted = true;
            var results = eligibleRows.Select(row =>
            {
                var succeeded = BusinessService.ConfirmOrder(row.OrderId, AppSession.Employee!.EmId, out var message);
                return (succeeded, row.OrderId, text: $"{row.OrderNumber}：{message}");
            }).ToList();
            viewModel.UncheckOrders(results.Where(result => result.succeeded).Select(result => result.OrderId));
            var summary = string.Join(Environment.NewLine, results.Select(result => result.text));
            if (excludedCount > 0) summary += $"\n対象外のため処理しなかった受注: {excludedCount} 件";
            var allSucceeded = excludedCount == 0 && results.All(result => result.succeeded);
            F_OrderNotice.Show(NavigationOwner ?? this, "受注確定結果", summary,
                allSucceeded ? OrderNoticeKind.Success : OrderNoticeKind.Warning);
        }
        catch (Exception ex)
        {
            F_OrderNotice.Show(NavigationOwner ?? this, "受注確定", "受注を確定できませんでした。\n" + ex.Message, OrderNoticeKind.Error);
        }
        finally
        {
            if (attempted) await LoadRowsAsync(viewModel.PageNumber);
            viewModel.IsBusy = false;
        }
    }

    private void RowConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (TakeRowAction(sender) is { CanConfirm: true } row) ConfirmRows(new[] { row });
    }

    private void HideSelected(object? sender, RoutedEventArgs e)
    {
        HideRows(viewModel.SelectedOrders);
    }

    private async void HideRows(IEnumerable<OrderListRow> selectedRows)
    {
        if (viewModel.IsBusy || viewModel.IsLoading || viewModel.IsDraftTab) return;
        if (!AppSession.CanManageOrders) { F_OrderNotice.Show(NavigationOwner ?? this, "権限エラー", "受注の非表示は管理者または営業担当者のみ実行できます。", OrderNoticeKind.Warning); return; }
        var ids = selectedRows.Where(row => row.CanHide).Select(row => row.OrderId).ToArray();
        if (ids.Length == 0) { F_OrderNotice.Show(NavigationOwner ?? this, "受注非表示", "非表示にできる受注を選択してください。", OrderNoticeKind.Warning); return; }
        viewModel.IsBusy = true;
        try
        {
            var dialog = new F_OrderHide(ids) { Owner = NavigationOwner ?? this };
            dialog.ShowDialog();
            if (dialog.HasAttempted)
            {
                viewModel.UncheckOrders(dialog.SuccessfulOrderIds);
                await LoadRowsAsync(viewModel.PageNumber);
            }
        }
        finally
        {
            viewModel.IsBusy = false;
        }
    }

    private void RowHideClick(object? sender, RoutedEventArgs e)
    {
        if (TakeRowAction(sender) is { CanHide: true } row) HideRows(new[] { row });
    }

    private void RowRestoreClick(object? sender, RoutedEventArgs e)
    {
        if (TakeRowAction(sender) is { CanRestore: true } row) RestoreOrder(row.OrderId);
    }

    private bool RestoreOrder(int orderId)
    {
        if (!AppSession.IsAdmin)
        {
            F_OrderNotice.Show(NavigationOwner ?? this, "権限エラー", "受注の表示復帰は管理者のみ実行できます。", OrderNoticeKind.Warning);
            return false;
        }
        var reason = Microsoft.VisualBasic.Interaction.InputBox("表示復帰理由を入力してください。", "受注の表示復帰", "");
        if (string.IsNullOrWhiteSpace(reason))
        {
            F_OrderNotice.Show(NavigationOwner ?? this, "表示復帰", "表示復帰には理由が必要です。", OrderNoticeKind.Warning);
            return false;
        }
        if (!BusinessService.SetOrderVisibility(orderId, AppSession.Employee!.EmId, false, reason, out var message))
        {
            F_OrderNotice.Show(NavigationOwner ?? this, "表示復帰", message, OrderNoticeKind.Warning);
            return false;
        }
        _ = LoadRowsAsync(viewModel.PageNumber);
        return true;
    }

    private void RowUpdateClick(object? sender, RoutedEventArgs e)
    {
        if (TakeRowAction(sender) is not { } row) return;
        if (!row.CanEdit)
        {
            F_OrderNotice.Show(NavigationOwner ?? this, "受注更新", "未確定かつ表示中の受注だけを更新できます。", OrderNoticeKind.Warning);
            return;
        }
        var snapshot = LoadOrderDetailSnapshot(row.OrderId, editableOnly: true);
        if (snapshot == null)
        {
            F_OrderNotice.Show(NavigationOwner ?? this, "受注更新", "この受注は現在更新できません。一覧を更新します。", OrderNoticeKind.Information);
            _ = LoadRowsAsync(viewModel.PageNumber);
            return;
        }
        var detail = new F_OrderDetail(snapshot, NavigationOwner ?? this);
        detail.OpenUpdateEditor();
        _ = LoadRowsAsync(viewModel.PageNumber);
    }

    private async void OrderRowDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!viewModel.CanUseOrderList || e.ChangedButton != System.Windows.Input.MouseButton.Left
            || sender is not DataGrid currentGrid
            || e.OriginalSource is not DependencyObject source
            || IsInside<ButtonBase>(source)
            || ItemsControl.ContainerFromElement(currentGrid, source) is not DataGridRow { Item: OrderListRow row }) return;

        e.Handled = true;
        EndListDrag(sender, e);
        currentGrid.SelectedItem = row;
        if (row.IsDraft) await RunDraftActionAsync(row, delete: false);
        else ShowDetails(row);
    }

    private async void RowDetailsClick(object? sender, RoutedEventArgs e)
    {
        if (TakeRowAction(sender) is not { } row) return;
        if (row.IsDraft) await RunDraftActionAsync(row, delete: false);
        else ShowDetails(row);
    }

    private void ShowDetails(OrderListRow row)
    {
        if (!viewModel.CanUseRegisteredOrders || row.IsDraft) return;
        var snapshot = LoadOrderDetailSnapshot(row.OrderId);
        if (snapshot == null) return;
        new F_OrderDetail(snapshot, NavigationOwner ?? this).ShowDialog();
        _ = LoadRowsAsync(viewModel.PageNumber);
    }

    private async void RowOpenDraftClick(object sender, RoutedEventArgs e)
    {
        if (TakeRowAction(sender) is { IsDraft: true } row) await RunDraftActionAsync(row, delete: false);
    }

    private async void RowDeleteDraftClick(object sender, RoutedEventArgs e)
    {
        if (TakeRowAction(sender) is { IsDraft: true } row) await RunDraftActionAsync(row, delete: true);
    }

    private async Task RunDraftActionAsync(OrderListRow row, bool delete)
    {
        if (!viewModel.CanUseOrderList || !viewModel.IsDraftTab || !viewModel.Rows.Contains(row)
            || row.DraftToken is not { } token || NavigationOwner is not F_Main owner || !owner.IsOrderWorkspaceActive) return;
        var version = orderQueryVersion;
        bool IsCurrent() => version == orderQueryVersion && owner.IsOrderWorkspaceActive;
        viewModel.IsBusy = true;
        var refresh = false;
        try
        {
            if (delete)
            {
                if (!F_OrderNotice.Confirm(owner, "下書きの削除", $"{row.CustomerName}の下書きを削除しますか？",
                        "削除する", destructive: true)) return;
                await Task.Run(() => draftService.Delete(token));
                refresh = true;
            }
            else
            {
                var draft = await Task.Run(() => draftService.Open(token.Id));
                if (IsCurrent()) owner.ShowOrderRegistration(draft);
            }
        }
        catch (Exception ex)
        {
            refresh = true;
            if (!IsCurrent()) return;
            var message = ex is OrderDraftException ? ex.Message : "接続を確認してから、もう一度お試しください。";
            if (ex is not OrderDraftException) Trace.TraceError("下書きの操作に失敗しました: {0}", ex);
            F_OrderNotice.Show(owner, delete ? "下書きを削除できませんでした" : "下書きを開けませんでした", message, OrderNoticeKind.Warning);
        }
        finally
        {
            if (refresh && owner.IsOrderWorkspaceActive) await LoadRowsAsync(viewModel.PageNumber);
            viewModel.IsBusy = false;
        }
    }

    private OrderDetailSnapshot? LoadOrderDetailSnapshot(int orderId, bool editableOnly = false)
    {
        using var db = new SalesManagementContext();
        var query = db.TOrders
            .AsNoTracking()
            .Include(x => x.Cl)
            .Include(x => x.So)
            .Include(x => x.Em)
            .Include(x => x.TOrderDetails)
            .ThenInclude(x => x.Pr)
            .AsQueryable();
        if (!AppSession.CanManageOrders || AppSession.Employee == null) return null;
        if (!AppSession.IsAdmin) query = query.Where(x => x.SoId == AppSession.Employee.SoId);
        if (editableOnly) query = query.Where(order => order.OrFlag == 0 && (order.OrStateFlag ?? 0) == 0 && !order.TChumons.Any());
        var order = query.SingleOrDefault(x => x.OrId == orderId);
        if (order == null) return null;

        return new OrderDetailSnapshot
        {
            OrderId = order.OrId,
            OrderDate = order.OrDate,
            DeliveryDate = order.OrDeliveryDate,
            OfficeName = order.So.SoName,
            OfficeId = order.SoId,
            CustomerId = order.ClId,
            CustomerName = order.Cl.ClName,
            CustomerAddress = order.Cl.ClAddress,
            CustomerPhone = order.Cl.ClPhone,
            CustomerContact = order.ClCharge,
            EmployeeName = order.Em.EmName,
            EmployeeId = order.EmId,
            StateFlag = order.OrStateFlag ?? 0,
            ManagementFlag = order.OrFlag,
            HiddenReason = order.OrHidden,
            Lines = order.TOrderDetails.Where(x => x.OrQuantity != 0 || x.OrTotalPrice != 0m)
                .OrderBy(x => x.OrDetailId)
                .Select(x => new OrderDetailLine
                {
                    DetailId = x.OrDetailId,
                    ProductId = x.PrId,
                    ProductCode = x.Pr.PrJcode ?? x.Pr.PrModelNumber,
                    ProductName = x.Pr.PrName,
                    UnitPrice = x.OrQuantity == 0 ? x.Pr.Price : decimal.Round(x.OrTotalPrice / x.OrQuantity, 0),
                    Quantity = x.OrQuantity,
                    Subtotal = x.OrTotalPrice
                })
                .ToList()
        };
    }

    private sealed record FilterItem(int Id, string Name, string? Display = null)
    {
        public override string ToString() => Display ?? Name;
    }

}

public sealed class OrderDetailSnapshot
{
    public int OrderId { get; init; }
    public DateTime OrderDate { get; init; }
    public DateTime? DeliveryDate { get; init; }
    public string OfficeName { get; init; } = "";
    public int OfficeId { get; init; }
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = "";
    public string CustomerAddress { get; init; } = "";
    public string CustomerPhone { get; init; } = "";
    public string? CustomerContact { get; init; }
    public string EmployeeName { get; init; } = "";
    public int EmployeeId { get; init; }
    public int StateFlag { get; init; }
    public int ManagementFlag { get; init; }
    public string? HiddenReason { get; init; }
    public List<OrderDetailLine> Lines { get; init; } = new();
}

public sealed record OrderUpdateOption(int Id, string Name, int OfficeId, string Phone, string Address)
{
    public override string ToString() => Name;
}

public sealed record OrderProductOption(int Id, string Name, string Code, decimal Price)
{
    public string Display => $"[{Code}] {Name}（単価: ¥{Price:N0}）";
    public override string ToString() => Display;
}

public sealed class OrderDetailLine
{
    public int DetailId { get; init; }
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public decimal UnitPrice { get; init; }
    public bool HasKnownUnitPrice { get; init; } = true;
    public int Quantity { get; init; }
    public decimal Subtotal { get; init; }
}
