using System.Windows;
using System.Windows.Controls;

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

    private void ReceiptDate_ValidationError(object sender, DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        viewModel.ReceiptDate = null;
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.CanConfirm || AppSession.Employee is not { } employee) return;
        var record = viewModel.Record;
        var receiptDate = viewModel.ReceiptDate;
        var lines = record.IsAwaitingReceipt ? viewModel.ActualReceiptLines : viewModel.ExpectedLines;
        viewModel.IsProcessing = true;
        WarehouseMovementOperationResult result;
        try
        {
            result = await Task.Run(() => record.IsAwaitingReceipt
                ? WarehousingService.ConfirmPurchaseReceipt(employee.EmId, record.ReferenceId, receiptDate, lines)
                : record.Kind == WarehouseMovementKind.Inbound
                    ? WarehousingService.ConfirmShelf(employee.EmId, record.Id, lines)
                    : OutboundService.Confirm(employee.EmId, record.Id));
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
