namespace SalesManagement_SysDev;

public sealed class HomeViewModel(Action<string> navigate)
{
    public string Greeting => $"{AppSession.Employee?.EmName} さん、おかえりなさい";
    public string OfficeName => AppSession.Employee?.So.SoName ?? "";
    public string Role => AppSession.Role;
    public bool CanManageOrders => AppSession.CanManageOrders;
    public bool CanViewMaster => AppSession.IsAdmin || AppSession.IsSales || AppSession.IsLogistics;
    public bool CanViewInventory => AppSession.IsAdmin || AppSession.IsLogistics;
    public RelayCommand NavigateCommand { get; } = new(parameter =>
    {
        if (parameter is not string page) return;
        var allowed = page switch
        {
            "受注管理" => AppSession.CanManageOrders,
            "商品管理" or "顧客管理" => AppSession.IsAdmin || AppSession.IsSales || AppSession.IsLogistics,
            "在庫管理" => AppSession.IsAdmin || AppSession.IsLogistics,
            _ => false
        };
        if (allowed) navigate(page);
    });
}
