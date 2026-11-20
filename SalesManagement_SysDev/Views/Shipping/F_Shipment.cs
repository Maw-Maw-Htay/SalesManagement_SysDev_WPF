using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
namespace SalesManagement_SysDev;

public sealed class F_Shipment : ManagementListPage
{
    public F_Shipment() : base("出荷管理", () => ShipmentService.CanView)
    { if (AccessGranted) Content = new ShipmentManagementView(); }
    protected override void Load() { }
}
