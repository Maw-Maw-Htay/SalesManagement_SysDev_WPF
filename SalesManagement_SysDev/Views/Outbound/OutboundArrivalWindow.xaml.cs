using System.Windows;
using System.Windows.Controls;

namespace SalesManagement_SysDev;

public partial class OutboundArrivalWindow : Window
{
    private bool opening;
    private readonly int outboundId;
    public OutboundArrivalWindow(WarehouseMovementRecord record)
    {
        InitializeComponent();
        outboundId = record.Id;
        DataContext = record;
        var hasArrival = ArrivalService.CanView && record.RelatedArrival != null;
        ArrivalsGrid.ItemsSource = hasArrival ? new[] { record.RelatedArrival! } : Array.Empty<ArrivalRecord>();
        EmptyText.Visibility = hasArrival ? Visibility.Collapsed : Visibility.Visible;
        _ = new RegistrationWindowPlacement(this, 730, 385, 625, 300);
    }

    private async void Detail_Click(object sender, RoutedEventArgs e)
    {
        if (opening || !ArrivalService.CanView || sender is not Button { DataContext: ArrivalRecord arrival }) return;
        opening = true;
        try
        {
            var detail = await Task.Run(() => OutboundService.LoadDetail(outboundId)?.Record.RelatedArrival?.Id == arrival.Id
                ? ArrivalService.LoadDetail(arrival.Id) : null);
            if (!IsVisible) return;
            if (detail == null || detail.Arrival.OrderId != arrival.OrderId || detail.Arrival.OfficeId != arrival.OfficeId
                || detail.Arrival.CustomerId != arrival.CustomerId)
            {
                F_OrderNotice.Show(this, "入荷詳細", "対象の入荷データを確認できません。", OrderNoticeKind.Warning);
                return;
            }
            new F_ArrivalDetail(detail, readOnly: true) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            if (IsVisible) F_OrderNotice.Show(this, "入荷詳細", "再読込して入荷データを確認してください。", OrderNoticeKind.Error);
        }
        finally { opening = false; }
    }
}
