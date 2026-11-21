using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace SalesManagement_SysDev;

public partial class F_OrderRegistration : UserControl, IDisposable
{
    private readonly OrderRegistrationViewModel viewModel;
    private readonly Dictionary<DatePicker, string> rejectedDates = [];
    private bool hasLoaded;
    private bool isDisposed;

    public F_OrderRegistration() : this(() => { }, () => OrderDraftNavigationChoice.ContinueEditing)
    {
    }

    public F_OrderRegistration(Action returnToList, Func<OrderDraftNavigationChoice> confirmNavigation,
        Action<OrderDetailSnapshot>? showDetails = null, Action? startNextOrder = null, OrderDraftSnapshot? draft = null)
    {
        InitializeComponent();
        viewModel = new OrderRegistrationViewModel(returnToList, confirmNavigation, showDetails: showDetails,
            startNextOrder: startNextOrder, draft: draft);
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

    public Task<bool> TryApproveNavigationAsync() => viewModel.TryApproveCloseAsync();

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
        if (viewModel.IsDraft)
        {
            RestoreDateText(OrderDateInput, viewModel.OrderDateInputText);
            RestoreDateText(DeliveryDateInput, viewModel.DeliveryDateInputText);
        }
    }

    private void DateValidationError(object sender, DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        if (DataContext is not OrderRegistrationViewModel model || sender is not DatePicker picker) return;
        model.SetDateInputText(picker == DeliveryDateInput, e.Text);
        rejectedDates[picker] = e.Text;
        // DatePicker restores its last valid text after rejecting an edit. Keep the unfinished text in the form.
        Dispatcher.InvokeAsync(() =>
        {
            if (rejectedDates.Remove(picker, out var text) && !isDisposed) RestoreDateText(picker, text);
        }, DispatcherPriority.Input);
    }
    private void DateInputChanged(object sender, SelectionChangedEventArgs e)
    {
        var picker = (DatePicker)sender;
        if (DataContext is OrderRegistrationViewModel model && !rejectedDates.ContainsKey(picker))
            model.SetDateInputText(picker == DeliveryDateInput, picker.SelectedDate?.ToString("d") ?? "");
    }
    private void DateTextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is OrderRegistrationViewModel model && sender is DatePicker picker
            && !rejectedDates.ContainsKey(picker) && e.OriginalSource is TextBox input)
            model.SetDateInputText(sender == DeliveryDateInput, input.Text);
    }

    private static void RestoreDateText(DatePicker picker, string text)
    {
        picker.ApplyTemplate();
        if (picker.Template.FindName("PART_TextBox", picker) is DatePickerTextBox input)
            input.SetCurrentValue(TextBox.TextProperty, text);
    }
}
