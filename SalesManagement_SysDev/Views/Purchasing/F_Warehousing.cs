using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
namespace SalesManagement_SysDev;

public sealed class F_Warehousing : ManagementListPage
{
    public F_Warehousing() : base("入庫管理", () => WarehousingService.CanView)
    {
        if (AccessGranted) Content = new WarehouseMovementManagementView(WarehouseMovementKind.Inbound);
    }
    protected override void Load() { }
}
