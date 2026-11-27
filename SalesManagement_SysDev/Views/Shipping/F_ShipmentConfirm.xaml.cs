using System.Windows;

namespace SalesManagement_SysDev;

public partial class F_ShipmentConfirm : Window
{
    private readonly ShipmentConfirmDialogViewModel viewModel;
    public F_ShipmentConfirm(ShipmentRecord shipment)
    {
        InitializeComponent();
        viewModel = new ShipmentConfirmDialogViewModel(shipment);
        DataContext = viewModel;
        Closing += (_, e) => e.Cancel = viewModel.IsProcessing;
    }
    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.CanConfirm || !ShipmentService.CanConfirm) return;
        viewModel.IsProcessing = true;
        try
        {
            var result = await Task.Run(() => ShipmentService.Confirm(AppSession.Employee?.EmId ?? 0, viewModel.Shipment.Id));
            if (result.Succeeded)
            {
                viewModel.IsProcessing = false;
                F_OrderNotice.Show(this, "出荷を確定しました", result.Message, OrderNoticeKind.Success);
                DialogResult = true;
            }
            else F_OrderNotice.Show(this, "出荷を確定できませんでした", result.Message, OrderNoticeKind.Error);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Shipment confirmation failed: {ex}");
            F_OrderNotice.Show(this, "出荷を確定できませんでした", "出荷の確定を保存できませんでした。データベースの状態を確認してください。", OrderNoticeKind.Error);
        }
        finally { viewModel.IsProcessing = false; }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { if (!viewModel.IsProcessing) DialogResult = false; }
}
