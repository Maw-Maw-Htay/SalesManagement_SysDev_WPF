using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SalesManagement_SysDev;

public partial class PurchaseReceiptHistoryWindow : Window
{
    private readonly IReadOnlyList<PurchaseReceiptRecord> receipts;
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly HashSet<DatePicker> invalidDates = [];
    private bool opening;

    public PurchaseReceiptHistoryWindow(IReadOnlyList<PurchaseReceiptRecord> receipts)
    {
        this.receipts = receipts;
        InitializeComponent();
        _ = new RegistrationWindowPlacement(this, 900, 640, 660, 420);
        debounce.Tick += (_, _) => { debounce.Stop(); ApplyFilters(); };
        Closed += (_, _) => debounce.Stop();
        ApplyFilters();
    }

    private void Filters_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is DatePicker picker) invalidDates.Remove(picker);
        debounce.Stop();
        debounce.Start();
    }

    private void Date_Invalid(object sender, DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        if (sender is DatePicker picker) invalidDates.Add(picker);
        ApplyFilters();
    }

    private void Date_Editing(object sender, TextCompositionEventArgs e)
    {
        if (sender is DatePicker picker) invalidDates.Remove(picker);
    }

    private void ApplyFilters()
    {
        var start = StartDate.SelectedDate?.Date;
        var end = EndDate.SelectedDate?.Date;
        if (invalidDates.Count > 0 || start > end)
        {
            FilterError.Text = invalidDates.Count > 0 ? "入庫日を正しい形式で入力してください。" : "入庫日の開始日は終了日以前にしてください。";
            ReceiptsGrid.ItemsSource = null;
            return;
        }
        var term = Search.Text.Trim();
        var matches = receipts.Where(x =>
            (!start.HasValue || x.Date.Date >= start) && (!end.HasValue || x.Date.Date <= end)
            && (x.OrderCode.Contains(term, StringComparison.OrdinalIgnoreCase) || x.Code.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.MakerName.Contains(term, StringComparison.CurrentCultureIgnoreCase))).ToArray();
        ReceiptsGrid.ItemsSource = matches;
        FilterError.Text = matches.Length == 0 ? "条件に一致する入庫記録はありません。" : "";
    }

    private async void Receipt_Click(object sender, RoutedEventArgs e)
    {
        if (ReceiptsGrid.SelectedItem is PurchaseReceiptRecord row) await OpenAsync(row);
    }

    private async void Receipt_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(ReceiptsGrid, source) is DataGridRow { Item: PurchaseReceiptRecord row })
            await OpenAsync(row);
    }

    private async Task OpenAsync(PurchaseReceiptRecord row)
    {
        if (opening) return;
        opening = true;
        try { await OpenReceiptAsync(this, row); }
        finally { opening = false; }
    }

    internal static async Task OpenReceiptAsync(Window owner, PurchaseReceiptRecord row)
    {
        try
        {
            var detail = await Task.Run(() => WarehousingService.LoadDetail(row.Id));
            if (!owner.IsVisible) return;
            if (detail == null)
            {
                F_OrderNotice.Show(owner, "入庫詳細", "対象の入庫記録を表示できません。", OrderNoticeKind.Warning);
                return;
            }
            var snapshot = new F_WarehouseMovementDetail(detail, readOnly: true) { Owner = owner };
            snapshot.ShowDialog();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            if (owner.IsVisible) F_OrderNotice.Show(owner, "入庫詳細", "入庫記録を読み込めませんでした。接続状態を確認してください。", OrderNoticeKind.Error);
        }
    }
}
