using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
namespace SalesManagement_SysDev;

public sealed class F_PasswordChange : Window
{
    private readonly TextBox employeeIdBox = new();
    private readonly PasswordBox currentPasswordBox = new();
    private readonly PasswordBox newPasswordBox = new();
    private readonly PasswordBox confirmationBox = new();
    private readonly TextBlock feedback = new();
    private readonly Button saveButton = new();

    public int EmployeeId { get; private set; }
    public string ChangedPassword { get; private set; } = "";

    public F_PasswordChange(string? employeeId = null, string? currentPassword = null)
    {
        Title = "パスワード変更";
        Width = 480;
        Height = 600;
        MinWidth = 420;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = AppTheme.Canvas;
        FontFamily = AppTypography.FontFamily;
        FontSize = AppTypography.Body.Size;
        FontWeight = AppTypography.Body.Weight;

        employeeIdBox.Text = employeeId ?? "";
        employeeIdBox.MaxLength = 10;
        currentPasswordBox.Password = currentPassword ?? "";
        foreach (var input in new Control[] { employeeIdBox, currentPasswordBox, newPasswordBox, confirmationBox })
        {
            input.Height = 40;
            input.Padding = new Thickness(10, 7, 10, 7);
            input.FontSize = AppTypography.Input.Size;
            input.Background = AppTheme.Surface;
            input.BorderBrush = AppTheme.InputBorder;
        }
        currentPasswordBox.MaxLength = 10;
        newPasswordBox.MaxLength = 10;
        confirmationBox.MaxLength = 10;

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Border
        {
            Background = AppTheme.Primary,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24),
            Margin = new Thickness(0, 0, 0, 15)
        };
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "新しいパスワードを設定", Foreground = Brushes.White, FontSize = AppTypography.PageTitle.Size, FontWeight = AppTypography.PageTitle.Weight, TextWrapping = TextWrapping.Wrap });
        heading.Children.Add(new TextBlock { Text = "アカウントを確認してから変更します。", Foreground = AppTheme.Surface, FontSize = AppTypography.Section.Size, Margin = new Thickness(0, 5, 0, 0) });
        header.Child = heading;
        root.Children.Add(header);

        var form = new StackPanel { Margin = new Thickness(4, 0, 4, 8), VerticalAlignment = VerticalAlignment.Center };
        AddField(form, "社員ID", employeeIdBox, "社員IDを数字で入力");
        AddField(form, "現在のパスワード", currentPasswordBox, "現在のパスワードを入力");
        AddField(form, "新しいパスワード", newPasswordBox, "半角英数字4～10文字");
        AddField(form, "新しいパスワード（確認）", confirmationBox, "もう一度入力");
        feedback.Margin = new Thickness(0, 12, 0, 0);
        feedback.TextWrapping = TextWrapping.Wrap;
        feedback.FontSize = AppTypography.Validation.Size;
        feedback.MinHeight = 32;
        form.Children.Add(feedback);
        Grid.SetRow(form, 1);
        root.Children.Add(form);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelButton = new Button
        {
            Content = "キャンセル",
            Width = 100,
            Height = 40,
            Margin = new Thickness(0, 0, 8, 0),
            Background = AppTheme.Surface,
            BorderBrush = AppTheme.Border,
            Foreground = AppTheme.Text,
            IsCancel = true
        };
        cancelButton.Click += (_, _) => Close();
        saveButton.Content = "変更する";
        saveButton.Width = 128;
        saveButton.Height = 40;
        saveButton.Background = AppTheme.Primary;
        saveButton.Foreground = Brushes.White;
        saveButton.BorderBrush = AppTheme.Primary;
        saveButton.FontWeight = AppTypography.Body.Weight;
        saveButton.IsEnabled = false;
        saveButton.Click += SavePassword_Click;
        actions.Children.Add(cancelButton);
        actions.Children.Add(saveButton);
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);
        Content = root;

        employeeIdBox.TextChanged += (_, _) => RefreshValidation();
        currentPasswordBox.PasswordChanged += (_, _) => RefreshValidation();
        newPasswordBox.PasswordChanged += (_, _) => RefreshValidation();
        confirmationBox.PasswordChanged += (_, _) => RefreshValidation();
        Loaded += (_, _) =>
        {
            RefreshValidation();
            if (string.IsNullOrWhiteSpace(employeeIdBox.Text)) employeeIdBox.Focus();
            else if (string.IsNullOrEmpty(currentPasswordBox.Password)) currentPasswordBox.Focus();
            else newPasswordBox.Focus();
        };
    }

    private static void AddField(Panel parent, string label, Control input, string tooltip)
    {
        parent.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = AppTheme.Text,
            FontSize = AppTypography.Label.Size,
            FontWeight = AppTypography.Label.Weight,
            Margin = new Thickness(0, 10, 0, 5)
        });
        input.ToolTip = tooltip;
        parent.Children.Add(input);
    }

    private void RefreshValidation()
    {
        var employeeIdValid = int.TryParse(employeeIdBox.Text.Trim(), out var id) && id > 0;
        var current = currentPasswordBox.Password;
        var candidate = newPasswordBox.Password;
        var confirmation = confirmationBox.Password;
        var passwordValid = candidate.Length is >= 4 and <= 10 && candidate.All(char.IsAsciiLetterOrDigit);
        var different = !string.Equals(current, candidate, StringComparison.Ordinal);
        var matches = string.Equals(candidate, confirmation, StringComparison.Ordinal);

        saveButton.IsEnabled = employeeIdValid && current.Length > 0 && passwordValid && different && matches;
        feedback.Foreground = saveButton.IsEnabled
            ? AppTheme.SuccessText
            : AppTheme.Text;
        feedback.Text = !employeeIdValid
            ? "社員IDを数字で入力してください。"
            : current.Length == 0
                ? "現在のパスワードを入力してください。"
                : candidate.Length == 0
                    ? "新しいパスワードを入力してください。"
                    : !passwordValid
                        ? "新しいパスワードは半角英数字4～10文字で入力してください。"
                        : !different
                            ? "現在とは異なるパスワードを設定してください。"
                            : confirmation.Length == 0
                                ? "確認欄にも同じパスワードを入力してください。"
                                : !matches
                                    ? "新しいパスワードが一致していません。"
                                    : "入力内容を確認しました。変更できます。";
    }

    private void SavePassword_Click(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(employeeIdBox.Text.Trim(), out var id)) return;
        try
        {
            if (!BusinessService.ChangePassword(id, currentPasswordBox.Password, newPasswordBox.Password, out var message))
            {
                feedback.Foreground = AppTheme.Danger;
                feedback.Text = message;
                currentPasswordBox.Clear();
                currentPasswordBox.Focus();
                return;
            }
            EmployeeId = id;
            ChangedPassword = newPasswordBox.Password;
            DialogResult = true;
        }
        catch
        {
            feedback.Foreground = AppTheme.Danger;
            feedback.Text = "パスワードを変更できませんでした。接続を確認して、もう一度お試しください。";
        }
    }
}
