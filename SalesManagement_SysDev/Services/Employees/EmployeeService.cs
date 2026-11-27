using System.Data;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public sealed record EmployeeRecord(int Id, string Name, string Phone, DateTime HireDate,
    int OfficeId, string OfficeName, int PositionId, string RoleName, int PositionFlag, int ManagementFlag, string? HiddenReason)
{
    public string Code => $"E-{Id:0000}";
    public string LoginId => Id.ToString();
    public string RoleLabel => EmployeeService.RoleLabel(RoleName);
    public bool CanLogin => ManagementFlag == EmployeeService.ActiveFlag && PositionFlag == 0;
    public string ManagementLabel => ManagementFlag switch
    {
        EmployeeService.ActiveFlag => "表示中", EmployeeService.HiddenFlag => "非表示", _ => "要確認"
    };
}

public sealed record EmployeeOffice(int Id, string Name);
public sealed record EmployeeRole(int Id, string Name, string Label);
public sealed record EmployeeAssignmentRecord(DateTimeOffset RecordedAt, string Operation,
    string BeforeOffice, string AfterOffice, string BeforeRole, string AfterRole, string ActorName);
public sealed record EmployeeDirectory(IReadOnlyList<EmployeeRecord> Employees,
    IReadOnlyList<EmployeeOffice> Offices, IReadOnlyList<EmployeeRole> Roles);
public sealed record EmployeeDetail(EmployeeRecord Employee, IReadOnlyList<EmployeeAssignmentRecord> Assignments);
public sealed record EmployeeEditSnapshot(int Id, string Name, string Phone, DateTime HireDate,
    int OfficeId, int PositionId, int ManagementFlag);
public sealed record EmployeeOperationResult(bool Succeeded, string Message, int? EmployeeId = null);

public static class EmployeeFilter
{
    public static bool Matches(EmployeeRecord row, string id, string name, string office,
        string role, string phone, int? managementFlag)
    {
        if (managementFlag.HasValue && row.ManagementFlag != managementFlag.Value) return false;
        if (!string.IsNullOrWhiteSpace(id)
            && !row.Code.Contains(id.Trim(), StringComparison.CurrentCultureIgnoreCase)
            && !row.LoginId.Contains(id.Trim(), StringComparison.CurrentCultureIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(name) && !row.Name.Contains(name.Trim(), StringComparison.CurrentCultureIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(office) && !row.OfficeName.Contains(office.Trim(), StringComparison.CurrentCultureIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(role)
            && !row.RoleLabel.Contains(role.Trim(), StringComparison.CurrentCultureIgnoreCase)
            && !row.RoleName.Contains(role.Trim(), StringComparison.CurrentCultureIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(phone) && !row.Phone.Contains(phone.Trim(), StringComparison.CurrentCultureIgnoreCase)) return false;
        return true;
    }
}

/// <summary>Employee administration. Credentials are never returned in read models.</summary>
public static class EmployeeService
{
    public const int ActiveFlag = 0;
    public const int HiddenFlag = 2;
    private static readonly string[] ApprovedRoleNames = ["管理者", "営業", "物流"];

    public static bool CanManage => AppSession.Employee is { EmFlag: ActiveFlag } && AppSession.IsAdmin;

    public static EmployeeDirectory Load()
    {
        EnsureCanManage();
        using var db = new SalesManagementContext();
        EnsureCurrentAdmin(db, AppSession.Employee!.EmId);
        var employees = db.MEmployees.AsNoTracking().Include(x => x.So).Include(x => x.Po)
            .OrderBy(x => x.EmId)
            .Select(x => new EmployeeRecord(x.EmId, x.EmName, x.EmPhone, x.EmHiredate, x.SoId, x.So.SoName,
                x.PoId, x.Po.PoName, x.Po.PoFlag, x.EmFlag, x.EmHidden)).ToList();
        var offices = db.MSalesOffices.AsNoTracking().Where(x => x.SoFlag == 0).OrderBy(x => x.SoName)
            .Select(x => new EmployeeOffice(x.SoId, x.SoName)).ToList();
        var roles = db.MPositions.AsNoTracking().Where(x => x.PoFlag == 0 && ApprovedRoleNames.Contains(x.PoName))
            .OrderBy(x => x.PoId).Select(x => new EmployeeRole(x.PoId, x.PoName, RoleLabel(x.PoName))).ToList();
        return new(employees, offices, roles);
    }

    public static EmployeeDetail? GetDetail(int employeeId)
    {
        EnsureCanManage();
        using var db = new SalesManagementContext();
        EnsureCurrentAdmin(db, AppSession.Employee!.EmId);
        var employee = db.MEmployees.AsNoTracking().Include(x => x.So).Include(x => x.Po)
            .Where(x => x.EmId == employeeId)
            .Select(x => new EmployeeRecord(x.EmId, x.EmName, x.EmPhone, x.EmHiredate, x.SoId, x.So.SoName,
                x.PoId, x.Po.PoName, x.Po.PoFlag, x.EmFlag, x.EmHidden)).SingleOrDefault();
        if (employee == null) return null;
        var assignments = db.EmployeeAssignmentHistories.AsNoTracking().Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.RecordedAt).ThenByDescending(x => x.Id)
            .Select(x => new EmployeeAssignmentRecord(x.RecordedAt, x.Operation,
                x.BeforeOfficeName ?? "—", x.AfterOfficeName, x.BeforeRoleName ?? "—", x.AfterRoleName, x.ActorName))
            .ToList();
        return new(employee, assignments);
    }

    public static EmployeeOperationResult Create(int actorId, int employeeId, string name, string phone,
        int officeId, int positionId, string initialPassword)
    {
        if (!CanManageActor(actorId)) return Denied();
        if (employeeId is < 1 or > 999999) return new(false, "社員IDは1～6桁の数字で入力してください。");
        var invalid = Validate(name, phone, initialPassword);
        if (invalid != null) return new(false, invalid);
        using var db = new SalesManagementContext();
        try
        {
            using var tx = db.Database.BeginTransaction(IsolationLevel.Serializable);
            EnsureCurrentAdmin(db, actorId);
            if (!TryResolveAssignment(db, officeId, positionId, out var office, out var position))
                return new(false, "有効な営業所と役割を選択してください。");
            if (db.MEmployees.Any(x => x.EmId == employeeId))
                return new(false, "この社員IDはすでに使われています。");
            var employee = new MEmployee
            {
                EmId = employeeId, EmName = name.Trim(), EmPhone = phone.Trim(), SoId = officeId, PoId = positionId,
                EmHiredate = DateTime.Today, EmFlag = ActiveFlag, EmHidden = null,
                EmPassword = initialPassword
            };
            db.MEmployees.Add(employee);
            db.SaveChanges();
            db.EmployeeAssignmentHistories.Add(new EmployeeAssignmentHistory
            {
                EmployeeId = employee.EmId, EmployeeName = employee.EmName, RecordedAt = DateTimeOffset.UtcNow,
                ActorEmployeeId = actorId, ActorName = CurrentActorName(), AfterOfficeId = office.SoId,
                AfterOfficeName = office.SoName, AfterPositionId = position.PoId, AfterRoleName = position.PoName,
                Operation = "社員登録"
            });
            db.SaveChanges(); tx.Commit();
            return new(true, "社員とログインアカウントを登録しました。", employee.EmId);
        }
        catch (UnauthorizedAccessException ex) { return new(false, ex.Message); }
        catch (Exception) { return new(false, "社員を登録できませんでした。入力内容とデータベースの状態を確認してください。"); }
    }

    public static EmployeeOperationResult Update(int actorId, EmployeeEditSnapshot expected,
        string name, string phone, int officeId, int positionId)
    {
        if (!CanManageActor(actorId)) return Denied();
        var invalid = Validate(name, phone, null);
        if (invalid != null) return new(false, invalid);
        using var db = new SalesManagementContext();
        try
        {
            using var tx = db.Database.BeginTransaction(IsolationLevel.Serializable);
            EnsureCurrentAdmin(db, actorId);
            var employee = db.MEmployees.FromSqlInterpolated($"SELECT * FROM dbo.M_Employee WITH (UPDLOCK, HOLDLOCK) WHERE EmID = {expected.Id}")
                .Include(x => x.So).Include(x => x.Po).SingleOrDefault();
            if (employee == null) return new(false, "社員が存在しません。");
            if (employee.EmName != expected.Name || employee.EmPhone != expected.Phone || employee.EmHiredate != expected.HireDate
                || employee.SoId != expected.OfficeId || employee.PoId != expected.PositionId || employee.EmFlag != expected.ManagementFlag)
                return new(false, "別の利用者が社員情報を更新しました。最新情報を読み込み直してください。");
            if (employee.EmFlag != ActiveFlag) return new(false, "非表示の社員は更新できません。");
            if (!TryResolveAssignment(db, officeId, positionId, out var office, out var position))
                return new(false, "有効な営業所と役割を選択してください。");
            if (employee.EmId == actorId && employee.PoId != positionId)
                return new(false, "ログイン中の自分自身の権限は変更できません。");
            var isAssignmentChanged = employee.SoId != officeId || employee.PoId != positionId;
            if (employee.Po.PoName == "管理者" && position.PoName != "管理者" && IsLastActiveAdministrator(db, employee.EmId))
                return new(false, "最後の有効な管理者の権限は変更できません。");
            var oldOffice = employee.So; var oldPosition = employee.Po;
            employee.EmName = name.Trim(); employee.EmPhone = phone.Trim(); employee.SoId = officeId; employee.PoId = positionId;
            db.SaveChanges();
            if (isAssignmentChanged)
            {
                db.EmployeeAssignmentHistories.Add(new EmployeeAssignmentHistory
                {
                    EmployeeId = employee.EmId, EmployeeName = employee.EmName, RecordedAt = DateTimeOffset.UtcNow,
                    ActorEmployeeId = actorId, ActorName = CurrentActorName(), BeforeOfficeId = oldOffice.SoId,
                    BeforeOfficeName = oldOffice.SoName, AfterOfficeId = office.SoId, AfterOfficeName = office.SoName,
                    BeforePositionId = oldPosition.PoId, BeforeRoleName = oldPosition.PoName,
                    AfterPositionId = position.PoId, AfterRoleName = position.PoName, Operation = "所属・権限変更"
                });
                db.SaveChanges();
            }
            tx.Commit();
            if (isAssignmentChanged) HistoryNotificationService.RecordNewManagementEdit();
            return new(true, "社員情報を更新しました。", employee.EmId);
        }
        catch (UnauthorizedAccessException ex) { return new(false, ex.Message); }
        catch (Exception) { return new(false, "社員情報を更新できませんでした。一覧を再読込してからお試しください。"); }
    }

    public static EmployeeOperationResult Hide(int actorId, int employeeId, string? reason)
    {
        if (!CanManageActor(actorId)) return Denied();
        if (employeeId <= 0) return new(false, "対象の社員を確認してください。");
        var reasonError = ValidateHideReason(reason);
        if (reasonError != null) return new(false, reasonError);
        if (actorId == employeeId) return new(false, "ログイン中の自分自身は非表示にできません。");
        using var db = new SalesManagementContext();
        try
        {
            using var tx = db.Database.BeginTransaction(IsolationLevel.Serializable);
            EnsureCurrentAdmin(db, actorId);
            var employee = db.MEmployees.FromSqlInterpolated($"SELECT * FROM dbo.M_Employee WITH (UPDLOCK, HOLDLOCK) WHERE EmID = {employeeId}")
                .Include(x => x.Po).SingleOrDefault();
            if (employee == null) return new(false, "社員が存在しません。");
            if (employee.EmFlag != ActiveFlag) return new(false, "社員は表示中ではありません。");
            if (employee.Po.PoName == "管理者" && IsLastActiveAdministrator(db, employee.EmId))
                return new(false, "最後の有効な管理者は非表示にできません。");
            employee.EmFlag = HiddenFlag;
            employee.EmHidden = reason!.Trim();
            db.SaveChanges(); tx.Commit();
            return new(true, "社員を非表示にしました。ログインも無効になり、社員データと取引履歴は保持されます。", employee.EmId);
        }
        catch (UnauthorizedAccessException ex) { return new(false, ex.Message); }
        catch (Exception) { return new(false, "社員を非表示にできませんでした。一覧を再読込してからお試しください。"); }
    }

    public static EmployeeEditSnapshot Snapshot(EmployeeRecord row) =>
        new(row.Id, row.Name, row.Phone, row.HireDate, row.OfficeId, row.PositionId, row.ManagementFlag);

    public static string RoleLabel(string role) => role switch
    {
        "営業" => "営業担当", "物流" => "物流担当", "管理者" => "管理者", _ => role
    };

    public static string? Validate(string name, string phone, string? password)
    {
        if (string.IsNullOrWhiteSpace(name)) return "社員名を入力してください。";
        if (name.Trim().Length > 50) return "社員名は50文字以内で入力してください。";
        if (string.IsNullOrWhiteSpace(phone)) return "電話番号を入力してください。";
        if (phone.Trim().Length > 13) return "電話番号は13文字以内で入力してください。";
        if (password != null && (password.Length is < 4 or > 10 || !password.All(char.IsAsciiLetterOrDigit)))
            return "初期パスワードは半角英数字4～10文字で入力してください。";
        return null;
    }

    public static string? ValidateHideReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "非表示理由を入力してください。";
        if (reason.Trim().Length > 500) return "非表示理由は500文字以内で入力してください。";
        return null;
    }

    private static bool TryResolveAssignment(SalesManagementContext db, int officeId, int positionId,
        out MSalesOffice office, out MPosition position)
    {
        office = db.MSalesOffices.SingleOrDefault(x => x.SoId == officeId && x.SoFlag == 0)!;
        position = db.MPositions.SingleOrDefault(x => x.PoId == positionId && x.PoFlag == 0 && ApprovedRoleNames.Contains(x.PoName))!;
        return office != null && position != null;
    }

    private static bool IsLastActiveAdministrator(SalesManagementContext db, int exceptEmployeeId) =>
        !db.MEmployees.Any(x => x.EmId != exceptEmployeeId && x.EmFlag == ActiveFlag
            && x.Po.PoFlag == 0 && x.Po.PoName == "管理者");

    private static void EnsureCurrentAdmin(SalesManagementContext db, int employeeId)
    {
        if (!db.MEmployees.Any(x => x.EmId == employeeId && x.EmFlag == ActiveFlag && x.Po.PoFlag == 0 && x.Po.PoName == "管理者"))
            throw new UnauthorizedAccessException("有効な管理者のみ社員情報を管理できます。画面を開き直してください。");
    }

    private static void EnsureCanManage()
    {
        if (!CanManage || AppSession.Employee == null) throw new UnauthorizedAccessException("社員情報を管理する権限がありません。");
    }

    private static bool CanManageActor(int actorId) => CanManage && AppSession.Employee?.EmId == actorId;
    private static string CurrentActorName() => AppSession.Employee?.EmName ?? "";
    private static EmployeeOperationResult Denied() => new(false, "社員情報を管理する権限がありません。管理者としてログインし直してください。");
}
