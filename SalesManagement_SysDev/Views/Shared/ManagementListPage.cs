using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
namespace SalesManagement_SysDev;

public abstract class ManagementListPage : UserControl, IDisposable
{
    protected readonly SalesManagementContext Db = new();
    private readonly ManagementListViewModel viewModel;
    protected bool AccessGranted { get; }
    public string PageTitle { get; }

    protected ManagementListPage(string title, Func<bool> canOpen)
    {
        PageTitle = title;
        viewModel = new ManagementListViewModel(title, Load);
        AccessGranted = canOpen();
        if (!AccessGranted)
        {
            Content = new TextBlock
            {
                Text = "この画面を利用する権限がありません。",
                Margin = new Thickness(24),
                Foreground = AppTheme.Danger
            };
            return;
        }
        Content = new ManagementListView { DataContext = viewModel };
    }

    protected abstract void Load();
    protected void ShowError(Exception ex) => MessageBox.Show("処理に失敗しました。\n" + ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
    protected void Display<T>(IEnumerable<T> rows) => viewModel.Display(rows);
    public void Dispose() => Db.Dispose();
}
