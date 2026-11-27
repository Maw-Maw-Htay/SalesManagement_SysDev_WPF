using System.Windows.Controls;

namespace SalesManagement_SysDev;

public partial class OrderProductInput : UserControl
{
    public OrderProductInput() => InitializeComponent();

    internal void FocusProductDetails() => ProductDetailsButton.Focus();
}
