using System.Windows;

namespace SalesManagement_SysDev;

public partial class F_WarehouseMovementDetail : Window
{
    private readonly WarehouseMovementDetail detail;
    private readonly WarehouseMovementDetailViewModel viewModel;
    private bool processing;

    public F_WarehouseMovementDetail(WarehouseMovementDetail detail, bool readOnly = false)
    {
        InitializeComponent();
        this.detail = detail;
        viewModel = new WarehouseMovementDetailViewModel(detail, readOnly);
        DataContext = viewModel;
        QuantityColumn.Header = viewModel.QuantityLabel;
        if (!viewModel.HasRelatedPurchase) OrderedColumn.Visibility = Visibility.Collapsed;
        if (!viewModel.ShowReceiptDifferences) DifferenceColumn.Visibility = Visibility.Collapsed;
        Closing += (_, e) => { if (processing) e.Cancel = true; };
    }

    private void Close_Click(object sender, RoutedEventArgs e) { if (!processing) DialogResult = false; }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (processing || !viewModel.CanConfirm) return;
        var dialog = new F_WarehouseMovementConfirm(detail) { Owner = this };
        if (dialog.ShowDialog() == true) DialogResult = true;
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        if (processing || !viewModel.CanHide) return;
        var dialog = new F_WarehouseMovementHide(detail.Record, detail.Lines) { Owner = this };
        if (dialog.ShowDialog() == true) DialogResult = true;
    }

    private async void Purchase_Click(object sender, RoutedEventArgs e)
    {
        if (processing || !viewModel.HasRelatedPurchase) return;
        processing = true;
        try
        {
            var purchase = await Task.Run(() => PurchaseOrderService.LoadDetail(detail.Record.ReferenceId));
            if (purchase == null)
                F_OrderNotice.Show(this, "発注詳細", "関連する発注が見つからないか、参照範囲外です。", OrderNoticeKind.Warning);
            else
                new F_HattyuDetail(purchase, readOnly: true) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            F_OrderNotice.Show(this, "発注詳細", ex is UnauthorizedAccessException ? ex.Message : "関連する発注を取得できませんでした。", OrderNoticeKind.Warning);
        }
        finally { processing = false; }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (processing || !viewModel.CanRestore || AppSession.Employee is not { } employee) return;
        var receipt = detail.Record;
        if (!F_OrderNotice.Confirm(this, "入庫を表示に戻しますか？",
            $"入庫番号：{receipt.Code}\n関連発注番号：{receipt.ReferenceCode}\n管理状態：非表示 → 表示中\n\n在庫数量と入庫の確定状態は変わりません。",
            "表示に戻す")) return;
        processing = true;
        WarehouseMovementOperationResult result;
        try { result = await Task.Run(() => WarehousingService.Restore(employee.EmId, receipt)); }
        catch (Exception) { result = new(false, "入庫を復元できませんでした。再読込してからお試しください。"); }
        finally { processing = false; }
        F_OrderNotice.Show(this, "入庫の表示復元", result.Message, result.Succeeded ? OrderNoticeKind.Success : OrderNoticeKind.Warning);
        if (result.Succeeded) DialogResult = true;
    }
}
