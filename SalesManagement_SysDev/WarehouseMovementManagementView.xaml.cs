using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace SalesManagement_SysDev;

public partial class WarehouseMovementManagementView : UserControl
{
    private readonly WarehouseMovementViewModel viewModel;
    private bool isOpeningDetail;
    private WarehouseBusinessStatusFilter statusTab;

    public WarehouseMovementManagementView(WarehouseMovementKind kind) : this(new WarehouseMovementViewModel(kind)) { }

    internal WarehouseMovementManagementView(WarehouseMovementViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        viewModel.IsFiltersExpanded = false;
        DataContext = viewModel;
        UpdateMode();
        Unloaded += (_, _) => viewModel.Dispose();
    }

    private async void Reload_Click(object sender, RoutedEventArgs e) => await viewModel.LoadAsync();
    private void ToggleFilters_Click(object sender, RoutedEventArgs e) => viewModel.ToggleFilters();
    private void ClearConditions_Click(object sender, RoutedEventArgs e) => viewModel.ClearConditions();
    private void ClearQuickSearch_Click(object sender, RoutedEventArgs e) { viewModel.Keyword = ""; QuickSearch.Focus(); }
    private void Previous_Click(object sender, RoutedEventArgs e) => viewModel.SetPage(viewModel.Page - 1);
    private void Next_Click(object sender, RoutedEventArgs e) => viewModel.SetPage(viewModel.Page + 1);
    private void AllTab_Click(object sender, RoutedEventArgs e) => SelectTab(WarehouseManagementTab.Visible);
    private void PendingTab_Click(object sender, RoutedEventArgs e) => SelectTab(WarehouseManagementTab.Visible, WarehouseBusinessStatusFilter.Unconfirmed);
    private void ConfirmedTab_Click(object sender, RoutedEventArgs e) => SelectTab(WarehouseManagementTab.Visible, WarehouseBusinessStatusFilter.Confirmed);
    private void HiddenTab_Click(object sender, RoutedEventArgs e) => SelectTab(WarehouseManagementTab.Hidden);

    private void SelectTab(WarehouseManagementTab tab, WarehouseBusinessStatusFilter status = WarehouseBusinessStatusFilter.All)
    {
        statusTab = status;
        viewModel.SelectTab(tab, status);
        UpdateMode();
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        viewModel.ToggleInboundHistory();
        UpdateMode();
    }

    private void UpdateMode()
    {
        var history = viewModel.ShowingInboundHistory;
        var hidden = viewModel.ActiveTab == WarehouseManagementTab.Hidden;
        StatusMenu.Visibility = history ? Visibility.Collapsed : Visibility.Visible;
        SelectionColumn.Visibility = ReceiptActions.Visibility = history || hidden ? Visibility.Collapsed : Visibility.Visible;
        VisibilityColumn.Visibility = hidden || history && viewModel.CanViewHidden ? Visibility.Visible : Visibility.Collapsed;
        MovementsGrid.CanUserSortColumns = !history;
        BusinessStatusPicker.IsEnabled = !history;
        if (history) foreach (var column in MovementsGrid.Columns) column.SortDirection = null;
        AllTab.IsChecked = !hidden && statusTab == WarehouseBusinessStatusFilter.All;
        PendingTab.IsChecked = !hidden && statusTab == WarehouseBusinessStatusFilter.Unconfirmed;
        ConfirmedTab.IsChecked = !hidden && statusTab == WarehouseBusinessStatusFilter.Confirmed;
        HiddenTab.IsChecked = hidden;
    }

    private void ReceivingView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.HeightChanged && AdvancedScroll != null)
            AdvancedScroll.MaxHeight = Math.Clamp(e.NewSize.Height - 365, 90, 220);
    }

    private void Date_ValidationError(object sender, DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        viewModel.SetDateError("有効な日付を指定してください。無効な入力は適用されません。");
    }

    private void Date_Changed(object sender, SelectionChangedEventArgs e) => viewModel.SetDateError("");

    private void MovementsGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (viewModel.ShowingInboundHistory) return;
        var descending = e.Column.SortDirection == ListSortDirection.Ascending;
        foreach (var column in MovementsGrid.Columns) column.SortDirection = null;
        e.Column.SortDirection = descending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        Func<WarehouseMovementRecord, IComparable?> key = e.Column.SortMemberPath switch
        {
            nameof(WarehouseMovementRow.ReferenceCode) => r => r.ReferenceId,
            nameof(WarehouseMovementRow.PartnerDisplay) => r => r.PartnerDisplay,
            nameof(WarehouseMovementRow.MovementDateText) => r => r.MovementDate,
            nameof(WarehouseMovementRow.OrderedQuantityText) => r => r.OrderedQuantity,
            nameof(WarehouseMovementRow.QuantityText) => r => r.Quantity,
            nameof(WarehouseMovementRow.BusinessStatusLabel) => r => r.BusinessState,
            _ => r => r.Id
        };
        viewModel.Sort(key, descending);
    }

    private void SelectReceipt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: WarehouseMovementRow row } toggle)
        {
            viewModel.SelectReceipt(row);
            toggle.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
        }
    }

    private async void ConfirmSelected_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.CanConfirmReceipt || viewModel.SelectedReceipt is not { } selection || isOpeningDetail) return;
        isOpeningDetail = true;
        try
        {
            var detail = await Task.Run(() => LoadInboundDetail(selection.Data, includePurchase: false));
            if (!IsLoaded || !viewModel.CanConfirmReceipt || !ReferenceEquals(selection, viewModel.SelectedReceipt)) return;
            if (detail == null || !detail.Record.IsUnconfirmed || detail.Record.ManagementFlag != WarehousingService.ActiveFlag)
            {
                F_OrderNotice.Show(Window.GetWindow(this), "入庫を確定できません",
                    "入庫の状態が変更されました。再読込してからお試しください。", OrderNoticeKind.Warning);
                await viewModel.LoadAsync();
                return;
            }
            var dialog = new F_WarehouseMovementConfirm(detail) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true) await viewModel.LoadAsync();
        }
        catch (Exception ex)
        {
            if (IsLoaded) ShowLoadError(ex);
        }
        finally { isOpeningDetail = false; }
    }

    private async void Detail_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: WarehouseMovementRow row }) await OpenDetailAsync(row.Data);
    }

    private async void MovementsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source || FindAncestor<ButtonBase>(source) != null) return;
        if (ItemsControl.ContainerFromElement(MovementsGrid, source) is not DataGridRow { Item: WarehouseMovementRow row }) return;
        e.Handled = true;
        await OpenDetailAsync(row.Data);
    }

    private async Task OpenDetailAsync(WarehouseMovementRecord row)
    {
        if (isOpeningDetail) return;
        isOpeningDetail = true;
        var history = viewModel.ShowingInboundHistory;
        try
        {
            var detail = await Task.Run(() => LoadInboundDetail(row, includePurchase: true));
            if (!IsLoaded) return;
            if (detail == null)
            {
                F_OrderNotice.Show(Window.GetWindow(this), "詳細を表示できません", "対象の入庫が見つからないか、参照範囲外です。", OrderNoticeKind.Warning);
                await viewModel.LoadAsync();
                return;
            }
            var dialog = new F_WarehouseMovementDetail(detail, readOnly: history || viewModel.ShowingInboundHistory) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true) await viewModel.LoadAsync();
        }
        catch (Exception ex) { if (IsLoaded) ShowLoadError(ex); }
        finally { isOpeningDetail = false; }
    }

    private static WarehouseMovementDetail? LoadInboundDetail(WarehouseMovementRecord row, bool includePurchase) =>
        row.IsAwaitingReceipt ? WarehousingService.LoadAwaitingReceipt(row.ReferenceId)
            : WarehousingService.LoadDetail(row.Id, includePurchase);

    private void ShowLoadError(Exception ex)
    {
        F_OrderNotice.Show(Window.GetWindow(this), "入庫情報を表示できません",
            ex is UnauthorizedAccessException ? ex.Message : "入庫情報を取得できませんでした。接続を確認してください。",
            OrderNoticeKind.Warning);
    }

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null)
        {
            if (element is T match) return match;
            element = element is Visual or Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return null;
    }
}
