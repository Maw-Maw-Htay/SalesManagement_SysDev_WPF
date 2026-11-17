using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace SalesManagement_SysDev;

public sealed partial class F_OrderManagement
{
    private List<FilterItem> customerCandidates = [];
    private List<FilterItem> employeeCandidates = [];
    private int? selectedCustomerNameId;
    private int? selectedEmployeeNameId;
    private bool settingSuggestion;

    private void CustomerNameTextChanged(object sender, TextChangedEventArgs e)
    {
        if (settingSuggestion) return;
        selectedCustomerNameId = null;
        RefreshSuggestions(customerNameFilter, customerCandidates, customerSuggestions, customerSuggestionPopup);
        SearchTextChanged(sender, e);
    }

    private void EmployeeNameTextChanged(object sender, TextChangedEventArgs e)
    {
        if (settingSuggestion) return;
        selectedEmployeeNameId = null;
        RefreshSuggestions(employeeNameFilter, employeeCandidates, employeeSuggestions, employeeSuggestionPopup);
        SearchTextChanged(sender, e);
    }

    private void RefreshSuggestions(TextBox input, List<FilterItem> candidates, ListBox list, Popup popup)
    {
        var prefix = input.Text.Trim();
        var isNumericCustomerId = ReferenceEquals(input, customerNameFilter)
            && int.TryParse(prefix, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _);
        List<FilterItem> matches = prefix.Length == 0 || isNumericCustomerId ? [] : candidates
            .Where(item => item.Name.Contains(prefix, StringComparison.CurrentCultureIgnoreCase))
            .Take(12).ToList();
        list.ItemsSource = matches;
        popup.PlacementTarget = input;
        popup.Width = input.ActualWidth;
        popup.IsOpen = filterEventsEnabled && input.IsKeyboardFocused && matches.Count > 0;
    }

    private void SuggestionInputGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (settingSuggestion) return;
        if (sender == customerNameFilter)
            RefreshSuggestions(customerNameFilter, customerCandidates, customerSuggestions, customerSuggestionPopup);
        else if (sender == employeeNameFilter)
            RefreshSuggestions(employeeNameFilter, employeeCandidates, employeeSuggestions, employeeSuggestionPopup);
    }

    private void SuggestionInputKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox input) return;
        var list = input == customerNameFilter ? customerSuggestions : employeeSuggestions;
        var popup = input == customerNameFilter ? customerSuggestionPopup : employeeSuggestionPopup;
        if (e.Key == Key.Down && popup.IsOpen)
        {
            list.SelectedIndex = 0;
            list.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            popup.IsOpen = false;
            keywordRefreshTimer.Stop();
            LoadRows();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (popup.IsOpen) popup.IsOpen = false;
            else input.Clear();
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            popup.IsOpen = false;
        }
    }

    private void SuggestionMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list || e.OriginalSource is not DependencyObject source
            || ItemsControl.ContainerFromElement(list, source) is not ListBoxItem { Content: FilterItem item }) return;
        CommitSuggestion(list == customerSuggestions ? customerNameFilter : employeeNameFilter, item);
        e.Handled = true;
    }

    private void SuggestionListKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ListBox list) return;
        var input = list == customerSuggestions ? customerNameFilter : employeeNameFilter;
        var popup = list == customerSuggestions ? customerSuggestionPopup : employeeSuggestionPopup;
        if (e.Key == Key.Enter && list.SelectedItem is FilterItem item)
        {
            CommitSuggestion(input, item);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            popup.IsOpen = false;
            input.Focus();
            e.Handled = true;
        }
    }

    private void CommitSuggestion(TextBox input, FilterItem item)
    {
        settingSuggestion = true;
        try
        {
            input.Text = item.Name;
            input.CaretIndex = input.Text.Length;
            if (input == customerNameFilter) selectedCustomerNameId = item.Id;
            else selectedEmployeeNameId = item.Id;
            customerSuggestionPopup.IsOpen = false;
            employeeSuggestionPopup.IsOpen = false;
            input.Focus();
        }
        finally
        {
            settingSuggestion = false;
        }

        keywordRefreshTimer.Stop();
        UpdateClearFiltersButton();
        LoadRows();
    }
}
