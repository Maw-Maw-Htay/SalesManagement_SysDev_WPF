namespace SalesManagement_SysDev;

public enum OrderDraftNavigationChoice { ContinueEditing, SaveAndLeave, DiscardAndLeave }

public sealed record OrderDraftToken(Guid Id, byte[]? Version = null);
public sealed record OrderDraftSelection(int? Id, string Text);
public sealed record OrderDraftDate(DateTime? Value, string UnparsedText);
public sealed record OrderDraftHeader(OrderDraftSelection Office, OrderDraftSelection Customer,
    OrderDraftDate OrderDate, OrderDraftDate DeliveryDate, string Remarks,
    OrderDraftSelection PendingProduct, decimal? PendingUnitPrice, string PendingQuantity);
public sealed record OrderDraftLine(int ProductId, string ProductCode, string ProductName,
    decimal UnitPrice, int Quantity, string QuantityText);
public sealed record OrderDraftForm(OrderDraftHeader Header, IReadOnlyList<OrderDraftLine> Lines);
public sealed record OrderDraftSnapshot(OrderDraftToken Token, DateTimeOffset UpdatedAt, OrderDraftForm Form);
public sealed record OrderDraftRegistration(int OrderId, bool AlreadyRegistered = false);

public interface IOrderDraftStore
{
    IReadOnlyList<OrderDraftSnapshot> List();
    OrderDraftSnapshot Open(Guid id);
    OrderDraftToken Save(OrderDraftToken token, OrderDraftForm form);
    void Delete(OrderDraftToken token);
    OrderDraftRegistration Register(OrderDraftToken token, OrderDraftForm form);
}
