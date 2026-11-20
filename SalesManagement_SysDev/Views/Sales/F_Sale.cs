using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
namespace SalesManagement_SysDev;

public sealed class F_Sale : ManagementListPage
{
    public F_Sale() : base("売上管理", () => SalesService.CanView)
    {
        if (AccessGranted) Content = new SalesManagementView();
    }
    protected override void Load() { }
}
