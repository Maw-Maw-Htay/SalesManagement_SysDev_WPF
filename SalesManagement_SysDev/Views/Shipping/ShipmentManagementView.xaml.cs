using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace SalesManagement_SysDev;

public partial class ShipmentManagementView : UserControl
{
    private void BusinessHistory_Click(object sender, RoutedEventArgs e) => BusinessHistoryWindow.OpenFor(this, "出荷管理");

    private readonly ShipmentManagementViewModel viewModel;
    private bool isOpeningDetail;

    public ShipmentManagementView()
    {
        InitializeComponent();
        viewModel = new ShipmentManagementViewModel();
        viewModel.IsFiltersExpanded = false;
        DataContext = viewModel;
        UpdateTabs(viewModel.ActiveTab);
        Unloaded += (_, _) => viewModel.Dispose();
    }

    private async void Reload_Click(object sender, RoutedEventArgs e) => await viewModel.LoadAsync();
    private void ToggleFilters_Click(object sender, RoutedEventArgs e) => viewModel.ToggleFilters();
    private void ClearConditions_Click(object sender, RoutedEventArgs e) => viewModel.ClearConditions();
    private void ClearQuickSearch_Click(object sender, RoutedEventArgs e) { viewModel.Keyword = ""; QuickSearch.Focus(); }
    private void AllTab_Click(object sender, RoutedEventArgs e) => SelectTab(ShipmentManagementTab.All);
    private void VisibleTab_Click(object sender, RoutedEventArgs e) => SelectTab(ShipmentManagementTab.Visible);
    private void HiddenTab_Click(object sender, RoutedEventArgs e) => SelectTab(ShipmentManagementTab.Hidden);
    private void SelectTab(ShipmentManagementTab tab) { viewModel.SelectTab(tab); UpdateTabs(tab); }
    private void UpdateTabs(ShipmentManagementTab active)
    {
        AllTab.IsChecked = active == ShipmentManagementTab.All;
        VisibleTab.IsChecked = active == ShipmentManagementTab.Visible;
        HiddenTab.IsChecked = active == ShipmentManagementTab.Hidden;
    }

    private void QuickSearchRow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wrapActions = e.NewSize.Width < 720;
        Grid.SetRow(SearchActions, wrapActions ? 1 : 0);
        Grid.SetColumn(SearchActions, wrapActions ? 0 : 1);
        Grid.SetColumnSpan(QuickSearchField, wrapActions ? 2 : 1);
        SearchActions.Margin = new Thickness(0, wrapActions ? 6 : 0, 0, 0);
    }

    private void StatusFiltersRow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wrapActions = e.NewSize.Width < 820;
        Grid.SetRow(StatusActions, wrapActions ? 1 : 0);
        Grid.SetColumn(StatusActions, wrapActions ? 0 : 1);
        Grid.SetColumnSpan(StatusActions, wrapActions ? 2 : 1);
        StatusActions.Margin = new Thickness(0, wrapActions ? 6 : 0, 0, 0);
    }

    private async void Detail_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (isOpeningDetail || sender is not Button { Tag: ShipmentRow row }) return;
        await OpenDetailAsync(row.Data.Id);
    }

    private async void ShipmentsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (FindAncestor<ButtonBase>(source) != null) { e.Handled = true; return; }
        var row = ItemsControl.ContainerFromElement(ShipmentsGrid, source) as DataGridRow;
        if (row?.Item is not ShipmentRow shipment) return;
        e.Handled = true;
        await OpenDetailAsync(shipment.Data.Id);
    }

    private async Task OpenDetailAsync(int shipmentId)
    {
        if (isOpeningDetail) return;
        isOpeningDetail = true;
        try
        {
            var detail = await Task.Run(() => ShipmentService.LoadDetail(shipmentId));
            if (detail == null)
            {
                F_OrderNotice.Show(Window.GetWindow(this), "出荷詳細を表示できません",
                    "出荷が見つからないか、表示する権限がありません。", OrderNoticeKind.Warning);
                return;
            }
            var dialog = new F_ShipmentDetail(detail) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true) await viewModel.LoadAsync();
        }
        catch (UnauthorizedAccessException ex)
        {
            F_OrderNotice.Show(Window.GetWindow(this), "出荷詳細を表示できません", ex.Message, OrderNoticeKind.Warning);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Shipment detail load failed: {ex}");
            F_OrderNotice.Show(Window.GetWindow(this), "出荷詳細を表示できません",
                "出荷詳細を読み込めませんでした。データベースの接続状態を確認してください。", OrderNoticeKind.Error);
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
