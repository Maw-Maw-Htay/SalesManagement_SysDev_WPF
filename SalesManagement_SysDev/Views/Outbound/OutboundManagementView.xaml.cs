using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace SalesManagement_SysDev;

public partial class OutboundManagementView : UserControl
{
    private readonly WarehouseMovementViewModel viewModel;
    private bool isOpeningDetail;
    private readonly HashSet<DatePicker> invalidDates = [];

    public OutboundManagementView() : this(new WarehouseMovementViewModel(WarehouseMovementKind.Outbound)) { }

    internal OutboundManagementView(WarehouseMovementViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        viewModel.IsFiltersExpanded = false;
        DataContext = viewModel;
        SelectTab(AllTab, WarehouseManagementTab.Visible);
        viewModel.Rows.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewStartingIndex == 0)
                MovementsGrid.ScrollIntoView(e.NewItems![0]);
        };
        Unloaded += (_, _) => viewModel.Dispose();
    }

    private void AllTab_Click(object sender, RoutedEventArgs e) => SelectTab(AllTab, WarehouseManagementTab.Visible);
    private void PendingTab_Click(object sender, RoutedEventArgs e) =>
        SelectTab(PendingTab, WarehouseManagementTab.Visible, WarehouseBusinessStatusFilter.Unconfirmed);
    private void ConfirmedTab_Click(object sender, RoutedEventArgs e) =>
        SelectTab(ConfirmedTab, WarehouseManagementTab.Visible, WarehouseBusinessStatusFilter.Confirmed);
    private void HiddenTab_Click(object sender, RoutedEventArgs e) => SelectTab(HiddenTab, WarehouseManagementTab.Hidden);

    private void SelectTab(MenuItem item, WarehouseManagementTab tab, WarehouseBusinessStatusFilter status = WarehouseBusinessStatusFilter.All)
    {
        viewModel.SelectTab(tab, status);
        foreach (var menuItem in new[] { AllTab, PendingTab, ConfirmedTab, HiddenTab })
            menuItem.IsChecked = ReferenceEquals(menuItem, item);
    }

    private void View_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        FilterFields.Columns = e.NewSize.Width < 620 ? 2 : 4;
        FilterScroll.MaxHeight = Math.Clamp(e.NewSize.Height - 405, 50, 195);
    }

    private void StatusRow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wrap = e.NewSize.Width < 525;
        Grid.SetRow(HistoryButton, wrap ? 1 : 0);
        Grid.SetColumn(HistoryButton, wrap ? 0 : 1);
        Grid.SetColumnSpan(HistoryButton, wrap ? 2 : 1);
        Grid.SetColumnSpan(StatusTabs, wrap ? 2 : 1);
    }

    private void ClearQuickSearch_Click(object sender, RoutedEventArgs e)
    {
        viewModel.Keyword = "";
        QuickSearch.Focus();
    }

    private void ToggleFilters_Click(object sender, RoutedEventArgs e) => viewModel.ToggleFilters();
    private void ClearConditions_Click(object sender, RoutedEventArgs e)
    {
        invalidDates.Clear();
        viewModel.ClearConditions();
    }
    private async void Reload_Click(object sender, RoutedEventArgs e) => await viewModel.LoadAsync();
    private void History_Click(object sender, RoutedEventArgs e)
    {
        viewModel.ShowingOutboundHistory = !viewModel.ShowingOutboundHistory;
        HistoryEmployeeColumn.Visibility = viewModel.ShowingOutboundHistory ? Visibility.Visible : Visibility.Collapsed;
        invalidDates.Clear();
        viewModel.ClearConditions();
    }
    private void Previous_Click(object sender, RoutedEventArgs e) => viewModel.SetPage(viewModel.Page - 1);
    private void Next_Click(object sender, RoutedEventArgs e) => viewModel.SetPage(viewModel.Page + 1);
    private void PageNumber_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (int.TryParse(PageNumber.Text, out var next)) viewModel.SetPage(next);
        PageNumber.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        e.Handled = true;
    }

    private void Date_Invalid(object sender, DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        if (sender is DatePicker picker) invalidDates.Add(picker);
        viewModel.SetDateError("出庫日を正しい形式で入力してください。");
    }
    private void Date_Changed(object sender, SelectionChangedEventArgs e) => ClearDateError(sender);
    private void Date_Editing(object sender, TextCompositionEventArgs e) => ClearDateError(sender);
    private void ClearDateError(object sender)
    {
        if (sender is DatePicker picker && invalidDates.Remove(picker) && invalidDates.Count == 0)
            viewModel.SetDateError("");
    }

    private void Grid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        Func<WarehouseMovementRecord, IComparable?>? key = e.Column.SortMemberPath switch
        {
            "Id" => x => x.Id,
            "RelatedOrderId" => x => x.RelatedOrderId,
            "PartnerName" => x => x.PartnerName,
            "MovementDate" => x => x.MovementDate,
            "OfficeName" => x => x.OfficeName,
            "EmployeeName" => x => x.EmployeeName,
            "LineCount" => x => x.LineCount,
            "Quantity" => x => x.Quantity,
            "BusinessState" => x => x.BusinessState,
            "ArrivalStatus" => x => x.ArrivalStatus,
            _ => null
        };
        if (key == null) return;
        var reverse = e.Column.SortDirection == ListSortDirection.Ascending;
        foreach (var column in MovementsGrid.Columns) column.SortDirection = null;
        e.Column.SortDirection = reverse ? ListSortDirection.Descending : ListSortDirection.Ascending;
        viewModel.Sort(key, reverse);
    }

    private async void Detail_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (isOpeningDetail || sender is not Button { Tag: WarehouseMovementRow row }) return;
        await OpenDetailAsync(row.Data.Id);
    }

    private async void MovementsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (FindAncestor<ButtonBase>(source) != null) { e.Handled = true; return; }
        var row = ItemsControl.ContainerFromElement(MovementsGrid, source) as DataGridRow;
        if (row?.Item is not WarehouseMovementRow movement) return;
        e.Handled = true;
        await OpenDetailAsync(movement.Data.Id);
    }

    private async Task OpenDetailAsync(int id)
    {
        if (isOpeningDetail) return;
        isOpeningDetail = true;
        try
        {
            var detail = await Task.Run(() => OutboundService.LoadDetail(id));
            if (detail == null)
            {
                F_OrderNotice.Show(Window.GetWindow(this), "詳細を表示できません",
                    "対象データが見つからないか、表示する権限がありません。", OrderNoticeKind.Warning);
                return;
            }
            var dialog = new OutboundDetailWindow(detail, viewModel.ShowingOutboundHistory) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true) await viewModel.LoadAsync();
        }
        catch (UnauthorizedAccessException ex)
        {
            F_OrderNotice.Show(Window.GetWindow(this), "詳細を表示できません", ex.Message, OrderNoticeKind.Warning);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Outbound detail load failed: {ex}");
            F_OrderNotice.Show(Window.GetWindow(this), "詳細を表示できません",
                "詳細を読み込めませんでした。データベースの接続状態を確認してください。", OrderNoticeKind.Error);
        }
        finally { isOpeningDetail = false; }
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
