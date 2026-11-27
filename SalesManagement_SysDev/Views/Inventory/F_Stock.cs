using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
namespace SalesManagement_SysDev;

public sealed class F_Stock : ManagementListPage
{
    public F_Stock() : base("在庫管理", () => InventoryService.CanView)
    {
        if (AccessGranted) Content = new InventoryManagementView();
    }
    protected override void Load() { }
}
