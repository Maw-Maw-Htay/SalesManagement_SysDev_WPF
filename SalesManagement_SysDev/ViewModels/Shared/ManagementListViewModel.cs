using System.ComponentModel;
using System.Windows.Data;

namespace SalesManagement_SysDev;

public sealed class ManagementListViewModel(string title, Action reload) : ViewModelBase
{
    private string searchText = "";
    private ICollectionView? rows;
    public string PageTitle { get; } = title;
    public string ListTitle => PageTitle.EndsWith("一覧", StringComparison.Ordinal) ? PageTitle
        : (PageTitle.EndsWith("管理", StringComparison.Ordinal) ? PageTitle[..^2] : PageTitle) + "一覧";
    public string Description => "登録済みのデータを検索・確認できます。";
    public RelayCommand ReloadCommand { get; } = new(reload);
    public ICollectionView? Rows => rows;
    public bool IsEmpty => rows == null || rows.IsEmpty;
    public string ResultSummary => $"{rows?.Cast<object>().Count() ?? 0:N0} 件";
    public string SearchText
    {
        get => searchText;
        set { if (Set(ref searchText, value)) ApplySearch(); }
    }

    public void Display<T>(IEnumerable<T> data)
    {
        rows = CollectionViewSource.GetDefaultView(data.ToList());
        ApplySearch();
        Notify(nameof(Rows));
    }

    private void ApplySearch()
    {
        if (rows == null) return;
        var keyword = SearchText.Trim();
        rows.Filter = row => keyword.Length == 0 || row != null && row.GetType().GetProperties()
            .Any(property => (property.GetValue(row)?.ToString() ?? "")
                .Contains(keyword, StringComparison.CurrentCultureIgnoreCase));
        rows.Refresh();
        Notify(nameof(IsEmpty));
        Notify(nameof(ResultSummary));
    }
}
