using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;

namespace SalesManagement_SysDev;

public sealed class SidebarViewModel : INotifyPropertyChanged
{
    private bool isSidebarExpanded = true;

    public SidebarViewModel()
    {
        ToggleSidebarCommand = new SidebarToggleCommand(this);
    }

    public bool IsSidebarExpanded
    {
        get => isSidebarExpanded;
        set
        {
            if (isSidebarExpanded == value) return;
            isSidebarExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSidebarExpanded)));
        }
    }

    public ICommand ToggleSidebarCommand { get; }
    public ObservableCollection<SidebarEntry> MenuItems { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    private sealed class SidebarToggleCommand(SidebarViewModel sidebar) : ICommand
    {
        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => sidebar.IsSidebarExpanded = !sidebar.IsSidebarExpanded;

        // Toggling is always available, including while another screen is displayed.
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }
    }
}

public sealed class SidebarEntry : ViewModelBase
{
    private readonly SidebarViewModel sidebar;
    private bool isActive;
    private int unreadCount;

    public SidebarEntry(SidebarViewModel sidebar, string label, string iconName = "", Action? navigate = null,
        bool hasSeparator = false, bool supportsUnreadBadge = false)
    {
        this.sidebar = sidebar;
        Label = label; IconName = iconName; HasSeparator = hasSeparator;
        SupportsUnreadBadge = supportsUnreadBadge;
        IsCategory = navigate == null;
        NavigateCommand = new RelayCommand(() => navigate?.Invoke());
        sidebar.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SidebarViewModel.IsSidebarExpanded)) Notify(nameof(IsSidebarExpanded));
        };
    }
    public string Label { get; }
    public string IconName { get; }
    public bool HasSeparator { get; }
    public bool IsCategory { get; }
    public bool IsSidebarExpanded => sidebar.IsSidebarExpanded;
    public bool IsActive { get => isActive; set => Set(ref isActive, value); }
    public bool SupportsUnreadBadge { get; }
    public int UnreadCount
    {
        get => unreadCount;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "未読件数は0以上で指定してください。");
            if (!Set(ref unreadCount, value)) return;
            Notify(nameof(UnreadCountText));
            Notify(nameof(ShowUnreadBadge));
        }
    }
    public string UnreadCountText => unreadCount > 99 ? "99+" : unreadCount.ToString(CultureInfo.InvariantCulture);
    public bool ShowUnreadBadge => SupportsUnreadBadge && unreadCount > 0;
    public RelayCommand NavigateCommand { get; }
}
