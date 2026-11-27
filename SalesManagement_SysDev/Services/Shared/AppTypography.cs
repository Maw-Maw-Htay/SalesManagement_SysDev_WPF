using System.Windows;
using System.Windows.Media;

namespace SalesManagement_SysDev;

// Code-built views read the same scale and weights as XAML views.
internal static class AppTypography
{
    public static FontFamily FontFamily => (FontFamily)Application.Current.FindResource("TypographyFontFamily");
    private static (double Size, FontWeight Weight) Read(string role, string weight) =>
        ((double)Application.Current.FindResource($"Typography{role}Size"),
         (FontWeight)Application.Current.FindResource($"Typography{weight}"));

    public static (double Size, FontWeight Weight) PageTitle => Read("PageTitle", "Bold");
    public static (double Size, FontWeight Weight) Section => Read("Section", "SemiBold");
    public static (double Size, FontWeight Weight) CardHeading => Read("CardHeading", "SemiBold");
    public static (double Size, FontWeight Weight) SystemTitle => Read("SystemTitle", "Bold");
    public static (double Size, FontWeight Weight) Navigation => Read("Navigation", "Medium");
    public static (double Size, FontWeight Weight) SidebarCategory => Read("SidebarCategory", "SemiBold");
    public static (double Size, FontWeight Weight) Input => Read("Input", "Regular");
    public static (double Size, FontWeight Weight) Label => Read("Label", "Medium");
    public static (double Size, FontWeight Weight) TableHeader => Read("TableHeader", "SemiBold");
    public static (double Size, FontWeight Weight) Body => Read("Body", "Regular");
    public static (double Size, FontWeight Weight) Button => Read("Button", "SemiBold");
    public static (double Size, FontWeight Weight) Badge => Read("Badge", "Medium");
    public static (double Size, FontWeight Weight) Validation => Read("Validation", "Regular");
}
