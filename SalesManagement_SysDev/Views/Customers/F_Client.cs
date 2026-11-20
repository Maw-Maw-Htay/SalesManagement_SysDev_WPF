using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
namespace SalesManagement_SysDev;

public sealed class F_Client : ManagementListPage
{
    public F_Client() : base("顧客管理", () => AppSession.IsAdmin || AppSession.IsSales || AppSession.IsLogistics)
    { if (AccessGranted) Content = new CustomerManagementView(); }
    protected override void Load() { }
}
