using System.Windows;

namespace SalesManagement_SysDev;

public partial class OutboundDetailWindow : Window
{
    private readonly WarehouseMovementDetail detail;
    private readonly WarehouseMovementDetailViewModel viewModel;
    private bool openingRelated;

    public OutboundDetailWindow(WarehouseMovementDetail detail, bool readOnly = false)
    {
        InitializeComponent();
        this.detail = detail;
        viewModel = new WarehouseMovementDetailViewModel(detail, readOnly);
        DataContext = viewModel;
        _ = new RegistrationWindowPlacement(this, 860, 650, 600, 420);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.CanConfirm && new F_WarehouseMovementConfirm(detail) { Owner = this }.ShowDialog() == true)
            DialogResult = true;
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.CanHide && new F_WarehouseMovementHide(detail.Record, detail.Lines) { Owner = this }.ShowDialog() == true)
            DialogResult = true;
    }

    private async void Order_Click(object sender, RoutedEventArgs e)
    {
        if (openingRelated || !viewModel.CanOpenRelatedOrder || detail.Record.RelatedOrderId is not int id) return;
        openingRelated = true;
        try
        {
            var model = new ChumonManagementViewModel(loadOnCreate: false);
            var order = await model.GetDetailsAsync(id);
            if (!IsVisible) return;
            if (order == null || order.RelatedOrderId != detail.Record.ReferenceId || order.State != "確定済み"
                || !WarehouseMovementRules.LinesMatch(detail.Lines.Select(x => new WarehouseMovementLineInput(x.ProductId, x.Quantity)).ToArray(),
                    order.Lines.Select(x => new WarehouseMovementLineInput(x.ProductId, x.Quantity)).ToArray()))
            {
                F_OrderNotice.Show(this, "関連注文詳細", "対象の注文を確認できません。", OrderNoticeKind.Warning);
                return;
            }
            var row = new OrderManagementRow { OrderId = id };
            new OrderDetailDialog(order, model, row, this, readOnly: true).ShowDialog();
        }
        finally { openingRelated = false; }
    }

    private void Arrival_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.CanOpenRelatedArrival) return;
        new OutboundArrivalWindow(detail.Record) { Owner = this }.ShowDialog();
    }
}
