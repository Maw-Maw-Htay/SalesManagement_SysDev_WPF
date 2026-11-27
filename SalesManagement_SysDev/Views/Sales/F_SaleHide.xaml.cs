using System.Windows;

namespace SalesManagement_SysDev;

public partial class F_SaleHide : Window
{
    private readonly SalesHideDialogViewModel viewModel;
    public string Reason => viewModel.Reason;

    public F_SaleHide(SalesRecord sale, Window? owner)
    {
        InitializeComponent();
        Owner = owner;
        Title = $"売上を非表示にする — {sale.Code}";
        WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        var workWidth = owner?.ActualWidth > 0 ? owner.ActualWidth : SystemParameters.WorkArea.Width;
        var workHeight = owner?.ActualHeight > 0 ? owner.ActualHeight : SystemParameters.WorkArea.Height;
        Width = Math.Min(540, workWidth * .9);
        MaxWidth = workWidth * .92;
        MaxHeight = workHeight * .86;
        SizeToContent = SizeToContent.Height;
        viewModel = new SalesHideDialogViewModel(sale, () => DialogResult = true);
        DataContext = viewModel;
    }
}
