using System.Windows.Controls;

namespace SalesManagement_SysDev;

public partial class ManagementListView : UserControl
{
    public ManagementListView() => InitializeComponent();

    private void FormatColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.Column is not DataGridTextColumn column) return;
        var numeric = e.PropertyType == typeof(int) || e.PropertyType == typeof(decimal);
        column.ElementStyle = (System.Windows.Style)FindResource(numeric ? "TableNumber" : "TableText");
        if (e.PropertyType == typeof(decimal)) column.Binding.StringFormat = "¥#,##0";
        if (e.PropertyType == typeof(DateTime) || e.PropertyType == typeof(DateTime?)) column.Binding.StringFormat = "yyyy-MM-dd";
    }
}
