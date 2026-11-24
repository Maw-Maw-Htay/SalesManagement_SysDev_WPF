using System.Data;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

public sealed record CustomerRecord(int Id, int OfficeId, string OfficeName, string Name, string ContactPhone,
    string PostalCode, string Address, string Fax, int ManagementFlag, string? HiddenReason);

public sealed record CustomerOffice(int Id, string Name);

public sealed record CustomerEditSnapshot(int Id, int OfficeId, string Name, string Phone, string PostalCode,
    string Address, string Fax, int ManagementFlag);

public sealed record CustomerOperationResult(bool Succeeded, string Message, int? CustomerId = null, int PossibleDuplicates = 0);

public static class CustomerInput
{
    public static string FormatCode(int id) => FormattableString.Invariant($"C-{id:0000}");

    internal static string Normalize(string value) => value.Trim().Normalize(NormalizationForm.FormKC);

    public static string NormalizePostalCode(string value) => Normalize(value).Replace("-", "");

    internal static string PhoneDigits(string value) => string.Concat(Normalize(value).Where(char.IsAsciiDigit));

    public static string PhoneError(string value, string? original = null)
    {
        var text = Normalize(value);
        if (text.Length == 0) return "電話番号を入力してください。";
        if (value.Trim().Length > 13) return "13文字以内で入力してください。";
        if (value.Trim() == original?.Trim()) return "";
        var digits = PhoneDigits(text);
        if (text.StartsWith("+81", StringComparison.Ordinal) && digits.StartsWith("81", StringComparison.Ordinal))
            digits = "0" + digits[2..];
        return digits.Length is 10 or 11 && digits[0] == '0'
            && text.TrimStart('+').All(c => char.IsAsciiDigit(c) || c is ' ' or '-' or '(' or ')')
            && (!text.StartsWith('+') || text.StartsWith("+81", StringComparison.Ordinal))
            ? "" : "日本の電話番号を半角数字やハイフンで入力してください。";
    }

    public static string PostalError(string value, string? original = null)
    {
        if (value.Trim() == original?.Trim() || string.IsNullOrWhiteSpace(value)) return "";
        var text = Normalize(value);
        var digits = NormalizePostalCode(value);
        return digits.Length == 7 && digits.All(char.IsAsciiDigit)
            && (text.Length == 7 || text.Length == 8 && text[3] == '-')
            ? "" : "郵便番号は7桁（例：123-4567）で入力してください。";
    }

    internal static bool IsPossibleDuplicate(string name, string phone, string address, string otherName, string otherPhone, string otherAddress)
    {
        static string Compact(string t) => string.Concat(Normalize(t).Where(c => !char.IsWhiteSpace(c)));
        var number = PhoneDigits(phone);
        var location = Compact(address);
        return string.Equals(Compact(name), Compact(otherName), StringComparison.OrdinalIgnoreCase)
            || number.Length >= 10 && number == PhoneDigits(otherPhone)
            || location.Length >= 5 && string.Equals(location, Compact(otherAddress), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>顧客マスタの読取・登録・更新・非表示／復元。DBスキーマは変更しない。</summary>
public static class CustomerService
{
    public const int ActiveFlag = 0;
    // Existing customer data uses 1 with a recorded hide reason. Older WPF builds
    // wrote 2 with a required reason; reasonless 2 records are not safe to restore.
    public const int HiddenFlag = 1;
    private const int PreviousWpfHiddenFlag = 2;
    public static bool IsRestorable(int flag, string? reason) =>
        flag == HiddenFlag || flag == PreviousWpfHiddenFlag && !string.IsNullOrWhiteSpace(reason);

    internal sealed record ActorScope(int Id, int OfficeId, string Role)
    {
        public bool IsAdmin => Role == "管理者";
        public bool CanMaintain => Role is "管理者" or "営業";
    }

    public static (IReadOnlyList<CustomerRecord> Customers, IReadOnlyList<CustomerOffice> Offices) Load()
    {
        using var db = new SalesManagementContext();
        var actor = GetActor(db);
        var customers = ProjectCustomers(ReadableCustomers(db, actor).OrderBy(x => x.ClId)).ToList();
        var officesQuery = db.MSalesOffices.AsNoTracking().Where(x => x.SoFlag == 0);
        if (!actor.IsAdmin) officesQuery = officesQuery.Where(x => x.SoId == actor.OfficeId);
        var offices = officesQuery.OrderBy(x => x.SoName).Select(x => new CustomerOffice(x.SoId, x.SoName)).ToList();
        return (customers, offices);
    }

    public static CustomerRecord? ReadDetail(int customerId)
    {
        using var db = new SalesManagementContext();
        return ProjectCustomers(ReadableCustomers(db, GetActor(db)).Where(x => x.ClId == customerId)).SingleOrDefault();
    }

    private static IQueryable<MClient> ReadableCustomers(SalesManagementContext db, ActorScope actor)
    {
        var query = db.MClients.AsNoTracking().Where(x => actor.IsAdmin || x.SoId == actor.OfficeId);
        if (!actor.CanMaintain) query = query.Where(x => x.ClFlag == ActiveFlag);
        return query;
    }

    private static IQueryable<CustomerRecord> ProjectCustomers(IQueryable<MClient> customers) =>
        customers.Select(x => new CustomerRecord(x.ClId, x.SoId, x.So.SoName, x.ClName, x.ClPhone,
            x.ClPostal, x.ClAddress, x.ClFax, x.ClFlag, x.ClHidden));

    public static CustomerOperationResult Create(int employeeId, string name, string phone, string postalCode,
        string address, string fax, int officeId, bool allowPossibleDuplicate = false)
    {
        using var db = new SalesManagementContext();
        return Create(db, employeeId, name, phone, postalCode, address, fax, officeId, allowPossibleDuplicate);
    }

    internal static CustomerOperationResult Create(SalesManagementContext db, int employeeId, string name, string phone,
        string postalCode, string address, string fax, int officeId, bool allowPossibleDuplicate)
    {
        if (!CanMaintain(employeeId)) return Denied();
        var invalid = Validate(name, phone, postalCode, address, fax);
        if (invalid != null) return new(false, invalid);
        try
        {
            using var tx = db.Database.BeginTransaction(IsolationLevel.Serializable);
            var actor = GetActor(db, employeeId);
            if (!CanAssignOffice(db, actor, officeId)) return new(false, "担当営業所を選択してください。");
            if (!allowPossibleDuplicate)
            {
                var possible = db.MClients.AsNoTracking().Where(x => actor.IsAdmin || x.SoId == actor.OfficeId)
                    .Select(x => new { x.ClName, x.ClPhone, x.ClAddress }).AsEnumerable()
                    .Count(x => CustomerInput.IsPossibleDuplicate(name, phone, address, x.ClName, x.ClPhone, x.ClAddress));
                if (possible > 0)
                    return new(false, "顧客名・電話番号・住所が一致する登録があります。", PossibleDuplicates: possible);
            }
            var client = new MClient
            {
                SoId = officeId, ClName = name.Trim(), ClPhone = phone.Trim(), ClPostal = CustomerInput.NormalizePostalCode(postalCode),
                ClAddress = address.Trim(), ClFax = fax.Trim(), ClFlag = ActiveFlag, ClHidden = null
            };
            db.MClients.Add(client);
            db.SaveChanges();
            tx.Commit();
            return new(true, "顧客を登録しました。", client.ClId);
        }
        catch (UnauthorizedAccessException) { return Denied(); }
        catch (Exception)
        {
            return new(false, "顧客を登録できませんでした。入力内容とデータベースの状態を確認してください。");
        }
    }

    public static CustomerOperationResult Update(int employeeId, CustomerEditSnapshot expected, string name,
        string phone, string postalCode, string address, string fax, int officeId)
    {
        using var db = new SalesManagementContext();
        return Update(db, employeeId, expected, name, phone, postalCode, address, fax, officeId);
    }

    internal static CustomerOperationResult Update(SalesManagementContext db, int employeeId, CustomerEditSnapshot expected,
        string name, string phone, string postalCode, string address, string fax, int officeId)
    {
        if (!CanMaintain(employeeId)) return Denied();
        var invalid = Validate(name, phone, postalCode, address, fax, expected);
        if (invalid != null) return new(false, invalid);
        try
        {
            using var tx = db.Database.BeginTransaction(IsolationLevel.Serializable);
            var actor = GetActor(db, employeeId);
            var client = FindForUpdate(db, actor, expected.Id);
            if (client == null || client.ClFlag != ActiveFlag) return new(false, "顧客が存在しないか、更新できる状態ではありません。");
            if (client.SoId != expected.OfficeId || client.ClName != expected.Name || client.ClPhone != expected.Phone
                || client.ClPostal != expected.PostalCode || client.ClAddress != expected.Address || client.ClFax != expected.Fax
                || client.ClFlag != expected.ManagementFlag)
                return new(false, "別の利用者が顧客情報を更新しました。最新情報を読み込み直してください。");
            if (!CanAssignOffice(db, actor, officeId)) return new(false, "担当営業所を選択してください。");
            client.ClName = name.Trim(); client.ClPhone = phone.Trim();
            if (postalCode.Trim() != expected.PostalCode.Trim()) client.ClPostal = CustomerInput.NormalizePostalCode(postalCode);
            client.ClAddress = address.Trim(); client.ClFax = fax.Trim(); client.SoId = officeId;
            db.SaveChanges();
            tx.Commit();
            return new(true, "顧客情報を更新しました。", client.ClId);
        }
        catch (UnauthorizedAccessException) { return Denied(); }
        catch (Exception)
        {
            return new(false, "顧客情報を更新できませんでした。一覧を再読込してからお試しください。");
        }
    }

    public static CustomerOperationResult Hide(int employeeId, int customerId, string? reason)
    {
        using var db = new SalesManagementContext();
        return Hide(db, employeeId, customerId, reason);
    }

    internal static CustomerOperationResult Hide(SalesManagementContext db, int employeeId, int customerId, string? reason)
    {
        if (!CanMaintain(employeeId)) return Denied();
        if (customerId <= 0) return new(false, "対象の顧客を確認してください。");
        if (string.IsNullOrWhiteSpace(reason)) return new(false, "非表示理由を入力してください。");
        try
        {
            using var tx = db.Database.BeginTransaction(IsolationLevel.Serializable);
            var actor = GetActor(db, employeeId);
            var client = FindForUpdate(db, actor, customerId);
            if (client == null) return new(false, "顧客が存在しないか、所属営業所の範囲外です。");
            if (client.ClFlag != ActiveFlag) return new(false, "顧客は表示中ではありません。");
            client.ClFlag = HiddenFlag;
            client.ClHidden = reason.Trim();
            db.SaveChanges();
            tx.Commit();
            return new(true, "顧客を非表示にしました。関連する受注・注文データは変更していません。", client.ClId);
        }
        catch (UnauthorizedAccessException) { return Denied(); }
        catch (Exception)
        {
            return new(false, "顧客を非表示にできませんでした。一覧を再読込してからお試しください。");
        }
    }

    public static CustomerOperationResult Restore(int employeeId, CustomerRecord expected)
    {
        using var db = new SalesManagementContext();
        return Restore(db, employeeId, expected);
    }

    internal static CustomerOperationResult Restore(SalesManagementContext db, int employeeId, CustomerRecord expected)
    {
        if (!CanMaintain(employeeId)) return Denied();
        if (expected.Id <= 0 || !IsRestorable(expected.ManagementFlag, expected.HiddenReason))
            return new(false, "この顧客の管理状態は復元対象ではありません。");
        try
        {
            using var tx = db.Database.BeginTransaction(IsolationLevel.Serializable);
            var actor = GetActor(db, employeeId);
            var client = FindForUpdate(db, actor, expected.Id);
            if (client == null || !IsRestorable(client.ClFlag, client.ClHidden)
                || client.ClFlag != expected.ManagementFlag || client.ClHidden != expected.HiddenReason || client.SoId != expected.OfficeId)
                return new(false, "顧客の状態が変更されたか、対象範囲外です。一覧を再読込してください。");
            client.ClFlag = ActiveFlag;
            client.ClHidden = null;
            db.SaveChanges();
            tx.Commit();
            return new(true, "顧客を表示中に戻しました。", client.ClId);
        }
        catch (UnauthorizedAccessException) { return Denied(); }
        catch (Exception) { return new(false, "顧客の表示を復元できませんでした。一覧を再読込してからお試しください。"); }
    }

    public static CustomerEditSnapshot Snapshot(CustomerRecord row) => new(row.Id, row.OfficeId, row.Name,
        row.ContactPhone, row.PostalCode, row.Address, row.Fax, row.ManagementFlag);

    public static bool CanView => CanEdit;
    public static bool CanViewHidden => CanEdit;
    public static bool CanEdit => AppSession.Employee is { EmFlag: 0, Po.PoFlag: 0 } && AppSession.CanManageOrders;
    public static bool CanViewOrders => CanEdit;

    internal static ActorScope GetActor(SalesManagementContext db, int? maintainingEmployeeId = null)
    {
        var session = AppSession.Employee;
        const string error = "顧客情報を表示または変更する権限がありません。再ログインしてください。";
        if (!CanView || session is not { EmFlag: 0, Po.PoFlag: 0 } || maintainingEmployeeId.HasValue && session.EmId != maintainingEmployeeId)
            throw new UnauthorizedAccessException(error);
        var actor = db.MEmployees.AsNoTracking().Where(x => x.EmId == session.EmId && x.EmFlag == 0 && x.Po.PoFlag == 0)
            .Select(x => new ActorScope(x.EmId, x.SoId, x.Po.PoName)).SingleOrDefault();
        if (actor == null || actor.OfficeId != session.SoId || actor.Role != session.Po.PoName
            || actor.Role is not ("管理者" or "営業") || maintainingEmployeeId.HasValue && !actor.CanMaintain)
            throw new UnauthorizedAccessException(error);
        return actor;
    }

    private static bool CanMaintain(int employeeId) => CanEdit && AppSession.Employee is { } actor
        && actor.EmId == employeeId && actor.EmFlag == 0;

    private static MClient? FindForUpdate(SalesManagementContext db, ActorScope actor, int customerId)
    {
        var query = db.MClients.FromSqlInterpolated($"SELECT * FROM dbo.M_Client WITH (UPDLOCK, HOLDLOCK) WHERE ClID = {customerId}");
        if (!actor.IsAdmin) query = query.Where(x => x.SoId == actor.OfficeId);
        return query.SingleOrDefault();
    }

    private static bool CanAssignOffice(SalesManagementContext db, ActorScope actor, int officeId) =>
        (actor.IsAdmin || actor.OfficeId == officeId) && db.MSalesOffices.Any(x => x.SoId == officeId && x.SoFlag == 0);

    private static CustomerOperationResult Denied() => new(false, "顧客情報を変更する権限がありません。画面を開き直してください。");

    private static string? Validate(string name, string phone, string postalCode, string address, string fax, CustomerEditSnapshot? original = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return "顧客名を入力してください。";
        if (name.Trim().Length > 50 || address.Trim().Length > 50 || fax.Trim().Length > 13)
            return "入力文字数の上限を超えています。顧客名・住所は50文字、FAXは13文字以内です。";
        var phoneError = CustomerInput.PhoneError(phone, original?.Phone);
        if (phoneError.Length > 0) return phoneError;
        var postalError = CustomerInput.PostalError(postalCode, original?.PostalCode);
        if (postalError.Length > 0) return postalError;
        return null;
    }
}
