namespace SalesManagement_SysDev;

public sealed record OrderEntrySession(string EmployeeName, string Role, string OfficeName)
{
    internal static OrderEntrySession Create(string? orderOffice = null)
    {
        var employee = AppSession.Employee;
        var office = orderOffice ?? employee?.So?.SoName;
        var prefix = SalesManagementContext.IsDemoMode ? "［デモ］ " : "";
        return new(prefix + employee?.EmName, AppSession.Role, string.IsNullOrWhiteSpace(office) ? "未設定" : office);
    }
}
