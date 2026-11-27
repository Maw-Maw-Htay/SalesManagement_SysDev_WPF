using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace SalesManagement_SysDev;

public partial class PurchaseOrderManagementView : UserControl
{
    private readonly PurchaseOrderManagementViewModel viewModel;
    private bool isOpeningDetail;
    private readonly HashSet<DatePicker> invalidDates = [];

    public PurchaseOrderManagementView() : this(new PurchaseOrderManagementViewModel()) { }

    internal PurchaseOrderManagementView(PurchaseOrderManagementViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        viewModel.IsFiltersExpanded = false;
        DataContext = viewModel;
        UpdateTabs(viewModel.CanViewHidden ? VisibleTab : AllTab);
        viewModel.Rows.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewStartingIndex == 0)
                OrdersGrid.ScrollIntoView(e.NewItems![0]);
        };
        Unloaded += (_, _) => viewModel.Dispose();
    }

    private async void Reload_Click(object sender, RoutedEventArgs e) => await viewModel.LoadAsync();
    private void ToggleFilters_Click(object sender, RoutedEventArgs e) => viewModel.ToggleFilters();
    private void ClearConditions_Click(object sender, RoutedEventArgs e)
    {
        invalidDates.Clear();
        viewModel.ClearConditions();
    }
    private void ClearQuickSearch_Click(object sender, RoutedEventArgs e) { viewModel.Keyword = ""; QuickSearch.Focus(); }
    private void AllTab_Click(object sender, RoutedEventArgs e) => SelectTab(AllTab, PurchaseManagementTab.All);
    private void VisibleTab_Click(object sender, RoutedEventArgs e) => SelectTab(VisibleTab, PurchaseManagementTab.Visible);
    private void HiddenTab_Click(object sender, RoutedEventArgs e) => SelectTab(HiddenTab, PurchaseManagementTab.Hidden);
    private void WaitingTab_Click(object sender, RoutedEventArgs e) => SelectTab(WaitingTab, PurchaseManagementTab.Visible, PurchaseProgressFilter.Waiting);
    private void PartialTab_Click(object sender, RoutedEventArgs e) => SelectTab(PartialTab, PurchaseManagementTab.Visible, PurchaseProgressFilter.Partial);
    private void ReceivedTab_Click(object sender, RoutedEventArgs e) => SelectTab(ReceivedTab, PurchaseManagementTab.Visible, PurchaseProgressFilter.Received);

    private void SelectTab(MenuItem item, PurchaseManagementTab tab, PurchaseProgressFilter progress = PurchaseProgressFilter.All)
    {
        viewModel.SelectTab(tab, progress);
        UpdateTabs(item);
    }

    private void UpdateTabs(MenuItem active)
    {
        foreach (var tab in new[] { AllTab, VisibleTab, HiddenTab, WaitingTab, PartialTab, ReceivedTab })
            tab.IsChecked = ReferenceEquals(tab, active);
    }

    private void Previous_Click(object sender, RoutedEventArgs e) => viewModel.GoToPage(viewModel.Page - 1);
    private void Next_Click(object sender, RoutedEventArgs e) => viewModel.GoToPage(viewModel.Page + 1);
    private void PageNumber_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (int.TryParse(PageNumber.Text, out var page)) viewModel.GoToPage(page);
        PageNumber.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        e.Handled = true;
    }

    private void Date_Invalid(object sender, DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        if (sender is DatePicker picker) invalidDates.Add(picker);
        viewModel.SetDateInputError("発注日を正しい形式で入力してください。");
    }

    private void Date_Changed(object sender, SelectionChangedEventArgs e) => ClearDateError(sender);
    private void Date_Editing(object sender, TextCompositionEventArgs e) => ClearDateError(sender);
    private void ClearDateError(object sender)
    {
        if (sender is DatePicker picker && invalidDates.Remove(picker) && invalidDates.Count == 0)
            viewModel.SetDateInputError("");
    }

    private void OrdersGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        Func<PurchaseOrderRecord, IComparable>? key = e.Column.SortMemberPath switch
        {
            "Id" => x => x.Id,
            "OrderDate" => x => x.OrderDate,
            "MakerName" => x => x.MakerName,
            "EmployeeName" => x => x.EmployeeName,
            "ProductLineCount" => x => x.ProductLineCount,
            "OrderedQuantity" => x => x.OrderedQuantity,
            "ReceiptStatus" => x => x.ReceiptStatus,
            "ManagementFlag" => x => x.ManagementFlag,
            _ => null
        };
        if (key == null) return;
        var direction = e.Column.SortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        foreach (var column in OrdersGrid.Columns) column.SortDirection = null;
        e.Column.SortDirection = direction;
        viewModel.Sort(key, direction);
    }

    private void View_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        FilterFields.Columns = e.NewSize.Width < 610 ? 2 : 4;
        FilterScroll.MaxHeight = Math.Clamp(e.NewSize.Height - 385, 48, 132);
    }

    private void QuickSearchRow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wrapActions = e.NewSize.Width < 410;
        Grid.SetRow(SearchActions, wrapActions ? 1 : 0);
        Grid.SetColumn(SearchActions, wrapActions ? 0 : 1);
        Grid.SetColumnSpan(QuickSearchField, wrapActions ? 2 : 1);
        SearchActions.Margin = new Thickness(0, wrapActions ? 6 : 0, 0, 0);
    }

    private void StatusFiltersRow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wrapActions = e.NewSize.Width < 930;
        Grid.SetRow(StatusActions, wrapActions ? 1 : 0);
        Grid.SetColumn(StatusActions, wrapActions ? 0 : 1);
        Grid.SetColumnSpan(StatusActions, wrapActions ? 2 : 1);
        Grid.SetColumnSpan(StatusTabs, wrapActions ? 2 : 1);
        StatusActions.Margin = new Thickness(0, wrapActions ? 6 : 0, 0, 0);
    }

    internal async Task OpenRegistrationAsync()
    {
        if (!viewModel.CanRegister) return;
        var dialog = new F_HattyuRegister { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        await viewModel.LoadAsync();
        if (dialog.OpenSavedDetail && dialog.SavedOrderId is int id) await OpenDetailAsync(id);
    }

    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        if ((OrdersGrid.SelectedItem as PurchaseOrderRow ?? viewModel.SelectedRow) is not { } row || isOpeningDetail) return;
        isOpeningDetail = true;
        try
        {
            var detail = await Task.Run(() => PurchaseOrderService.LoadDetail(row.Data.Id));
            if (detail == null) F_OrderNotice.Show(Window.GetWindow(this), "発注書", "対象の発注を表示できません。", OrderNoticeKind.Warning);
            else new PurchaseOrderPrintPreview(detail) { Owner = Window.GetWindow(this) }.ShowDialog();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            F_OrderNotice.Show(Window.GetWindow(this), "発注書", "発注情報を読み込めませんでした。再読込してからお試しください。", OrderNoticeKind.Error);
        }
        finally { isOpeningDetail = false; }
    }

    private async void History_Click(object sender, RoutedEventArgs e)
    {
        if (isOpeningDetail) return;
        isOpeningDetail = true;
        try
        {
            var receipts = await Task.Run(PurchaseOrderService.LoadReceipts);
            new PurchaseReceiptHistoryWindow(receipts) { Owner = Window.GetWindow(this) }.ShowDialog();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            F_OrderNotice.Show(Window.GetWindow(this), "発注履歴", "入庫履歴を読み込めませんでした。接続状態を確認してください。", OrderNoticeKind.Error);
        }
        finally { isOpeningDetail = false; }
    }

    private void Csv_Click(object sender, RoutedEventArgs e) => PurchaseOrderCsv.Save(Window.GetWindow(this), viewModel.ExportRows);

    private async void AssignOffice_Click(object sender, RoutedEventArgs e)
    {
        if (isOpeningDetail || viewModel.IsLoading || viewModel.IsBusy
            || !PurchaseOrderService.CanAssignOffice || AppSession.Employee is not { } actor) return;
        var owner = Window.GetWindow(this);
        if ((OrdersGrid.SelectedItem as PurchaseOrderRow ?? viewModel.SelectedRow) is not { Data.RegisteredOfficeId: null } row)
        {
            F_OrderNotice.Show(owner, "旧発注の営業所を設定", "営業所が未設定の発注を一覧から1件選んでください。",
                OrderNoticeKind.Information);
            return;
        }

        isOpeningDetail = true;
        try
        {
            var offices = await Task.Run(() => PurchaseOrderService.LoadAssignableOffices(actor.EmId));
            var picker = new ComboBox { ItemsSource = offices, DisplayMemberPath = nameof(PurchaseOrderService.OfficeOption.Name),
                Margin = new Thickness(0, 12, 0, 12) };
            var save = new Button { Content = "営業所を設定", IsDefault = true, IsEnabled = false };
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock { Text = $"{row.Data.Code} の発注時の営業所を資料で確認し、選択してください。設定後はこの営業所の物流担当が扱えます。",
                TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(picker);
            panel.Children.Add(save);
            var dialog = new Window { Title = "旧発注の営業所を設定", Owner = owner, Width = 480,
                SizeToContent = SizeToContent.Height, Content = panel, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            picker.SelectionChanged += (_, _) => save.IsEnabled = picker.SelectedItem != null;
            save.Click += (_, _) => dialog.DialogResult = true;
            if (dialog.ShowDialog() != true || picker.SelectedItem is not PurchaseOrderService.OfficeOption office) return;
            if (!F_OrderNotice.Confirm(owner, "旧発注の営業所を設定",
                $"発注番号 {row.Data.Code} の営業所を「{office.Name}」に設定します。\n\nこの画面から設定後の変更はできません。設定しますか？",
                "この営業所に設定する"))
                return;
            var result = await Task.Run(() => PurchaseOrderService.AssignOffice(actor.EmId, row.Data.Id, office.Id));
            F_OrderNotice.Show(owner, "旧発注の営業所を設定", result.Message,
                result.Succeeded ? OrderNoticeKind.Success : OrderNoticeKind.Error);
            if (result.Succeeded) await viewModel.LoadAsync();
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine(error);
            F_OrderNotice.Show(owner, "旧発注の営業所を設定", "営業所を読み込めませんでした。", OrderNoticeKind.Error);
        }
        finally { isOpeningDetail = false; }
    }

    private async void ConfirmSelected_Click(object sender, RoutedEventArgs e)
    {
        if (isOpeningDetail || !viewModel.CanConfirmSelected || viewModel.SelectedRow is not { } row
            || AppSession.Employee is not { } actor) return;
        var owner = Window.GetWindow(this);
        if (!F_OrderNotice.Confirm(owner, "発注確定", "選択した発注を確定し、入庫情報を作成しますか？", "確定する"))
            return;
        isOpeningDetail = true;
        try
        {
            var result = await Task.Run(() => PurchaseOrderService.Confirm(row.Data.Id, actor.EmId));
            await viewModel.LoadAsync();
            F_OrderNotice.Show(owner, "発注確定", result.Message,
                result.Succeeded ? OrderNoticeKind.Success : OrderNoticeKind.Error);
        }
        finally { isOpeningDetail = false; }
    }

    private async void HideSelected_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedRow is not { } row || !PurchaseOrderService.CanHide) return;
        var dialog = new F_HattyuHide(row.Data) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        PurchaseOperationResult result;
        try { result = await viewModel.HideAsync(row, dialog.Reason); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            result = new(false, null, "再読込して発注の状態を確認してください。");
        }
        F_OrderNotice.Show(Window.GetWindow(this), result.Succeeded ? "発注を非表示にしました" : "発注を非表示にできませんでした",
            result.Message, result.Succeeded ? OrderNoticeKind.Success : OrderNoticeKind.Error);
    }

    private async void Detail_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (isOpeningDetail) return;
        if (sender is not Button { Tag: PurchaseOrderRow row }) return;
        await OpenDetailAsync(row.Data.Id);
    }

    private async void OrdersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        // The row's 「詳細」 button has its own Click handler. Let it own the gesture;
        // otherwise the same double-click can start both handlers and stack dialogs.
        if (FindAncestor<ButtonBase>(source) != null || FindAncestor<CheckBox>(source) != null)
        {
            e.Handled = true;
            return;
        }
        var row = ItemsControl.ContainerFromElement(OrdersGrid, source) as DataGridRow;
        if (row?.Item is PurchaseOrderRow order)
        {
            e.Handled = true;
            await OpenDetailAsync(order.Data.Id);
        }
    }

    private async Task OpenDetailAsync(int orderId)
    {
        if (isOpeningDetail) return;
        isOpeningDetail = true;
        try
        {
            var detail = await Task.Run(() => PurchaseOrderService.LoadDetail(orderId));
            if (detail == null)
            {
                F_OrderNotice.Show(Window.GetWindow(this), "発注詳細を表示できません", "発注が見つからないか、表示する権限がありません。", OrderNoticeKind.Warning);
                return;
            }
            var dialog = new F_HattyuDetail(detail) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true) await viewModel.LoadAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Purchase detail load failed: {ex}");
            F_OrderNotice.Show(Window.GetWindow(this), "発注詳細を表示できません", "発注詳細を読み込めませんでした。データベースの接続状態を確認してください。", OrderNoticeKind.Error);
        }
        finally
        {
            isOpeningDetail = false;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null)
        {
            if (element is T match) return match;
            element = element is Visual or Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }
        return null;
    }
}
