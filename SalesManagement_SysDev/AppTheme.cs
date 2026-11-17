using System.Windows;
using System.Windows.Media;

namespace SalesManagement_SysDev;

// Programmatic screens use the same resources as XAML screens.
internal static class AppTheme
{
    private static SolidColorBrush Get(string name) => (SolidColorBrush)Application.Current.FindResource("Theme" + name);

    public static SolidColorBrush Primary => Get(nameof(Primary));
    public static SolidColorBrush Secondary => Get(nameof(Secondary));
    public static SolidColorBrush Accent => Get(nameof(Accent));
    public static SolidColorBrush PaleBlue => Get(nameof(PaleBlue));
    public static SolidColorBrush Text => Get(nameof(Text));
    public static SolidColorBrush SecondaryText => Get(nameof(SecondaryText));
    public static SolidColorBrush Canvas => Get(nameof(Canvas));
    public static SolidColorBrush Surface => Get(nameof(Surface));
    public static SolidColorBrush Sidebar => Get(nameof(Sidebar));
    public static SolidColorBrush SidebarCategory => Get(nameof(SidebarCategory));
    public static SolidColorBrush Subtle => Get(nameof(Subtle));
    public static SolidColorBrush Border => Get(nameof(Border));
    public static SolidColorBrush InputBorder => Get(nameof(InputBorder));
    public static SolidColorBrush Hover => Get(nameof(Hover));
    public static SolidColorBrush Danger => Get(nameof(Danger));
    public static SolidColorBrush DangerText => Get(nameof(DangerText));
    public static SolidColorBrush DangerSurface => Get(nameof(DangerSurface));
    public static SolidColorBrush DangerBorder => Get(nameof(DangerBorder));
    public static SolidColorBrush Success => Get(nameof(Success));
    public static SolidColorBrush SuccessText => Get(nameof(SuccessText));
    public static SolidColorBrush SuccessSurface => Get(nameof(SuccessSurface));
    public static SolidColorBrush Warning => Get(nameof(Warning));
    public static SolidColorBrush WarningText => Get(nameof(WarningText));
    public static SolidColorBrush WarningSurface => Get(nameof(WarningSurface));
    public static SolidColorBrush IconDefault => Get(nameof(IconDefault));
    public static SolidColorBrush IconActive => Get(nameof(IconActive));
    public static SolidColorBrush IconPrimary => Get(nameof(IconPrimary));
    public static SolidColorBrush IconSuccess => Get(nameof(IconSuccess));
    public static SolidColorBrush IconWarning => Get(nameof(IconWarning));
    public static SolidColorBrush IconError => Get(nameof(IconError));
}
