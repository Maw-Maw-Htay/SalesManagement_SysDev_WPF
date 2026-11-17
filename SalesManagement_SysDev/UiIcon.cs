using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace SalesManagement_SysDev;

internal static class UiIcon
{
    public const double Small = 16;
    public const double Sidebar = 18;
    public const double Medium = 20;
    public const double Large = 24;
    public const double StrokeWidth = 1.8;
    public const double TextGap = 8;
    public const double ButtonTarget = 36;

    public static LucideIcon Create(string key, double size = Medium, Brush? stroke = null)
    {
        if (size is not (Small or Sidebar or Medium or Large))
            throw new ArgumentOutOfRangeException(nameof(size), "Use a standard icon size: 16, 18, 20, or 24 px.");

        return new LucideIcon
        {
            Data = (Geometry)Application.Current.FindResource($"Icon{key}"),
            Width = size,
            Height = size,
            Stroke = stroke ?? AppTheme.IconDefault,
            StrokeThickness = StrokeWidth,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
    }

    public static Button IconButton(string key, string label, Brush? stroke = null)
    {
        var button = new Button
        {
            Content = Create(key, Small, stroke),
            Style = (Style)Application.Current.FindResource("ThemeIconButton"),
            ToolTip = label
        };
        AutomationProperties.SetName(button, label);
        return button;
    }

    public static StackPanel WithText(string key, string text, Brush? stroke = null, double size = Small)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Create(key, size, stroke));
        row.Children.Add(new TextBlock { Text = text, Margin = new Thickness(TextGap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    public static StackPanel Label(string key, string text, Brush foreground, double? fontSize = null, double iconSize = Medium, Thickness? margin = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = margin ?? new Thickness(0, 0, 0, 10) };
        row.Children.Add(Create(key, iconSize));
        row.Children.Add(new Border { Width = TextGap });
        var heading = fontSize is null ? AppTypography.CardHeading : AppTypography.Section;
        row.Children.Add(new TextBlock { Text = text, Foreground = foreground, FontSize = fontSize ?? heading.Size, FontWeight = fontSize is null ? heading.Weight : AppTypography.Section.Weight, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        return row;
    }
}
