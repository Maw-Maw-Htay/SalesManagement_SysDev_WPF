using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
namespace SalesManagement_SysDev;

public sealed class F_Syukko : ManagementListPage
{
    public F_Syukko() : base("出庫管理", () => OutboundService.CanView)
    {
        if (AccessGranted) Content = new OutboundManagementView();
    }
    protected override void Load() { }
}
