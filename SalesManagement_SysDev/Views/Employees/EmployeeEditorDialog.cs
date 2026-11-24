using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;

namespace SalesManagement_SysDev;

internal sealed class EmployeeEditorDialog : Window
{
    private readonly EmployeeManagementViewModel viewModel;
    private readonly EmployeeRecord? original;
    private readonly TextBox employeeId = FormTextBox(7);
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
        if (isNew) AddField(basic, "社員ID（1～6桁） *", employeeId);
        else AddReadOnlyField(basic, "社員番号", original!.Code);
        AddField(basic, "社員名 *", name);
        AddField(basic, "所属営業所 *", office);
        AddReadOnlyField(basic, "入社日", original?.HireDate.ToString("yyyy/MM/dd") ?? "登録日");
        AddField(basic, "電話番号 *", phone);
        var basicCard = CardFrame(basic);
        content.Children.Add(basicCard);

        var account = Card("アカウント・権限");
        AddReadOnlyField(account, "ログインID", original?.LoginId ?? "入力した社員ID");
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
        employeeId.TextChanged += (_, _) => ValidateForm();
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
                ? await viewModel.CreateAsync(int.Parse(employeeId.Text), name.Text, phone.Text, (int)office.SelectedValue, (int)role.SelectedValue, passwordValue)
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
        if (original == null && !HasValidEmployeeId()) return false;
        if (original != null && !HasChanges()) return false;
        return original != null || password.Password.Length > 0 && password.Password == confirmation.Password;
    }

    private bool HasValidEmployeeId() => employeeId.Text.Length is >= 1 and <= 6
        && employeeId.Text.All(char.IsAsciiDigit) && int.TryParse(employeeId.Text, out var id) && id > 0;

    private void ValidateForm()
    {
        save.IsEnabled = CanSave();
        validation.Text = EmployeeService.Validate(name.Text, phone.Text, original == null ? password.Password : null)
            ?? (original == null && !HasValidEmployeeId() ? "社員IDは1～6桁の数字で入力してください。"
                : original == null && password.Password.Length == 0 ? "初期パスワードを入力してください。"
                : original == null && password.Password != confirmation.Password ? "初期パスワードと確認用パスワードが一致しません。"
                : office.SelectedValue is not int officeId || role.SelectedValue is not int roleId
                    || !viewModel.Offices.Any(x => x.Id == officeId) || !viewModel.Roles.Any(x => x.Id == roleId)
                    ? "有効な所属営業所と権限ロールを選択してください。"
                : original != null && !HasChanges() ? "変更内容はありません。" : "");
    }

    private bool HasChanges() => original == null
        ? employeeId.Text.Length != 0 || name.Text.Length != 0 || phone.Text.Length != 0 || office.SelectedValue != null || role.SelectedValue != null
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
