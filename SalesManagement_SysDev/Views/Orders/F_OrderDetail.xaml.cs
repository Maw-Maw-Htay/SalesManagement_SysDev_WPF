using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SalesManagement_SysDev;

public partial class F_OrderDetail : Window
{
    private readonly OrderDetailSnapshot data;
    private readonly OrderDetailViewModel viewModel;
    private readonly bool readOnly;
    private bool detailClosed, historyRequested;

    public F_OrderDetail(OrderDetailSnapshot data, Window owner) : this(data, owner, false) { }

    internal F_OrderDetail(OrderDetailSnapshot data, Window owner, bool readOnly)
    {
        InitializeComponent();
        this.data = data;
        this.readOnly = readOnly;
        Owner = owner;
        viewModel = new OrderDetailViewModel(data, EditOrder, ConfirmOrder, HideOrder, PrintSlip, Close, readOnly);
        DataContext = viewModel;
        Title = $"受注詳細 - ORD-{data.OrderId:000000}";
        _ = new RegistrationWindowPlacement(this, preferredWidth: 900, preferredHeight: 640, minimumWidth: 760, minimumHeight: 460);
        Loaded += async (_, _) =>
        {
            if (!AppSession.CanManageOrders)
            {
                F_OrderNotice.Show(Owner ?? this, "権限エラー", "受注詳細は管理者または営業担当者のみ利用できます。", OrderNoticeKind.Warning);
                Close();
                return;
            }
            await LoadRecordedHistoryAsync();
        };
        Closed += (_, _) => detailClosed = true;
    }

    public void OpenUpdateEditor() => EditOrder();

    private async void EditOrder()
    {
        if (readOnly) return;
        if (!AppSession.CanManageOrders || AppSession.Employee == null)
        {
            F_OrderNotice.Show(Owner ?? this, "権限エラー", "受注の更新は管理者または営業担当者のみ実行できます。", OrderNoticeKind.Warning);
            return;
        }
        if (data.StateFlag != 0 || data.ManagementFlag != 0)
        {
            F_OrderNotice.Show(Owner ?? this, "受注更新", "未確定かつ表示中の受注だけを更新できます。", OrderNoticeKind.Warning);
            return;
        }
        if ((Owner ?? this) is F_Main shell)
        {
            if (await shell.ShowOrderUpdateAsync(data) && IsVisible) Close();
            return;
        }
        var dialog = new F_OrderUpdate(data) { Owner = Owner ?? this };
        dialog.ShowDialog();
        if (dialog.HasSaved && IsVisible) Close();
    }

    private async Task LoadRecordedHistoryAsync()
    {
        if (historyRequested || detailClosed) return;
        historyRequested = true;
        try
        {
            var history = await Task.Run(() =>
            {
                using var db = new SalesManagementContext();
                return OrderHistoryService.Read(db, new OrderHistoryFilter { OrderId = data.OrderId }).Rows;
            });
            if (!detailClosed) viewModel.SetHistory(history);
        }
        catch
        {
            if (!detailClosed) viewModel.SetHistory([], "監査履歴を取得できませんでした。過去の日時・実行者は推測せず、画面を開き直して確認してください。");
        }
    }

    private void ConfirmOrder()
    {
        if (readOnly) return;
        if (!(AppSession.IsAdmin || AppSession.IsSales))
        {
            F_OrderNotice.Show(Owner ?? this, "権限エラー", "受注確定は管理者または営業担当者のみ実行できます。", OrderNoticeKind.Warning);
            return;
        }
        if (data.StateFlag == 1)
        {
            F_OrderNotice.Show(Owner ?? this, "受注確定", "この受注はすでに確定済みです。", OrderNoticeKind.Information);
            return;
        }
        if (AppSession.Employee == null) return;
        if (!F_OrderNotice.Confirm(Owner ?? this, "受注を確定",
            $"受注 ORD-{data.OrderId:000000} を確定します。よろしいですか？\n注文が作成されます。在庫はこの操作では減算しません。", "受注を確定")) return;
        if (!BusinessService.ConfirmOrder(data.OrderId, AppSession.Employee.EmId, out var message))
        {
            F_OrderNotice.Show(Owner ?? this, "受注確定エラー", message, OrderNoticeKind.Error);
            return;
        }
        F_OrderNotice.Show(Owner ?? this, "受注確定", message, OrderNoticeKind.Success);
        Close();
    }

    private void HideOrder()
    {
        if (readOnly) return;
        if (!(AppSession.IsAdmin || AppSession.IsSales))
        {
            F_OrderNotice.Show(Owner ?? this, "権限エラー", "受注の非表示は管理者または営業担当者のみ実行できます。", OrderNoticeKind.Warning);
            return;
        }
        var dialog = new F_OrderHide([data.OrderId]) { Owner = this };
        dialog.ShowDialog();
        if (dialog.SuccessfulOrderIds.Contains(data.OrderId)) Close();
    }

    private void PrintSlip()
    {
        if (!viewModel.CanPrint) return;
        try
        {
            if (!OrderSlipPrintService.TryLoadConfirmedOrder(data.OrderId, out var slip, out var message) || slip == null)
            {
                F_OrderNotice.Show(Owner ?? this, "伝票印刷", message, OrderNoticeKind.Warning);
                return;
            }

            new OrderSlipPrintPreviewWindow(slip) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            F_OrderNotice.Show(Owner ?? this, "伝票印刷", "受注伝票を読み込めませんでした。\n" + ex.Message, OrderNoticeKind.Error);
        }
    }

    private void HeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source != null)
        {
            if (source is ButtonBase) return;
            source = source is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(source);
        }
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void InformationSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < 620;
        Grid.SetColumnSpan(OrderCard, stacked ? 2 : 1);
        Grid.SetColumn(CustomerCard, stacked ? 0 : 1);
        Grid.SetColumnSpan(CustomerCard, stacked ? 2 : 1);
        Grid.SetRow(CustomerCard, stacked ? 1 : 0);
        OrderCard.Margin = stacked ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 6, 0);
        CustomerCard.Margin = stacked ? new Thickness(0) : new Thickness(6, 0, 0, 0);
    }
}
