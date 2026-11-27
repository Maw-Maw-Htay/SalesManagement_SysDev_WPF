using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SalesManagement_SysDev;

public partial class CustomerManagementView : UserControl
{
    private CustomerManagementViewModel ViewModel => (CustomerManagementViewModel)DataContext;
    private bool presentingDetail;

    public CustomerManagementView() : this(new CustomerManagementViewModel()) { }

    internal CustomerManagementView(CustomerManagementViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        UpdateTabVisuals(viewModel.ActiveTab);
    }

    private void AllTab_Click(object sender, RoutedEventArgs e) => SelectTab(CustomerStatusTab.All);
    private void VisibleTab_Click(object sender, RoutedEventArgs e) => SelectTab(CustomerStatusTab.Visible);
    private void HiddenTab_Click(object sender, RoutedEventArgs e) => SelectTab(CustomerStatusTab.Hidden);
    private void SelectTab(CustomerStatusTab tab)
    {
        ViewModel.SelectTab(tab);
        UpdateTabVisuals(ViewModel.ActiveTab);
    }

    private void UpdateTabVisuals(CustomerStatusTab active)
    {
        var tabs = new[] { (AllTab, CustomerStatusTab.All), (VisibleTab, CustomerStatusTab.Visible), (HiddenTab, CustomerStatusTab.Hidden) };
        foreach (var (button, tab) in tabs)
        {
            var selected = tab == active;
            button.Tag = selected ? "StatusActive" : null;
        }
    }

    private void ToggleFilters_Click(object sender, RoutedEventArgs e) => ViewModel.IsFiltersExpanded = !ViewModel.IsFiltersExpanded;
    private void ClearConditions_Click(object sender, RoutedEventArgs e) => ViewModel.ClearConditions();
    private void ClearQuickSearch_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.QuickSearch = "";
        QuickSearchBox.Focus();
    }
    private async void Reload_Click(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync();
    private void PreviousPage_Click(object sender, RoutedEventArgs e) => ViewModel.PageNumber--;
    private void NextPage_Click(object sender, RoutedEventArgs e) => ViewModel.PageNumber++;
    private void CustomerGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        var direction = e.Column.SortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        foreach (var column in CustomerGrid.Columns) column.SortDirection = null;
        e.Column.SortDirection = direction;
        ViewModel.SortBy(e.Column.SortMemberPath, direction);
    }
    private void SelectAll_Click(object sender, RoutedEventArgs e) => ViewModel.SelectAllVisible(ViewModel.VisibleSelectionState != true);
    private void ClearSelection_Click(object sender, RoutedEventArgs e) => ViewModel.ClearSelection();

    internal async void OpenRegistration()
    {
        if (!ViewModel.CanRegister) return;
        var editor = new CustomerEditorViewModel(ViewModel.Offices);
        var owner = Window.GetWindow(this);
        var dialog = new CustomerEditorDialog(editor, ViewModel, owner);
        if (dialog.ShowDialog() != true || dialog.SavedCustomerId is not { } id) return;
        var completion = new F_OrderNotice("顧客登録完了", dialog.ResultMessage, OrderNoticeKind.Success,
            "顧客詳細を表示", "一覧に戻る", showCancel: true) { Owner = owner };
        if (completion.ShowDialog() == true) await OpenDetailAsync(id);
    }

    private void OpenDetail_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CustomerRow row) _ = OpenDetailAsync(row.Id);
    }

    private void CustomerGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source) return;
        for (var parent = source; parent != null && parent != CustomerGrid;
            parent = parent is Visual ? VisualTreeHelper.GetParent(parent) : LogicalTreeHelper.GetParent(parent))
        {
            if (parent is ButtonBase) return;
            if (parent is DataGridRow { Item: CustomerRow row })
            {
                e.Handled = true;
                _ = OpenDetailAsync(row.Id);
                return;
            }
        }
    }

    private async Task OpenDetailAsync(int id)
    {
        if (presentingDetail || !ViewModel.CanReload) return;
        presentingDetail = true;
        var owner = Window.GetWindow(this);
        try
        {
            CustomerRecord? customer;
            try { customer = await Task.Run(() => CustomerService.ReadDetail(id)); }
            catch (Exception ex)
            {
                if (IsLoaded) F_OrderNotice.Show(owner, "顧客詳細", ex is UnauthorizedAccessException ? ex.Message
                    : "顧客情報を取得できませんでした。接続状態を確認してください。", OrderNoticeKind.Warning);
                return;
            }
            if (!IsLoaded) return;
            if (customer == null)
            {
                F_OrderNotice.Show(owner, "顧客詳細", "顧客が見つからないか、参照範囲外です。", OrderNoticeKind.Warning);
                return;
            }
            var dialog = new CustomerDetailDialog(customer, ViewModel.CanEditCustomers, owner);
            dialog.ShowDialog();
            switch (dialog.NextAction)
            {
                case "edit": OpenEditor(customer); break;
                case "hide": await HideRowsAsync([new CustomerRow(customer)]); break;
                case "restore": await RestoreAsync(customer); break;
            }
        }
        finally { presentingDetail = false; }
    }

    private async void HideSelected_Click(object sender, RoutedEventArgs e)
    {
        await HideRowsAsync(ViewModel.SelectedRows);
    }

    private async Task HideRowsAsync(IReadOnlyCollection<CustomerRow> rows)
    {
        if (!ViewModel.CanEditCustomers || rows.Count == 0) return;
        var owner = Window.GetWindow(this);
        var dialog = new CustomerHideDialog(rows, owner);
        if (dialog.ShowDialog() != true) return;
        var result = await ViewModel.HideAsync(rows, dialog.Reason);
        F_OrderNotice.Show(owner, result.Succeeded ? "顧客を非表示にしました" : "顧客非表示の結果", result.Message,
            result.Succeeded ? OrderNoticeKind.Success : OrderNoticeKind.Warning);
    }

    private void OpenEditor(CustomerRecord customer)
    {
        if (!ViewModel.CanEditCustomers || customer.ManagementFlag != CustomerService.ActiveFlag) return;
        var editor = new CustomerEditorViewModel(ViewModel.Offices, customer);
        var dialog = new CustomerEditorDialog(editor, ViewModel, Window.GetWindow(this));
        if (dialog.ShowDialog() == true)
        {
            if (ViewModel.Rows.FirstOrDefault(x => x.Id == customer.Id) is { } updated)
            {
                CustomerGrid.SelectedItem = updated;
                CustomerGrid.ScrollIntoView(updated);
            }
            F_OrderNotice.Show(Window.GetWindow(this), "顧客更新", dialog.ResultMessage, OrderNoticeKind.Success);
        }
    }

    private async Task RestoreAsync(CustomerRecord customer)
    {
        if (!ViewModel.CanEditCustomers || !CustomerService.IsRestorable(customer.ManagementFlag, customer.HiddenReason)) return;
        var owner = Window.GetWindow(this);
        if (!F_OrderNotice.Confirm(owner, "顧客を表示に戻しますか？",
            $"顧客ID：{CustomerInput.FormatCode(customer.Id)}\n顧客名：{customer.Name}\n管理状態：非表示 → 表示中\n\n顧客の表示状態だけを変更します。",
            "表示に戻す")) return;
        var result = await ViewModel.RestoreAsync(customer);
        F_OrderNotice.Show(owner, result.Succeeded ? "顧客の表示を復元しました" : "復元できませんでした", result.Message,
            result.Succeeded ? OrderNoticeKind.Success : OrderNoticeKind.Warning);
    }
}

internal sealed class CustomerEditorDialog : Window
{
    private readonly CustomerEditorViewModel editor;
    private readonly CustomerManagementViewModel ownerViewModel;
    private readonly Button saveButton = new();
    private readonly Grid bodyGrid = new() { Margin = new Thickness(16) };
    private Button? cancelButton;
    private bool saved;
    public string ResultMessage { get; private set; } = "";
    public int? SavedCustomerId { get; private set; }

    public CustomerEditorDialog(CustomerEditorViewModel editor, CustomerManagementViewModel ownerViewModel, Window? owner)
    {
        this.editor = editor; this.ownerViewModel = ownerViewModel;
        Owner = owner; Title = editor.Header; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
        WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        var availableWidth = owner?.ActualWidth > 0 ? owner.ActualWidth : SystemParameters.WorkArea.Width;
        var availableHeight = owner?.ActualHeight > 0 ? owner.ActualHeight : SystemParameters.WorkArea.Height;
        MaxWidth = Math.Max(480, availableWidth * .94); MaxHeight = Math.Max(420, availableHeight * .85);
        Width = Math.Min(900, MaxWidth); MinWidth = Math.Min(640, MaxWidth);
        SizeToContent = SizeToContent.Height;
        FontFamily = AppTypography.FontFamily; FontSize = 14; DataContext = editor;

        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(CreateHeader(editor.Header, editor.Subtitle));
        var stacked = Width < 620;
        if (stacked)
        {
            bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        else
        {
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 });
        }
        var formCard = (FrameworkElement)CreateForm(); var assignment = (FrameworkElement)CreateAssignment();
        if (stacked)
        {
            Grid.SetRow(assignment, 1); formCard.Margin = new Thickness(0, 0, 0, 10);
        }
        else
        {
            Grid.SetColumn(assignment, 1);
            assignment.Margin = new Thickness(10, 0, 0, 0);
        }
        bodyGrid.Children.Add(formCard); bodyGrid.Children.Add(assignment);
        var bodyScroll = new ScrollViewer { Content = bodyGrid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(bodyScroll, 1); root.Children.Add(bodyScroll);
        var footer = CreateFooter(); Grid.SetRow(footer, 2); root.Children.Add(footer);
        Content = new Border { Background = AppTheme.Surface, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = root };
        PropertyChangedEventHandler stateChanged = (_, _) => saveButton.IsEnabled = editor.CanSave && !ownerViewModel.IsBusy;
        editor.PropertyChanged += stateChanged; ownerViewModel.PropertyChanged += stateChanged;
        Closed += (_, _) => { editor.PropertyChanged -= stateChanged; ownerViewModel.PropertyChanged -= stateChanged; };
        Closing += (_, e) =>
        {
            if (saved) return;
            if (editor.IsSaving) { e.Cancel = true; return; }
            if (editor.HasChanges && !F_OrderNotice.Confirm(this, "未保存の変更", "入力した内容を破棄して閉じますか？", "破棄する", "入力に戻る"))
                e.Cancel = true;
        };
    }

    internal static UIElement CreateHeader(string title, string subtitle)
    {
        var border = new Border { Background = AppTheme.Primary, Padding = new Thickness(24, 12, 18, 12) };
        var header = new DockPanel();
        var close = new Button { Content = "×", Width = 32, Height = 32, Foreground = Brushes.White, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Padding = new Thickness(0), FontSize = 20, ToolTip = "閉じる" };
        System.Windows.Automation.AutomationProperties.SetName(close, "閉じる");
        close.Click += (_, _) => Window.GetWindow(close)?.Close();
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock { Text = subtitle, Foreground = AppTheme.PaleBlue, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        header.Children.Add(stack); border.Child = header; return border;
    }

    private UIElement CreateForm()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "基本情報", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        AddReadOnly(panel, "顧客ID", editor.CustomerCode);
        AddInput(panel, "顧客名（会社名） *", nameof(editor.Name), nameof(editor.NameError), 50);
        AddInput(panel, "郵便番号", nameof(editor.PostalCode), nameof(editor.PostalError), 8);
        AddInput(panel, "住所", nameof(editor.Address), nameof(editor.AddressError), 50);
        AddInput(panel, "電話番号 *", nameof(editor.Phone), nameof(editor.PhoneError), 13);
        AddInput(panel, "FAX", nameof(editor.Fax), nameof(editor.FaxError), 13);
        return Card(panel);
    }

    private UIElement CreateAssignment()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "取引・担当情報", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        panel.Children.Add(FieldLabel("担当営業所 *"));
        var office = new ComboBox { Height = 38, ItemsSource = editor.Offices, DisplayMemberPath = nameof(CustomerOffice.Name), SelectedValuePath = nameof(CustomerOffice.Id), IsEnabled = editor.CanChangeOffice };
        System.Windows.Automation.AutomationProperties.SetName(office, "担当営業所");
        office.SetBinding(ComboBox.SelectedValueProperty, new System.Windows.Data.Binding(nameof(editor.OfficeId)) { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
        panel.Children.Add(office);
        AddFieldError(panel, nameof(editor.OfficeError));
        if (!editor.CanChangeOffice)
            panel.Children.Add(new TextBlock { Text = "ログイン中の営業所に固定されています。", Foreground = AppTheme.SecondaryText, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        AddReadOnly(panel, "管理状態", editor.ManagementState);
        return Card(panel);
    }

    private void AddInput(Panel panel, string label, string property, string errorProperty, int maxLength)
    {
        panel.Children.Add(FieldLabel(label));
        var input = new TextBox { Height = 38, MaxLength = maxLength, Padding = new Thickness(10, 7, 10, 7), Background = AppTheme.Surface, BorderBrush = AppTheme.InputBorder };
        System.Windows.Automation.AutomationProperties.SetName(input, label);
        input.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(property) { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
        panel.Children.Add(input);
        AddFieldError(panel, errorProperty);
    }

    private static void AddFieldError(Panel panel, string errorProperty)
    {
        var error = new TextBlock { Foreground = AppTheme.DangerText, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        var style = new Style(typeof(TextBlock));
        var hideEmpty = new Trigger { Property = TextBlock.TextProperty, Value = "" };
        hideEmpty.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
        style.Triggers.Add(hideEmpty);
        error.Style = style;
        error.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(errorProperty)); panel.Children.Add(error);
    }

    private static void AddReadOnly(Panel panel, string label, string value)
    {
        panel.Children.Add(FieldLabel(label));
        panel.Children.Add(new TextBlock { Text = value, Foreground = AppTheme.Text, Background = AppTheme.Canvas, Padding = new Thickness(10, 7, 10, 7), TextWrapping = TextWrapping.Wrap });
    }

    private UIElement CreateFooter()
    {
        var panel = new DockPanel { Margin = new Thickness(16, 10, 16, 10) };
        var error = new TextBlock { Foreground = AppTheme.DangerText, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 500 };
        error.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(editor.OperationError)));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        cancelButton = new Button { Content = "キャンセル", IsCancel = true, Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(0, 0, 8, 0), Background = AppTheme.Surface, BorderBrush = AppTheme.Border };
        buttons.Children.Add(cancelButton);
        saveButton.Content = editor.IsNew ? "顧客を登録" : "変更を保存"; saveButton.Padding = new Thickness(20, 9, 20, 9);
        saveButton.Background = AppTheme.Primary; saveButton.Foreground = Brushes.White; saveButton.BorderBrush = AppTheme.Primary; saveButton.IsEnabled = editor.CanSave;
        saveButton.Click += Save_Click; buttons.Children.Add(saveButton); DockPanel.SetDock(buttons, Dock.Right); panel.Children.Add(buttons); panel.Children.Add(error);
        return new Border { BorderBrush = AppTheme.Border, BorderThickness = new Thickness(0, 1, 0, 0), Child = panel };
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!editor.CanSave || editor.IsSaving || ownerViewModel.IsBusy) return;
        editor.IsSaving = true; saveButton.Content = "保存中…"; saveButton.IsEnabled = false;
        bodyGrid.IsEnabled = false;
        editor.OperationError = "";
        if (cancelButton != null) cancelButton.IsEnabled = false;
        try
        {
            var result = await ownerViewModel.SaveAsync(editor);
            if (result.PossibleDuplicates > 0 && editor.IsNew)
            {
                if (!F_OrderNotice.Confirm(this, "重複の可能性があります",
                    $"入力した顧客名・電話番号・住所のいずれかが一致する顧客が {result.PossibleDuplicates:N0} 件あります。別の顧客として登録しますか？",
                    "登録を続ける", "入力に戻る")) return;
                result = await ownerViewModel.SaveAsync(editor, allowPossibleDuplicate: true);
            }
            if (!result.Succeeded) { editor.OperationError = result.Message; return; }
            SavedCustomerId = result.CustomerId;
            ResultMessage = result.Message + (SavedCustomerId is { } id ? $"\n顧客ID：{CustomerInput.FormatCode(id)}" : "");
            saved = true;
            DialogResult = true;
        }
        finally
        {
            editor.IsSaving = false; saveButton.Content = editor.IsNew ? "顧客を登録" : "変更を保存";
            bodyGrid.IsEnabled = true;
            saveButton.IsEnabled = editor.CanSave && !ownerViewModel.IsBusy;
            if (cancelButton != null) cancelButton.IsEnabled = true;
        }
    }

    private static TextBlock FieldLabel(string text) => new() { Text = text, Foreground = AppTheme.Text, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 10, 0, 5) };
    private static Border Card(UIElement child) => new() { Background = AppTheme.Surface, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Child = child };
}

internal sealed class CustomerDetailDialog : Window
{
    public string? NextAction { get; private set; }

    public CustomerDetailDialog(CustomerRecord customer, bool canEdit, Window? owner)
    {
        Owner = owner; Title = $"顧客詳細 C-{customer.Id:0000}"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
        WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        var availableWidth = owner?.ActualWidth > 0 ? owner.ActualWidth : SystemParameters.WorkArea.Width;
        var availableHeight = owner?.ActualHeight > 0 ? owner.ActualHeight : SystemParameters.WorkArea.Height;
        MaxWidth = Math.Max(480, availableWidth * .92); MaxHeight = Math.Max(400, availableHeight * .85);
        Width = Math.Min(860, MaxWidth); MinWidth = Math.Min(560, MaxWidth); Height = Math.Min(620, MaxHeight); MinHeight = Math.Min(400, MaxHeight);
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var visible = customer.ManagementFlag == CustomerService.ActiveFlag;
        var state = visible ? "表示中" : "非表示";
        root.Children.Add(CustomerEditorDialog.CreateHeader("顧客詳細", $"{CustomerInput.FormatCode(customer.Id)}　{customer.Name}　／　{state}"));
        var cards = new Grid { Margin = new Thickness(2, 10, 2, 2) };
        var stacked = Width < 620;
        if (stacked)
        {
            cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        else
        {
            cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        var basic = InfoCard("会社情報", [("顧客ID", CustomerInput.FormatCode(customer.Id)), ("顧客名", customer.Name), ("電話番号", customer.ContactPhone),
            ("郵便番号", Display(customer.PostalCode)), ("住所", customer.Address), ("FAX", Display(customer.Fax))]);
        var management = InfoCard("取引・担当情報", [("担当営業所", customer.OfficeName), ("管理状態", state),
            ("非表示理由", visible ? "—" : Display(customer.HiddenReason))]);
        if (stacked)
        {
            Grid.SetRow(management, 1);
            basic.Margin = new Thickness(0, 0, 0, 10);
        }
        else
        {
            Grid.SetColumn(management, 1); basic.Margin = new Thickness(0, 0, 8, 0); management.Margin = new Thickness(8, 0, 0, 0);
        }
        cards.Children.Add(basic); cards.Children.Add(management);
        var scroll = new ScrollViewer { Content = cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var tabs = new TabControl { Margin = new Thickness(14), Background = AppTheme.Surface, BorderThickness = new Thickness(0) };
        tabs.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/SalesManagement_SysDev;component/Resources/EmployeeDetail.xaml", UriKind.Relative)
        });
        tabs.Items.Add(new TabItem { Header = "基本情報", Content = scroll });
        tabs.Items.Add(new TabItem { Header = "取引履歴（受注）", Content = CustomerService.CanViewOrders
            ? new CustomerOrderHistoryView(customer.Id)
            : Notice("受注の参照には管理者または営業担当者の権限が必要です。") });
        tabs.Items.Add(new TabItem { Header = "変更履歴", Content = Notice("顧客マスタの変更履歴は記録されていません。") });
        Grid.SetRow(tabs, 1); root.Children.Add(tabs);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(18, 10, 20, 14) };
        if (visible && canEdit)
        {
            var hide = new Button { Content = "非表示にする", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 0), Foreground = AppTheme.DangerText, BorderBrush = AppTheme.Danger };
            hide.Click += (_, _) => { NextAction = "hide"; Close(); }; buttons.Children.Add(hide);
            var edit = new Button { Content = "編集", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 0), Background = AppTheme.Primary, Foreground = Brushes.White, BorderBrush = AppTheme.Primary };
            edit.Click += (_, _) => { NextAction = "edit"; Close(); }; buttons.Children.Add(edit);
        }
        else if (canEdit && CustomerService.IsRestorable(customer.ManagementFlag, customer.HiddenReason))
        {
            var restore = new Button { Content = "表示を復元", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 0),
                Background = AppTheme.Primary, Foreground = Brushes.White, BorderBrush = AppTheme.Primary };
            restore.Click += (_, _) => { NextAction = "restore"; Close(); }; buttons.Children.Add(restore);
        }
        buttons.Children.Add(new Button { Content = "閉じる", Padding = new Thickness(16, 8, 16, 8), IsCancel = true, BorderBrush = AppTheme.Border });
        var footer = new Border { BorderBrush = AppTheme.Border, BorderThickness = new Thickness(0, 1, 0, 0), Child = buttons };
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        Content = new Border { Background = AppTheme.Surface, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = root };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }

    private static Border Notice(string text) => new()
    {
        Margin = new Thickness(2, 10, 2, 2), Padding = new Thickness(14), Background = AppTheme.Canvas,
        CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Top,
        Child = new TextBlock { Text = text, Foreground = AppTheme.SecondaryText, TextWrapping = TextWrapping.Wrap }
    };

    private static Border InfoCard(string title, IReadOnlyList<(string Label, string Value)> fields)
    {
        var stack = new StackPanel(); stack.Children.Add(new TextBlock { Text = title, Foreground = AppTheme.Primary, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < fields.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = fields[i].Label, Foreground = AppTheme.SecondaryText, Margin = new Thickness(0, 4, 8, 4), VerticalAlignment = VerticalAlignment.Top };
            var value = new TextBlock { Text = fields[i].Value, Foreground = AppTheme.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4), VerticalAlignment = VerticalAlignment.Top };
            Grid.SetRow(label, i); Grid.SetRow(value, i); Grid.SetColumn(value, 1); grid.Children.Add(label); grid.Children.Add(value);
        }
        stack.Children.Add(grid);
        return new Border { Background = AppTheme.Surface, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Child = stack };
    }
    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "未登録" : value;
}

internal sealed class CustomerHideDialog : Window
{
    private readonly TextBox reason = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 86, MaxHeight = 140,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10), Background = AppTheme.Surface, BorderBrush = AppTheme.InputBorder };
    public string Reason => reason.Text.Trim();

    public CustomerHideDialog(IReadOnlyCollection<CustomerRow> customers, Window? owner)
    {
        Owner = owner; Title = "顧客を非表示にしますか？"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
        WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        var ownerWidth = owner?.ActualWidth > 0 ? owner.ActualWidth : SystemParameters.WorkArea.Width;
        var ownerHeight = owner?.ActualHeight > 0 ? owner.ActualHeight : SystemParameters.WorkArea.Height;
        MaxWidth = Math.Max(360, ownerWidth * .9); MaxHeight = Math.Max(340, ownerHeight * .85); Width = Math.Min(540, MaxWidth); MinWidth = Math.Min(380, MaxWidth); SizeToContent = SizeToContent.Height;
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Border { Background = AppTheme.Primary, Padding = new Thickness(20, 0, 12, 0) };
        var headerGrid = new Grid(); headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.Children.Add(new TextBlock { Text = Title, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Content = "×", Width = 34, Height = 34, FontSize = 21, IsCancel = true, Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        Grid.SetColumn(close, 1); headerGrid.Children.Add(close); header.Child = headerGrid; root.Children.Add(header);
        var body = new StackPanel { Margin = new Thickness(22, 18, 22, 14) };
        body.Children.Add(new TextBlock { Text = customers.Count == 1 ? $"顧客 C-{customers.First().Id:0000} を非表示にしますか？" : $"選択した {customers.Count:N0} 件の顧客を非表示にしますか？", FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var entries = string.Join(Environment.NewLine, customers.Take(8).Select(x => $"顧客ID：{x.Code}　顧客名：{x.Name}\n管理状態：{x.ManagementState}"));
        if (customers.Count > 8) entries += $"\nほか {customers.Count - 8:N0} 件";
        body.Children.Add(new TextBlock { Text = entries, Background = AppTheme.Canvas, Padding = new Thickness(10), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        body.Children.Add(new TextBlock { Text = "非表示理由 *", FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 5) });
        var input = new Grid(); var placeholder = new TextBlock { Text = "非表示にする理由を入力してください", Foreground = AppTheme.SecondaryText, Margin = new Thickness(11, 12, 8, 0), IsHitTestVisible = false };
        var validation = new TextBlock { Text = "非表示理由を入力してください。", Foreground = AppTheme.DangerText, Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed };
        var hide = new Button { Content = "非表示にする", IsEnabled = false, Padding = new Thickness(16, 8, 16, 8),
            Background = AppTheme.Danger, Foreground = Brushes.White, BorderBrush = AppTheme.Danger };
        reason.TextChanged += (_, _) =>
        {
            var hasReason = !string.IsNullOrWhiteSpace(reason.Text);
            placeholder.Visibility = string.IsNullOrEmpty(reason.Text) ? Visibility.Visible : Visibility.Collapsed;
            validation.Visibility = hasReason ? Visibility.Collapsed : Visibility.Visible;
            hide.IsEnabled = hasReason;
        };
        input.Children.Add(reason); input.Children.Add(placeholder); body.Children.Add(input);
        body.Children.Add(validation);
        body.Children.Add(new TextBlock { Text = "非表示にすると、表示中の顧客一覧や新規受注の顧客候補に表示されなくなります。登録済みの受注や売上は変更されません。",
            Foreground = AppTheme.WarningText, Background = AppTheme.Canvas, Padding = new Thickness(10), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        var bodyScroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(bodyScroll, 1); root.Children.Add(bodyScroll);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 10, 20, 14) };
        actions.Children.Add(new Button { Content = "キャンセル", IsCancel = true, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 0), BorderBrush = AppTheme.Border });
        hide.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(reason.Text))
            {
                validation.Visibility = Visibility.Visible;
                reason.Focus();
                return;
            }
            DialogResult = true;
        };
        actions.Children.Add(hide);
        var footer = new Border { BorderBrush = AppTheme.Border, BorderThickness = new Thickness(0, 1, 0, 0), Child = actions };
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        Content = new Border { Background = AppTheme.Surface, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = root };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
    }
}
