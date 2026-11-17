using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace SalesManagement_SysDev;

public partial class SalesManagementView : UserControl
{
    private void BusinessHistory_Click(object sender, RoutedEventArgs e) => BusinessHistoryWindow.OpenFor(this, "売上管理");

    private SalesManagementViewModel ViewModel => (SalesManagementViewModel)DataContext;

    public SalesManagementView()
    {
        InitializeComponent();
        DataContext = new SalesManagementViewModel();
        ViewModel.IsFiltersExpanded = false;
        SalesGrid.Columns[0].Visibility = ViewModel.CanHideSales ? Visibility.Visible : Visibility.Collapsed;
        UpdateTabs(SalesStatusTab.Visible);
        Unloaded += (_, _) => ViewModel.Dispose();
    }

    private void AllTab_Click(object sender, RoutedEventArgs e) => SetTab(SalesStatusTab.All);
    private void VisibleTab_Click(object sender, RoutedEventArgs e) => SetTab(SalesStatusTab.Visible);
    private void HiddenTab_Click(object sender, RoutedEventArgs e) => SetTab(SalesStatusTab.Hidden);
    private void SetTab(SalesStatusTab tab) { ViewModel.SelectTab(tab); UpdateTabs(tab); }

    private void UpdateTabs(SalesStatusTab active)
    {
        AllTab.IsChecked = active == SalesStatusTab.All;
        VisibleTab.IsChecked = active == SalesStatusTab.Visible;
        HiddenTab.IsChecked = active == SalesStatusTab.Hidden;
    }

    private void ClearQuickSearch_Click(object sender, RoutedEventArgs e) { ViewModel.Keyword = ""; QuickSearch.Focus(); }
    private void ToggleFilters_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleFilters();
    private void ClearConditions_Click(object sender, RoutedEventArgs e) => ViewModel.ClearConditions();
    private async void Reload_Click(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync();
    private void ClearSelection_Click(object sender, RoutedEventArgs e) => ViewModel.ClearSelection();

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

    private void ListHeader_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wrapActions = e.NewSize.Width < 760;
        Grid.SetRow(BulkActions, wrapActions ? 1 : 0);
        Grid.SetColumn(BulkActions, wrapActions ? 0 : 1);
        Grid.SetColumnSpan(BulkActions, wrapActions ? 2 : 1);
        Grid.SetRow(ListHint, wrapActions ? 2 : 1);
        BulkActions.Margin = new Thickness(0, wrapActions ? 8 : 0, 0, 0);
        ListHint.Margin = new Thickness(0, wrapActions ? 4 : 6, 0, 0);
    }

    private void OpenDetail_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SalesRow row) OpenDetail(row);
    }

    private void SalesGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideActionControl(e.OriginalSource as DependencyObject)) return;
        if (SalesGrid.SelectedItem is SalesRow row) OpenDetail(row);
    }

    private async void OpenDetail(SalesRow row)
    {
        SalesDetail? detail;
        try { detail = await Task.Run(() => SalesService.GetDetail(row.Data.Id)); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Sales detail load failed: {ex}");
            F_OrderNotice.Show(Window.GetWindow(this), "売上詳細", "売上情報を取得できませんでした。データベースの接続状態を確認してください。", OrderNoticeKind.Warning);
            return;
        }
        if (detail == null)
        {
            F_OrderNotice.Show(Window.GetWindow(this), "売上詳細", "売上が存在しないか、参照できる営業所の範囲外です。", OrderNoticeKind.Warning);
            return;
        }

        var dialog = new F_SaleDetail(detail, ViewModel.CanHideSales, Window.GetWindow(this));
        dialog.ShowDialog();
        if (dialog.HideRequested) await HideRowAsync(row);
    }

    private async void HideSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedRow is { } row) await HideRowAsync(row);
    }

    private async Task HideRowAsync(SalesRow row)
    {
        if (!ViewModel.CanHideSales || row.Data.ManagementFlag != SalesService.ActiveFlag) return;
        ViewModel.SelectOnly(row);
        var dialog = new F_SaleHide(row.Data, Window.GetWindow(this));
        if (dialog.ShowDialog() != true) return;
        var result = await ViewModel.HideAsync(row, dialog.Reason);
        F_OrderNotice.Show(Window.GetWindow(this), result.Succeeded ? "売上非表示完了" : "売上非表示の結果",
            result.Message, result.Succeeded ? OrderNoticeKind.Success : OrderNoticeKind.Warning);
    }

    private static bool IsInsideActionControl(DependencyObject? node)
    {
        while (node != null)
        {
            if (node is ButtonBase) return true;
            node = node switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(node),
                ContentElement content => ContentOperations.GetParent(content) ?? LogicalTreeHelper.GetParent(content),
                _ => LogicalTreeHelper.GetParent(node)
            };
        }
        return false;
    }
}
