using System.Windows.Input;

namespace SalesManagement_SysDev;

public sealed record OrderHideResult(int OrderId, bool Succeeded, string Message);

public interface IOrderHideDataSource
{
    Task<OrderHideResult> HideAsync(int orderId, int employeeId, string reason);
}

internal static class OrderVisibilityDataSource
{
    public static Task<OrderHideResult> ChangeAsync(int orderId, int employeeId, bool hidden, string reason) => Task.Run(() =>
    {
        var succeeded = BusinessService.SetOrderVisibility(orderId, employeeId, hidden, reason, out var message);
        return new OrderHideResult(orderId, succeeded, message);
    });
}

public sealed class OrderHideViewModel : ViewModelBase
{
    private readonly int[] orderIds;
    private readonly int employeeId;
    private readonly Func<int, int, string, Task<OrderHideResult>> changeVisibility;
    private readonly HashSet<int> succeeded = [];
    private readonly Dictionary<int, string> failures = [];
    private readonly RelayCommand hideCommand, closeCommand;
    private string reason = "", reasonError = "", feedback = "";
    private bool isBusy, hasAttempted;

    public OrderHideViewModel(IReadOnlyCollection<int> orderIds, Action close, IOrderHideDataSource? source = null)
        : this(orderIds, close, restore: false, source)
    {
    }

    internal OrderHideViewModel(IReadOnlyCollection<int> orderIds, Action close, bool restore, IOrderHideDataSource? source)
    {
        this.orderIds = orderIds.Distinct().ToArray();
        employeeId = AppSession.Employee?.EmId ?? 0;
        IsRestore = restore;
        changeVisibility = source is null
            ? (id, actorId, reason) => OrderVisibilityDataSource.ChangeAsync(id, actorId, !restore, reason)
            : source.HideAsync;
        hideCommand = new RelayCommand(async () => await ApplyAsync(), () => CanHide);
        closeCommand = new RelayCommand(close, () => CanClose);
        if (this.orderIds.Length == 0 || this.orderIds.Any(id => id <= 0))
            feedback = $"{ActionPhrase}する受注を選択してください。";
    }

    public bool IsRestore { get; }
    private string ActionPhrase => IsRestore ? "復元" : "非表示に";
    public string DialogTitle => IsRestore ? "受注を復元" : "受注を非表示";
    public string ReasonLabel => IsRestore ? "復元理由" : "非表示理由";
    public string ReasonExample => IsRestore ? "例：誤って非表示にしたため復元する" : "例：重複登録のため非表示にする";
    public string VisibilityHelp => IsRestore ? "復元した受注は、受注一覧に再表示されます。" : "非表示にした受注は、非表示一覧から再表示できます。";
    public string TargetText => orderIds.Length == 1 ? $"対象受注：ORD-{orderIds[0]:000000}" : $"対象：選択した受注 {orderIds.Length}件";
    public string ConfirmationMessage => $"{(orderIds.Length == 1 ? "この受注" : "選択した受注")}を{ActionPhrase}しますか？";
    public string Reason
    {
        get => reason;
        set
        {
            if (!CanEdit || !Set(ref reason, value)) return;
            ValidateReason();
            Refresh();
        }
    }
    public string ReasonError { get => reasonError; private set => Set(ref reasonError, value); }
    public string Feedback { get => feedback; private set => Set(ref feedback, value); }
    public bool IsBusy => isBusy;
    public bool CanClose => !isBusy;
    public bool CanEdit => !isBusy && !IsComplete;
    public bool IsComplete => orderIds.Length > 0 && succeeded.Count == orderIds.Length;
    public bool HasAttempted => hasAttempted;
    public bool HasChanges => succeeded.Count > 0;
    public bool HasFailures => failures.Count > 0 || (Feedback.Length > 0 && !IsComplete);
    public string FailureDetails => string.Join("\n", orderIds.Where(failures.ContainsKey).Select(id => $"ORD-{id:000000}：{failures[id]}"));
    public IReadOnlyCollection<int> SuccessfulOrderIds => succeeded.ToArray();
    public bool CanHide => CanEdit && orderIds.Length > 0 && orderIds.All(id => id > 0) && !string.IsNullOrWhiteSpace(reason);
    public string ConfirmLabel => isBusy ? "処理中…" : hasAttempted && !IsComplete ? "失敗分を再試行" : $"{ActionPhrase}する";
    public string CloseLabel => hasAttempted ? "閉じる" : "キャンセル";
    public ICommand HideCommand => hideCommand;
    public ICommand CloseCommand => closeCommand;

    private bool ValidateReason()
    {
        ReasonError = string.IsNullOrWhiteSpace(reason) ? $"{ReasonLabel}を入力してください。" : "";
        return ReasonError.Length == 0;
    }

    public Task HideAsync() => ApplyAsync();

    private async Task ApplyAsync()
    {
        if (isBusy || IsComplete) return;
        if (!ValidateReason() || !CanHide) return;
        // Capture the submitted text once; each successful order keeps the reason from its own transaction.
        var submittedReason = reason.Trim();
        isBusy = true;
        Feedback = "";
        Refresh();
        try
        {
            // Existing bulk policy: each order + audit is atomic, other orders continue after a failure.
            foreach (var id in orderIds.Where(id => !succeeded.Contains(id)))
            {
                try
                {
                    var result = await changeVisibility(id, employeeId, submittedReason);
                    if (result.OrderId != id) throw new InvalidOperationException("対象受注と処理結果が一致しません。");
                    if (result.Succeeded) { succeeded.Add(id); failures.Remove(id); }
                    else failures[id] = result.Message;
                }
                catch
                {
                    failures[id] = "処理結果を確認できませんでした。一覧・履歴を確認してから再試行してください。";
                }
            }
            hasAttempted = true;
            Feedback = IsComplete
                ? $"{(orderIds.Length == 1 ? "受注" : $"{succeeded.Count}件の受注")}を{ActionPhrase}しました。"
                : $"成功 {succeeded.Count}件 / 失敗 {failures.Count}件。失敗した受注は下記を確認してください。";
        }
        finally
        {
            isBusy = false;
            Refresh();
        }
    }

    private void Refresh()
    {
        foreach (var name in new[] { nameof(IsBusy), nameof(CanClose), nameof(CanEdit), nameof(IsComplete), nameof(HasAttempted),
                     nameof(HasChanges), nameof(HasFailures), nameof(FailureDetails), nameof(CanHide), nameof(ConfirmLabel), nameof(CloseLabel) }) Notify(name);
        hideCommand.Refresh(); closeCommand.Refresh();
    }
}
