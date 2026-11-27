using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public partial class HistoryManagementView : UserControl
{
    private readonly int? employeeScope;
    private readonly Action<int> openOrder;
    private readonly Action? onHistoryViewed;
    private readonly string defaultModule;
    private readonly DispatcherTimer searchTimer;
    private readonly Dictionary<string, (MenuItem Item, TextBlock Count)> moduleItems = [];
    private CancellationTokenSource? loadCancellation;
    private int pageIndex;
    private bool initializing = true;
    private bool updatingFilters;
    private DatePicker? invalidDate;
    private const string EmployeeHistoryPrompt = "社員番号・社員名・変更内容で検索...";

    public HistoryManagementView(Action<int> openOrder, Action? onHistoryViewed = null,
        bool startInEmployeeHistory = false, int? employeeId = null)
    {
        this.openOrder = openOrder;
        this.onHistoryViewed = onHistoryViewed;
        InitializeComponent();

        var useEmployeeHistory = startInEmployeeHistory && AppSession.IsAdmin;
        employeeScope = useEmployeeHistory ? employeeId : null;
        defaultModule = useEmployeeHistory ? HistoryManagementService.EmployeesModule : "すべて";
        var modules = HistoryManagementService.GetModules(AppSession.Role);
        if (employeeScope.HasValue)
            modules = [new(HistoryManagementService.EmployeesModule, "社員", true)];
        ModuleFilter.ItemsSource = modules.Select(x => x.Key.Length == 0 ? "すべて" : x.Key)
            .Concat(AppSession.IsAdmin && !employeeScope.HasValue ? [HistoryManagementService.EmployeesModule] : Array.Empty<string>()).ToArray();
        foreach (var module in modules) AddModuleTab(module);
        ModuleFilter.SelectedItem = defaultModule;
        UpdateModuleTabs();
        PageSizePicker.ItemsSource = new[] { 20, 50 };
        PageSizePicker.SelectedIndex = 0;
        if (useEmployeeHistory && employeeId is { } id)
            SearchBox.Text = $"E-{id:0000}";
        SetOperationOptions();
        OperationFilter.SelectedIndex = 0;
        initializing = false;

        searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
        searchTimer.Tick += async (_, _) =>
        {
            searchTimer.Stop();
            await ReloadAsync();
        };
        Loaded += View_Loaded;
        Unloaded += View_Unloaded;
        UpdateSearchPlaceholder();
    }

    private void View_Loaded(object sender, RoutedEventArgs e)
    {
        _ = ReloadAsync();
    }

    private void View_Unloaded(object sender, RoutedEventArgs e)
    {
        searchTimer.Stop();
        loadCancellation?.Cancel();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSearchPlaceholder();
        if (!initializing && !updatingFilters) ScheduleReload(resetPage: true);
    }

    private void ActorFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!initializing && !updatingFilters) ScheduleReload(resetPage: true);
    }

    private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || updatingFilters) return;
        if (ReferenceEquals(sender, ModuleFilter))
        {
            var previous = OperationFilter.SelectedItem as string;
            SetOperationOptions();
            if (previous != null && OperationFilter.Items.Contains(previous)) OperationFilter.SelectedItem = previous;
            else OperationFilter.SelectedIndex = 0;
            UpdateSearchPlaceholder();
            UpdateModuleTabs();
        }
        ScheduleReload(resetPage: true, immediate: true);
    }

    private void DateFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, invalidDate)) invalidDate = null;
        if (!initializing && !updatingFilters) ScheduleReload(resetPage: true, immediate: true);
    }

    private void DateFilter_Invalid(object? sender, DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        invalidDate = sender as DatePicker;
        searchTimer.Stop(); loadCancellation?.Cancel();
        ShowError("日付を確認してください（例：2026/11/16）。");
    }

    private void SetOperationOptions()
    {
        var orderOperations = OrderHistoryOperations.All;
        var selectedModule = ModuleFilter.SelectedItem as string ?? "すべて";
        var operations = selectedModule switch
        {
            HistoryManagementService.OrdersModule => orderOperations,
            HistoryManagementService.EmployeesModule or HistoryManagementService.MasterModule when AppSession.IsAdmin => HistoryManagementService.EmployeeOperations.ToArray(),
            "すべて" when AppSession.IsAdmin => orderOperations.Concat(HistoryManagementService.EmployeeOperations).Distinct().ToArray(),
            "すべて" => orderOperations,
            _ => []
        };
        OperationFilter.ItemsSource = new[] { "すべて" }.Concat(operations).ToArray();
    }

    private void ToggleFilters_Click(object sender, RoutedEventArgs e)
    {
        AdvancedFilters.Visibility = AdvancedFilters.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        updatingFilters = true;
        ModuleFilter.SelectedItem = defaultModule;
        SetOperationOptions();
        OperationFilter.SelectedIndex = 0;
        FromDate.SelectedDate = null;
        ToDate.SelectedDate = null;
        FromDate.Text = ToDate.Text = "";
        invalidDate = null;
        ActorFilter.Clear();
        TargetNumberFilter.Clear(); TargetNameFilter.Clear();
        SearchBox.Clear();
        updatingFilters = false;
        UpdateSearchPlaceholder();
        UpdateModuleTabs();
        AdvancedFilters.Visibility = Visibility.Collapsed;
        ScheduleReload(resetPage: true, immediate: true);
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => _ = ReloadAsync();

    private void PreviousPage_Click(object sender, RoutedEventArgs e)
    {
        if (pageIndex == 0) return;
        pageIndex--;
        _ = ReloadAsync();
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        pageIndex++;
        _ = ReloadAsync();
    }

    private void PageSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!initializing) ScheduleReload(resetPage: true, immediate: true);
    }

    private string SelectedModule => ModuleFilter.SelectedItem is string value && value != "すべて" ? value : "";

    private void AddModuleTab(HistoryModule module)
    {
        var icon = new LucideIcon { Data = (Geometry)FindResource("IconHistory"), Width = 13, Height = 13,
            Tag = AppTheme.SecondaryText, Margin = new Thickness(0,0,4,0), Style = (Style)FindResource("StatusTabIcon") };
        var label = new TextBlock { Text = module.Label, Tag = AppTheme.SecondaryText, VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)FindResource("StatusTabColoredText") };
        var count = new TextBlock { Text = "", Tag = AppTheme.Primary, VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11, Style = (Style)FindResource("StatusTabColoredText") };
        var countBadge = new Border { Child = count, Tag = AppTheme.PaleBlue, CornerRadius = new CornerRadius(8),
            Padding = new Thickness(5,0,5,0), Margin = new Thickness(4,0,0,0), VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)FindResource("StatusTabCountBadge"), Visibility = Visibility.Collapsed };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(icon); header.Children.Add(label); header.Children.Add(countBadge);
        var item = new MenuItem { Header = header, Style = (Style)FindResource("StatusMenuItem"), Tag = module,
            ToolTip = module.Key == HistoryManagementService.MasterModule && module.HasAuditSource
                ? "社員登録・所属／権限変更。顧客・商品マスタの変更は未記録です。"
                : module.HasAuditSource ? module.Label + "の操作履歴" : "操作履歴は記録されていません" };
        AutomationProperties.SetName(item, module.Label);
        item.Click += (_, _) =>
        {
            ModuleFilter.SelectedItem = module.Key.Length == 0 ? "すべて" : module.Key;
            UpdateModuleTabs();
        };
        ModuleTabs.Items.Add(item);
        moduleItems.Add(module.Key, (item,count));
    }

    private void UpdateModuleTabs()
    {
        var key = SelectedModule;
        if (key == HistoryManagementService.EmployeesModule && !employeeScope.HasValue) key = HistoryManagementService.MasterModule;
        foreach (var (module, item) in moduleItems) item.Item.IsChecked = key == module;
    }

    private void SetPageButtons(int index, int pages)
    {
        PageButtons.Children.Clear();
        var start = Math.Max(0, Math.Min(index - 1, pages - 3));
        for (var i = start; i < Math.Min(start + 3, pages); i++)
        {
            var target = i;
            var button = new Button { Content = i + 1, MinWidth = 30, Padding = new Thickness(5,3,5,3), Margin = new Thickness(0,0,4,0),
                Style = (Style)FindResource(i == index ? "ThemePrimaryButton" : "OrderToolbarButton"), IsTabStop = i != index };
            AutomationProperties.SetName(button, $"{i + 1}ページ{(i == index ? "（表示中）" : "")}");
            button.Click += (_, _) => { if (target != pageIndex) { pageIndex = target; _ = ReloadAsync(); } };
            PageButtons.Children.Add(button);
        }
    }

    private void ScheduleReload(bool resetPage, bool immediate = false)
    {
        if (resetPage) pageIndex = 0;
        loadCancellation?.Cancel();
        searchTimer.Stop();
        loadCancellation?.Cancel();
        if (invalidDate != null && DateTime.TryParse(invalidDate.Text, CultureInfo.CurrentCulture, DateTimeStyles.None, out _)) invalidDate = null;
        if (invalidDate != null) { ShowError("操作日時を正しい日付で入力するか、条件をクリアしてください。"); return; }
        if (immediate) _ = ReloadAsync();
        else searchTimer.Start();
    }

    private async Task ReloadAsync()
    {
        searchTimer.Stop();
        if (FromDate.SelectedDate.HasValue && ToDate.SelectedDate.HasValue
            && FromDate.SelectedDate.Value.Date > ToDate.SelectedDate.Value.Date)
        {
            ShowError("開始日は終了日以前の日付を指定してください。");
            return;
        }

        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
        loadCancellation = new CancellationTokenSource();
        var token = loadCancellation.Token;
        var selectedOperation = OperationFilter.SelectedItem as string;
        var filter = new HistoryManagementFilter
        {
            Module = SelectedModule,
            Operation = string.IsNullOrWhiteSpace(selectedOperation) || selectedOperation == "すべて" ? "" : selectedOperation,
            From = FromDate.SelectedDate,
            To = ToDate.SelectedDate,
            Actor = ActorFilter.Text,
            Keyword = SearchBox.Text,
            TargetNumber = TargetNumberFilter.Text, TargetName = TargetNameFilter.Text, EmployeeId = employeeScope,
            PageIndex = pageIndex,
            PageSize = PageSizePicker.SelectedItem is int size ? size : 20
        };
        AdvancedCaption.Text = filter.Operation.Length > 0 || filter.Actor.Trim().Length > 0 || filter.From.HasValue || filter.To.HasValue
            || filter.TargetNumber.Trim().Length > 0 || filter.TargetName.Trim().Length > 0 ? "詳細条件（適用中）" : "詳細条件";

        LoadingOverlay.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
        ErrorOverlay.Visibility = Visibility.Collapsed;
        PreviousPageButton.IsEnabled = false;
        NextPageButton.IsEnabled = false;
        PageButtons.Children.Clear(); HistoryGrid.ItemsSource = null;
        try
        {
            using var db = new SalesManagementContext();
            var result = await HistoryManagementService.ReadAsync(db, filter, token);
            if (token.IsCancellationRequested) return;

            pageIndex = result.PageIndex;
            HistoryGrid.ItemsSource = result.Rows;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            var hasSource = result.Counts.ContainsKey(filter.Module);
            EmptyTitle.Text = hasSource ? "該当する操作履歴がありません" : "この業務の操作履歴は記録されていません";
            EmptyHint.Text = filter.Module == HistoryManagementService.MasterModule && !hasSource ? "顧客・商品の過去の変更は記録されていません。"
                : hasSource ? "検索条件を変更してお試しください。" : "確認できる取引は「業務履歴」または各管理画面をご覧ください。";
            EmptyState.Visibility = result.Total == 0 ? Visibility.Visible : Visibility.Collapsed;
            ErrorOverlay.Visibility = Visibility.Collapsed;

            var first = result.Total == 0 ? 0 : result.PageIndex * result.PageSize + 1;
            var last = result.Total == 0 ? 0 : Math.Min(result.Total, result.PageIndex * result.PageSize + result.Rows.Count);
            ResultCount.Text = !hasSource ? "—" : result.Total == 0 ? "0件" : $"{first:N0}～{last:N0}件 / 全{result.Total:N0}件";
            SetPageButtons(result.PageIndex, result.Total == 0 ? 0 : (result.Total - 1) / result.PageSize + 1);
            foreach (var (key, tab) in moduleItems)
            {
                var visible = result.Counts.TryGetValue(key, out var count);
                tab.Count.Text = visible ? count.ToString("N0") : "";
                ((Border)tab.Count.Parent).Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
            PreviousPageButton.IsEnabled = result.PageIndex > 0;
            NextPageButton.IsEnabled = (result.PageIndex + 1) * result.PageSize < result.Total;
            if (hasSource) onHistoryViewed?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            ShowError(ex is UnauthorizedAccessException ? ex.Message : "履歴を取得できませんでした。接続状態を確認して再読込してください。");
        }
    }

    private void ShowError(string message)
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;
        ErrorText.Text = message;
        ErrorOverlay.Visibility = Visibility.Visible;
        PreviousPageButton.IsEnabled = false;
        NextPageButton.IsEnabled = false;
        PageButtons.Children.Clear(); HistoryGrid.ItemsSource = null; ResultCount.Text = "—";
    }

    private void UpdateSearchPlaceholder()
    {
        var prompt = Equals(ModuleFilter?.SelectedItem, HistoryManagementService.EmployeesModule)
            ? EmployeeHistoryPrompt
            : "伝票番号・商品名・顧客名・担当者・変更内容で検索...";
        if (SearchPlaceholder == null || SearchBox == null) return;
        SearchPlaceholder.Text = prompt;
        AutomationProperties.SetName(SearchBox, prompt);
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenDetails_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is HistoryEventRow row) ShowDetails(row);
        e.Handled = true;
    }

    private void HistoryGrid_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source
            || IsInside<ButtonBase>(source)
            || ItemsControl.ContainerFromElement(HistoryGrid, source) is not DataGridRow { Item: HistoryEventRow row }) return;
        e.Handled = true;
        ShowDetails(row);
    }

    private void HistoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (TimelineButton != null) TimelineButton.IsEnabled = HistoryGrid.SelectedItem is HistoryEventRow; }

    private void Timeline_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is HistoryEventRow row && Window.GetWindow(this) is { } owner)
            new HistoryTimelineWindow(owner, row, ShowDetails).ShowDialog();
    }

    private void BusinessHistory_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } owner) new BusinessHistoryWindow(owner, SelectedModule, openOrder: openOrder).ShowDialog();
    }

    private void ShowDetails(HistoryEventRow row) => ShowDetails(row, Window.GetWindow(this) ?? Application.Current.MainWindow);

    private void ShowDetails(HistoryEventRow row, Window? owner)
    {
        if (row.OrderEntry is { } orderEntry)
        {
            new F_OrderHistoryDetail(orderEntry, owner!, () => openOrder(row.OrderId!.Value)).ShowDialog();
            return;
        }
        if (row.EmployeeEntry is { } employeeEntry) new EmployeeHistoryDetailWindow(row, employeeEntry, owner).ShowDialog();
    }

    private static bool IsInside<TElement>(DependencyObject source) where TElement : DependencyObject
    {
        for (DependencyObject? current = source; current != null;
             current = current is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(current))
            if (current is TElement) return true;
        return false;
    }
}

internal sealed class EmployeeHistoryDetailWindow : Window
{
    public EmployeeHistoryDetailWindow(HistoryEventRow row, EmployeeAssignmentHistory entry, Window? owner)
    {
        Owner = owner;
        Title = "履歴詳細 — 社員操作履歴";
        Width = Math.Min(720, Math.Max(440, SystemParameters.WorkArea.Width * .9));
        MinWidth = Math.Min(440, Width);
        Height = Math.Min(570, Math.Max(360, SystemParameters.WorkArea.Height * .8));
        MinHeight = Math.Min(360, Height);
        WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;

        var frame = new Border { Background = AppTheme.Surface, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), ClipToBounds = true };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });

        var header = new Border { Background = AppTheme.Primary, Padding = new Thickness(20, 0, 12, 0) };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock { Text = Title, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold });
        titleStack.Children.Add(new TextBlock { Text = $"{row.TargetNumber}  |  {row.TargetName}", Foreground = Brushes.White, FontSize = 12, Margin = new Thickness(0, 2, 0, 0) });
        headerGrid.Children.Add(titleStack);
        var closeHeader = new Button { Content = "×", Width = 36, Height = 36, FontSize = 24, Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), ToolTip = "閉じる" };
        closeHeader.Click += (_, _) => Close();
        Grid.SetColumn(closeHeader, 1); headerGrid.Children.Add(closeHeader); header.Child = headerGrid;
        root.Children.Add(header);

        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "操作情報", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = AppTheme.Text, Margin = new Thickness(0, 0, 0, 8) });
        body.Children.Add(CreateInfoCard(new[]
        {
            ("操作日時", row.Timestamp), ("操作種別", row.Operation), ("操作担当者", row.ActorName),
            ("業務区分", row.Module), ("対象番号", row.TargetNumber), ("履歴イベントID", entry.Id.ToString(CultureInfo.InvariantCulture))
        }));
        var tabs = new TabControl { Margin = new Thickness(0,12,0,0) };
        tabs.Items.Add(new TabItem { Header = "変更内容", Content = CreateChangesCard(entry) });
        tabs.Items.Add(new TabItem { Header = "関連情報", Content = CreateInfoCard(new[] { ("対象社員", $"{row.TargetNumber}　{row.TargetName}"), ("処理結果", "未記録") }) });
        body.Children.Add(tabs);
        var scroll = new ScrollViewer { Content = body, Padding = new Thickness(20), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);

        var footer = new DockPanel { LastChildFill = false, Margin = new Thickness(20, 8, 20, 8) };
        footer.Children.Add(new TextBlock { Text = $"対象番号：{row.TargetNumber}", Foreground = AppTheme.Secondary, VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Content = "閉じる", MinWidth = 88, Padding = new Thickness(14, 7, 14, 7), IsCancel = true, Background = AppTheme.Surface, BorderBrush = AppTheme.Border };
        DockPanel.SetDock(close, Dock.Right); footer.Children.Add(close);
        if (row.EmployeeId is int employeeId && EmployeeService.CanManage)
        {
            var open = new Button { Content = "社員詳細を開く", Margin = new Thickness(0,0,8,0), MinWidth = 125 };
            DockPanel.SetDock(open, Dock.Right); footer.Children.Add(open);
            open.Click += async (_, _) =>
            {
                open.IsEnabled = false;
                try
                {
                    var detail = await Task.Run(() => EmployeeService.GetDetail(employeeId));
                    if (IsVisible && detail != null) new EmployeeDetailDialog(detail, canManage: false, this).ShowDialog();
                    else if (IsVisible) F_OrderNotice.Show(this, "社員詳細", "対象の社員を参照できません。", OrderNoticeKind.Warning);
                }
                catch { if (IsVisible) F_OrderNotice.Show(this, "社員詳細", "対象の社員を参照できません。", OrderNoticeKind.Warning); }
                finally { open.IsEnabled = true; }
            };
        }
        var footerBorder = new Border { BorderBrush = AppTheme.Border, BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
        Grid.SetRow(footerBorder, 2); root.Children.Add(footerBorder);
        frame.Child = root; Content = frame;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }

    private static Border CreateInfoCard(IReadOnlyList<(string Label, string Value)> fields)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(126) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < fields.Count; index++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = fields[index].Label, Foreground = AppTheme.SecondaryText, Margin = new Thickness(0, 4, 12, 4) };
            var value = new TextBlock { Text = fields[index].Value, Foreground = AppTheme.Text, Margin = new Thickness(0, 4, 0, 4), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(label, index); Grid.SetRow(value, index); Grid.SetColumn(value, 1);
            grid.Children.Add(label); grid.Children.Add(value);
        }
        return new Border { Background = AppTheme.Canvas, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(14), Child = grid };
    }

    private static Border CreateChangesCard(EmployeeAssignmentHistory entry)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddCell(grid, "項目", 0, 0, true); AddCell(grid, "変更前", 0, 1, true); AddCell(grid, "変更後", 0, 2, true);
        var row = 1;
        void Compare(string label, string? before, string after)
        {
            if (before == after) return;
            AddCell(grid, label, row, 0); AddCell(grid, before ?? "未記録", row, 1);
            AddCell(grid, string.IsNullOrWhiteSpace(after) ? "未記録" : after, row++, 2, highlight: before != null);
        }
        Compare("営業所", entry.BeforeOfficeName, entry.AfterOfficeName);
        Compare("役割", entry.BeforeRoleName, entry.AfterRoleName);
        var note = new TextBlock
        {
            Text = entry.Operation == "社員登録" ? "登録時点の所属・役割を記録しています。変更前の値は未記録です。" : "記録済みの所属・役割変更を表示しています。",
            Foreground = AppTheme.Secondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0)
        };
        var panel = new StackPanel(); panel.Children.Add(grid); panel.Children.Add(note);
        return new Border { Background = AppTheme.Surface, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12), Child = panel };
    }

    private static void AddCell(Grid grid, string text, int row, int column, bool header = false, bool highlight = false)
    {
        while (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var block = new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 7, 8, 7),
            Foreground = header ? AppTheme.Primary : AppTheme.Text, FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal
        };
        if (header || highlight) block.Background = AppTheme.PaleBlue;
        Grid.SetRow(block, row); Grid.SetColumn(block, column); grid.Children.Add(block);
    }
}
