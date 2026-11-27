using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SalesManagement_SysDev;

public partial class F_OrderHistoryDetail : Window
{
    public F_OrderHistoryDetail(OrderOperationHistory entry, Window owner, Action openOrder)
    {
        InitializeComponent();
        Owner = owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = AppTypography.FontFamily;
        MaxWidth = Math.Max(320, SystemParameters.WorkArea.Width * 0.92);
        Width = Math.Min(760, MaxWidth);
        MinWidth = SystemParameters.WorkArea.Width >= 660 ? 600 : Math.Min(600, MaxWidth);
        MaxHeight = Math.Max(220, SystemParameters.WorkArea.Height * 0.85);
        DataContext = new OrderHistoryDetailViewModel(entry, Close, () =>
        {
            Close();
            openOrder();
        });
    }

    private void HeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source != null)
        {
            if (source is ButtonBase) return;
            source = source is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(source);
        }
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
