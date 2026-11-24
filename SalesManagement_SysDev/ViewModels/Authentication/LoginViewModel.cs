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
        LoginCommand = new RelayCommand(async () => { if (DatabaseReady || await PrepareDatabaseAsync()) Login(loggedIn); }, () => !checking);
        ChangePasswordCommand = new RelayCommand(async () => { if (DatabaseReady || await PrepareDatabaseAsync()) changePassword(); }, () => !checking);
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
        "Failed" => "データベースの準備を完了できませんでした。",
        _ => "システムに接続しています…"
    };

    private bool checking;
    public async Task<bool> PrepareDatabaseAsync()
    {
        if (checking) return false;
        checking = true;
        LoginCommand.Refresh();
        ChangePasswordCommand.Refresh();
        try
        {
            var retry = connectionState == "Failed";
            SetConnection("Pending");
            return ShowDatabaseResult(await Task.Run(() => CheckDatabase(retry)));
        }
        finally
        {
            checking = false;
            LoginCommand.Refresh();
            ChangePasswordCommand.Refresh();
        }
    }

    public bool PrepareDatabase() => ShowDatabaseResult(CheckDatabase(connectionState == "Failed"));

    private enum DatabaseCheckResult { Ready, ConnectionFailed, SchemaFailed }

    private static DatabaseCheckResult CheckDatabase(bool resetPool)
    {
        var failure = DatabaseCheckResult.ConnectionFailed;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                if (SalesManagementContext.IsPracticeMode && !SalesManagementContext.IsDemoMode)
                    DatabaseInitializer.EnsureReady(resetPool || attempt > 0);
                using var db = new SalesManagementContext();
                if ((resetPool || attempt > 0) && (!SalesManagementContext.IsPracticeMode || SalesManagementContext.IsDemoMode))
                    SqlConnection.ClearPool((SqlConnection)db.Database.GetDbConnection());
                if (!(SalesManagementContext.IsDemoMode ? IsDemoReady(db) : db.Database.CanConnect()))
                    throw new InvalidOperationException($"{SalesManagementContext.CatalogName} に接続できません。");

                if (!SalesManagementContext.IsDemoMode && !SalesManagementContext.IsPracticeMode)
                {
                    db.Database.OpenConnection();
                    failure = DatabaseCheckResult.SchemaFailed;
                    using var probe = db.Database.GetDbConnection().CreateCommand();
                    probe.CommandText = """
                        SELECT
                            CASE WHEN COL_LENGTH(N'dbo.M_Employee', N'EmPasswordHash') IS NULL
                                OR OBJECT_ID(N'dbo.T_EmployeeAssignmentHistory', N'U') IS NULL THEN 1 ELSE 0 END
                          + CASE WHEN COL_LENGTH(N'dbo.T_Order', N'OrDeliveryDate') IS NULL THEN 2 ELSE 0 END
                          + CASE WHEN OBJECT_ID(N'dbo.T_OrderOperationHistory', N'U') IS NULL THEN 4 ELSE 0 END;
                        """;
                    var missing = (int)probe.ExecuteScalar()!;
                    if ((missing & 1) != 0) EmployeeSchemaInitializer.EnsureReady(db);
                    if ((missing & 2) != 0) OrderSchemaInitializer.EnsureReady(db);
                    if ((missing & 4) != 0) OrderHistoryService.EnsureReady(db);
                }
                return DatabaseCheckResult.Ready;
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Database connection check attempt {attempt + 1} failed: {exception}");
                if (attempt == 0) System.Threading.Thread.Sleep(500);
            }
        }

        return failure;
    }

    private bool ShowDatabaseResult(DatabaseCheckResult result)
    {
        if (result == DatabaseCheckResult.Ready)
        {
            SetConnection("Ready");
            ClearFieldErrors();
            ShowFeedback("", LoginFeedback.None);
            return true;
        }
        SetConnection("Failed");
        ClearFieldErrors();
        var failureMessage = result switch
        {
            DatabaseCheckResult.SchemaFailed => "DBには接続できましたが、必要なテーブルや列を準備できませんでした。DB管理者にスキーマと更新権限の確認を依頼してください。",
            _ when SalesManagementContext.IsDemoMode => "SalesManagement_Demo の準備を確認できませんでした。デモデータを準備してから「接続を再試行」を押してください。",
            _ when SalesManagementContext.IsPracticeMode => "SalesManagement_Practice を準備できませんでした。SQL Server LocalDB (MSSQLLocalDB) の状態を確認し、「接続を再試行」を押してください。",
            _ => "接続を確認できませんでした。設定したSQL Serverとデータベースの状態を確認し、「接続を再試行」を押してください。既定の接続先はLocalDBのSalesManagementです。"
        };
        ShowFeedback(failureMessage, LoginFeedback.ConnectionError);
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
