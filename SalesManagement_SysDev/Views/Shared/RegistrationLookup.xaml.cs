using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SalesManagement_SysDev;

// Focus, popup placement and keyboard navigation are view concerns. The VM validates selections.
public partial class RegistrationLookup : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(RegistrationLookup),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(RegistrationLookup));
    public static readonly DependencyProperty SelectCommandProperty = DependencyProperty.Register(nameof(SelectCommand), typeof(ICommand), typeof(RegistrationLookup));
    public static readonly DependencyProperty SuggestionTemplateProperty = DependencyProperty.Register(nameof(SuggestionTemplate), typeof(DataTemplate), typeof(RegistrationLookup));
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(RegistrationLookup), new PropertyMetadata(""));
    public static readonly DependencyProperty AccessibleNameProperty = DependencyProperty.Register(nameof(AccessibleName), typeof(string), typeof(RegistrationLookup), new PropertyMetadata(""));
    public static readonly DependencyProperty CommittedTextProperty = DependencyProperty.Register(nameof(CommittedText), typeof(string), typeof(RegistrationLookup));
    public static readonly DependencyProperty SelectSingleMatchOnEnterProperty = DependencyProperty.Register(nameof(SelectSingleMatchOnEnter), typeof(bool), typeof(RegistrationLookup));
    public static readonly DependencyProperty InvalidProperty = DependencyProperty.Register(nameof(Invalid), typeof(bool), typeof(RegistrationLookup), new PropertyMetadata(false));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public ICommand? SelectCommand { get => (ICommand?)GetValue(SelectCommandProperty); set => SetValue(SelectCommandProperty, value); }
    public DataTemplate? SuggestionTemplate { get => (DataTemplate?)GetValue(SuggestionTemplateProperty); set => SetValue(SuggestionTemplateProperty, value); }
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public string AccessibleName { get => (string)GetValue(AccessibleNameProperty); set => SetValue(AccessibleNameProperty, value); }
    public string? CommittedText { get => (string?)GetValue(CommittedTextProperty); set => SetValue(CommittedTextProperty, value); }
    public bool SelectSingleMatchOnEnter { get => (bool)GetValue(SelectSingleMatchOnEnterProperty); set => SetValue(SelectSingleMatchOnEnterProperty, value); }
    public bool Invalid { get => (bool)GetValue(InvalidProperty); set => SetValue(InvalidProperty, value); }
    private bool returningFocus;
    public RegistrationLookup() => InitializeComponent();
    public void FocusInput()
    {
        SuggestionsPopup.IsOpen = false;
        ReturnFocus();
    }
    private void InputFocused(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!returningFocus) SuggestionsPopup.IsOpen = true;
    }
    private void QueryChanged(object sender, TextChangedEventArgs e)
    {
        if (SuggestionList == null) return;
        SuggestionList.SelectedIndex = -1;
        if (QueryInput.IsKeyboardFocused && !returningFocus) SuggestionsPopup.IsOpen = true;
    }
    private void ToggleSuggestions(object sender, RoutedEventArgs e)
    {
        var open = !SuggestionsPopup.IsOpen;
        ReturnFocus(); SuggestionsPopup.IsOpen = open;
    }
    private void InputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && SuggestionList.Items.Count > 0)
        {
            SuggestionsPopup.IsOpen = true;
            SuggestionList.SelectedIndex = 0;
            SuggestionList.Focus();
            SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && SelectSingleMatchOnEnter)
        {
            if (SuggestionsPopup.IsOpen && SuggestionList.Items.Count == 1) Commit(SuggestionList.Items[0]);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SuggestionsPopup.IsOpen) { SuggestionsPopup.IsOpen = false; e.Handled = true; }
        else if (e.Key == Key.Tab) SuggestionsPopup.IsOpen = false;
    }
    private void SuggestionsClosed(object? sender, EventArgs e)
    {
        if (CommittedText is not { } text || Text == text) return;
        returningFocus = true;
        try { SetCurrentValue(TextProperty, text); }
        finally { returningFocus = false; }
    }
    private void SuggestionClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject origin && ItemsControl.ContainerFromElement(SuggestionList, origin) is ListBoxItem item)
        { Commit(item.Content); e.Handled = true; }
    }
    private void SuggestionKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SuggestionList.SelectedItem is { } item) { Commit(item); e.Handled = true; }
        else if (e.Key == Key.Escape) { SuggestionsPopup.IsOpen = false; ReturnFocus(); e.Handled = true; }
        else if (e.Key == Key.Tab) { SuggestionsPopup.IsOpen = false; ReturnFocus(); }
    }
    private void Commit(object item)
    {
        if (SelectCommand?.CanExecute(item) == true) SelectCommand.Execute(item);
        SuggestionsPopup.IsOpen = false;
        ReturnFocus();
    }
    private void ReturnFocus()
    {
        returningFocus = true;
        QueryInput.Focus(); QueryInput.CaretIndex = QueryInput.Text.Length;
        returningFocus = false;
    }
}
