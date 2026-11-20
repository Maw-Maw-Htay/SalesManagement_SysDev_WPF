using System.Windows;
using System.Windows.Controls;

namespace SalesManagement_SysDev;

public partial class F_HattyuDetail : Window
{
    private readonly PurchaseOrderDetailViewModel viewModel;
    private readonly PurchaseOrderDetail detail;
    private bool processing;
    public bool WasHidden { get; private set; }

    public F_HattyuDetail(PurchaseOrderDetail detail) : this(detail, readOnly: false) { }

    internal F_HattyuDetail(PurchaseOrderDetail detail, bool readOnly)
    {
        InitializeComponent();
        this.detail = detail;
        viewModel = new PurchaseOrderDetailViewModel(detail, !readOnly && PurchaseOrderService.CanHide);
        DataContext = viewModel;
        _ = new RegistrationWindowPlacement(this, 880, 680, 670, 440);
        Closing += (_, e) => e.Cancel = processing;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (!processing) DialogResult = WasHidden;
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (processing || !PurchaseOrderService.CanView) return;
        try { new PurchaseOrderPrintPreview(detail) { Owner = this }.ShowDialog(); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            F_OrderNotice.Show(this, "発注書", "発注書のプレビューを表示できませんでした。", OrderNoticeKind.Error);
        }
    }

    private async void Receipt_Click(object sender, RoutedEventArgs e)
    {
        if (processing || sender is not Button { DataContext: PurchaseReceiptRecord row }) return;
        processing = true;
        try { await PurchaseReceiptHistoryWindow.OpenReceiptAsync(this, row); }
        finally { processing = false; }
    }

    private async void Hide_Click(object sender, RoutedEventArgs e)
    {
        if (processing || !viewModel.CanHide || !PurchaseOrderService.CanHide) return;
        var confirmation = new F_HattyuHide(viewModel.Order) { Owner = this };
        if (confirmation.ShowDialog() != true) return;
        processing = true;

        PurchaseOperationResult result;
        try
        {
            result = await Task.Run(() => PurchaseOrderService.Hide(AppSession.Employee!.EmId,
                viewModel.Order.Id, confirmation.Reason));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Purchase hide failed: {ex}");
            result = new(false, null, "発注を非表示にできませんでした。データベースの接続状態を確認してください。");
        }
        finally { processing = false; }

        F_OrderNotice.Show(this, result.Succeeded ? "発注を非表示にしました" : "発注を非表示にできませんでした",
            result.Message, result.Succeeded ? OrderNoticeKind.Success : OrderNoticeKind.Error);
        if (!result.Succeeded) return;
        WasHidden = true;
        DialogResult = true;
    }
}
