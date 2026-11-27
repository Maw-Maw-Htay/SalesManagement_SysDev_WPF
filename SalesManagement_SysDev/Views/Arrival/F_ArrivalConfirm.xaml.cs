using System.Windows;

namespace SalesManagement_SysDev;

public partial class F_ArrivalConfirm : Window
{
    private readonly ArrivalConfirmDialogViewModel viewModel;

    public F_ArrivalConfirm(ArrivalRecord arrival)
    {
        InitializeComponent();
        viewModel = new ArrivalConfirmDialogViewModel(arrival);
        DataContext = viewModel;
        Closing += (_, e) => e.Cancel = viewModel.IsProcessing;
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.CanConfirm || !ArrivalService.CanConfirm) return;
        viewModel.IsProcessing = true;
        try
        {
            var employeeId = AppSession.Employee?.EmId ?? 0;
            var result = await Task.Run(() => ArrivalService.Confirm(employeeId, viewModel.Arrival.Id));
            if (result.Succeeded)
            {
                viewModel.IsProcessing = false;
                F_OrderNotice.Show(this, "入荷を確定しました", result.Message, OrderNoticeKind.Success);
                DialogResult = true;
            }
            else
            {
                F_OrderNotice.Show(this, "入荷を確定できませんでした", result.Message, OrderNoticeKind.Error);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Arrival confirmation failed: {ex}");
            F_OrderNotice.Show(this, "入荷を確定できませんでした",
                "入荷と出荷情報を保存できませんでした。データベースの状態を確認してください。", OrderNoticeKind.Error);
        }
        finally { viewModel.IsProcessing = false; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.IsProcessing) DialogResult = false;
    }
}
