using System.Windows;

namespace SalesManagement_SysDev;

public partial class F_ShipmentHide : Window
{
    private readonly ShipmentHideDialogViewModel viewModel;
    public string Reason => viewModel.Reason;
    public F_ShipmentHide(ShipmentRecord shipment)
    {
        InitializeComponent();
        viewModel = new ShipmentHideDialogViewModel(shipment);
        DataContext = viewModel;
    }
    private void Confirm_Click(object sender, RoutedEventArgs e) { if (viewModel.CanConfirm && ShipmentService.CanHide) DialogResult = true; }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
