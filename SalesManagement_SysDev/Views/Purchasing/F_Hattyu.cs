using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
namespace SalesManagement_SysDev;

public sealed class F_Hattyu : ManagementListPage
{
    public F_Hattyu() : base("発注管理", () => PurchaseOrderService.CanView)
    {
        if (AccessGranted) Content = new PurchaseOrderManagementView();
    }

    protected override void Load()
    {
        // The purchase-order view model owns its list query and refresh lifecycle.
    }
}
