using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace SalesManagement_SysDev;

public partial class InventoryManagementView : UserControl
{
    private InventoryManagementViewModel ViewModel => (InventoryManagementViewModel)DataContext;
    public InventoryManagementView() : this(new InventoryManagementViewModel()) { }

    internal InventoryManagementView(InventoryManagementViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Rows.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewStartingIndex == 0)
                InventoryGrid.ScrollIntoView(e.NewItems![0]);
        };
        UpdateTabs(null);
    }

    private async void InventoryView_Loaded(object sender, RoutedEventArgs e)
    {
        RestoreStockButton.Visibility = InventoryService.CanRestore ? Visibility.Visible : Visibility.Collapsed;
        await ViewModel.LoadAsync();
    }

    private async void RestoreStock_Click(object sender, RoutedEventArgs e)
    {
        if (!InventoryService.CanRestore) return;
        var owner = Window.GetWindow(this);
        RestoreStockButton.IsEnabled = false;
        try
        {
            var rows = await Task.Run(InventoryService.LoadHiddenStocks);
            if (rows.Count == 0)
            {
                F_OrderNotice.Show(owner, "在庫の復元", "非表示の在庫はありません。", OrderNoticeKind.Information);
                return;
            }
            var list = new ListBox { ItemsSource = rows, DisplayMemberPath = nameof(InventoryService.HiddenStock.Label) };
            var dialog = new Window { Title = "非表示在庫を復元", Owner = owner, Width = 520, Height = 360,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
            var panel = new DockPanel { Margin = new Thickness(16) };
            var restore = new Button { Content = "選択した在庫を復元", IsDefault = true, IsEnabled = false,
                Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(restore, Dock.Bottom);
            panel.Children.Add(restore);
            panel.Children.Add(list);
            dialog.Content = panel;
            list.SelectionChanged += (_, _) => restore.IsEnabled = list.SelectedItem != null;
            restore.Click += (_, _) => dialog.DialogResult = true;
            if (dialog.ShowDialog() != true || list.SelectedItem is not InventoryService.HiddenStock stock) return;
            var result = await Task.Run(() => InventoryService.Restore(stock));
            F_OrderNotice.Show(owner, "在庫の復元", result.Message,
                result.Succeeded ? OrderNoticeKind.Success : OrderNoticeKind.Error);
            if (result.Succeeded) await ViewModel.LoadAsync();
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("非表示在庫の取得に失敗しました: {0}", error);
            F_OrderNotice.Show(owner, "在庫の復元", "非表示在庫を取得できませんでした。", OrderNoticeKind.Error);
        }
        finally { RestoreStockButton.IsEnabled = true; }
    }
    private void InventoryView_Unloaded(object sender, RoutedEventArgs e) => ViewModel.StopRequests();
    private async void AllTab_Click(object sender, RoutedEventArgs e) => await SetTabAsync(null);
    private async void NormalTab_Click(object sender, RoutedEventArgs e) => await SetTabAsync(InventoryCondition.Normal);
    private async void LowTab_Click(object sender, RoutedEventArgs e) => await SetTabAsync(InventoryCondition.Low);
    private async void EmptyTab_Click(object sender, RoutedEventArgs e) => await SetTabAsync(InventoryCondition.Empty);
    private async Task SetTabAsync(InventoryCondition? condition)
    {
        UpdateTabs(condition);
        await ViewModel.SelectConditionAsync(condition);
    }
    private void UpdateTabs(InventoryCondition? selected)
    {
        AllTab.IsChecked = selected == null;
        NormalTab.IsChecked = selected == InventoryCondition.Normal;
        LowTab.IsChecked = selected == InventoryCondition.Low;
        EmptyTab.IsChecked = selected == InventoryCondition.Empty;
    }
    private void ClearQuickSearch_Click(object sender, RoutedEventArgs e) { ViewModel.QuickSearch = ""; QuickSearch.Focus(); }
    private void ToggleFilters_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleFilters();
    private void MovementHistory_Click(object sender, RoutedEventArgs e) => BusinessHistoryWindow.OpenFor(this, "在庫管理");
    private async void ClearConditions_Click(object sender, RoutedEventArgs e) => await ViewModel.ClearConditionsAsync();
    private async void Reload_Click(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync();
    private async void Previous_Click(object sender, RoutedEventArgs e) => await ViewModel.GoToPageAsync(ViewModel.CurrentPage - 1);
    private async void Next_Click(object sender, RoutedEventArgs e) => await ViewModel.GoToPageAsync(ViewModel.CurrentPage + 1);

    private async void PageNumber_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (int.TryParse(PageNumber.Text, out var page)) await ViewModel.GoToPageAsync(page);
        PageNumber.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }

    private async void InventoryGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (!Enum.TryParse<InventorySort>(e.Column.SortMemberPath, out var sort)) return;
        var descending = e.Column.SortDirection == ListSortDirection.Ascending;
        foreach (var column in InventoryGrid.Columns) column.SortDirection = null;
        e.Column.SortDirection = descending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        await ViewModel.SortAsync(sort, descending);
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanExport) return;
        var dialog = new SaveFileDialog
        {
            Filter = "CSV ファイル (*.csv)|*.csv", DefaultExt = ".csv", AddExtension = true,
            FileName = $"在庫一覧_{DateTime.Today:yyyyMMdd}.csv"
        };
        var owner = Window.GetWindow(this);
        if (dialog.ShowDialog(owner) != true || !ViewModel.CanExport) return;
        var destination = dialog.FileName;
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
                await ViewModel.ExportAsync(writer);
            File.Move(temporary, destination, overwrite: true);
            F_OrderNotice.Show(owner, "CSV出力", "絞り込み結果をCSVファイルに出力しました。", OrderNoticeKind.Success);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Inventory CSV failed: {ex}");
            F_OrderNotice.Show(owner, "CSV出力", "CSVを保存できませんでした。保存先やデータベースの接続を確認してください。", OrderNoticeKind.Error);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void InventoryView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SummaryCards.Columns = e.NewSize.Width < 535 && e.NewSize.Height >= 520 ? 2 : 4;
        AdvancedFilters.Columns = e.NewSize.Width < 610 ? 2 : 3;
        FiltersScroll.MaxHeight = Math.Clamp(e.NewSize.Height - 365, 44, 130);
    }
    private void QuickSearchRow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wrapActions = e.NewSize.Width < 430;
        Grid.SetRow(SearchActions, wrapActions ? 1 : 0);
        Grid.SetColumn(SearchActions, wrapActions ? 0 : 1);
        Grid.SetColumnSpan(QuickSearchField, wrapActions ? 2 : 1);
        SearchActions.Margin = new Thickness(0, wrapActions ? 6 : 0, 0, 0);
    }
    private void StatusFiltersRow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wrapActions = e.NewSize.Width < 710;
        Grid.SetRow(StatusActions, wrapActions ? 1 : 0);
        Grid.SetColumn(StatusActions, wrapActions ? 0 : 1);
        Grid.SetColumnSpan(StatusActions, wrapActions ? 2 : 1);
        StatusActions.Margin = new Thickness(0, wrapActions ? 6 : 0, 0, 0);
    }
    private void InventoryGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        for (DependencyObject? node = source; node != null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is ButtonBase) return;
            if (node is DataGridRow { Item: InventoryRowData row }) { OpenDetails(row); return; }
            if (ReferenceEquals(node, InventoryGrid)) return;
        }
    }

    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: InventoryRowData row }) OpenDetails(row);
    }

    private async void OpenDetails(InventoryRowData row)
    {
        if (InventoryService.CanView && new InventoryDetailDialog(row, Window.GetWindow(this)).ShowDialog() == true)
            await ViewModel.LoadAsync();
    }
}

internal sealed class InventoryDetailDialog : Window
{
    public InventoryDetailDialog(InventoryRowData row, Window? owner)
    {
        Title = "在庫詳細"; Width = Math.Min(760, SystemParameters.WorkArea.Width * .9); MinWidth = Math.Min(480, Width);
        Height = Math.Min(570, SystemParameters.WorkArea.Height * .85);
        MaxHeight = SystemParameters.WorkArea.Height * .9; WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        Owner = owner; ResizeMode = ResizeMode.CanResize; Background = AppTheme.Canvas; ShowInTaskbar = false;
        var root = new DockPanel();
        var header = new Border { Background = AppTheme.Primary, Padding = new Thickness(22, 16, 22, 16) };
        header.Child = new TextBlock { Text = $"在庫詳細 — {row.DisplayCode}", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20) };
        if (InventoryService.CanMaintain(row)) AddStockActions(footer, row);
        var close = new Button { Content = "閉じる", MinWidth = 100, IsDefault = true, IsCancel = true }; footer.Children.Add(close);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var content = new StackPanel { Margin = new Thickness(16) };
        var fields = new UniformGrid { Columns = Width < 620 ? 1 : 2 };
        fields.Children.Add(Card("商品情報", ("商品コード", row.DisplayCode), ("JANコード", string.IsNullOrEmpty(row.ProductCode) ? "未登録" : row.ProductCode),
            ("商品名", row.ProductName), ("メーカー", row.Maker), ("カテゴリ", row.Classification), ("型番", row.ModelNumber), ("色", row.Color)));
        fields.Children.Add(Card("在庫情報", ("現在庫数", row.QuantityText), ("安全在庫数", row.SafetyStockText), ("在庫状態", row.ConditionDetail),
            ("最終更新日時", row.LastUpdatedText), ("管理状態", row.ManagementLabel)));
        content.Children.Add(fields);
        content.Children.Add(Card("最近の在庫移動", ("記録状況", "在庫の増減前後や操作担当者を確認できる記録はありません。")));
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = root;
        close.Click += (_, _) => Close();
    }

    private void AddStockActions(StackPanel footer, InventoryRowData row)
    {
        var quantity = new TextBox { Text = row.Quantity!.Value.ToString(), Width = 95, MaxLength = 10,
            Margin = new Thickness(0, 0, 8, 0) };
        System.Windows.Automation.AutomationProperties.SetName(quantity, "更新後の在庫数");
        var save = new Button { Content = "在庫数を更新", Margin = new Thickness(0, 0, 8, 0) };
        var hide = new Button { Content = "非表示", Margin = new Thickness(0, 0, 8, 0) };
        footer.Children.Add(quantity);
        footer.Children.Add(save);
        footer.Children.Add(hide);
        save.Click += async (_, _) =>
        {
            if (!int.TryParse(quantity.Text, out var stock) || stock < 0)
            {
                ShowResult((false, "在庫数は0以上の整数で入力してください。"));
                return;
            }
            await ApplyAsync(() => InventoryService.UpdateQuantity(row, stock));
        };
        hide.Click += async (_, _) =>
        {
            if (F_OrderNotice.Confirm(this, "在庫の非表示", $"{row.DisplayCode} の在庫を非表示にしますか？\n\n復元するまで、この商品の注文確定と棚入れ確定ができなくなります。復元は管理者が行えます。", "非表示にする"))
                await ApplyAsync(() => InventoryService.Hide(row));
        };

        Closing += (_, e) => e.Cancel = !footer.IsEnabled;

        async Task ApplyAsync(Func<(bool Succeeded, string Message)> operation)
        {
            footer.IsEnabled = false;
            (bool Succeeded, string Message) result;
            try { result = await Task.Run(operation); }
            finally { footer.IsEnabled = true; }
            ShowResult(result);
        }
    }

    private void ShowResult((bool Succeeded, string Message) result)
    {
        F_OrderNotice.Show(this, "在庫管理", result.Message,
            result.Succeeded ? OrderNoticeKind.Success : OrderNoticeKind.Error);
        if (result.Succeeded && IsVisible) DialogResult = true;
    }

    private static Border Card(string title, params (string Label, string Value)[] fields)
    {
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = AppTheme.Primary, Margin = new Thickness(0, 0, 0, 14) });
        foreach (var field in fields)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = field.Label, Foreground = AppTheme.SecondaryText, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            var value = new TextBlock { Text = field.Value, Foreground = AppTheme.Text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Medium };
            Grid.SetColumn(value, 1); row.Children.Add(value); panel.Children.Add(row);
        }
        return new Border { Background = AppTheme.Surface, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(0, 0, 8, 8), Child = panel };
    }
}
