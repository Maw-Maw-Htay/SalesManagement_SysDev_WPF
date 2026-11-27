using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
namespace SalesManagement_SysDev;

public sealed class F_Chumon : ManagementListPage
{
    public F_Chumon() : base("注文管理", () => AppSession.IsAdmin || AppSession.IsSales)
    {
        if (AccessGranted) Content = new OrderManagementView();
    }
    protected override void Load()
    {
        // OrderManagementView owns its query lifetime and refreshes after each operation.
    }
}
