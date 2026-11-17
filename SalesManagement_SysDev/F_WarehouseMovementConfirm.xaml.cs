using System.ComponentModel;
using System.Windows;

namespace SalesManagement_SysDev;

public partial class F_WarehouseMovementConfirm : Window
{
    private readonly WarehouseMovementConfirmViewModel viewModel;

    public F_WarehouseMovementConfirm(WarehouseMovementDetail detail)
    {
        InitializeComponent();
        viewModel = new WarehouseMovementConfirmViewModel(detail);
        DataContext = viewModel;
        Closing += (_, e) => { if (viewModel.IsProcessing) e.Cancel = true; };
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.IsProcessing) DialogResult = false;
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.CanConfirm || AppSession.Employee is not { } employee) return;
        viewModel.IsProcessing = true;
        WarehouseMovementOperationResult result;
        try
        {
            result = await Task.Run(() => viewModel.IsInbound
                ? WarehousingService.ConfirmShelf(employee.EmId, viewModel.Record.Id, viewModel.ExpectedLines)
                : OutboundService.Confirm(employee.EmId, viewModel.Record.Id));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Warehouse movement confirmation failed: {ex}");
            result = new(false, "処理に失敗しました。データベースの状態を確認してください。");
        }
        finally { viewModel.IsProcessing = false; }

        if (!result.Succeeded)
        {
            F_OrderNotice.Show(this, "確定できません", result.Message, OrderNoticeKind.Warning);
            return;
        }
        F_OrderNotice.Show(this, viewModel.IsInbound ? "入庫を確定しました" : "出庫を確定しました",
            result.Message, OrderNoticeKind.Success);
        DialogResult = true;
    }
}
