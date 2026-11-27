using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SalesManagement_SysDev;

public sealed class IconResourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string { Length: > 0 } name ? Application.Current.TryFindResource("Icon" + name) : null;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
