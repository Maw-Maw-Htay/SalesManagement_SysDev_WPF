using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public enum LoginFeedback { None, EmployeeIdError, PasswordError, CredentialsError, ConnectionError, Information, Success }

public sealed class LoginViewModel : ViewModelBase
{
    private string employeeId = "", password = "", message = "", connectionState = "Pending";
    private bool passwordIsVisible, employeeIdInvalid, passwordInvalid, isBusy;
    private readonly CancellationToken lifetime;
    private LoginFeedback feedback;
    public LoginViewModel(Action loggedIn, Action changePassword, CancellationToken lifetime = default)
    {
        this.lifetime = lifetime;
        LoginCommand = new RelayCommand(async () => await LoginAsync(loggedIn), () => CanEditCredentials);
        ChangePasswordCommand = new RelayCommand(async () =>
        {
            if ((DatabaseReady || await PrepareDatabaseAsync()) && !lifetime.IsCancellationRequested) changePassword();
        }, () => CanEditCredentials);
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
    public bool CanEditCredentials => !isBusy && !lifetime.IsCancellationRequested;
    public string LoginLabel => connectionState == "Failed" ? "接続を再試行" : "ログイン";
    public string ConnectionMessage => connectionState switch
    {
        "Ready" => "システムに接続しました。ログインできます。",
        "Failed" => "システムに接続できません。管理者へお問い合わせください。",
        _ => "システムに接続しています…"
    };

    public async Task<bool> PrepareDatabaseAsync()
    {
        if (!CanEditCredentials) return false;
        var resetPool = connectionState == "Failed";
        SetBusy(true);
        SetConnection("Pending");
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                // Login health checks must not create schema or seed data. They only probe the existing database.
                try
                {
                    using var db = new SalesManagementContext();
                    if (resetPool || attempt > 0)
                        SqlConnection.ClearPool((SqlConnection)db.Database.GetDbConnection());
                    if (!(SalesManagementContext.IsDemoMode ? await IsDemoReadyAsync(db, lifetime) : await db.Database.CanConnectAsync(lifetime)))
                        throw new InvalidOperationException($"{SalesManagementContext.CatalogName} に接続できません。");

                    lifetime.ThrowIfCancellationRequested();
                    SetConnection("Ready");
                    ClearFieldErrors();
                    ShowFeedback("", LoginFeedback.None);
                    return true;
                }
                catch (Exception exception)
                {
                    lifetime.ThrowIfCancellationRequested();
                    System.Diagnostics.Debug.WriteLine($"Database connection check attempt {attempt + 1} failed: {exception}");
                    if (attempt == 0) await Task.Delay(500, lifetime);
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
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return false; }
        finally { SetBusy(false); }
    }

    private static async Task<bool> IsDemoReadyAsync(SalesManagementContext db, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM sys.extended_properties
            WHERE DB_NAME() = N'SalesManagement_Demo' AND class = 0
              AND name = N'KansaiOADemoReady' AND CONVERT(nvarchar(32), value) = N'KansaiOA/v1';
            """;
        return await command.ExecuteScalarAsync(cancellationToken) is 1;
    }
    private async Task LoginAsync(Action loggedIn)
    {
        if (!DatabaseReady && !await PrepareDatabaseAsync()) return;
        if (!CanEditCredentials) return;
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
            MEmployee? employee;
            SetBusy(true);
            try { employee = await BusinessService.LoginAsync(id, Password, lifetime); }
            finally { SetBusy(false); }
            if (lifetime.IsCancellationRequested) return;
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
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch
        {
            if (lifetime.IsCancellationRequested) return;
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
    private void SetBusy(bool busy)
    {
        isBusy = busy;
        Notify(nameof(CanEditCredentials));
        LoginCommand.Refresh();
        ChangePasswordCommand.Refresh();
    }
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
