using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
namespace SalesManagement_SysDev;

public sealed class F_Product : ManagementListPage
{
    public F_Product() : base("商品管理", () => ProductService.CanView)
    {
        if (AccessGranted) Content = new ProductManagementView();
    }

    protected override void Load()
    {
        // ProductManagementView owns its filtered query and refresh lifecycle.
    }
}
