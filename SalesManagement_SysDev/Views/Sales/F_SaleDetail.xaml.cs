using System.Windows;

namespace SalesManagement_SysDev;

public partial class F_SaleDetail : Window
{
    public bool HideRequested { get; private set; }

    public F_SaleDetail(SalesDetail detail, bool canHide, Window? owner)
    {
        InitializeComponent();
        DataContext = new SalesDetailDialogViewModel(detail, canHide);
        Owner = owner;
        Title = detail.Sale.DetailHeader;
        WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        var workWidth = owner?.ActualWidth > 0 ? owner.ActualWidth : SystemParameters.WorkArea.Width;
        var workHeight = owner?.ActualHeight > 0 ? owner.ActualHeight : SystemParameters.WorkArea.Height;
        Width = Math.Min(980, workWidth * .94);
        MinWidth = Math.Min(700, Width);
        MaxWidth = workWidth * .96;
        MaxHeight = workHeight * .88;
        SizeToContent = SizeToContent.Height;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Hide_Click(object sender, RoutedEventArgs e) { HideRequested = true; DialogResult = true; }
}
