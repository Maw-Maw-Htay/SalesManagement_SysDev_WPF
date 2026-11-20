using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace SalesManagement_SysDev;

public partial class ArrivalManagementView : UserControl
{
    private void BusinessHistory_Click(object sender, RoutedEventArgs e) => BusinessHistoryWindow.OpenFor(this, "入荷管理");

    private readonly ArrivalManagementViewModel viewModel;
    private bool isOpeningDetail;

    public ArrivalManagementView()
    {
        InitializeComponent();
        viewModel = new ArrivalManagementViewModel();
        viewModel.IsFiltersExpanded = false;
        DataContext = viewModel;
        UpdateTabs(viewModel.ActiveTab);
        Unloaded += (_, _) => viewModel.Dispose();
    }

    private async void Reload_Click(object sender, RoutedEventArgs e) => await viewModel.LoadAsync();
    private void ToggleFilters_Click(object sender, RoutedEventArgs e) => viewModel.ToggleFilters();
    private void ClearConditions_Click(object sender, RoutedEventArgs e) => viewModel.ClearConditions();
    private void ClearQuickSearch_Click(object sender, RoutedEventArgs e) { viewModel.Keyword = ""; QuickSearch.Focus(); }
    private void AllTab_Click(object sender, RoutedEventArgs e) => SelectTab(ArrivalManagementTab.All);
    private void VisibleTab_Click(object sender, RoutedEventArgs e) => SelectTab(ArrivalManagementTab.Visible);
    private void HiddenTab_Click(object sender, RoutedEventArgs e) => SelectTab(ArrivalManagementTab.Hidden);
    private void SelectTab(ArrivalManagementTab tab) { viewModel.SelectTab(tab); UpdateTabs(tab); }
    private void UpdateTabs(ArrivalManagementTab active)
    {
        AllTab.IsChecked = active == ArrivalManagementTab.All;
        VisibleTab.IsChecked = active == ArrivalManagementTab.Visible;
        HiddenTab.IsChecked = active == ArrivalManagementTab.Hidden;
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
        if (isOpeningDetail || sender is not Button { Tag: ArrivalRow row }) return;
        await OpenDetailAsync(row.Data.Id);
    }

    private async void ArrivalsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (FindAncestor<ButtonBase>(source) != null) { e.Handled = true; return; }
        var row = ItemsControl.ContainerFromElement(ArrivalsGrid, source) as DataGridRow;
        if (row?.Item is not ArrivalRow arrival) return;
        e.Handled = true;
        await OpenDetailAsync(arrival.Data.Id);
    }

    private async Task OpenDetailAsync(int arrivalId)
    {
        if (isOpeningDetail) return;
        isOpeningDetail = true;
        try
        {
            var detail = await Task.Run(() => ArrivalService.LoadDetail(arrivalId));
            if (detail == null)
            {
                F_OrderNotice.Show(Window.GetWindow(this), "入荷詳細を表示できません",
                    "入荷が見つからないか、表示する権限がありません。", OrderNoticeKind.Warning);
                return;
            }
            var dialog = new F_ArrivalDetail(detail) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true) await viewModel.LoadAsync();
        }
        catch (UnauthorizedAccessException ex)
        {
            F_OrderNotice.Show(Window.GetWindow(this), "入荷詳細を表示できません", ex.Message, OrderNoticeKind.Warning);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Arrival detail load failed: {ex}");
            F_OrderNotice.Show(Window.GetWindow(this), "入荷詳細を表示できません",
                "入荷詳細を読み込めませんでした。データベースの接続状態を確認してください。", OrderNoticeKind.Error);
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
