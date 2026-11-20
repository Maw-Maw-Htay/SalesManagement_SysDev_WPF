using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SalesManagement_SysDev;

public partial class F_OrderRegistration : UserControl, IDisposable
{
    private readonly OrderRegistrationViewModel viewModel;
    private bool hasLoaded;
    private bool isDisposed;

    public F_OrderRegistration() : this(() => { }, () => false)
    {
    }

    public F_OrderRegistration(Action returnToList, Func<bool> confirmDiscard, Action<OrderDetailSnapshot>? showDetails = null,
        Action? startNextOrder = null)
    {
        InitializeComponent();
        viewModel = new OrderRegistrationViewModel(returnToList, confirmDiscard, showDetails: showDetails, startNextOrder: startNextOrder);
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(OrderRegistrationViewModel.IsProductDetailsVisible))
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (isDisposed || !IsLoaded) return;
                    if (viewModel.IsProductDetailsVisible) ProductDetailsOverlay.FocusCloseButton();
                    else ProductDetailsButton.Focus();
                }));
            else if (args.PropertyName == nameof(OrderRegistrationViewModel.IsCompletionDialogVisible))
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (isDisposed || !IsLoaded) return;
                    if (viewModel.IsCompletionDialogVisible) ContinueOrderRegistrationButton.Focus();
                    else if (viewModel.IsEditing)
                    {
                        InputScroll.ScrollToTop();
                        CustomerLookup.FocusInput();
                    }
                }));
        };
        OrderDateInput.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(DateTextChanged));
        DeliveryDateInput.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(DateTextChanged));
        Loaded += OnLoaded;
    }

    public bool TryApproveNavigation() => viewModel.TryApproveClose();

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        viewModel.Dispose();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (hasLoaded) return;
        hasLoaded = true;
        if (!AppSession.CanManageOrders)
        {
            F_OrderNotice.Show(Window.GetWindow(this)!, "権限エラー", "受注登録は管理者または営業担当者のみ利用できます。", OrderNoticeKind.Warning);
            return;
        }
        viewModel.Load();
    }

    private void DateValidationError(object sender, DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        if (DataContext is OrderRegistrationViewModel model) model.SetDateInputText(sender == DeliveryDateInput, e.Text);
    }
    private void DateInputChanged(object sender, SelectionChangedEventArgs e)
    {
        var picker = (DatePicker)sender;
        if (DataContext is OrderRegistrationViewModel model) model.SetDateInputText(picker == DeliveryDateInput, picker.SelectedDate?.ToString("d") ?? "");
    }
    private void DateTextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is OrderRegistrationViewModel model && e.OriginalSource is TextBox input)
            model.SetDateInputText(sender == DeliveryDateInput, input.Text);
    }
}
