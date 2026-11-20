using System.Windows.Input;

namespace SalesManagement_SysDev;

public sealed record OrderHideResult(int OrderId, bool Succeeded, string Message);

public interface IOrderHideDataSource
{
    Task<OrderHideResult> HideAsync(int orderId, int employeeId, string reason);
}

internal sealed class OrderHideDataSource : IOrderHideDataSource
{
    public Task<OrderHideResult> HideAsync(int orderId, int employeeId, string reason) => Task.Run(() =>
    {
        var succeeded = BusinessService.SetOrderVisibility(orderId, employeeId, true, reason, out var message);
        return new OrderHideResult(orderId, succeeded, message);
    });
}

public sealed class OrderHideViewModel : ViewModelBase
{
    private readonly int[] orderIds;
    private readonly int employeeId;
    private readonly IOrderHideDataSource source;
    private readonly HashSet<int> succeeded = [];
    private readonly Dictionary<int, string> failures = [];
    private readonly RelayCommand hideCommand, closeCommand;
    private string reason = "", reasonError = "", feedback = "";
    private bool isBusy, hasAttempted;

    public OrderHideViewModel(IReadOnlyCollection<int> orderIds, Action close, IOrderHideDataSource? source = null)
    {
        this.orderIds = orderIds.Distinct().ToArray();
        employeeId = AppSession.Employee?.EmId ?? 0;
        this.source = source ?? new OrderHideDataSource();
        hideCommand = new RelayCommand(async () => await HideAsync(), () => CanHide);
        closeCommand = new RelayCommand(close, () => CanClose);
        if (this.orderIds.Length == 0 || this.orderIds.Any(id => id <= 0))
            feedback = "非表示にする受注を選択してください。";
    }

    public string TargetText => orderIds.Length == 1 ? $"対象受注：ORD-{orderIds[0]:000000}" : $"対象：選択した受注 {orderIds.Length}件";
    public string ConfirmationMessage => orderIds.Length == 1 ? "この受注を非表示にしますか？" : "選択した受注を非表示にしますか？";
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
    public string ConfirmLabel => isBusy ? "処理中…" : hasAttempted && !IsComplete ? "失敗分を再試行" : "非表示にする";
    public string CloseLabel => hasAttempted ? "閉じる" : "キャンセル";
    public ICommand HideCommand => hideCommand;
    public ICommand CloseCommand => closeCommand;

    private bool ValidateReason()
    {
        ReasonError = string.IsNullOrWhiteSpace(reason) ? "非表示理由を入力してください。" : "";
        return ReasonError.Length == 0;
    }

    public async Task HideAsync()
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
                    var result = await source.HideAsync(id, employeeId, submittedReason);
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
                ? orderIds.Length == 1 ? "受注を非表示にしました。" : $"{succeeded.Count}件の受注を非表示にしました。"
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
