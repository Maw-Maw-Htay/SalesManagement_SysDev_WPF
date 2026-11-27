using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SalesManagement_SysDev;

public partial class OrderManagementView : UserControl
{
    private void BusinessHistory_Click(object sender, RoutedEventArgs e) => BusinessHistoryWindow.OpenFor(this, "注文管理");

    private readonly HashSet<DatePicker> invalidDateInputs = [];
    private ChumonManagementViewModel ViewModel => (ChumonManagementViewModel)DataContext;

    public OrderManagementView()
    {
        InitializeComponent();
        DataContext = new ChumonManagementViewModel();
        UpdateTabVisuals(0);
    }

    private void All_Click(object sender, RoutedEventArgs e) => SelectTab(0);
    private void Pending_Click(object sender, RoutedEventArgs e) => SelectTab(1);
    private void Confirmed_Click(object sender, RoutedEventArgs e) => SelectTab(2);
    private void Hidden_Click(object sender, RoutedEventArgs e) => SelectTab(3);
    private void SelectTab(int tab)
    {
        ViewModel.SelectTab(tab);
        UpdateTabVisuals(tab);
    }
    private void UpdateTabVisuals(int active)
    {
        var tabs = new[] { AllTab, PendingTab, ConfirmedTab, HiddenTab };
        for (var i = 0; i < tabs.Length; i++)
            tabs[i].IsChecked = i == active;
    }

    private void QuickSearchRow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wrapActions = e.NewSize.Width < 465;
        Grid.SetRow(SearchActions, wrapActions ? 1 : 0);
        Grid.SetColumn(SearchActions, wrapActions ? 0 : 1);
        Grid.SetColumnSpan(QuickSearchField, wrapActions ? 2 : 1);
        SearchActions.Margin = new Thickness(0, wrapActions ? 6 : 0, 0, 0);
    }

    private void OrderView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        AdvancedFields.Columns = e.NewSize.Width < 690 ? 2 : 4;
        // Reserve space for the list and its actions when the window is short.
        AdvancedFieldsScroll.MaxHeight = Math.Clamp(e.NewSize.Height - 420, 54, 214);
        ListHelp.Visibility = e.NewSize.Height < 470 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ListHeader_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wrapActions = e.NewSize.Width < 520;
        Grid.SetRow(BulkActions, wrapActions ? 1 : 0);
        Grid.SetColumn(BulkActions, wrapActions ? 0 : 1);
        Grid.SetColumnSpan(BulkActions, wrapActions ? 2 : 1);
        BulkActions.Margin = new Thickness(0, wrapActions ? 8 : 0, 0, 0);
    }

    private void ClearQuickSearch_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.QuickSearchText = "";
        QuickSearch.Focus();
    }

    private void ToggleFilters_Click(object sender, RoutedEventArgs e) => ViewModel.IsFiltersExpanded = !ViewModel.IsFiltersExpanded;
    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        invalidDateInputs.Clear();
        UpdateDateInputError();
        ViewModel.ClearConditions();
    }

    private void FilterDateValidationError(object sender, DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        if (sender is DatePicker picker) invalidDateInputs.Add(picker);
        UpdateDateInputError();
    }

    private void FilterDateChanged(object sender, SelectionChangedEventArgs e) => ClearDateInputError(sender);
    private void FilterDateEditing(object sender, TextCompositionEventArgs e) => ClearDateInputError(sender);

    private void ClearDateInputError(object sender)
    {
        if (sender is DatePicker picker && invalidDateInputs.Remove(picker)) UpdateDateInputError();
    }

    private void UpdateDateInputError()
    {
        var labels = invalidDateInputs.Select(System.Windows.Automation.AutomationProperties.GetName);
        DateInputError.Text = invalidDateInputs.Count == 0 ? "" : $"{string.Join("、", labels)}：日付を正しく入力してください。";
        DateInputError.Visibility = invalidDateInputs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsBusy) await ViewModel.LoadAsync();
    }
    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox) ViewModel.ToggleSelectVisible(checkBox.IsChecked == true);
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanConfirm) return;
        var selected = ViewModel.Rows.Where(x => x.IsSelected && x.IsEligible).ToList();
        if (selected.Count == 0) return;
        var summary = string.Join("\n", selected.Take(5).Select(x =>
            $"{x.OrderNumber}　{x.Customer}　{x.LineCount} 品目 / {x.TotalQuantity:N0} 個　{x.Total?.ToString("¥#,##0") ?? "金額記録なし"}"));
        if (selected.Count > 5) summary += $"\nほか {selected.Count - 5:N0} 件";
        summary += "\n\n注文確定後、在庫数が減少し、出庫情報が作成されます。";
        if (!F_OrderNotice.Confirm(Window.GetWindow(this), "注文確定の確認",
                $"選択した {selected.Count:N0} 件の注文を確定します。よろしいですか？\n\n{summary}", "注文を確定")) return;
        var result = await ViewModel.ConfirmSelectedAsync();
        var kind = result.Failure == 0 ? OrderNoticeKind.Success : result.Success > 0 ? OrderNoticeKind.Warning : OrderNoticeKind.Error;
        F_OrderNotice.Show(Window.GetWindow(this), "注文確定の結果", result.Result, kind);
    }

    private async void Hide_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanHide) return;
        var selected = ViewModel.Rows.Where(x => x.IsSelected && x.ManagementFlag == 0).ToList();
        if (selected.Count == 0) return;
        var owner = Window.GetWindow(this);
        var dialog = new OrderHideConfirmationDialog(selected, owner);
        if (dialog.ShowDialog() != true) return;
        var result = await ViewModel.HideSelectedAsync(dialog.Reason);
        var kind = result.Failure == 0 ? OrderNoticeKind.Success : result.Success > 0 ? OrderNoticeKind.Warning : OrderNoticeKind.Error;
        F_OrderNotice.Show(owner, result.Failure == 0 ? "注文を非表示にしました" : "注文非表示の結果", result.Result, kind);
    }

    private async void OrdersGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<DataGridRow>(source)?.Item is OrderManagementRow row)
            await OpenDetailsAsync(row);
    }

    private async void Details_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: OrderManagementRow row }) await OpenDetailsAsync(row);
    }

    private async Task OpenDetailsAsync(OrderManagementRow row)
    {
        var details = await ViewModel.GetDetailsAsync(row.OrderId);
        if (details == null) { F_OrderNotice.Show(Window.GetWindow(this), "注文詳細", ViewModel.Feedback, OrderNoticeKind.Warning); return; }
        var dialog = new OrderDetailDialog(details, ViewModel, row, Window.GetWindow(this));
        dialog.ShowDialog();
    }

    private void Grid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1 && e.OriginalSource is DependencyObject source
            && FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) != null) e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T found) return found;
            child = child is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(child)
                : LogicalTreeHelper.GetParent(child);
        }
        return null;
    }
}

internal sealed class OrderDetailDialog : Window
{
    private readonly OrderDetailView detail;
    private readonly ChumonManagementViewModel viewModel;
    private readonly OrderManagementRow row;
    private readonly bool canOperate;

    public OrderDetailDialog(OrderDetailView detail, ChumonManagementViewModel viewModel, OrderManagementRow row, Window? owner, bool readOnly = false)
    {
        this.detail = detail; this.viewModel = viewModel; this.row = row;
        canOperate = !readOnly && AppSession.CanManageOrders;
        Owner = owner;
        Title = "注文詳細"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize;
        AllowsTransparency = true; Background = System.Windows.Media.Brushes.Transparent; ShowInTaskbar = false;
        WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        var availableWidth = owner?.ActualWidth > 0 ? owner.ActualWidth : SystemParameters.WorkArea.Width;
        var availableHeight = owner?.ActualHeight > 0 ? owner.ActualHeight : SystemParameters.WorkArea.Height;
        MaxWidth = Math.Min(SystemParameters.WorkArea.Width * .96, Math.Max(760, availableWidth * .94));
        MaxHeight = Math.Min(SystemParameters.WorkArea.Height * .94, Math.Max(460, availableHeight * .88));
        Width = Math.Min(900, MaxWidth); MinWidth = Math.Min(760, MaxWidth);
        Height = Math.Min(640, MaxHeight); MinHeight = Math.Min(460, MaxHeight);
        FontFamily = AppTypography.FontFamily; FontSize = 14;

        var frame = new Border { Background = AppTheme.Surface, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), ClipToBounds = true };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(CreateHeader());
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };

        var body = new StackPanel { Margin = new Thickness(22, 18, 22, 16) };
        var infoGrid = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        var customerCard = CreateInfoCard("顧客情報", [
            ("顧客名", detail.CustomerName), ("電話番号", Display(detail.CustomerPhone)), ("住所", Display(detail.CustomerAddress))]);
        var orderCard = CreateInfoCard("注文情報", [
            ("注文番号", $"CH-{detail.OrderId:000000}"), ("関連受注番号", $"ORD-{detail.RelatedOrderId:000000}"),
            ("注文日", FormatDate(detail.OrderDate)), ("納期", FormatDate(detail.DeliveryDate)),
            ("受付営業所", detail.OfficeName), ("注文状態", detail.State), ("管理状態", detail.ManagementState)]);
        bool? cardsStacked = null;
        void ArrangeInfoCards(double width)
        {
            var stackCards = width < 780;
            if (cardsStacked == stackCards) return;
            cardsStacked = stackCards;
            infoGrid.Children.Clear(); infoGrid.RowDefinitions.Clear(); infoGrid.ColumnDefinitions.Clear();
            if (stackCards)
            {
                infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(customerCard, 0); Grid.SetRow(orderCard, 1);
                Grid.SetColumn(customerCard, 0); Grid.SetColumn(orderCard, 0);
                customerCard.Margin = new Thickness(0, 0, 0, 10); orderCard.Margin = new Thickness(0);
            }
            else
            {
                infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(customerCard, 0); Grid.SetRow(orderCard, 0); Grid.SetColumn(customerCard, 0); Grid.SetColumn(orderCard, 1);
                customerCard.Margin = new Thickness(0, 0, 8, 0); orderCard.Margin = new Thickness(8, 0, 0, 0);
            }
            infoGrid.Children.Add(customerCard); infoGrid.Children.Add(orderCard);
        }
        ArrangeInfoCards(Width);
        SizeChanged += (_, e) => ArrangeInfoCards(e.NewSize.Width);
        body.Children.Add(infoGrid);

        var detailHeading = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        detailHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        detailHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        detailHeading.Children.Add(new TextBlock { Text = "注文明細", FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var stockNote = new TextBlock { Text = "在庫は商品ごとの合計数量で判定し、確定時に再確認します。", FontSize = 11, Foreground = AppTheme.Secondary, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(stockNote, 1); detailHeading.Children.Add(stockNote); body.Children.Add(detailHeading);

        var lines = new DataGrid
        {
            ItemsSource = detail.Lines, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
            CanUserDeleteRows = false, CanUserSortColumns = false, CanUserReorderColumns = false, CanUserResizeRows = false,
            EnableRowVirtualization = true, HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, RowHeight = double.NaN, MinRowHeight = 38, ColumnHeaderHeight = 32,
            MinHeight = 104, MaxHeight = 258,
            Background = AppTheme.Surface, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(1),
            HorizontalGridLinesBrush = AppTheme.Border
        };
        lines.SetValue(ScrollViewer.CanContentScrollProperty, true);
        VirtualizingPanel.SetScrollUnit(lines, ScrollUnit.Pixel);
        lines.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        lines.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        lines.Columns.Add(new DataGridTextColumn { Header = "No.", Binding = new System.Windows.Data.Binding(nameof(OrderLineView.Number)), Width = new DataGridLength(46), MinWidth = 46, ElementStyle = NumericCellStyle() });
        lines.Columns.Add(new DataGridTextColumn { Header = "商品コード / 商品名", Binding = new System.Windows.Data.Binding(nameof(OrderLineView.ProductLabel)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 170, ElementStyle = ProductCellStyle() });
        lines.Columns.Add(new DataGridTextColumn { Header = "単価", Binding = new System.Windows.Data.Binding(nameof(OrderLineView.UnitPrice)) { StringFormat = "¥#,##0.##", TargetNullValue = "未記録" }, Width = new DataGridLength(126), MinWidth = 126, ElementStyle = NumericCellStyle() });
        lines.Columns.Add(new DataGridTextColumn { Header = "数量", Binding = new System.Windows.Data.Binding(nameof(OrderLineView.Quantity)) { StringFormat = "N0" }, Width = new DataGridLength(108), MinWidth = 108, ElementStyle = NumericCellStyle() });
        lines.Columns.Add(new DataGridTextColumn { Header = "行合計", Binding = new System.Windows.Data.Binding(nameof(OrderLineView.LineTotal)) { StringFormat = "¥#,##0", TargetNullValue = "未記録" }, Width = new DataGridLength(118), MinWidth = 118, ElementStyle = NumericCellStyle() });
        lines.Columns.Add(new DataGridTextColumn { Header = "現在庫", Binding = new System.Windows.Data.Binding(nameof(OrderLineView.CurrentStockText)), Width = new DataGridLength(108), MinWidth = 108, ElementStyle = NumericCellStyle() });
        lines.Columns.Add(new DataGridTextColumn { Header = "在庫確認", Binding = new System.Windows.Data.Binding(nameof(OrderLineView.Availability)), Width = new DataGridLength(114), MinWidth = 114 });
        body.Children.Add(lines);
        var total = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        total.Children.Add(new TextBlock { Text = "注文合計", Foreground = AppTheme.Secondary, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) });
        total.Children.Add(new TextBlock { Text = detail.Total?.ToString("¥#,##0") ?? "関連受注に金額記録なし", HorizontalAlignment = HorizontalAlignment.Right, FontSize = 17, FontWeight = FontWeights.Bold, Foreground = AppTheme.Primary });
        body.Children.Add(total);
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);

        var footer = new Grid { Margin = new Thickness(20, 10, 20, 12) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var footerMessage = detail.ManagementState == "非表示" ? "非表示の注文は参照のみです。"
            : !canOperate ? "この注文の操作権限がありません。"
            : detail.State == "確定済み" ? "この注文は確定済みです。"
            : "確定時に在庫を再確認し、在庫を減算して出庫情報を作成します。";
        footer.Children.Add(new TextBlock { Text = footerMessage, Foreground = AppTheme.Secondary, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 14, 0) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (canOperate && detail.ManagementState == "通常表示")
        {
            var hide = new Button { Content = "非表示にする", Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0), Background = AppTheme.Surface, Foreground = AppTheme.Danger, BorderBrush = AppTheme.Danger, BorderThickness = new Thickness(1) };
            hide.Click += Hide; actions.Children.Add(hide);
        }
        if (canOperate && detail.ManagementState == "通常表示" && detail.State == "未確定")
        {
            var confirm = new Button { Content = "注文を確定", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 0), Background = AppTheme.Primary, Foreground = System.Windows.Media.Brushes.White, BorderBrush = AppTheme.Primary };
            confirm.Click += Confirm; actions.Children.Add(confirm);
        }
        var close = new Button { Content = "閉じる", Padding = new Thickness(18, 8, 18, 8), IsCancel = true, Background = AppTheme.Surface, Foreground = AppTheme.Text, BorderBrush = AppTheme.Border };
        actions.Children.Add(close); Grid.SetColumn(actions, 1); footer.Children.Add(actions);
        var footerBorder = new Border { BorderBrush = AppTheme.Border, BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
        Grid.SetRow(footerBorder, 2); root.Children.Add(footerBorder);
        frame.Child = root; Content = frame;
    }

    private UIElement CreateHeader()
    {
        var header = new Border { Background = AppTheme.Primary, Padding = new Thickness(20, 0, 12, 0) };
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new LucideIcon
        {
            Data = Application.Current.TryFindResource("IconOrder") as Geometry,
            Width = 20, Height = 20, Stroke = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 0, 11, 0), VerticalAlignment = VerticalAlignment.Center
        });
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
        title.Children.Add(new TextBlock { Text = "注文詳細", Foreground = System.Windows.Media.Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(new TextBlock { Text = $"CH-{detail.OrderId:000000}", Foreground = System.Windows.Media.Brushes.White, Opacity = .82, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(title);
        stack.Children.Add(StatusBadge(detail.State, detail.State == "確定済み" ? AppTheme.SuccessSurface : AppTheme.WarningSurface, detail.State == "確定済み" ? AppTheme.SuccessText : AppTheme.WarningText));
        stack.Children.Add(StatusBadge(detail.ManagementState, detail.ManagementState == "非表示" ? AppTheme.Subtle : AppTheme.PaleBlue, detail.ManagementState == "非表示" ? AppTheme.Secondary : AppTheme.Primary));
        grid.Children.Add(stack);
        var close = new Button
        {
            Content = new LucideIcon { Data = Application.Current.TryFindResource("IconX") as Geometry, Width = 18, Height = 18, Stroke = System.Windows.Media.Brushes.White, StrokeThickness = 2 },
            Width = 36, Height = 36, MinWidth = 36, MinHeight = 36, Padding = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent, Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0), ToolTip = "閉じる", Cursor = Cursors.Hand,
            Template = CloseIconButtonTemplate()
        };
        System.Windows.Automation.AutomationProperties.SetName(close, "注文詳細を閉じる");
        close.Click += (_, _) => Close(); Grid.SetColumn(close, 1); grid.Children.Add(close);
        header.Child = grid; return header;
    }

    private static ControlTemplate CloseIconButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "CloseButtonBorder";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)), "CloseButtonBorder"));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(72, 255, 255, 255)), "CloseButtonBorder"));
        template.Triggers.Add(pressed);
        return template;
    }

    private static Border StatusBadge(string text, System.Windows.Media.Brush background, System.Windows.Media.Brush foreground) => new()
    {
        Background = background, CornerRadius = new CornerRadius(12), Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(0, 0, 6, 0),
        Child = new TextBlock { Text = text, Foreground = foreground, FontSize = 11, VerticalAlignment = VerticalAlignment.Center }
    };

    private static Border CreateInfoCard(string title, IReadOnlyList<(string Label, string Value)> fields)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < fields.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new Border
            {
                Background = AppTheme.PaleBlue, CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 5, 7, 5), Margin = new Thickness(0, 2, 8, 2),
                Child = new TextBlock { Text = fields[i].Label, Foreground = AppTheme.Secondary, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top }
            };
            var value = new TextBlock { Text = fields[i].Value, Foreground = AppTheme.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6), VerticalAlignment = VerticalAlignment.Top };
            Grid.SetRow(label, i); Grid.SetRow(value, i); Grid.SetColumn(value, 1); grid.Children.Add(label); grid.Children.Add(value);
        }
        panel.Children.Add(grid);
        return new Border { Background = AppTheme.Surface, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Child = panel };
    }

    private static Style ProductCellStyle() => new(typeof(TextBlock))
    {
        Setters = { new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Left), new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap), new Setter(TextBlock.TextTrimmingProperty, TextTrimming.None), new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center), new Setter(FrameworkElement.MarginProperty, new Thickness(7, 2, 5, 2)) }
    };

    private static Style NumericCellStyle() => new(typeof(TextBlock))
    {
        Setters = { new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right), new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center), new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0)) }
    };
    private static string Display(string value) => string.IsNullOrWhiteSpace(value) ? "未登録" : value;
    private static string FormatDate(DateTime? value) => value?.ToString("yyyy/MM/dd") ?? "未設定";

    private async void Confirm(object sender, RoutedEventArgs e)
    {
        if (!canOperate || viewModel.IsBusy || detail.ManagementState != "通常表示" || detail.State != "未確定") return;
        var confirmation = F_OrderNotice.Confirm(this, "注文確定の確認", $"注文 CH-{detail.OrderId:000000} を確定しますか？\n確定後、在庫数が減少し、出庫情報が作成されます。", "注文を確定");
        if (!confirmation) return;
        var result = await viewModel.ConfirmSingleAsync(row);
        var kind = result.Failure == 0 ? OrderNoticeKind.Success : OrderNoticeKind.Error;
        F_OrderNotice.Show(this, "注文確定の結果", result.Result, kind);
        if (result.Success > 0) Close();
    }

    private async void Hide(object sender, RoutedEventArgs e)
    {
        if (!canOperate || viewModel.IsBusy || detail.ManagementState != "通常表示") return;
        var selected = new[] { row };
        var dialog = new OrderHideConfirmationDialog(selected, this);
        if (dialog.ShowDialog() != true) return;
        var result = await viewModel.HideSingleAsync(row, dialog.Reason);
        F_OrderNotice.Show(this, result.Failure == 0 ? "注文を非表示にしました" : "注文非表示の結果", result.Result,
            result.Failure == 0 ? OrderNoticeKind.Success : result.Success > 0 ? OrderNoticeKind.Warning : OrderNoticeKind.Error);
        if (result.Success > 0) Close();
    }
}

internal sealed class OrderHideConfirmationDialog : Window
{
    private readonly TextBox reasonBox = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10), Background = AppTheme.Surface, BorderBrush = AppTheme.InputBorder, Margin = new Thickness(0, 6, 0, 14) };
    public string Reason => reasonBox.Text;

    public OrderHideConfirmationDialog(IReadOnlyCollection<OrderManagementRow> targets, Window? owner)
    {
        Owner = owner; Title = "注文を非表示にする"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = System.Windows.Media.Brushes.Transparent; ShowInTaskbar = false;
        WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        var ownerWidth = owner?.ActualWidth > 0 ? owner.ActualWidth : SystemParameters.WorkArea.Width;
        var ownerHeight = owner?.ActualHeight > 0 ? owner.ActualHeight : SystemParameters.WorkArea.Height;
        MaxWidth = Math.Max(360, ownerWidth * .9); MaxHeight = Math.Max(360, ownerHeight * .85);
        Width = Math.Min(540, MaxWidth); MinWidth = Math.Min(380, MaxWidth); SizeToContent = SizeToContent.Height;
        var frame = new Border { Background = AppTheme.Surface, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), ClipToBounds = true };
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new Border { Background = AppTheme.Primary, Padding = new Thickness(20, 0, 12, 0) };
        var titleGrid = new Grid(); titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleGrid.Children.Add(new TextBlock { Text = "注文を非表示にする", Foreground = System.Windows.Media.Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Content = "×", Width = 34, Height = 34, FontSize = 21, Background = System.Windows.Media.Brushes.Transparent, Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0), IsCancel = true };
        Grid.SetColumn(close, 1); titleGrid.Children.Add(close); heading.Child = titleGrid; root.Children.Add(heading);
        var body = new StackPanel { Margin = new Thickness(22, 18, 22, 14) };
        body.Children.Add(new TextBlock { Text = targets.Count == 1 ? $"注文 CH-{targets.First().OrderId:000000} を非表示にしますか？" : $"選択した {targets.Count:N0} 件の注文を非表示にしますか？", FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var rowsText = string.Join(Environment.NewLine, targets.Take(8).Select(x => $"注文番号：{x.OrderNumber}　　顧客名：{x.Customer}"));
        if (targets.Count > 8) rowsText += $"\nほか {targets.Count - 8:N0} 件";
        body.Children.Add(new TextBlock { Text = rowsText, TextWrapping = TextWrapping.Wrap, Background = AppTheme.Canvas, Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 12) });
        body.Children.Add(new TextBlock { Text = "非表示理由（任意）", FontWeight = FontWeights.Medium });
        var input = new Grid();
        var placeholder = new TextBlock { Text = "理由を入力（省略できます）", Foreground = AppTheme.Secondary, Margin = new Thickness(11, 13, 8, 0), IsHitTestVisible = false };
        reasonBox.TextChanged += (_, _) => placeholder.Visibility = string.IsNullOrEmpty(reasonBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        input.Children.Add(reasonBox); input.Children.Add(placeholder); body.Children.Add(input);
        body.Children.Add(new TextBlock { Text = "注文データは削除されません。管理状態のみ変更されます。", Foreground = AppTheme.Secondary, TextWrapping = TextWrapping.Wrap });
        var bodyScroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(bodyScroll, 1); root.Children.Add(bodyScroll);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 10, 20, 14) };
        footer.Children.Add(new Button { Content = "キャンセル", IsCancel = true, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 0), Background = AppTheme.Surface, BorderBrush = AppTheme.Border });
        var confirm = new Button { Content = "非表示にする", IsDefault = true, Padding = new Thickness(16, 8, 16, 8), Background = AppTheme.Surface, Foreground = AppTheme.Danger, BorderBrush = AppTheme.Danger };
        confirm.Click += (_, _) => DialogResult = true; footer.Children.Add(confirm);
        var footerBorder = new Border { BorderBrush = AppTheme.Border, BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
        Grid.SetRow(footerBorder, 2); root.Children.Add(footerBorder);
        frame.Child = root; Content = frame;
        MaxHeight = Math.Min(MaxHeight, Math.Max(340, ownerHeight * .85));
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
    }
}
