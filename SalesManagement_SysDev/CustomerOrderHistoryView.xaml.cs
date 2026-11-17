using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SalesManagement_SysDev;

public partial class CustomerOrderHistoryView : UserControl
{
    private const int PageSize = 20;
    private readonly int targetId;
    private readonly bool byProduct;
    private int pageNumber = 1;
    private bool started, loading, openingDetail;

    public CustomerOrderHistoryView(int customerId) : this(customerId, false) { }

    internal static CustomerOrderHistoryView ForProduct(int productId) => new(productId, true);

    private CustomerOrderHistoryView(int targetId, bool byProduct)
    {
        this.targetId = targetId;
        this.byProduct = byProduct;
        InitializeComponent();
        if (byProduct) ApplyProductColumns();
    }

    private void ApplyProductColumns()
    {
        var columns = OrdersGrid.Columns;
        columns.RemoveAt(5); // Visibility is shown with the order status for product lines.
        columns.RemoveAt(2); // Customer history counts lines; product history shows each line.
        columns.Insert(2, new DataGridTextColumn
        {
            Header = "顧客名", Binding = new Binding(nameof(ProductOrderRow.Customer)), MinWidth = 90,
            Width = new DataGridLength(1.2, DataGridLengthUnitType.Star), ElementStyle = (Style)FindResource("TableText")
        });
        DataGridTextColumn Number(string header, Binding binding, double width) => new()
        {
            Header = header, Binding = binding, Width = new DataGridLength(width),
            HeaderStyle = (Style)FindResource("OrderRightHeader"), ElementStyle = (Style)FindResource("TableNumber")
        };
        columns.Insert(3, Number("数量", new Binding(nameof(ProductOrderRow.Quantity)) { StringFormat = "N0" }, 58));
        var unitPrice = Number("単価（明細）", new Binding(nameof(ProductOrderRow.UnitPriceText)), 108);
        unitPrice.Header = new TextBlock { Text = "単価（明細）", ToolTip = "明細に保存された金額を数量で割った単価です。" };
        columns.Insert(4, unitPrice);
        columns[6].Width = new DataGridLength(120);
    }

    private async void View_Loaded(object sender, RoutedEventArgs e)
    {
        if (started) return;
        started = true;
        await LoadAsync(1);
    }

    private async void Reload_Click(object sender, RoutedEventArgs e) => await LoadAsync(pageNumber);
    private async void Previous_Click(object sender, RoutedEventArgs e) => await LoadAsync(pageNumber - 1);
    private async void Next_Click(object sender, RoutedEventArgs e) => await LoadAsync(pageNumber + 1);

    private async Task LoadAsync(int requestedPage)
    {
        if (loading || openingDetail) return;
        loading = true;
        ReloadButton.IsEnabled = PreviousButton.IsEnabled = NextButton.IsEnabled = false;
        OrdersGrid.IsEnabled = false;
        Feedback.Text = "受注を読み込んでいます…";
        try
        {
            var page = await Task.Run(() =>
            {
                if (byProduct)
                {
                    var productPage = ProductOrderHistory.ReadPage(targetId, requestedPage, PageSize);
                    return (Rows: (System.Collections.IList)productPage.Rows.ToArray(), productPage.PageNumber, productPage.PageCount, productPage.Total);
                }
                var customerPage = CustomerOrderHistory.ReadPage(targetId, requestedPage, PageSize);
                return (Rows: (System.Collections.IList)customerPage.Rows.ToArray(), customerPage.PageNumber, customerPage.PageCount, customerPage.Total);
            });
            pageNumber = page.PageNumber;
            OrdersGrid.ItemsSource = page.Rows;
            Feedback.Text = page.Total == 0 ? "参照できる受注がありません。" : "";
            PreviousButton.IsEnabled = pageNumber > 1;
            NextButton.IsEnabled = pageNumber < page.PageCount;
            PageIndicator.Text = $"{pageNumber:N0} / {page.PageCount:N0}";
            var start = (pageNumber - 1) * PageSize;
            RangeIndicator.Text = page.Total == 0 ? "0 件" : $"{start + 1:N0}～{start + page.Rows.Count:N0} 件 / 全 {page.Total:N0} 件";
        }
        catch (Exception ex)
        {
            Feedback.Text = ex is UnauthorizedAccessException ? ex.Message : "受注履歴を読み込めませんでした。再読込してください。";
            OrdersGrid.ItemsSource = null;
            PageIndicator.Text = RangeIndicator.Text = "";
        }
        finally { loading = false; ReloadButton.IsEnabled = OrdersGrid.IsEnabled = true; }
    }

    private async void OpenDetail_Click(object sender, RoutedEventArgs e)
    {
        if (loading || openingDetail) return;
        var orderId = (sender as FrameworkElement)?.DataContext switch
        {
            ProductOrderRow row when byProduct => row.OrderId,
            CustomerOrderRow row when !byProduct => row.Id,
            _ => 0
        };
        if (orderId <= 0) return;
        openingDetail = true;
        try
        {
            var detail = await Task.Run(() => byProduct ? ProductOrderHistory.ReadDetail(targetId, orderId)
                : CustomerOrderHistory.ReadDetail(targetId, orderId));
            if (!IsLoaded || Window.GetWindow(this) is not { } owner) return;
            if (detail == null)
                F_OrderNotice.Show(owner, "受注詳細", "受注が見つからないか、参照範囲外です。", OrderNoticeKind.Warning);
            else
                new F_OrderDetail(detail, owner, readOnly: true).ShowDialog();
        }
        catch (Exception ex)
        {
            if (IsLoaded)
                F_OrderNotice.Show(Window.GetWindow(this), "受注詳細",
                    ex is UnauthorizedAccessException ? ex.Message : "受注を取得できませんでした。", OrderNoticeKind.Warning);
        }
        finally { openingDetail = false; }
    }
}
