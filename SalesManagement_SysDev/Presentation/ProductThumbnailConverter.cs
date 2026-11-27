using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace SalesManagement_SysDev;

public sealed class ProductThumbnailConverter : IValueConverter
{
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg"];

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int productId || productId <= 0) return null;

        var fileStem = Path.Combine(AppContext.BaseDirectory, "ProductImages", FormattableString.Invariant($"P-{productId:0000}"));
        foreach (var extension in Extensions)
        {
            var path = fileStem + extension;
            if (!File.Exists(path)) continue;

            try
            {
                using var stream = File.OpenRead(path);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 192;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or FileFormatException or NotSupportedException)
            {
                // Try another supported file before showing the placeholder.
            }
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
