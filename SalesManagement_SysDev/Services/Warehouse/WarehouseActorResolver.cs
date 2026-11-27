using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace SalesManagement_SysDev;

internal static class WarehouseActorResolver
{
    public static bool CanUse => AppSession.Employee is { EmFlag: 0 }
        && (AppSession.IsAdmin || AppSession.IsLogistics);
    public static bool CanAdmin => AppSession.Employee is { EmFlag: 0 } && AppSession.IsAdmin;

    public static WarehouseActor? Resolve(SalesManagementContext db, int employeeId)
    {
        var session = AppSession.Employee;
        if (session is not { EmFlag: 0 } || session.EmId != employeeId) return null;
        var actor = db.MEmployees.AsNoTracking()
            .Where(x => x.EmId == employeeId && x.EmFlag == 0 && x.Po.PoFlag == 0)
            .Select(x => new WarehouseActor(x.EmId, x.SoId, x.Po.PoName)).SingleOrDefault();
        if (actor == null || actor.OfficeId != session.SoId
            || !string.Equals(actor.RoleName, AppSession.Role, StringComparison.Ordinal) || !actor.IsAllowed) return null;
        return actor;
    }

    public static void EnsureCanUse() { if (!CanUse) throw new UnauthorizedAccessException("入庫・出庫管理を利用する権限がありません。"); }
    public static void EnsureCanUse(WarehouseActor? actor)
    {
        if (actor is not { IsAllowed: true })
            throw new UnauthorizedAccessException("入庫・出庫管理を利用する権限がありません。ログイン状態を確認してください。");
    }
}

internal sealed record WarehouseActor(int EmployeeId, int OfficeId, string RoleName)
{
    public bool IsAdministrator => RoleName == "管理者";
    public bool IsAllowed => IsAdministrator || RoleName == "物流";
}
