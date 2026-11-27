using System.Windows;
using System.Windows.Controls;

namespace SalesManagement_SysDev;

public partial class OrderProductDetailsOverlay : UserControl
{
    public OrderProductDetailsOverlay()
    {
        InitializeComponent();
        LayoutUpdated += (_, _) => CenterInOwner();
    }

    public void FocusCloseButton() => CloseProductDetailsButton.Focus();

    private void CenterInOwner()
    {
        if (!IsVisible || Window.GetWindow(this)?.Content is not FrameworkElement ownerContent) return;

        var ownerCenter = ownerContent.TranslatePoint(new Point(ownerContent.ActualWidth / 2, ownerContent.ActualHeight / 2), DialogViewport);
        var viewportCenterX = DialogViewport.ActualWidth / 2;
        var viewportCenterY = DialogViewport.ActualHeight / 2;
        var availableX = Math.Max(0, viewportCenterX - ProductDetailsCard.ActualWidth / 2);
        var availableY = Math.Max(0, viewportCenterY - ProductDetailsCard.ActualHeight / 2);
        DialogOffset.X = Math.Clamp(ownerCenter.X - viewportCenterX, -availableX, availableX);
        DialogOffset.Y = Math.Clamp(ownerCenter.Y - viewportCenterY, -availableY, availableY);
    }
}
