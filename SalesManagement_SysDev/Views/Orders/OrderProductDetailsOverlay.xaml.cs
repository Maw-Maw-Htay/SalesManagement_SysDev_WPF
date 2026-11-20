using System.Windows.Controls;
namespace SalesManagement_SysDev;
public partial class OrderProductDetailsOverlay : UserControl
{
    public OrderProductDetailsOverlay()=>InitializeComponent();
    public void FocusCloseButton()=>CloseProductDetailsButton.Focus();
}
