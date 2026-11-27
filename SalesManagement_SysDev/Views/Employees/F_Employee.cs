using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
namespace SalesManagement_SysDev;

public sealed class F_Employee : ManagementListPage
{
    public F_Employee() : base("社員管理", () => EmployeeService.CanManage)
    {
        if (AccessGranted) Content = new EmployeeManagementView();
    }
    protected override void Load() { }
}
