using System.Windows;

namespace SalesManagement_SysDev;

public partial class F_WarehouseMovementHide : Window
{
    private readonly WarehouseMovementHideViewModel viewModel;

    public F_WarehouseMovementHide(WarehouseMovementRecord record, IReadOnlyList<WarehouseMovementLine> lines)
    {
        InitializeComponent();
        viewModel = new WarehouseMovementHideViewModel(record, lines);
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
        var reason = viewModel.Reason;
        viewModel.IsProcessing = true;
        WarehouseMovementOperationResult result;
        try
        {
            result = await Task.Run(() => viewModel.Record.Kind == WarehouseMovementKind.Inbound
                ? WarehousingService.Hide(employee.EmId, viewModel.Record.Id, reason)
                : OutboundService.Hide(employee.EmId, viewModel.Record.Id, reason));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Warehouse movement hide failed: {ex}");
            result = new(false, "非表示にできませんでした。データベースの状態を確認してください。");
        }
        finally
        {
            viewModel.IsProcessing = false;
        }

        if (!result.Succeeded)
        {
            F_OrderNotice.Show(this, "非表示にできません", result.Message, OrderNoticeKind.Warning);
            return;
        }
        F_OrderNotice.Show(this, "非表示にしました", result.Message, OrderNoticeKind.Success);
        DialogResult = true;
    }

}
