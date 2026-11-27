using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace SalesManagement_SysDev;

internal static class OrderManagementPalette
{
    public static Brush Text => AppTheme.Text;
    public static Brush SecondaryText => AppTheme.SecondaryText;
    public static Brush Surface => AppTheme.Surface;
    public static Brush SurfaceAlt => AppTheme.Canvas;
    public static Brush Input => AppTheme.Surface;
    public static Brush Divider => AppTheme.Border;
    public static Brush Hover => AppTheme.Hover;
    public static Brush Selection => AppTheme.PaleBlue;
    public static Brush Focus => AppTheme.Accent;
    public static Brush Orange => AppTheme.WarningText;
    public static Brush OrangeSurface => AppTheme.WarningSurface;
    public static Brush Green => AppTheme.SuccessText;
    public static Brush GreenSurface => AppTheme.SuccessSurface;
    public static Brush Danger => AppTheme.DangerText;
    public static Brush DangerSurface => AppTheme.DangerSurface;
}

public sealed class OrderHistoryDisplayRow(OrderOperationHistory entry)
{
    public OrderOperationHistory Entry { get; } = entry;
    public string Timestamp => Entry.RecordedAt.ToOffset(OrderHistoryService.JapanOffset).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    public string Operation => Entry.Operation;
    public string OrderNumber => $"ORD-{Entry.OrderId:000000}";
    public string Customer => Entry.CustomerName;
    public string Details => Entry.Details;
    public string ChangeSummary => OrderHistoryAuditParser.Summary(Entry);
    public IReadOnlyList<OrderHistoryComparison> Comparisons => OrderHistoryAuditParser.Comparisons(Entry);
    public IReadOnlyList<OrderHistoryProductChange> ProductChanges => OrderHistoryAuditParser.ProductChanges(Entry);
    public string FlagTransition => string.Join("\n", Comparisons.Where(row => row.Field is "受注状態" or "管理状態")
        .Select(row => $"{row.Field}：{row.Before} → {row.After}"));
    public string Actor => Entry.ActorName + (Entry.ActorOfficeName.Length == 0 ? "" : $"（{Entry.ActorOfficeName}）");
    public Brush OperationForeground => Operation switch
    {
        OrderHistoryOperations.Created => OrderManagementPalette.Green, OrderHistoryOperations.Updated => OrderManagementPalette.Focus,
        OrderHistoryOperations.Confirmed => OrderManagementPalette.Green, OrderHistoryOperations.Hidden or OrderHistoryOperations.Deleted => OrderManagementPalette.Danger,
        OrderHistoryOperations.Restored => OrderManagementPalette.Text, _ => OrderManagementPalette.SecondaryText
    };
    public Brush OperationBackground => Operation switch
    {
        OrderHistoryOperations.Created => OrderManagementPalette.GreenSurface, OrderHistoryOperations.Updated => OrderManagementPalette.Selection,
        OrderHistoryOperations.Confirmed => OrderManagementPalette.GreenSurface, OrderHistoryOperations.Hidden or OrderHistoryOperations.Deleted => OrderManagementPalette.DangerSurface,
        OrderHistoryOperations.Restored => OrderManagementPalette.Hover, _ => OrderManagementPalette.Input
    };
}

public sealed partial class F_OrderManagement
{
    private void ConfigureHistoryControls()
    {
        historyOperationFilter.ItemsSource = new[] { "すべての操作種別" }.Concat(OrderHistoryOperations.All).ToList();
        historyOperationFilter.SelectedIndex = 0;
        historyOperationFilter.SelectionChanged += FilterSelectionChanged;
        historySearch.TextChanged += SearchTextChanged;
        historySearch.KeyDown += SearchKeyDown;
        historyGrid.ColumnHeaderStyle = grid.ColumnHeaderStyle;
        historyGrid.CellStyle = grid.CellStyle;
        historyGrid.RowStyle = grid.RowStyle;
        historyGrid.FontSize = AppTypography.Body.Size;
        historyGrid.FontWeight = AppTypography.Body.Weight;
        historyGrid.ColumnHeaderHeight = 40;
        historyGrid.MinRowHeight = 48;
        historyGrid.RowBackground = OrderManagementPalette.Surface;
        historyGrid.AlternatingRowBackground = OrderManagementPalette.SurfaceAlt;
        historyGrid.EnableRowVirtualization = true;
        historyGrid.EnableColumnVirtualization = true;
        var historyRowStyle = new Style(typeof(DataGridRow), historyGrid.RowStyle);
        var selectedTrigger = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, OrderManagementPalette.Selection));
        selectedTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, OrderManagementPalette.Focus));
        historyRowStyle.Triggers.Add(selectedTrigger);
        historyGrid.RowStyle = historyRowStyle;

        AddHistoryTextColumn("日時", nameof(OrderHistoryDisplayRow.Timestamp), 1.1, 145, false);
        var badge = new FrameworkElementFactory(typeof(Border));
        badge.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        badge.SetValue(Border.PaddingProperty, new Thickness(8, 4, 8, 4));
        badge.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 6, 8, 6));
        badge.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        badge.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        badge.SetBinding(Border.BackgroundProperty, new Binding(nameof(OrderHistoryDisplayRow.OperationBackground)));
        var badgeText = new FrameworkElementFactory(typeof(TextBlock));
        badgeText.SetBinding(TextBlock.TextProperty, new Binding(nameof(OrderHistoryDisplayRow.Operation)));
        badgeText.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(OrderHistoryDisplayRow.OperationForeground)));
        badgeText.SetValue(TextBlock.FontSizeProperty, AppTypography.Badge.Size);
        badgeText.SetValue(TextBlock.FontWeightProperty, AppTypography.Badge.Weight);
        badge.AppendChild(badgeText);
        historyGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "操作種別", SortMemberPath = nameof(OrderHistoryDisplayRow.Operation),
            CellTemplate = new DataTemplate { VisualTree = badge }, Width = new DataGridLength(1.2, DataGridLengthUnitType.Star), MinWidth = 120
        });
        var orderLink = new FrameworkElementFactory(typeof(Button));
        orderLink.SetBinding(ContentControl.ContentProperty, new Binding(nameof(OrderHistoryDisplayRow.OrderNumber)));
        orderLink.SetValue(Control.ForegroundProperty, OrderManagementPalette.Text);
        orderLink.SetValue(Control.BackgroundProperty, OrderManagementPalette.Hover);
        orderLink.SetValue(Control.BorderBrushProperty, OrderManagementPalette.Divider);
        orderLink.SetValue(Control.PaddingProperty, new Thickness(6, 3, 6, 3));
        orderLink.SetValue(Control.FontSizeProperty, AppTypography.Button.Size);
        orderLink.SetValue(Control.FontWeightProperty, AppTypography.Button.Weight);
        orderLink.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 6, 8, 6));
        orderLink.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        orderLink.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        orderLink.SetValue(FrameworkElement.ToolTipProperty, "現在の受注詳細を開く");
        orderLink.AddHandler(System.Windows.Controls.Button.ClickEvent, new RoutedEventHandler(HistoryOrderClick));
        historyGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "受注番号", SortMemberPath = "Entry.OrderId", CellTemplate = new DataTemplate { VisualTree = orderLink },
            Width = new DataGridLength(1.1, DataGridLengthUnitType.Star), MinWidth = 120
        });
        AddHistoryTextColumn("顧客名", nameof(OrderHistoryDisplayRow.Customer), 1.25, 130, false);
        AddHistoryTextColumn("変更内容", nameof(OrderHistoryDisplayRow.ChangeSummary), 2.2, 210, false);
        AddHistoryTextColumn("実行担当者", nameof(OrderHistoryDisplayRow.Actor), 1.2, 130, false);
        var detailButton = new FrameworkElementFactory(typeof(Button));
        detailButton.SetValue(ContentControl.ContentProperty, "詳細を見る");
        detailButton.SetValue(FrameworkElement.HeightProperty, 30d);
        detailButton.SetValue(Control.ForegroundProperty, OrderManagementPalette.Text);
        detailButton.SetValue(Control.BackgroundProperty, OrderManagementPalette.Input);
        detailButton.SetValue(Control.BorderBrushProperty, OrderManagementPalette.Divider);
        detailButton.SetValue(Control.PaddingProperty, new Thickness(8, 3, 8, 3));
        detailButton.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 4, 6, 4));
        detailButton.SetValue(FrameworkElement.ToolTipProperty, "保存された変更内容の詳細を開く");
        detailButton.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(HistoryDetailClick));
        historyGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "詳細", CellTemplate = new DataTemplate { VisualTree = detailButton },
            Width = new DataGridLength(0.9, DataGridLengthUnitType.Star), MinWidth = 96, CanUserSort = false
        });
        historyGrid.PreviewMouseDoubleClick += HistoryRowDoubleClick;
    }

    private void AddHistoryTextColumn(string header, string property, double weight, double minimum, bool wrap = true)
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.FontSizeProperty, AppTypography.Body.Size));
        style.Setters.Add(new Setter(TextBlock.FontWeightProperty, AppTypography.Body.Weight));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(8, 8, 8, 8)));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.MaxHeightProperty, 96d));
        style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, wrap ? TextWrapping.Wrap : TextWrapping.NoWrap));
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(property)));
        if (property == nameof(OrderHistoryDisplayRow.ChangeSummary))
            style.Setters.Add(new Setter(TextBlock.FontSizeProperty, 13d));
        if (property == nameof(OrderHistoryDisplayRow.Timestamp))
        {
            style.Setters.Add(new Setter(TextBlock.FontSizeProperty, AppTypography.Body.Size));
            style.Setters.Add(new Setter(TextBlock.ForegroundProperty, OrderManagementPalette.Text));
        }
        historyGrid.Columns.Add(new DataGridTextColumn
        {
            Header = header, Binding = new Binding(property), ElementStyle = style,
            Width = new DataGridLength(weight, DataGridLengthUnitType.Star), MinWidth = minimum, IsReadOnly = true
        });
    }

    private void UpdateHistoryViewState()
    {
        grid.Visibility = isHistoryView ? Visibility.Collapsed : Visibility.Visible;
        historyPanel.Visibility = isHistoryView ? Visibility.Visible : Visibility.Collapsed;
        orderEmptyState.Visibility = !isHistoryView && grid.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        deliveryDateField.Visibility = isHistoryView ? Visibility.Collapsed : Visibility.Visible;
        employeeFilterLabel.Text = isHistoryView ? "実行担当者" : "担当者";
        pendingBadge.Visibility = isHistoryView ? Visibility.Collapsed : Visibility.Visible;
        confirmedBadge.Visibility = isHistoryView ? Visibility.Collapsed : Visibility.Visible;
        hiddenBadge.Visibility = isHistoryView ? Visibility.Collapsed : Visibility.Visible;
        allBadge.Visibility = isHistoryView ? Visibility.Collapsed : Visibility.Visible;
        selectionCount.Visibility = isHistoryView ? Visibility.Collapsed : Visibility.Visible;
        orderActions.Visibility = isHistoryView ? Visibility.Collapsed : Visibility.Visible;
        resultCount.Visibility = isHistoryView ? Visibility.Collapsed : Visibility.Visible;
        dateRangeLabel.Text = isHistoryView ? "操作期間（履歴日時）" : "受注期間（受注日）";
        listInteractionHint.Text = isHistoryView ? "行をダブルクリックで履歴詳細、受注番号で受注詳細を表示" : "行をダブルクリックすると詳細を表示";
        ReflowFilterFields(filterDetails.ActualWidth);
        UpdateSelectionSummary();
    }

    private void HistoryRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source
            || IsInside<ButtonBase>(source)
            || ItemsControl.ContainerFromElement(historyGrid, source) is not DataGridRow { Item: OrderHistoryDisplayRow row }) return;
        e.Handled = true;
        ShowHistoryEntry(row);
    }

    private void HistoryOrderClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is OrderHistoryDisplayRow row) OpenHistoryOrder(row);
        e.Handled = true;
    }

    private void HistoryDetailClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is OrderHistoryDisplayRow row) ShowHistoryEntry(row);
        e.Handled = true;
    }

    private void OpenHistoryOrder(OrderHistoryDisplayRow row)
    {
        try
        {
            var snapshot = LoadOrderDetailSnapshot(new OrderListRow { 受注番号 = row.Entry.OrderId });
            if (snapshot == null)
            {
                F_OrderNotice.Show(NavigationOwner ?? this, "受注詳細", "受注が削除されているか、現在の営業所では参照できません。操作時の記録は履歴詳細で確認できます。", OrderNoticeKind.Information);
                return;
            }
            new F_OrderDetail(snapshot, NavigationOwner ?? this, readOnly: true).ShowDialog();
            LoadRows();
        }
        catch (Exception ex)
        { F_OrderNotice.Show(NavigationOwner ?? this, "受注詳細エラー", ex.Message, OrderNoticeKind.Error); }
    }

    private void ShowHistoryEntry(OrderHistoryDisplayRow row)
    {
        new F_OrderHistoryDetail(row.Entry, NavigationOwner ?? this, () => OpenHistoryOrder(row)).ShowDialog();
    }
}
