using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public enum LoginFeedback { None, EmployeeIdError, PasswordError, CredentialsError, ConnectionError, Information, Success }

public sealed class LoginViewModel : ViewModelBase
{
    private string employeeId = "", password = "", message = "", connectionState = "Pending";
    private bool passwordIsVisible, employeeIdInvalid, passwordInvalid;
    private LoginFeedback feedback;
    public LoginViewModel(Action loggedIn, Action changePassword)
    {
        LoginCommand = new RelayCommand(() => Login(loggedIn));
        ChangePasswordCommand = new RelayCommand(() => { if (DatabaseReady || PrepareDatabase()) changePassword(); });
        ForgotPasswordCommand = new RelayCommand(() =>
        {
            ClearFieldErrors();
            ShowFeedback("パスワードを忘れた場合は、管理者へお問い合わせください。", LoginFeedback.Information);
        });
        TogglePasswordCommand = new RelayCommand(() => { PasswordIsVisible = !PasswordIsVisible; FocusRequested?.Invoke("Password"); });
    }
    public RelayCommand LoginCommand { get; }
    public RelayCommand ChangePasswordCommand { get; }
    public RelayCommand ForgotPasswordCommand { get; }
    public RelayCommand TogglePasswordCommand { get; }
    public event Action<string>? FocusRequested;
    public string EmployeeId
    {
        get => employeeId;
        set
        {
            if (!Set(ref employeeId, value)) return;
            EmployeeIdInvalid = false;
            if (feedback == LoginFeedback.CredentialsError) PasswordInvalid = false;
            if (feedback is LoginFeedback.EmployeeIdError or LoginFeedback.CredentialsError or LoginFeedback.Information or LoginFeedback.Success) ShowFeedback("", LoginFeedback.None);
        }
    }
    public string Password
    {
        get => password;
        set
        {
            if (!Set(ref password, value)) return;
            PasswordInvalid = false;
            if (feedback == LoginFeedback.CredentialsError) EmployeeIdInvalid = false;
            if (feedback is LoginFeedback.PasswordError or LoginFeedback.CredentialsError or LoginFeedback.Information or LoginFeedback.Success) ShowFeedback("", LoginFeedback.None);
        }
    }
    public bool PasswordIsVisible { get => passwordIsVisible; set => Set(ref passwordIsVisible, value); }
    public bool EmployeeIdInvalid { get => employeeIdInvalid; private set => Set(ref employeeIdInvalid, value); }
    public bool PasswordInvalid { get => passwordInvalid; private set => Set(ref passwordInvalid, value); }
    public string Message => message;
    public LoginFeedback Feedback => feedback;
    public string ConnectionState => connectionState;
    public bool DatabaseReady => connectionState == "Ready";
    public string LoginLabel => connectionState == "Failed" ? "接続を再試行" : "ログイン";
    public string ConnectionMessage => connectionState switch
    {
        "Ready" => "システムに接続しました。ログインできます。",
        "Failed" => "システムに接続できません。管理者へお問い合わせください。",
        _ => "システムに接続しています…"
    };

    public bool PrepareDatabase()
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                // Login health checks must not create schema or seed data. They only probe the existing database.
                using var db = new SalesManagementContext();
                if (connectionState == "Failed" || attempt > 0)
                    SqlConnection.ClearPool((SqlConnection)db.Database.GetDbConnection());
                if (!(SalesManagementContext.IsDemoMode ? IsDemoReady(db) : db.Database.CanConnect()))
                    throw new InvalidOperationException($"{SalesManagementContext.CatalogName} に接続できません。");

                SetConnection("Ready");
                ClearFieldErrors();
                ShowFeedback("", LoginFeedback.None);
                return true;
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Database connection check attempt {attempt + 1} failed: {exception}");
                if (attempt == 0) System.Threading.Thread.Sleep(500);
            }
        }

        SetConnection("Failed");
        ClearFieldErrors();
        ShowFeedback(SalesManagementContext.IsDemoMode
            ? "SalesManagement_Demo の準備を確認できませんでした。デモデータを準備してから「接続を再試行」を押してください。"
            : "接続を確認できませんでした。SQL Server LocalDB (MSSQLLocalDB) と SalesManagement データベースの状態を確認し、「接続を再試行」を押してください。",
            LoginFeedback.ConnectionError);
        return false;
    }

    private static bool IsDemoReady(SalesManagementContext db)
    {
        var connection = db.Database.GetDbConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM sys.extended_properties
            WHERE DB_NAME() = N'SalesManagement_Demo' AND class = 0
              AND name = N'KansaiOADemoReady' AND CONVERT(nvarchar(32), value) = N'KansaiOA/v1';
            """;
        return command.ExecuteScalar() is 1;
    }
    private void Login(Action loggedIn)
    {
        if (!DatabaseReady && !PrepareDatabase()) return;
        if (!int.TryParse(EmployeeId.Trim(), out var id) || id <= 0)
        {
            EmployeeIdInvalid = true;
            ShowFeedback("社員IDは1以上の数字で入力してください。", LoginFeedback.EmployeeIdError);
            FocusRequested?.Invoke("EmployeeId");
            return;
        }
        if (string.IsNullOrWhiteSpace(Password))
        {
            PasswordInvalid = true;
            ShowFeedback("パスワードを入力してください。", LoginFeedback.PasswordError);
            FocusRequested?.Invoke("Password");
            return;
        }
        ShowFeedback("", LoginFeedback.None);
        try
        {
            var employee = BusinessService.Login(id, Password);
            if (employee == null)
            {
                Password = "";
                EmployeeIdInvalid = PasswordInvalid = true;
                ShowFeedback("社員IDまたはパスワードが正しくありません。", LoginFeedback.CredentialsError);
                FocusRequested?.Invoke("Password");
                return;
            }
            AppSession.Employee = employee;
            loggedIn();
            Password = "";
        }
        catch
        {
            SetConnection("Failed");
            ClearFieldErrors();
            ShowFeedback("ログインできませんでした。しばらくしてからもう一度お試しください。", LoginFeedback.ConnectionError);
        }
    }

    public void PasswordChanged(int id, string changedPassword)
    {
        EmployeeId = id.ToString(); PasswordIsVisible = false; Password = changedPassword;
        ClearFieldErrors();
        ShowFeedback("パスワードを変更しました。新しいパスワードでログインしてください。", LoginFeedback.Success);
        FocusRequested?.Invoke("Login");
    }
    private void ClearFieldErrors() { EmployeeIdInvalid = PasswordInvalid = false; }
    private void ShowFeedback(string text, LoginFeedback kind)
    {
        message = text; feedback = kind;
        Notify(nameof(Message)); Notify(nameof(Feedback));
    }
    private void SetConnection(string state)
    {
        connectionState = state;
        foreach (var name in new[] { nameof(ConnectionState), nameof(ConnectionMessage), nameof(LoginLabel), nameof(DatabaseReady) }) Notify(name);
    }
}
