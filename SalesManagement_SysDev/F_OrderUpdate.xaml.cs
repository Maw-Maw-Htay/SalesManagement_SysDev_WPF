using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace SalesManagement_SysDev;
public partial class F_OrderUpdate : Window, IDisposable
{
    private readonly OrderUpdateViewModel viewModel;
    private readonly Action? returnToList;
    private readonly Action<OrderDetailSnapshot>? showDetails;
    private readonly DispatcherTimer inventoryRefresh;
    private bool embedded, disposed;
    public F_OrderUpdate(OrderDetailSnapshot data, Action? returnToList=null, Action<OrderDetailSnapshot>? showDetails=null,
        IOrderUpdateDataSource? updateSource=null,IOrderRegistrationDataSource? lookupSource=null)
    {
        InitializeComponent();this.returnToList=returnToList;this.showDetails=showDetails;
        viewModel=new OrderUpdateViewModel(data,saved=>{if(saved) ShowCompletion();else RequestClose();},updateSource,lookupSource,
            confirmCustomerChange:_=>F_OrderNotice.Confirm(HostWindow,"顧客を変更しますか？",
                "変更すると、この受注の発注元顧客が更新されます。","変更する","キャンセル"));
        DataContext=viewModel;
        _=new RegistrationWindowPlacement(this,preferredWidth:1150,preferredHeight:760,minimumWidth:900,minimumHeight:580);
        Closing+=(_,e)=>{if(!disposed) e.Cancel=!viewModel.TryApproveClose(ConfirmDiscard);};
        Closed+=(_,_)=>Dispose();
        inventoryRefresh=new DispatcherTimer { Interval=TimeSpan.FromSeconds(15) };
        inventoryRefresh.Tick+=async (_,_)=>
        {
            if(RootLayout.IsVisible && HostWindow.IsActive) await viewModel.RefreshInventoryAsync();
        };
        RootLayout.Loaded+=(_,_)=>
        {
            if(disposed) return;
            viewModel.Load();
            inventoryRefresh.Start();
        };
        RootLayout.Unloaded+=(_,_)=>inventoryRefresh.Stop();
        OrderDateInput.AddHandler(TextBoxBase.TextChangedEvent,new TextChangedEventHandler(DateTextChanged));
        DeliveryDateInput.AddHandler(TextBoxBase.TextChangedEvent,new TextChangedEventHandler(DateTextChanged));
        viewModel.Entry.PropertyChanged+=(_,args)=>
        {
            if(args.PropertyName!=nameof(OrderRegistrationViewModel.IsProductDetailsVisible)) return;
            Dispatcher.BeginInvoke(new Action(()=>
            {
                if(disposed || !RootLayout.IsLoaded) return;
                if(viewModel.Entry.IsProductDetailsVisible) ProductDetailsOverlay.FocusCloseButton(); else ProductDetailsButton.Focus();
            }));
        };
    }
    private Window HostWindow=>Window.GetWindow(RootLayout)??Owner??this;
    public bool HasSaved=>viewModel.HasSaved;
    public UIElement DetachView()
    {
        embedded=true;Header.Visibility=Visibility.Collapsed;
        var root=RootLayout;Content=null;root.DataContext=viewModel;root.Resources=Resources;
        var scope=new NameScope();scope.RegisterName("RegistrationPanels",RegistrationPanels);NameScope.SetNameScope(root,scope);
        root.InputBindings.Add(new KeyBinding(viewModel.EscapeCommand,new KeyGesture(Key.Escape)));
        return root;
    }
    public bool TryApproveNavigation()=>viewModel.TryApproveClose(ConfirmDiscard);
    private bool ConfirmDiscard()=>F_OrderNotice.Confirm(HostWindow,"未保存の変更",
        "変更内容が保存されていません。画面を閉じてもよろしいですか？","破棄して戻る","編集を続ける",destructive:true);
    private void RequestClose()
    {
        if(embedded) {if(TryApproveNavigation()) returnToList?.Invoke();} else Close();
    }
    private void ShowCompletion()
    {
        var notice=new F_OrderNotice("受注更新完了",$"受注を更新しました。\n受注番号：{viewModel.Detail.OrderNumber}",OrderNoticeKind.Success,
            "受注詳細を表示","一覧に戻る",showCancel:true) {Owner=HostWindow};
        var result=notice.ShowDialog();
        if(result==true)
        {
            try
            {
                var saved=viewModel.LoadSavedOrder();
                if(saved==null) throw new InvalidOperationException();
                if(showDetails!=null) showDetails(saved);else new F_OrderDetail(saved,HostWindow).ShowDialog();
            }
            catch {F_OrderNotice.Show(HostWindow,"受注詳細","更新は保存されていますが、受注詳細を取得できませんでした。",OrderNoticeKind.Error);}
        }
        else if(((OrderNoticeViewModel)notice.DataContext).CancelWasClicked) RequestClose();
        else CancelButton.Focus();
    }
    private void DateValidationError(object sender,DatePickerDateValidationErrorEventArgs e)
    {e.ThrowException=false;if(RootLayout.DataContext is OrderUpdateViewModel model) model.SetDateInputText(sender==DeliveryDateInput,e.Text);}
    private void DateInputChanged(object sender,SelectionChangedEventArgs e)
    {
        var picker=(DatePicker)sender;
        if(RootLayout.DataContext is OrderUpdateViewModel model) model.SetDateInputText(picker==DeliveryDateInput,picker.SelectedDate?.ToString("d")??"");
    }
    private void DateTextChanged(object sender,TextChangedEventArgs e)
    {if(RootLayout.DataContext is OrderUpdateViewModel model && e.OriginalSource is TextBox input) model.SetDateInputText(sender==DeliveryDateInput,input.Text);}
    private void HeaderMouseDown(object sender,MouseButtonEventArgs e)
    {
        var source=e.OriginalSource as DependencyObject;
        while(source!=null)
        {
            if(source is ButtonBase) return;
            source=source is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(source);
        }
        if(e.ClickCount==1 && e.ButtonState==MouseButtonState.Pressed) DragMove();
    }
    public void Dispose()
    {
        if(disposed) return;disposed=true;inventoryRefresh.Stop();viewModel.Dispose();
        if(embedded) Close();
    }
}
