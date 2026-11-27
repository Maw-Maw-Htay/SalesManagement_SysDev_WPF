using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SalesManagement_SysDev;

public partial class F_OrderHide : Window
{
    private readonly OrderHideViewModel viewModel;

    public F_OrderHide(IReadOnlyCollection<int> orderIds, IOrderHideDataSource? source = null)
        : this(orderIds, restore: false, source)
    {
    }

    internal F_OrderHide(IReadOnlyCollection<int> orderIds, bool restore, IOrderHideDataSource? source = null)
    {
        InitializeComponent();
        viewModel = new OrderHideViewModel(orderIds, () => DialogResult = viewModel!.HasChanges, restore, source);
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, e) =>
        {
            FrameworkElement? feedback = e.PropertyName switch
            {
                nameof(OrderHideViewModel.ReasonError) when viewModel.ReasonError.Length > 0 => ReasonValidation,
                nameof(OrderHideViewModel.Feedback) when viewModel.Feedback.Length > 0 => ResultFeedback,
                _ => null
            };
            if (feedback != null && IsLoaded)
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => feedback.BringIntoView()));
        };
        _ = new OrderHideWindowPlacement(this);
        Closing += (_, e) => e.Cancel = !viewModel.CanClose;
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            ReasonInput.Focus();
            Keyboard.Focus(ReasonInput);
        }));
    }

    public IReadOnlyCollection<int> SuccessfulOrderIds => viewModel.SuccessfulOrderIds;
    public bool HasAttempted => viewModel.HasAttempted;

    private void HeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element != null)
        {
            if (element is ButtonBase) return;
            element = element is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(element);
        }
        if (viewModel.CanClose && e.ClickCount == 1 && e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
