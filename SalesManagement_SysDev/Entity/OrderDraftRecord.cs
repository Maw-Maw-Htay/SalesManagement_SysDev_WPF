namespace SalesManagement_SysDev;

internal sealed class OrderDraftRecord
{
    public Guid DraftId { get; set; }
    public int EmployeeId { get; set; }
    public int ScopeOfficeId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? FormJson { get; set; }
    public int? RegisteredOrderId { get; set; }
    public byte[] Version { get; set; } = [];
}
