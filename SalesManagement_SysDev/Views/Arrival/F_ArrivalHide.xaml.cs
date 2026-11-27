using System.Windows;

namespace SalesManagement_SysDev;

public partial class F_ArrivalHide : Window
{
    private readonly ArrivalHideDialogViewModel viewModel;
    public string Reason => viewModel.Reason;

    public F_ArrivalHide(ArrivalRecord arrival)
    {
        InitializeComponent();
        viewModel = new ArrivalHideDialogViewModel(arrival);
        DataContext = viewModel;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.CanConfirm && ArrivalService.CanHide) DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
