using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SalesManagement_SysDev;

public partial class F_HattyuRegister : Window
{
    private readonly PurchaseOrderRegisterViewModel viewModel;
    private bool bypassDiscardPrompt;
    public int? SavedOrderId { get; private set; }
    public bool OpenSavedDetail { get; private set; }

    public F_HattyuRegister() : this(new PurchaseOrderRegisterViewModel()) { }

    internal F_HattyuRegister(PurchaseOrderRegisterViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        _ = new RegistrationWindowPlacement(this, preferredWidth: 1000, preferredHeight: 680, minimumWidth: 700, minimumHeight: 460);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.CanCancel) return;
        DialogResult = false;
    }

    private async void Register_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.CanRegister) return;
        var result = await viewModel.RegisterAsync();
        if (!result.Succeeded)
        {
            F_OrderNotice.Show(this, "発注を登録できませんでした", result.Message, OrderNoticeKind.Error);
            return;
        }

        SavedOrderId = result.OrderId;
        var completion = new F_OrderNotice("発注登録完了", result.Message, OrderNoticeKind.Success,
            "発注詳細を表示", "一覧に戻る", showCancel: true) { Owner = this };
        OpenSavedDetail = completion.ShowDialog() == true;
        DialogResult = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        if (viewModel.CanCancel) DialogResult = false;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (viewModel.IsSaving)
        {
            e.Cancel = true;
            return;
        }
        if (SavedOrderId.HasValue || bypassDiscardPrompt || viewModel.Lines.Count == 0) return;
        bypassDiscardPrompt = F_OrderNotice.Confirm(this, "未保存の発注", "登録前の発注明細を破棄しますか？",
            "破棄する", "入力に戻る", destructive: true);
        e.Cancel = !bypassDiscardPrompt;
    }

    private void Maker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && viewModel.CanChangeMaker)
            ProductPicker.Text = "";
    }

    private void ClearLines_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.CanClearLines && F_OrderNotice.Confirm(this, "発注明細をすべて削除しますか？",
            "すべての商品を削除すると、仕入先を変更できるようになります。", "すべて削除", destructive: true))
            viewModel.ClearLines();
    }

    private void QuantityUp_Click(object sender, RoutedEventArgs e) => StepQuantity(1);
    private void QuantityDown_Click(object sender, RoutedEventArgs e) => StepQuantity(-1);
    private void StepQuantity(int change)
    {
        if (!int.TryParse(viewModel.QuantityText, out var value)) value = 1;
        viewModel.QuantityText = Math.Clamp((long)value + change, 1, int.MaxValue).ToString();
    }

    private void Quantity_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = e.Text.Any(c => !char.IsAsciiDigit(c));

    private void Date_Invalid(object sender, System.Windows.Controls.DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        viewModel.HasDateInputError = true;
    }
    private void Date_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => viewModel.HasDateInputError = false;
    private void Date_Editing(object sender, TextCompositionEventArgs e) => viewModel.HasDateInputError = false;

    private void Product_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedProduct is not { } product) return;
        F_OrderNotice.Show(this, "商品詳細",
            $"{product.DisplayCode}　{product.Name}\n仕入先　{product.MakerName}\n{viewModel.InventoryReferenceText}");
    }
}
