using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SalesManagement_SysDev;

public sealed class QuantityComboBox : ComboBox
{
    public static readonly DependencyProperty ConfirmCommandProperty = DependencyProperty.Register(
        nameof(ConfirmCommand), typeof(ICommand), typeof(QuantityComboBox));
    private TextBox? input;
    private bool synchronizing;

    public QuantityComboBox() => AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler(ConfirmOnEnter), true);

    public ICommand? ConfirmCommand
    {
        get => (ICommand?)GetValue(ConfirmCommandProperty);
        set => SetValue(ConfirmCommandProperty, value);
    }

    private void ConfirmOnEnter(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (!IsEnabled || IsReadOnly) return;
        // ComboBox's native key handler accepts the highlighted dropdown item first.
        GetBindingExpression(TextProperty)?.UpdateSource();
        if (ConfirmCommand?.CanExecute(null) == true) ConfirmCommand.Execute(null);
    }

    public override void OnApplyTemplate()
    {
        if (input != null) input.TextChanged -= InputChanged;
        base.OnApplyTemplate();
        input = GetTemplateChild("PART_EditableTextBox") as TextBox;
        if (input != null) input.TextChanged += InputChanged;
    }

    private void InputChanged(object sender, TextChangedEventArgs e)
    {
        if (synchronizing || input == null || SelectedIndex < 0 || Equals(SelectedItem, input.Text)) return;
        // With text search disabled, WPF keeps the old selection after manual input.
        // Clear it so choosing that same preset again also corrects the entered text.
        var text = input.Text;
        var selectionStart = input.SelectionStart;
        var selectionLength = input.SelectionLength;
        synchronizing = true;
        try
        {
            SetCurrentValue(SelectedIndexProperty, -1);
            SetCurrentValue(TextProperty, text);
            input.Select(Math.Min(selectionStart, input.Text.Length), Math.Min(selectionLength, input.Text.Length - Math.Min(selectionStart, input.Text.Length)));
        }
        finally { synchronizing = false; }
    }
}
