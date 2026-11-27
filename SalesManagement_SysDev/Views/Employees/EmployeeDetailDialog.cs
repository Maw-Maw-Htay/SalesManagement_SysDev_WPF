using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;

namespace SalesManagement_SysDev;

internal enum EmployeeDetailAction { None, Edit, Disable, History }

internal sealed class EmployeeDetailDialog : Window
{
    public EmployeeDetailAction NextAction { get; private set; }
    public EmployeeDetailDialog(EmployeeDetail detail, bool canManage, Window? owner)
    {
        var employee = detail.Employee;
        Owner = owner; Title = $"社員詳細 — {employee.Code}"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
        WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        var width = owner?.ActualWidth > 0 ? owner.ActualWidth : SystemParameters.WorkArea.Width;
        var height = owner?.ActualHeight > 0 ? owner.ActualHeight : SystemParameters.WorkArea.Height;
        Width = Math.Min(800, width * .94); MinWidth = Math.Min(590, Width);
        MaxHeight = Math.Min(height * .87, SystemParameters.WorkArea.Height * .9); Height = Math.Min(480, MaxHeight);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var loginState = employee.CanLogin ? "利用可能" : "無効";
        root.Children.Add(EmployeeEditorDialog.Header("社員詳細", $"{employee.Code}　{employee.Name}　／　{employee.ManagementLabel}　／　ログイン{loginState}"));
        var tabs = new TabControl { Margin = new Thickness(14), Background = AppTheme.Surface, BorderThickness = new Thickness(0) };
        tabs.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/SalesManagement_SysDev;component/Resources/EmployeeDetail.xaml", UriKind.Relative)
        });
        AddTab(tabs, "基本情報", DetailCard("基本情報", ("社員番号", employee.Code), ("社員名", employee.Name),
            ("所属営業所", employee.OfficeName), ("電話番号", employee.Phone), ("入社日", employee.HireDate.ToString("yyyy/MM/dd")),
            ("管理状態", employee.ManagementLabel), ("非表示理由", employee.HiddenReason ?? "—")));
        AddTab(tabs, "権限情報", DetailCard("アカウント・権限", ("ログインID", employee.LoginId),
            ("権限ロール", employee.RoleLabel), ("ログイン状態", loginState)));
        var historyCard = DetailCard("所属・権限の変更履歴");
        var historyPanel = (StackPanel)historyCard.Child;
        if (detail.Assignments.Count == 0) historyPanel.Children.Add(new TextBlock { Text = "記録済みの変更履歴はありません。", Foreground = AppTheme.SecondaryText });
        else foreach (var item in detail.Assignments)
        {
            historyPanel.Children.Add(new TextBlock { Text = $"{item.RecordedAt.ToOffset(OrderHistoryService.JapanOffset):yyyy/MM/dd HH:mm}　{item.Operation}　実行者：{item.ActorName}", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
            historyPanel.Children.Add(new TextBlock { Text = $"営業所：{item.BeforeOffice} → {item.AfterOffice}　権限：{EmployeeService.RoleLabel(item.BeforeRole)} → {EmployeeService.RoleLabel(item.AfterRole)}", Foreground = AppTheme.SecondaryText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        }
        var history = new Button { Content = "履歴管理で開く", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        history.Click += (_, _) => { NextAction = EmployeeDetailAction.History; Close(); };
        historyPanel.Children.Add(history);
        AddTab(tabs, "変更履歴", historyCard);
        Grid.SetRow(tabs, 1); root.Children.Add(tabs);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(18, 10, 20, 14) };
        if (canManage && employee.ManagementFlag == EmployeeService.ActiveFlag)
        {
            if (employee.Id != AppSession.Employee?.EmId)
            {
                var hide = new Button { Content = "アカウント無効化", Padding = new Thickness(13, 7, 13, 7), Margin = new Thickness(0, 0, 8, 0), Foreground = AppTheme.DangerText, BorderBrush = AppTheme.Danger };
                hide.Click += (_, _) => { NextAction = EmployeeDetailAction.Disable; Close(); }; actions.Children.Add(hide);
            }
            var edit = new Button { Content = "編集", Padding = new Thickness(13, 7, 13, 7), Margin = new Thickness(0, 0, 8, 0), Background = AppTheme.Primary, Foreground = Brushes.White, BorderBrush = AppTheme.Primary };
            edit.Click += (_, _) => { NextAction = EmployeeDetailAction.Edit; Close(); }; actions.Children.Add(edit);
        }
        actions.Children.Add(new Button { Content = "閉じる", IsCancel = true, Padding = new Thickness(13, 7, 13, 7) });
        var footer = new Border { BorderBrush = AppTheme.Border, BorderThickness = new Thickness(0, 1, 0, 0), Child = actions };
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        Content = new Border { Background = AppTheme.Surface, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = root };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
    }

    private static void AddTab(TabControl tabs, string title, Border card)
    {
        card.Margin = new Thickness(10);
        tabs.Items.Add(new TabItem
        {
            Header = title,
            Content = new ScrollViewer { Content = card, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }
        });
    }

    internal static Border DetailCard(string title, params (string Label, string Value)[] values)
    {
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = AppTheme.Primary, Margin = new Thickness(0, 0, 0, 12) });
        foreach (var (label, value) in values)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = label, Foreground = AppTheme.SecondaryText, VerticalAlignment = VerticalAlignment.Center });
            var text = new TextBlock { Text = value, Foreground = AppTheme.Text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 1); row.Children.Add(text); panel.Children.Add(row);
        }
        return new Border { Background = AppTheme.Surface, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Child = panel };
    }
}
