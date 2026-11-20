using System.Windows;

namespace SalesManagement_SysDev;

public partial class F_login : Window
{
    private readonly LoginViewModel viewModel;

    public F_login()
    {
        InitializeComponent();
        if (SalesManagementContext.IsDemoMode)
        {
            Title += "［デモ］";
            LoginSubtitle.Text = "関西OA機器販売株式会社（架空）｜デモ環境";
        }
        viewModel = new LoginViewModel(() =>
        {
            new F_Main { WindowState = WindowState.Maximized }.Show();
            Close();
        }, ChangePassword);
        DataContext = viewModel;
        viewModel.FocusRequested += FocusInput;
        viewModel.PropertyChanged += (_, e) =>
        {
            // PasswordBox has no bindable Password property; keep that view concern here.
            if (e.PropertyName == nameof(LoginViewModel.Password) && txtPassword.Password != viewModel.Password)
                txtPassword.Password = viewModel.Password;
        };
        Loaded += async (_, _) => { await viewModel.PrepareDatabaseAsync(); txtEmployeeId.Focus(); };
    }

    private void PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel model) model.Password = txtPassword.Password;
    }

    private void FocusInput(string input)
    {
        if (input == "EmployeeId") { txtEmployeeId.Focus(); txtEmployeeId.SelectAll(); }
        else if (input == "Login") loginButton.Focus();
        else if (viewModel.PasswordIsVisible)
        {
            txtPasswordVisible.Focus();
            txtPasswordVisible.CaretIndex = txtPasswordVisible.Text.Length;
        }
        else txtPassword.Focus();
    }

    private void ChangePassword()
    {
        var dialog = new F_PasswordChange(viewModel.EmployeeId.Trim(), viewModel.Password) { Owner = this };
        if (dialog.ShowDialog() == true) viewModel.PasswordChanged(dialog.EmployeeId, dialog.ChangedPassword);
    }
}
