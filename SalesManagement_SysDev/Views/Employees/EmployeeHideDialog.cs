using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;

namespace SalesManagement_SysDev;

internal sealed class EmployeeHideDialog : Window
{
    private readonly TextBox reason = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 72, MaxLength = 500, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    public string? Reason => string.IsNullOrWhiteSpace(reason.Text) ? null : reason.Text.Trim();
    public EmployeeHideDialog(EmployeeRecord employee, Window? owner)
    {
        Owner = owner; Title = "アカウント無効化の確認"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
        WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        var width = owner?.ActualWidth > 0 ? owner.ActualWidth : SystemParameters.WorkArea.Width;
        var height = owner?.ActualHeight > 0 ? owner.ActualHeight : SystemParameters.WorkArea.Height;
        Width = Math.Min(540, width * .9); MaxWidth = width * .92; MaxHeight = height * .85; SizeToContent = SizeToContent.Height;
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Border { Background = AppTheme.Primary, Padding = new Thickness(20, 0, 20, 0) };
        header.Child = new TextBlock { Text = Title, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
        root.Children.Add(header);
        var body = new StackPanel { Margin = new Thickness(22, 18, 22, 16) };
        body.Children.Add(new TextBlock { Text = "この社員のアカウントを無効化しますか？", FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        body.Children.Add(new TextBlock { Text = $"社員番号：{employee.Code}\n社員名：{employee.Name}\nログイン状態：{(employee.CanLogin ? "利用可能" : "無効")} → 無効\n管理状態：{employee.ManagementLabel} → 非表示", Background = AppTheme.Canvas, Padding = new Thickness(12), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "無効化すると、この社員はシステムにログインできなくなります。社員データと取引履歴は保持されます。",
            Foreground = AppTheme.WarningText, Background = AppTheme.Canvas, Padding = new Thickness(10), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        body.Children.Add(new TextBlock { Text = "非表示理由（必須）", FontWeight = FontWeights.Medium, Margin = new Thickness(0, 14, 0, 5) });
        reason.Padding = new Thickness(9); reason.BorderBrush = AppTheme.InputBorder; body.Children.Add(reason);
        var reasonError = new TextBlock { Text = "非表示理由を入力してください。", Foreground = AppTheme.DangerText, FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
        body.Children.Add(reasonError);
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 10, 20, 14) };
        actions.Children.Add(new Button { Content = "キャンセル", IsCancel = true, Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) });
        var hide = new Button { Content = "無効化する", IsEnabled = false, Padding = new Thickness(14, 7, 14, 7), Background = AppTheme.Danger, Foreground = Brushes.White, BorderBrush = AppTheme.Danger };
        hide.Click += (_, _) => { if (EmployeeService.ValidateHideReason(Reason) == null) DialogResult = true; }; actions.Children.Add(hide);
        reason.TextChanged += (_, _) =>
        {
            var error = EmployeeService.ValidateHideReason(Reason);
            hide.IsEnabled = error == null;
            reasonError.Text = error ?? "";
            reasonError.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
        };
        var footer = new Border { BorderBrush = AppTheme.Border, BorderThickness = new Thickness(0, 1, 0, 0), Child = actions };
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        Content = new Border { Background = AppTheme.Surface, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = root };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
    }
}
