using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SalesManagement_SysDev;

[TemplatePart(Name = "PART_Input", Type = typeof(TextBox))]
[TemplatePart(Name = "PART_Increase", Type = typeof(Button))]
[TemplatePart(Name = "PART_Decrease", Type = typeof(Button))]
public sealed class QuantityUpDown : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(QuantityUpDown),
        new FrameworkPropertyMetadata("1", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, Changed));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(int), typeof(QuantityUpDown),
        new PropertyMetadata(int.MaxValue, Changed), value => (int)value >= 1);
    public static readonly DependencyProperty ConfirmCommandProperty = DependencyProperty.Register(nameof(ConfirmCommand), typeof(ICommand), typeof(QuantityUpDown));
    public static readonly DependencyProperty PreserveInvalidInputProperty = DependencyProperty.Register(nameof(PreserveInvalidInput), typeof(bool), typeof(QuantityUpDown), new PropertyMetadata(false));
    private TextBox? input;
    private Button? increase, decrease;
    private string lastValid = "1";
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public int Maximum { get => (int)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public ICommand? ConfirmCommand { get => (ICommand?)GetValue(ConfirmCommandProperty); set => SetValue(ConfirmCommandProperty, value); }
    public bool PreserveInvalidInput { get => (bool)GetValue(PreserveInvalidInputProperty); set => SetValue(PreserveInvalidInputProperty, value); }

    public override void OnApplyTemplate()
    {
        if (input != null)
        {
            input.PreviewTextInput -= PreviewInput;
            input.PreviewKeyDown -= PreviewKey;
            input.LostKeyboardFocus -= CommitInput;
            DataObject.RemovePastingHandler(input, Pasting);
        }
        if (increase != null) increase.Click -= Increase;
        if (decrease != null) decrease.Click -= Decrease;
        base.OnApplyTemplate();
        input = GetTemplateChild("PART_Input") as TextBox;
        increase = GetTemplateChild("PART_Increase") as Button;
        decrease = GetTemplateChild("PART_Decrease") as Button;
        if (input != null)
        {
            input.PreviewTextInput += PreviewInput;
            input.PreviewKeyDown += PreviewKey;
            input.LostKeyboardFocus += CommitInput;
            DataObject.AddPastingHandler(input, Pasting);
        }
        if (increase != null) increase.Click += Increase;
        if (decrease != null) decrease.Click += Decrease;
        Refresh();
    }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((QuantityUpDown)sender).Refresh();
    private bool TryValue(out int value) => RegistrationQuantity.TryParse(Text ?? "", out value) && value <= Maximum;
    private void Refresh()
    {
        var valid = TryValue(out var value);
        if (valid) lastValid = Text;
        if (increase != null) increase.IsEnabled = valid && value < Maximum;
        if (decrease != null) decrease.IsEnabled = valid && value > 1;
    }
    private void Increase(object sender, RoutedEventArgs args) { if (TryValue(out var value) && value < Maximum) SetText(value + 1); }
    private void Decrease(object sender, RoutedEventArgs args) { if (TryValue(out var value) && value > 1) SetText(value - 1); }
    private void SetText(int value)
    {
        SetCurrentValue(TextProperty, value.ToString(CultureInfo.InvariantCulture));
        // Keep keyboard editing in the numeric field after clicking either step button.
        if (input != null) { input.Focus(); input.CaretIndex = input.Text.Length; }
    }
    private bool Accepts(string inserted)
    {
        if (input == null || inserted.Length == 0 || inserted.Any(c => c is < '0' or > '9')) return false;
        var proposed = input.Text.Remove(input.SelectionStart, input.SelectionLength).Insert(input.SelectionStart, inserted);
        return proposed.Length <= 10 && RegistrationQuantity.TryParse(proposed, out var value) && value <= Maximum;
    }
    private void PreviewInput(object sender, TextCompositionEventArgs args) => args.Handled = !Accepts(args.Text);
    private void PreviewKey(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter)
        {
            args.Handled = true;
            if (!IsEnabled) return;
            GetBindingExpression(TextProperty)?.UpdateSource();
            // Screens may opt into a separate confirmation command; Enter never invokes a save action.
            if (ConfirmCommand?.CanExecute(null) == true) ConfirmCommand.Execute(null);
        }
        else if (args.Key == Key.Space) args.Handled = true;
    }
    private void Pasting(object sender, DataObjectPastingEventArgs args)
    {
        var text = args.DataObject.GetData(DataFormats.UnicodeText) as string ?? args.DataObject.GetData(DataFormats.Text) as string;
        if (text == null || !Accepts(text)) args.CancelCommand();
    }
    private void CommitInput(object sender, KeyboardFocusChangedEventArgs args)
    {
        // Validation screens retain invalid text until it is corrected.
        if (PreserveInvalidInput || ConfirmCommand != null) return;
        if (!TryValue(out _)) SetCurrentValue(TextProperty, RegistrationQuantity.TryParse(lastValid, out var value)
            ? Math.Min(value, Maximum).ToString(CultureInfo.InvariantCulture) : "1");
    }
}
