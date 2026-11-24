using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;

namespace SalesManagement_SysDev;

public partial class EmployeeManagementView : UserControl
{
    private EmployeeManagementViewModel ViewModel => (EmployeeManagementViewModel)DataContext;
    private bool openingDetail;
    public EmployeeManagementView() : this(new EmployeeManagementViewModel()) { }

    internal EmployeeManagementView(EmployeeManagementViewModel viewModel)
    {
        InitializeComponent(); DataContext = viewModel; UpdateTabs(viewModel.ActiveTab);
    }

    private void AllTab_Click(object sender, RoutedEventArgs e) => SetTab(EmployeeStatusTab.All);
    private void VisibleTab_Click(object sender, RoutedEventArgs e) => SetTab(EmployeeStatusTab.Visible);
    private void HiddenTab_Click(object sender, RoutedEventArgs e) => SetTab(EmployeeStatusTab.Hidden);
    private void SetTab(EmployeeStatusTab tab) { ViewModel.SelectTab(tab); UpdateTabs(tab); }
    private void UpdateTabs(EmployeeStatusTab active)
    {
        foreach (var (button, tab) in new[] { (AllTab, EmployeeStatusTab.All), (VisibleTab, EmployeeStatusTab.Visible), (HiddenTab, EmployeeStatusTab.Hidden) })
        {
            var selected = active == tab;
            button.Tag = selected ? "StatusActive" : null;
        }
    }
    private void ToggleFilters_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleFilters();
    private void StatusToolbar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (EmployeeTools == null) return;
        var stack = e.NewSize.Width < 590;
        Grid.SetRow(EmployeeTools, stack ? 1 : 0);
        Grid.SetColumn(EmployeeTools, stack ? 0 : 1);
        EmployeeTools.Margin = stack ? new Thickness(0, 6, 0, 0) : new Thickness(8, 0, 0, 0);
    }
    private void ClearConditions_Click(object sender, RoutedEventArgs e) => ViewModel.ClearConditions();
    private void ClearQuickSearch_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.QuickSearch = "";
        QuickSearchBox.Focus();
    }
    private void HireDate_ValidationError(object sender, DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        ViewModel.ReportInvalidDate();
    }
    private async void Reload_Click(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync();
    private void PreviousPage_Click(object sender, RoutedEventArgs e) => ViewModel.PageNumber--;
    private void NextPage_Click(object sender, RoutedEventArgs e) => ViewModel.PageNumber++;
    private void EmployeeGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        var direction = e.Column.SortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        foreach (var column in EmployeeGrid.Columns) column.SortDirection = null;
        e.Column.SortDirection = direction;
        ViewModel.SortBy(e.Column.SortMemberPath, direction);
    }
    private void ClearSelection_Click(object sender, RoutedEventArgs e) => ViewModel.ClearSelection();
    internal void OpenRegistration()
    {
        if (!ViewModel.CanCreate) return;
        var dialog = new EmployeeEditorDialog(ViewModel, null, Window.GetWindow(this));
        if (dialog.ShowDialog() == true)
            F_OrderNotice.Show(Window.GetWindow(this), "社員登録完了", dialog.ResultMessage, OrderNoticeKind.Success);
    }
    private void Roles_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanViewRoles)
            new EmployeeRolesDialog(ViewModel.Roles, Window.GetWindow(this)).ShowDialog();
    }
    private void EmployeeHistory_Click(object sender, RoutedEventArgs e) => OpenHistory();
    private void OpenHistory(int? employeeId = null)
    {
        if (Window.GetWindow(this) is F_Main shell)
            shell.ShowEmployeeHistory(employeeId);
    }
    private void OpenDetail_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EmployeeRow row) _ = OpenDetailAsync(row);
    }
    private void EmployeeGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source) return;
        for (var element = source; element != null && element != EmployeeGrid;
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element))
        {
            if (element is ButtonBase) return;
            if (element is DataGridRow { Item: EmployeeRow row })
            {
                e.Handled = true;
                _ = OpenDetailAsync(row);
                return;
            }
        }
    }
    private async Task OpenDetailAsync(EmployeeRow row)
    {
        if (openingDetail || !ViewModel.CanReload) return;
        openingDetail = true;
        try
        {
            EmployeeDetail? detail;
            var owner = Window.GetWindow(this);
            try { detail = await Task.Run(() => EmployeeService.GetDetail(row.Data.Id)); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Employee detail load failed: {ex}");
                if (IsLoaded)
                    F_OrderNotice.Show(owner, "社員詳細",
                        ex is UnauthorizedAccessException ? ex.Message : "社員情報を取得できませんでした。接続状態を確認してください。", OrderNoticeKind.Warning);
                return;
            }
            if (!IsLoaded) return;
            if (detail == null) { F_OrderNotice.Show(owner, "社員詳細", "社員情報を取得できませんでした。", OrderNoticeKind.Warning); return; }
            var dialog = new EmployeeDetailDialog(detail, EmployeeService.CanManage, owner);
            dialog.ShowDialog();
            switch (dialog.NextAction)
            {
                case EmployeeDetailAction.Edit: OpenEditor(detail.Employee); break;
                case EmployeeDetailAction.Disable: await HideRowAsync(new EmployeeRow(detail.Employee)); break;
                case EmployeeDetailAction.History: OpenHistory(detail.Employee.Id); break;
            }
        }
        finally { openingDetail = false; }
    }
    private void OpenEditor(EmployeeRecord employee)
    {
        if (!ViewModel.CanEdit || employee.ManagementFlag != EmployeeService.ActiveFlag) return;
        var dialog = new EmployeeEditorDialog(ViewModel, employee, Window.GetWindow(this));
        if (dialog.ShowDialog() == true)
            F_OrderNotice.Show(Window.GetWindow(this), "社員更新完了", dialog.ResultMessage, OrderNoticeKind.Success);
    }
    private async void HideSelected_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanHide) return;
        var row = ViewModel.Rows.SingleOrDefault(x => x.IsSelected && x.IsSelectable);
        if (row != null) await HideRowAsync(row);
    }
    private async Task HideRowAsync(EmployeeRow row)
    {
        if (!ViewModel.CanEdit || !row.IsSelectable) return;
        var dialog = new EmployeeHideDialog(row.Data, Window.GetWindow(this));
        if (dialog.ShowDialog() != true) return;
        var result = await ViewModel.HideAsync(row, dialog.Reason);
        F_OrderNotice.Show(Window.GetWindow(this), result.Succeeded ? "アカウント無効化完了" : "アカウント無効化の結果", result.Message,
            result.Succeeded ? OrderNoticeKind.Success : OrderNoticeKind.Warning);
    }
}
