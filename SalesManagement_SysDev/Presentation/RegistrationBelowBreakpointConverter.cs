using System.Globalization;
using System.Windows.Data;

namespace SalesManagement_SysDev;

public sealed class RegistrationBelowBreakpointConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double size && size > 0 && size < double.Parse(parameter.ToString()!, CultureInfo.InvariantCulture);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
