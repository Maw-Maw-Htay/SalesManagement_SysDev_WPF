using System.Windows;
using System.Windows.Input;

namespace SalesManagement_SysDev;

public partial class F_HattyuHide : Window
{
    private readonly PurchaseOrderHideDialogViewModel viewModel;
    public string Reason => viewModel.Reason;

    public F_HattyuHide(PurchaseOrderRecord order)
    {
        InitializeComponent();
        viewModel = new PurchaseOrderHideDialogViewModel(order, () => DialogResult = true);
        DataContext = viewModel;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.CanConfirm) return;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        DialogResult = false;
    }
}
