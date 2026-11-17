using System.Windows;

namespace SalesManagement_SysDev;

public partial class F_ArrivalDetail : Window
{
    private readonly ArrivalDetailViewModel viewModel;
    public bool WasChanged { get; private set; }

    public F_ArrivalDetail(ArrivalDetail detail, bool readOnly = false)
    {
        InitializeComponent();
        viewModel = new ArrivalDetailViewModel(detail, readOnly);
        DataContext = viewModel;
        MaxHeight = Math.Max(480, SystemParameters.WorkArea.Height * .9);
        Height = Math.Min(700, MaxHeight);
        MaxWidth = Math.Max(700, SystemParameters.WorkArea.Width * .92);
        Width = Math.Min(940, MaxWidth);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = WasChanged;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.CanConfirm || !ArrivalService.CanConfirm) return;
        var confirmation = new F_ArrivalConfirm(viewModel.Arrival) { Owner = this };
        if (confirmation.ShowDialog() != true) return;
        WasChanged = true;
        DialogResult = true;
    }

    private async void Hide_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.CanHide || !ArrivalService.CanHide) return;
        var confirmation = new F_ArrivalHide(viewModel.Arrival) { Owner = this };
        if (confirmation.ShowDialog() != true) return;

        ArrivalOperationResult result;
        try
        {
            var employeeId = AppSession.Employee?.EmId ?? 0;
            result = await Task.Run(() => ArrivalService.Hide(employeeId, viewModel.Arrival.Id, confirmation.Reason));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Arrival hide failed: {ex}");
            result = new(false, "入荷を非表示にできませんでした。一覧を再読込してからお試しください。");
        }

        F_OrderNotice.Show(this, result.Succeeded ? "入荷を非表示にしました" : "入荷を非表示にできませんでした",
            result.Message, result.Succeeded ? OrderNoticeKind.Success : OrderNoticeKind.Error);
        if (!result.Succeeded) return;
        WasChanged = true;
        DialogResult = true;
    }
}
