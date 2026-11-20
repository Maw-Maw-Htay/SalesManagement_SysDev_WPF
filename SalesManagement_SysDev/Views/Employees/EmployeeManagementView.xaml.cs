using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;

namespace SalesManagement_SysDev;

public partial class EmployeeManagementView : UserControl
{
    private EmployeeManagementViewModel ViewModel => (EmployeeManagementViewModel)DataContext;
    private bool openingDetail;
    public EmployeeManagementView() : this(new EmployeeManagementViewModel()) { }

    internal EmployeeManagementView(EmployeeManagementViewModel viewModel)
    {
        InitializeComponent(); DataContext = viewModel; UpdateTabs(viewModel.ActiveTab);
    }

    private void AllTab_Click(object sender, RoutedEventArgs e) => SetTab(EmployeeStatusTab.All);
    private void VisibleTab_Click(object sender, RoutedEventArgs e) => SetTab(EmployeeStatusTab.Visible);
    private void HiddenTab_Click(object sender, RoutedEventArgs e) => SetTab(EmployeeStatusTab.Hidden);
    private void SetTab(EmployeeStatusTab tab) { ViewModel.SelectTab(tab); UpdateTabs(tab); }
    private void UpdateTabs(EmployeeStatusTab active)
    {
        foreach (var (button, tab) in new[] { (AllTab, EmployeeStatusTab.All), (VisibleTab, EmployeeStatusTab.Visible), (HiddenTab, EmployeeStatusTab.Hidden) })
        {
            var selected = active == tab;
            button.Tag = selected ? "StatusActive" : null;
        }
    }
    private void ToggleFilters_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleFilters();
    private void StatusToolbar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (EmployeeTools == null) return;
        var stack = e.NewSize.Width < 590;
        Grid.SetRow(EmployeeTools, stack ? 1 : 0);
        Grid.SetColumn(EmployeeTools, stack ? 0 : 1);
        EmployeeTools.Margin = stack ? new Thickness(0, 6, 0, 0) : new Thickness(8, 0, 0, 0);
    }
    private void ClearConditions_Click(object sender, RoutedEventArgs e) => ViewModel.ClearConditions();
    private void ClearQuickSearch_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.QuickSearch = "";
        QuickSearchBox.Focus();
    }
    private void HireDate_ValidationError(object sender, DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        ViewModel.ReportInvalidDate();
    }
    private async void Reload_Click(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync();
    private void PreviousPage_Click(object sender, RoutedEventArgs e) => ViewModel.PageNumber--;
    private void NextPage_Click(object sender, RoutedEventArgs e) => ViewModel.PageNumber++;
    private void EmployeeGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        var direction = e.Column.SortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        foreach (var column in EmployeeGrid.Columns) column.SortDirection = null;
        e.Column.SortDirection = direction;
        ViewModel.SortBy(e.Column.SortMemberPath, direction);
    }
    private void ClearSelection_Click(object sender, RoutedEventArgs e) => ViewModel.ClearSelection();
    internal void OpenRegistration()
    {
        if (!ViewModel.CanCreate) return;
        var dialog = new EmployeeEditorDialog(ViewModel, null, Window.GetWindow(this));
        if (dialog.ShowDialog() == true)
            F_OrderNotice.Show(Window.GetWindow(this), "社員登録完了", dialog.ResultMessage, OrderNoticeKind.Success);
    }
    private void Roles_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanViewRoles)
            new EmployeeRolesDialog(ViewModel.Roles, Window.GetWindow(this)).ShowDialog();
    }
    private void EmployeeHistory_Click(object sender, RoutedEventArgs e) => OpenHistory();
    private void OpenHistory(int? employeeId = null)
    {
        if (Window.GetWindow(this) is F_Main shell)
            shell.ShowEmployeeHistory(employeeId);
    }
    private void OpenDetail_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EmployeeRow row) _ = OpenDetailAsync(row);
    }
    private void EmployeeGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source) return;
        for (var element = source; element != null && element != EmployeeGrid;
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element))
        {
            if (element is ButtonBase) return;
            if (element is DataGridRow { Item: EmployeeRow row })
            {
                e.Handled = true;
                _ = OpenDetailAsync(row);
                return;
            }
        }
    }
    private async Task OpenDetailAsync(EmployeeRow row)
    {
        if (openingDetail || !ViewModel.CanReload) return;
        openingDetail = true;
        try
        {
            EmployeeDetail? detail;
            var owner = Window.GetWindow(this);
            try { detail = await Task.Run(() => EmployeeService.GetDetail(row.Data.Id)); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Employee detail load failed: {ex}");
                if (IsLoaded)
                    F_OrderNotice.Show(owner, "社員詳細",
                        ex is UnauthorizedAccessException ? ex.Message : "社員情報を取得できませんでした。接続状態を確認してください。", OrderNoticeKind.Warning);
                return;
            }
            if (!IsLoaded) return;
            if (detail == null) { F_OrderNotice.Show(owner, "社員詳細", "社員情報を取得できませんでした。", OrderNoticeKind.Warning); return; }
            var dialog = new EmployeeDetailDialog(detail, EmployeeService.CanManage, owner);
            dialog.ShowDialog();
            switch (dialog.NextAction)
            {
                case EmployeeDetailAction.Edit: OpenEditor(detail.Employee); break;
                case EmployeeDetailAction.Disable: await HideRowAsync(new EmployeeRow(detail.Employee)); break;
                case EmployeeDetailAction.History: OpenHistory(detail.Employee.Id); break;
            }
        }
        finally { openingDetail = false; }
    }
    private void OpenEditor(EmployeeRecord employee)
    {
        if (!ViewModel.CanEdit || employee.ManagementFlag != EmployeeService.ActiveFlag) return;
        var dialog = new EmployeeEditorDialog(ViewModel, employee, Window.GetWindow(this));
        if (dialog.ShowDialog() == true)
            F_OrderNotice.Show(Window.GetWindow(this), "社員更新完了", dialog.ResultMessage, OrderNoticeKind.Success);
    }
    private async void HideSelected_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanHide) return;
        var row = ViewModel.Rows.SingleOrDefault(x => x.IsSelected && x.IsSelectable);
        if (row != null) await HideRowAsync(row);
    }
    private async Task HideRowAsync(EmployeeRow row)
    {
        if (!ViewModel.CanEdit || !row.IsSelectable) return;
        var dialog = new EmployeeHideDialog(row.Data, Window.GetWindow(this));
        if (dialog.ShowDialog() != true) return;
        var result = await ViewModel.HideAsync(row, dialog.Reason);
        F_OrderNotice.Show(Window.GetWindow(this), result.Succeeded ? "アカウント無効化完了" : "アカウント無効化の結果", result.Message,
            result.Succeeded ? OrderNoticeKind.Success : OrderNoticeKind.Warning);
    }
}

internal sealed class EmployeeEditorDialog : Window
{
    private readonly EmployeeManagementViewModel viewModel;
    private readonly EmployeeRecord? original;
    private readonly TextBox name = FormTextBox(50);
    private readonly TextBox phone = FormTextBox(13);
    private readonly ComboBox office = new() { Height = 38, DisplayMemberPath = nameof(EmployeeOffice.Name), SelectedValuePath = nameof(EmployeeOffice.Id) };
    private readonly ComboBox role = new() { Height = 38, DisplayMemberPath = nameof(EmployeeRole.Label), SelectedValuePath = nameof(EmployeeRole.Id) };
    private readonly PasswordBox password = FormPasswordBox();
    private readonly PasswordBox confirmation = FormPasswordBox();
    private readonly TextBlock validation = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 32, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button save = new() { MinWidth = 132, Height = 40, Padding = new Thickness(15, 7, 15, 7) };
    private bool isSubmitting;
    private bool saved;
    public string ResultMessage { get; private set; } = "";

    public EmployeeEditorDialog(EmployeeManagementViewModel viewModel, EmployeeRecord? original, Window? owner)
    {
        this.viewModel = viewModel; this.original = original;
        var isNew = original == null;
        Owner = owner; Title = isNew ? "社員登録" : $"社員更新 — {original!.Code}";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize; ShowInTaskbar = false;
        AllowsTransparency = true; Background = Brushes.Transparent;
        WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        var availableWidth = owner?.ActualWidth > 0 ? owner.ActualWidth : SystemParameters.WorkArea.Width;
        var availableHeight = owner?.ActualHeight > 0 ? owner.ActualHeight : SystemParameters.WorkArea.Height;
        MaxWidth = Math.Min(1000, availableWidth * .94); MinWidth = Math.Min(640, MaxWidth); Width = Math.Min(960, MaxWidth);
        MaxHeight = Math.Min(availableHeight * .88, SystemParameters.WorkArea.Height * .9); SizeToContent = SizeToContent.Height;
        office.ItemsSource = viewModel.Offices; role.ItemsSource = viewModel.Roles;
        if (original != null)
        {
            name.Text = original.Name; phone.Text = original.Phone; office.SelectedValue = original.OfficeId; role.SelectedValue = original.PositionId;
            // A current assignment may no longer be eligible for new registrations.
            if (office.SelectedIndex < 0)
                office.ItemsSource = viewModel.Offices.Append(new EmployeeOffice(original.OfficeId, $"{original.OfficeName}（変更が必要）"));
            if (role.SelectedIndex < 0)
                role.ItemsSource = viewModel.Roles.Append(new EmployeeRole(original.PositionId, original.RoleName, $"{original.RoleLabel}（変更が必要）"));
            office.SelectedValue = original.OfficeId; role.SelectedValue = original.PositionId;
            role.IsEnabled = original.Id != AppSession.Employee?.EmId;
        }

        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(Header(isNew ? "社員登録" : "社員更新", isNew ? "社員情報とログインアカウントを登録します。"
            : $"{original!.Code}　{original.Name}　／　{original.ManagementLabel}　／　ログイン{(original.CanLogin ? "利用可能" : "無効")}"));

        var content = new Grid { Margin = new Thickness(16) };
        var twoColumns = Width >= 610;
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (twoColumns) content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var basic = Card("基本情報");
        AddReadOnlyField(basic, "社員番号", original?.Code ?? "登録時に自動採番");
        AddField(basic, "社員名 *", name);
        AddField(basic, "所属営業所 *", office);
        AddReadOnlyField(basic, "入社日", original?.HireDate.ToString("yyyy/MM/dd") ?? "登録日");
        AddField(basic, "電話番号 *", phone);
        var basicCard = CardFrame(basic);
        content.Children.Add(basicCard);

        var account = Card("アカウント・権限");
        AddReadOnlyField(account, "ログインID", original?.LoginId ?? "自動採番される社員番号の数字部分");
        AddField(account, "権限ロール *", role);
        if (isNew)
        {
            AddField(account, "初期パスワード *", password);
            AddField(account, "初期パスワード（確認） *", confirmation);
            AddReadOnlyField(account, "パスワードの条件", "半角英数字で4～10文字");
        }
        else AddReadOnlyField(account, "ログイン状態", original!.CanLogin ? "利用可能" : "無効");
        var accountCard = CardFrame(account);
        if (twoColumns) { Grid.SetColumn(accountCard, 1); accountCard.Margin = new Thickness(10, 0, 0, 0); }
        else
        {
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(accountCard, 1); accountCard.Margin = new Thickness(0, 10, 0, 0);
        }
        content.Children.Add(accountCard);
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(18, 10, 20, 14) };
        validation.Foreground = AppTheme.DangerText;
        var cancel = new Button { Content = "キャンセル", IsCancel = true, Height = 40, MinWidth = 100, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        actions.Children.Add(cancel);
        save.Content = isNew ? "社員を登録" : "変更を保存"; save.Background = AppTheme.Primary; save.Foreground = Brushes.White; save.BorderBrush = AppTheme.Primary;
        save.IsDefault = true; save.Click += async (_, _) => await SaveAsync(); actions.Children.Add(save);
        var footer = new DockPanel { LastChildFill = true, Margin = new Thickness(18, 8, 18, 0) };
        DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions); footer.Children.Add(validation);
        var footerBorder = new Border { BorderBrush = AppTheme.Border, BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
        Grid.SetRow(footerBorder, 2); root.Children.Add(footerBorder);
        Content = new Border { Background = AppTheme.Surface, BorderBrush = AppTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = root };
        name.TextChanged += (_, _) => ValidateForm(); phone.TextChanged += (_, _) => ValidateForm();
        office.SelectionChanged += (_, _) => ValidateForm(); role.SelectionChanged += (_, _) => ValidateForm();
        password.PasswordChanged += (_, _) => ValidateForm(); confirmation.PasswordChanged += (_, _) => ValidateForm();
        Closing += (_, e) =>
        {
            if (saved) return;
            if (isSubmitting) { e.Cancel = true; return; }
            if (HasChanges() && !F_OrderNotice.Confirm(this, "未保存の変更", "入力した内容を破棄して閉じますか？", "破棄する", "入力に戻る"))
                e.Cancel = true;
        };
        ValidateForm();
    }

    private async Task SaveAsync()
    {
        if (isSubmitting || !CanSave()) return;
        var isNew = original == null;
        if (original is { } employee)
        {
            var changes = new List<string>();
            if (employee.Name != name.Text.Trim()) changes.Add($"社員名：{employee.Name} → {name.Text.Trim()}");
            if (employee.Phone != phone.Text.Trim()) changes.Add($"電話番号：{employee.Phone} → {phone.Text.Trim()}");
            if (employee.OfficeId != (int)office.SelectedValue)
                changes.Add($"所属営業所：{employee.OfficeName} → {((EmployeeOffice)office.SelectedItem).Name}");
            if (employee.PositionId != (int)role.SelectedValue)
                changes.Add($"権限ロール：{employee.RoleLabel} → {((EmployeeRole)role.SelectedItem).Label}");
            var message = $"社員番号：{employee.Code}\n社員名：{employee.Name}\n\n" + string.Join("\n", changes);
            if (!F_OrderNotice.Confirm(this, "社員情報変更保存の確認", $"この内容で保存しますか？\n\n{message}", "変更を保存"))
                return;
        }
        isSubmitting = true; save.IsEnabled = false;
        name.IsEnabled = phone.IsEnabled = office.IsEnabled = role.IsEnabled = password.IsEnabled = confirmation.IsEnabled = false;
        var passwordValue = isNew ? password.Password : "";
        try
        {
            var result = isNew
                ? await viewModel.CreateAsync(name.Text, phone.Text, (int)office.SelectedValue, (int)role.SelectedValue, passwordValue)
                : await viewModel.UpdateAsync(original!, name.Text, phone.Text, (int)office.SelectedValue, (int)role.SelectedValue);
            if (isNew) { password.Clear(); confirmation.Clear(); }
            if (result.Succeeded)
            {
                ResultMessage = result.EmployeeId is { } id && isNew
                    ? $"{result.Message}\n\n社員番号：E-{id:0000}\nログインID：{id}"
                    : result.Message;
                saved = true;
                DialogResult = true;
                return;
            }
            validation.Text = result.Message;
        }
        finally
        {
            isSubmitting = false;
            name.IsEnabled = phone.IsEnabled = office.IsEnabled = password.IsEnabled = confirmation.IsEnabled = true;
            role.IsEnabled = original == null || original.Id != AppSession.Employee?.EmId;
            save.IsEnabled = CanSave();
        }
    }

    private bool CanSave()
    {
        if (isSubmitting || viewModel.IsBusy || string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(phone.Text)
            || office.SelectedValue == null || role.SelectedValue == null) return false;
        if (EmployeeService.Validate(name.Text, phone.Text, original == null ? password.Password : null) != null) return false;
        if (!viewModel.Offices.Any(x => x.Id == (int)office.SelectedValue) || !viewModel.Roles.Any(x => x.Id == (int)role.SelectedValue)) return false;
        if (original != null && !HasChanges()) return false;
        return original != null || password.Password.Length > 0 && password.Password == confirmation.Password;
    }

    private void ValidateForm()
    {
        save.IsEnabled = CanSave();
        validation.Text = EmployeeService.Validate(name.Text, phone.Text, original == null ? password.Password : null)
            ?? (original == null && password.Password.Length == 0 ? "初期パスワードを入力してください。"
                : original == null && password.Password != confirmation.Password ? "初期パスワードと確認用パスワードが一致しません。"
                : office.SelectedValue is not int officeId || role.SelectedValue is not int roleId
                    || !viewModel.Offices.Any(x => x.Id == officeId) || !viewModel.Roles.Any(x => x.Id == roleId)
                    ? "有効な所属営業所と権限ロールを選択してください。"
                : original != null && !HasChanges() ? "変更内容はありません。" : "");
    }

    private bool HasChanges() => original == null
        ? name.Text.Length != 0 || phone.Text.Length != 0 || office.SelectedValue != null || role.SelectedValue != null
            || HasPasswordInput()
        : original.Name != name.Text.Trim() || original.Phone != phone.Text.Trim()
            || !Equals(original.OfficeId, office.SelectedValue) || !Equals(original.PositionId, role.SelectedValue);

    private bool HasPasswordInput()
    {
        using var first = password.SecurePassword;
        using var second = confirmation.SecurePassword;
        return first.Length > 0 || second.Length > 0;
    }

    internal static Border Header(string title, string subtitle)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock { Text = subtitle, Foreground = Brushes.White, Opacity = .85, FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
        grid.Children.Add(stack);
        var close = new Button { Content = "×", Width = 34, Height = 34, FontSize = 21, Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        System.Windows.Automation.AutomationProperties.SetName(close, "閉じる");
        Grid.SetColumn(close, 1); grid.Children.Add(close);
        var border = new Border { Background = AppTheme.Primary, Padding = new Thickness(16, 9, 12, 9), Child = grid };
        close.Click += (sender, _) => Window.GetWindow((DependencyObject)sender)?.Close();
        return border;
    }
    private static StackPanel Card(string title)
    {
        var stack = new StackPanel(); stack.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = AppTheme.Primary, Margin = new Thickness(0, 0, 0, 12) });
        return stack;
    }
    private static Border CardFrame(Panel child) => new()
    {
        Child = child, Background = AppTheme.Surface, BorderBrush = AppTheme.Border,
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14)
    };
    private static void AddField(Panel panel, string label, Control input)
    {
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.Medium, Foreground = AppTheme.Text, Margin = new Thickness(0, 8, 0, 4) });
        input.Height = 38; input.Margin = new Thickness(0, 0, 0, 4); panel.Children.Add(input);
    }
    private static void AddReadOnlyField(Panel panel, string label, string value)
    {
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.Medium, Foreground = AppTheme.SecondaryText, Margin = new Thickness(0, 8, 0, 4) });
        panel.Children.Add(new TextBlock { Text = value, Foreground = AppTheme.Text, TextWrapping = TextWrapping.Wrap, Background = AppTheme.Canvas, Padding = new Thickness(10, 8, 10, 8) });
    }
    private static TextBox FormTextBox(int maxLength) => new() { MaxLength = maxLength, Padding = new Thickness(9, 6, 9, 6), BorderBrush = AppTheme.InputBorder, Background = AppTheme.Surface };
    private static PasswordBox FormPasswordBox() => new() { MaxLength = 10, Padding = new Thickness(9, 6, 9, 6), BorderBrush = AppTheme.InputBorder, Background = AppTheme.Surface };
}

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
