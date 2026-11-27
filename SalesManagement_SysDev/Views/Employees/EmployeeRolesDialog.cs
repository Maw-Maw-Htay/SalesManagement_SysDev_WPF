using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;

namespace SalesManagement_SysDev;

internal sealed class EmployeeRolesDialog : Window
{
    public EmployeeRolesDialog(IReadOnlyList<EmployeeRole> roles, Window? owner)
    {
        Owner = owner; Title = "権限管理（ロール管理）"; ShowInTaskbar = false;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        Width = Math.Min(740, (owner?.ActualWidth > 0 ? owner.ActualWidth : SystemParameters.WorkArea.Width) * .94);
        Height = Math.Min(440, (owner?.ActualHeight > 0 ? owner.ActualHeight : SystemParameters.WorkArea.Height) * .87);

        var root = new DockPanel();
        var header = EmployeeEditorDialog.Header(Title, "現在のロールと社員管理の利用範囲を確認します。");
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var close = new Button
        {
            Content = "閉じる", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(14, 0, 14, 12), Padding = new Thickness(15, 7, 15, 7)
        };
        DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close);

        var panel = new Grid { Margin = new Thickness(14) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        panel.ColumnDefinitions.Add(new ColumnDefinition());
        var rolesCard = EmployeeDetailDialog.DetailCard("ロール一覧");
        var list = new ListBox { ItemsSource = roles, DisplayMemberPath = nameof(EmployeeRole.Label), MaxHeight = 220 };
        ((Panel)rolesCard.Child).Children.Add(list);
        panel.Children.Add(rolesCard);
        var details = new ScrollViewer { Margin = new Thickness(10, 0, 0, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(details, 1); panel.Children.Add(details);
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not EmployeeRole role) return;
            // These are the current employee-service and employee-history authorization rules.
            var availability = role.Name == "管理者" ? "利用可" : "利用不可";
            var card = EmployeeDetailDialog.DetailCard("ロール詳細・機能権限", ("権限ロール", role.Label),
                ("社員管理", $"閲覧・登録・更新・非表示：{availability}"), ("履歴管理", $"社員変更履歴の閲覧：{availability}"));
            ((Panel)card.Child).Children.Add(new TextBlock
            {
                Text = "ロールごとの機能権限は参照専用です。社員へのロール割当は社員詳細の「編集」から変更できます。",
                Foreground = AppTheme.SecondaryText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0)
            });
            details.Content = card;
        };
        list.SelectedIndex = 0;
        root.Children.Add(panel);
        Content = new Border { Background = AppTheme.Surface, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = root };
    }
}
