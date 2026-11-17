using System.Windows;

namespace SalesManagement_SysDev;

public partial class F_ShipmentDetail : Window
{
    private readonly ShipmentDetailViewModel viewModel;
    private readonly bool readOnly;
    public bool WasChanged { get; private set; }

    public F_ShipmentDetail(ShipmentDetail detail, bool readOnly = false)
    {
        InitializeComponent();
        this.readOnly = readOnly;
        viewModel = new ShipmentDetailViewModel(detail);
        DataContext = viewModel;
        if (readOnly) { ConfirmButton.Visibility = Visibility.Collapsed; HideButton.Visibility = Visibility.Collapsed; }
        MaxHeight = Math.Max(480, SystemParameters.WorkArea.Height * .9);
        Height = Math.Min(700, MaxHeight);
        MaxWidth = Math.Max(700, SystemParameters.WorkArea.Width * .92);
        Width = Math.Min(940, MaxWidth);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = WasChanged;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (readOnly || !viewModel.CanConfirm || !ShipmentService.CanConfirm) return;
        var confirmation = new F_ShipmentConfirm(viewModel.Shipment) { Owner = this };
        if (confirmation.ShowDialog() != true) return;
        WasChanged = true;
        DialogResult = true;
    }

    private async void Hide_Click(object sender, RoutedEventArgs e)
    {
        if (readOnly || !viewModel.CanHide || !ShipmentService.CanHide) return;
        var confirmation = new F_ShipmentHide(viewModel.Shipment) { Owner = this };
        if (confirmation.ShowDialog() != true) return;
        ShipmentOperationResult result;
        try { result = await Task.Run(() => ShipmentService.Hide(AppSession.Employee?.EmId ?? 0, viewModel.Shipment.Id, confirmation.Reason)); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Shipment hide failed: {ex}");
            result = new(false, "出荷を非表示にできませんでした。一覧を再読込してからお試しください。");
        }
        F_OrderNotice.Show(this, result.Succeeded ? "出荷を非表示にしました" : "出荷を非表示にできませんでした",
            result.Message, result.Succeeded ? OrderNoticeKind.Success : OrderNoticeKind.Error);
        if (!result.Succeeded) return;
        WasChanged = true;
        DialogResult = true;
    }
}
