namespace SalesManagement_SysDev;

/// <summary>Tracks audited management edits since this login last opened history.</summary>
public static class HistoryNotificationService
{
    private static int unreadCount;

    public static int UnreadCount => Volatile.Read(ref unreadCount);
    public static event Action? UnreadCountChanged;

    public static void RecordNewManagementEdit()
    {
        Interlocked.Increment(ref unreadCount);
        NotifyChanged();
    }

    public static void MarkAllRead()
    {
        if (Interlocked.Exchange(ref unreadCount, 0) == 0) return;
        NotifyChanged();
    }

    private static void NotifyChanged()
    {
        var handlers = UnreadCountChanged;
        if (handlers == null) return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try { handler(); }
            catch { /* Notifications must not turn a committed business update into a failure. */ }
        }
    }
}
