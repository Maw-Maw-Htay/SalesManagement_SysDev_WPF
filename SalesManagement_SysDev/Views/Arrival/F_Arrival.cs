using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
namespace SalesManagement_SysDev;

public sealed class F_Arrival : ManagementListPage
{
    public F_Arrival() : base("入荷管理", () => ArrivalService.CanView)
    {
        if (AccessGranted) Content = new ArrivalManagementView();
    }
    protected override void Load() { }
}
