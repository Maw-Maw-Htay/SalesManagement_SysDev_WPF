using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows;

namespace SalesManagement_SysDev;

public sealed record OrderDetailBadge(string Text, string Tone);

public sealed class OrderDetailItemRow(OrderDetailLine line, int number)
{
    public int Number { get; } = number;
    public string ProductCode => line.ProductCode;
    public string ProductName => line.ProductName;
    public decimal UnitPrice => line.UnitPrice;
    public decimal? DisplayUnitPrice => line.HasKnownUnitPrice ? UnitPrice : null;
    public int Quantity => line.Quantity;
    public decimal Subtotal => line.Subtotal;
    public decimal Tax => OrderTax.Calculate(Subtotal);
    public decimal Total => Subtotal + Tax;
}

public sealed class OrderDetailHistoryRow : INotifyPropertyChanged
{
    private bool isExpanded;
    public OrderDetailHistoryRow(OrderOperationHistory entry)
    {
        Display = new OrderHistoryDisplayRow(entry);
        ToggleDetailsCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    public OrderHistoryDisplayRow Display { get; }
    public string OperationName => string.IsNullOrWhiteSpace(Display.Operation) ? "操作種別未記録" : Display.Operation;
    public string ChangeSummary => Display.ChangeSummary;
    public string RecordedDetails => string.IsNullOrWhiteSpace(Display.Entry.Details) ? "記録された詳細はありません。" : Display.Entry.Details;
    public string Timestamp => Display.Entry.RecordedAt == default ? "日時未記録"
        : Display.Entry.RecordedAt.ToOffset(OrderHistoryService.JapanOffset).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    public string Actor => string.IsNullOrWhiteSpace(Display.Entry.ActorName)
        ? Display.Entry.ActorEmployeeId.HasValue ? $"社員ID {Display.Entry.ActorEmployeeId.Value}（氏名未記録）" : "未記録" : Display.Entry.ActorName;
    public string Office => string.IsNullOrWhiteSpace(Display.Entry.ActorOfficeName) ? "営業所未記録" : Display.Entry.ActorOfficeName;
    public string ExpandLabel => IsExpanded ? "変更詳細を閉じる ▲" : "変更詳細を見る ▼";
    public Visibility ExpandedDetailsVisibility => IsExpanded ? Visibility.Visible : Visibility.Collapsed;
    public bool HasProductChanges => Display.ProductChanges.Count > 0;
    public IReadOnlyList<OrderHistoryComparison> Comparisons => Display.Comparisons;
    public IReadOnlyList<OrderHistoryProductChange> ProductChanges => Display.ProductChanges;
    public ICommand ToggleDetailsCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (isExpanded == value) return;
            isExpanded = value;
            foreach (var property in new[] { nameof(IsExpanded), nameof(ExpandLabel), nameof(ExpandedDetailsVisibility) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }

    internal static string StateName(int state) => state switch { 0 => "未確定", 1 => "確定済み", _ => $"状態 {state}" };
    internal static string ManagementName(int state) => state switch { 0 => "通常表示", 2 => "非表示", _ => $"管理状態 {state}" };
}

public sealed class OrderDetailViewModel : INotifyPropertyChanged
{
    private readonly OrderDetailSnapshot data;
    private readonly DetailCommand editCommand;
    private readonly DetailCommand confirmCommand;
    private readonly DetailCommand hideCommand;
    private readonly bool readOnly;
    private bool isBusy;
    private bool historyLoaded;
    private string? historyError;
    private IReadOnlyList<OrderDetailHistoryRow> history = [];

    public OrderDetailViewModel(OrderDetailSnapshot data, Action edit, Action confirm, Action hide, Action print, Action close, bool readOnly = false)
    {
        this.data = data;
        this.readOnly = readOnly;
        Items = data.Lines.Select((line, index) => new OrderDetailItemRow(line, index + 1)).ToArray();
        editCommand = new DetailCommand(() => Run(edit), () => !IsBusy && CanEdit);
        confirmCommand = new DetailCommand(() => Run(confirm), () => !IsBusy && CanConfirm);
        hideCommand = new DetailCommand(() => Run(hide), () => !IsBusy && CanHide);
        PrintCommand = new DetailCommand(() => Run(print), () => !IsBusy && CanPrint);
        CloseCommand = new DetailCommand(close);
    }

    public string OrderNumber => $"ORD-{data.OrderId:000000}";
    public string HeaderSummary => $"受注日：{data.OrderDate:yyyy/MM/dd}　受付営業所：{OfficeName}";
    public string FooterOrderNumber => $"受注番号：{OrderNumber}";
    public string Subtitle => $"受付営業所：{data.OfficeName}  |  登録日：{RegisteredAt}";
    public string FooterSummary => $"{Subtitle}  |  管理状態：{ManagementStatus.Text}";
    public string CustomerCode => $"C-{data.CustomerId:0000}";
    public string CustomerName => data.CustomerName;
    public string EmployeeName => data.EmployeeName;
    public string OfficeName => data.OfficeName;
    public string CustomerAddress => string.IsNullOrWhiteSpace(data.CustomerAddress) ? "未登録" : data.CustomerAddress;
    public string CustomerPhone => string.IsNullOrWhiteSpace(data.CustomerPhone) ? "未登録" : data.CustomerPhone;
    public string OrderDate => data.OrderDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string DeliveryDate => data.DeliveryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "未設定";
    public string DisplayOrderDate => data.OrderDate.ToString("yyyy/MM/dd (ddd)", CultureInfo.GetCultureInfo("ja-JP"));
    public string DisplayDeliveryDate => data.DeliveryDate?.ToString("yyyy/MM/dd (ddd)", CultureInfo.GetCultureInfo("ja-JP")) ?? "未設定";
    public string Remarks => string.IsNullOrWhiteSpace(data.CustomerContact) ? "（特記事項なし）" : data.CustomerContact;
    public string HiddenReason => string.IsNullOrWhiteSpace(data.HiddenReason) ? "未記録" : data.HiddenReason;
    public OrderDetailBadge BusinessStatus => new(OrderDetailHistoryRow.StateName(data.StateFlag), data.StateFlag == 1 ? "Success" : data.StateFlag == 0 ? "Warning" : "Neutral");
    public OrderDetailBadge ManagementStatus => new(OrderDetailHistoryRow.ManagementName(data.ManagementFlag), "Neutral");
    public bool IsHidden => data.ManagementFlag == 2;
    public bool IsConfirmed => data.StateFlag == 1;
    public OrderDetailBadge HeaderStatus => IsHidden ? new("非表示", "Neutral") : BusinessStatus;
    public IReadOnlyList<OrderDetailItemRow> Items { get; }
    public string ItemsTitle => $"受注明細（{Items.Count} 品）";
    public int TotalQuantity => Items.Sum(line => line.Quantity);
    public string QuantitySummary => $"総数量 {TotalQuantity:N0} 個";
    public decimal Subtotal => data.Lines.Sum(line => line.Subtotal);
    // Preserve the existing detail screen's tax rate and document-level rounding.
    public decimal Tax => OrderTax.Calculate(Subtotal);
    public decimal Total => Subtotal + Tax;
    public string RoundingNotice => Items.Sum(line => line.Tax) == Tax ? "" : "消費税合計は税抜合計に対して計算するため、行ごとの税額の合計と端数差が生じる場合があります。";
    public bool CanManage => !readOnly && AppSession.CanManageOrders && AppSession.Employee != null
        && (AppSession.IsAdmin || AppSession.Employee.SoId == data.OfficeId);
    public bool CanEdit => CanManage && data.StateFlag == 0 && data.ManagementFlag == 0;
    public bool CanConfirm => CanEdit;
    public bool CanHide => CanManage && data.ManagementFlag == 0;
    public bool CanPrint => CanManage && data.StateFlag == 1;
    public string ConfirmButtonText => data.StateFlag == 1 ? "確定済み" : "受注確定";
    public string ConfirmHint => data.StateFlag == 1 ? "この受注は確定済みです" : CanConfirm ? "未確定の受注を確定します" : "未確定かつ通常表示の受注だけ確定できます";
    public string EditHint => data.StateFlag == 1 ? "確定済みの受注は更新できません" : IsHidden ? "非表示の受注は更新できません" : CanEdit ? "数量・備考を更新します" : "この受注の更新権限がありません";
    public string PrintHint => data.StateFlag == 1 ? CanPrint ? "受注伝票を印刷します" : "この受注の印刷権限がありません" : "受注確定後に印刷できます";
    public bool ShowPrintHint => !readOnly && data.StateFlag != 1;
    public bool IsBusy => isBusy;
    public IReadOnlyList<OrderDetailHistoryRow> History => history;
    public string HistoryCountText => !historyLoaded ? "確認中" : historyError != null ? "取得失敗" : $"{History.Count:N0}件";
    public bool ShowHistoryNotice => historyError != null || history.Count == 0;
    public string HistoryNotice => !historyLoaded ? "監査履歴を読み込み中…"
        : historyError != null ? historyError
        : History.Count == 0 ? "記録された操作履歴はありません。過去の操作日時・実行者は未記録です。"
        : "日時は日本時間です。「記録開始時の状態」は履歴の記録開始日時を示し、それ以前の操作日時・実行者は未記録です。";
    public string ConfirmedAt
    {
        get
        {
            if (data.StateFlag == 0) return "未確定";
            if (data.StateFlag != 1) return "未記録";
            if (!historyLoaded) return "履歴を確認中";
            if (historyError != null) return "取得できませんでした";
            return History.FirstOrDefault(row => row.Display.Entry.Operation == OrderHistoryOperations.Confirmed
                && row.Display.Entry.AfterState == 1 && row.Display.Entry.RecordedAt != default)?.Timestamp ?? "未記録";
        }
    }
    public string RegisteredAt => !historyLoaded ? "履歴を確認中" : historyError != null ? "取得できませんでした"
        : History.FirstOrDefault(row => row.Display.Entry.Operation == OrderHistoryOperations.Created && row.Display.Entry.RecordedAt != default)
            ?.Display.Entry.RecordedAt.ToOffset(OrderHistoryService.JapanOffset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "未記録";

    public ICommand EditCommand => editCommand;
    public ICommand ConfirmCommand => confirmCommand;
    public ICommand HideCommand => hideCommand;
    public ICommand PrintCommand { get; }
    public ICommand CloseCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetHistory(IEnumerable<OrderOperationHistory> entries, string? error = null)
    {
        history = !CanManage ? [] : entries.Where(entry => entry.OrderId == data.OrderId && (AppSession.IsAdmin || entry.OfficeId == data.OfficeId))
            .OrderByDescending(entry => entry.RecordedAt).ThenByDescending(entry => entry.Id)
            .Select(entry => new OrderDetailHistoryRow(entry)).ToArray();
        historyError = error;
        historyLoaded = true;
        foreach (var property in new[] { nameof(History), nameof(HistoryCountText), nameof(HistoryNotice), nameof(ShowHistoryNotice), nameof(ConfirmedAt), nameof(RegisteredAt), nameof(Subtitle), nameof(FooterSummary) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    private void Run(Action action)
    {
        if (isBusy) return;
        isBusy = true;
        RefreshCommands();
        try { action(); }
        finally { isBusy = false; RefreshCommands(); }
    }

    private void RefreshCommands()
    {
        editCommand.Refresh();
        confirmCommand.Refresh();
        hideCommand.Refresh();
        ((DetailCommand)PrintCommand).Refresh();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBusy)));
    }

    private sealed class DetailCommand(Action execute, Func<bool>? canExecute = null) : ICommand
    {
        public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
        public void Execute(object? parameter) { if (CanExecute(parameter)) execute(); }
        public event EventHandler? CanExecuteChanged;
        public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
