using System.Windows;
using System.Windows.Media;

namespace SalesManagement_SysDev;

public enum OrderNoticeKind { Information, Success, Warning, Error, Confirmation, Danger }

public sealed partial class F_OrderNotice : Window
{
    private bool discardSelected;

    public F_OrderNotice(string title, string message, OrderNoticeKind kind, string confirmLabel = "閉じる",
        string cancelLabel = "キャンセル", bool showCancel = false)
        : this(title, message, kind, confirmLabel, cancelLabel, showCancel, false)
    {
    }

    private F_OrderNotice(string title, string message, OrderNoticeKind kind, string confirmLabel,
        string cancelLabel, bool showCancel, bool allowDiscard)
    {
        InitializeComponent();
        DataContext = new OrderNoticeViewModel(title, message, kind, confirmLabel, cancelLabel, showCancel,
            result => { if (result.HasValue) DialogResult = result; else Close(); },
            allowDiscard ? () => { discardSelected = true; DialogResult = false; } : null);
        MaxWidth = Math.Max(320, SystemParameters.WorkArea.Width * .9);
        MaxHeight = Math.Max(220, SystemParameters.WorkArea.Height * .85);
        Width = Math.Min(allowDiscard ? 520 : 460, MaxWidth);
    }

    public static void Show(Window? owner, string title, string message, OrderNoticeKind kind = OrderNoticeKind.Information)
    {
        var dialog = new F_OrderNotice(title, message, kind);
        AttachOwner(dialog, owner);
        dialog.ShowDialog();
    }

    public static bool Confirm(Window? owner, string title, string message, string confirmLabel = "確定",
        string cancelLabel = "キャンセル", bool destructive = false)
    {
        var dialog = new F_OrderNotice(title, message, destructive ? OrderNoticeKind.Danger : OrderNoticeKind.Confirmation,
            confirmLabel, cancelLabel, showCancel: true);
        AttachOwner(dialog, owner);
        return dialog.ShowDialog() == true;
    }

    public static OrderDraftNavigationChoice ConfirmDraftNavigation(Window? owner)
    {
        var dialog = new F_OrderNotice("未保存の変更", "受注の入力内容が保存されていません。画面を移動しますか？",
            OrderNoticeKind.Confirmation, "一時保存して移動", "入力を続ける", showCancel: true, allowDiscard: true);
        AttachOwner(dialog, owner);
        if (dialog.ShowDialog() == true) return OrderDraftNavigationChoice.SaveAndLeave;
        return dialog.discardSelected ? OrderDraftNavigationChoice.DiscardAndLeave : OrderDraftNavigationChoice.ContinueEditing;
    }

    private static void AttachOwner(F_OrderNotice dialog, Window? owner)
    {
        if (owner == null) return;
        dialog.Owner = owner;
        var workWidth = owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width;
        var workHeight = owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height;
        if (double.IsFinite(workWidth) && workWidth > 0)
        {
            dialog.MaxWidth = Math.Min(dialog.MaxWidth, Math.Max(320, workWidth * .92));
            dialog.Width = Math.Min(dialog.Width, dialog.MaxWidth);
        }
        if (double.IsFinite(workHeight) && workHeight > 0)
            dialog.MaxHeight = Math.Min(dialog.MaxHeight, Math.Max(220, workHeight * .85));
    }
}

public sealed class OrderNoticeViewModel
{
    public OrderNoticeViewModel(string title, string message, OrderNoticeKind kind, string confirmLabel,
        string cancelLabel, bool showCancel, Action<bool?> complete, Action? discard = null)
    {
        Title = title;
        Message = message;
        ConfirmLabel = confirmLabel;
        CancelLabel = cancelLabel;
        CancelVisibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
        HasDiscardAction = discard != null;
        Glyph = kind switch
        {
            OrderNoticeKind.Success => "✓",
            OrderNoticeKind.Warning or OrderNoticeKind.Danger => "!",
            OrderNoticeKind.Error => "×",
            OrderNoticeKind.Confirmation => "?",
            _ => "i"
        };
        AccentBrush = kind switch
        {
            OrderNoticeKind.Success => AppTheme.Success,
            OrderNoticeKind.Warning => AppTheme.Warning,
            OrderNoticeKind.Error or OrderNoticeKind.Danger => AppTheme.Danger,
            OrderNoticeKind.Confirmation => AppTheme.Accent,
            _ => AppTheme.Secondary
        };
        ConfirmBackground = kind == OrderNoticeKind.Danger ? AppTheme.Danger : AppTheme.Primary;
        ConfirmCommand = new RelayCommand(() => complete(true));
        CancelCommand = new RelayCommand(() => { CancelWasClicked = true; complete(false); });
        DiscardCommand = new RelayCommand(() => discard?.Invoke(), () => HasDiscardAction);
        CloseCommand = new RelayCommand(() => complete(null));
    }

    public string Title { get; }
    public string Message { get; }
    public string Glyph { get; }
    public string ConfirmLabel { get; }
    public string CancelLabel { get; }
    public Visibility CancelVisibility { get; }
    public bool HasDiscardAction { get; }
    public bool ConfirmIsDefault => !HasDiscardAction;
    public Brush AccentBrush { get; }
    public Brush ConfirmBackground { get; }
    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand DiscardCommand { get; }
    public RelayCommand CloseCommand { get; }
    public bool CancelWasClicked { get; private set; }
}
