using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SalesManagement_SysDev;

public partial class InventoryManagementView : UserControl
{
    private CancellationTokenSource? saveCancellation;
    private InventoryManagementViewModel ViewModel => (InventoryManagementViewModel)DataContext;
    public InventoryManagementView() : this(new InventoryManagementViewModel()) { }

    internal InventoryManagementView(InventoryManagementViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        UpdateTabs(null);
    }

    private void InventoryGrid_RowsUpdated(object sender, DataTransferEventArgs e)
    {
        if (e.Property != ItemsControl.ItemsSourceProperty
            || InventoryGrid.ItemsSource is not IReadOnlyList<InventoryRowData> { Count: > 0 } page) return;

        // Row containers can still belong to the previous page while the binding is being applied.
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (InventoryGrid.IsLoaded && ReferenceEquals(InventoryGrid.ItemsSource, page))
                InventoryGrid.ScrollIntoView(page[0]);
        }, DispatcherPriority.Loaded);
    }

    private async void InventoryView_Loaded(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync();
    private void InventoryView_Unloaded(object sender, RoutedEventArgs e)
    {
        saveCancellation?.Cancel();
        ViewModel.StopRequests();
    }
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

    private async void Export_Click(object sender, RoutedEventArgs e) => await SaveCsvAsync(InventorySaveScope.Filtered, "CSV出力");

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button || !ViewModel.CanSave || saveCancellation is not null) return;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void SaveMenu_Opened(object sender, RoutedEventArgs e)
    {
        var idle = saveCancellation is null;
        SaveFiltered.IsEnabled = idle && ViewModel.CanSaveScope(InventorySaveScope.Filtered);
        SaveMarked.IsEnabled = idle && ViewModel.CanSaveScope(InventorySaveScope.Marked);
        SaveAll.IsEnabled = idle && ViewModel.CanSaveScope(InventorySaveScope.All);
    }

    private async void SaveScope_Click(object sender, RoutedEventArgs e)
    {
        var scope = ReferenceEquals(sender, SaveAll) ? InventorySaveScope.All
            : ReferenceEquals(sender, SaveMarked) ? InventorySaveScope.Marked : InventorySaveScope.Filtered;
        await SaveCsvAsync(scope, "在庫一覧の保存");
    }

    private void CancelSave_Click(object sender, RoutedEventArgs e)
    {
        CancelSave.IsEnabled = false;
        saveCancellation?.Cancel();
    }

    private async Task SaveCsvAsync(InventorySaveScope scope, string title)
    {
        if (saveCancellation is not null || !ViewModel.CanSaveScope(scope)) return;
        var dialog = new SaveFileDialog
        {
            Filter = "CSV ファイル (*.csv)|*.csv", DefaultExt = ".csv", AddExtension = true,
            FileName = $"在庫一覧_{DateTime.Today:yyyyMMdd}.csv"
        };
        var owner = Window.GetWindow(this);
        if (dialog.ShowDialog(owner) != true || !ViewModel.CanSaveScope(scope)) return;
        var destination = dialog.FileName;
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".{Guid.NewGuid():N}.tmp");
        using var cancellation = new CancellationTokenSource();
        saveCancellation = cancellation;
        CancelSave.IsEnabled = true;
        CancelSave.Visibility = Visibility.Visible;
        string? message = null;
        var kind = OrderNoticeKind.Success;
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                if (!await ViewModel.SaveAsync(scope, writer, cancellation.Token)) return;
                await writer.FlushAsync(cancellation.Token);
            }
            await Task.Run(() =>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                File.Move(temporary, destination, overwrite: true);
            }, cancellation.Token);
            message = $"CSVファイルを保存しました。\n{destination}";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Inventory CSV failed: {ex}");
            message = "CSVを保存できませんでした。保存先やデータベースの接続を確認してください。";
            kind = OrderNoticeKind.Error;
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            saveCancellation = null;
            CancelSave.Visibility = Visibility.Collapsed;
        }
        if (message is not null && IsLoaded) F_OrderNotice.Show(owner, title, message, kind);
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
        var wrapActions = e.NewSize.Width < 850;
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

    private void OpenDetails(InventoryRowData row)
    {
        if (InventoryService.CanView) new InventoryDetailDialog(row, Window.GetWindow(this)).ShowDialog();
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
