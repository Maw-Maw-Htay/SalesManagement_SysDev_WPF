namespace SalesManagement_SysDev;

public sealed record OrderUpdateResult(bool Succeeded, string Message);

public interface IOrderUpdateDataSource
{
    Task<OrderUpdateResult> SaveAsync(OrderDetailSnapshot expected, OrderDetailSnapshot edited, string remarks, IReadOnlyCollection<OrderDetailUpdate> lines);
}

internal sealed class OrderUpdateDataSource : IOrderUpdateDataSource
{
    public Task<OrderUpdateResult> SaveAsync(OrderDetailSnapshot expected, OrderDetailSnapshot edited, string remarks, IReadOnlyCollection<OrderDetailUpdate> lines)
    {
        var employeeId = AppSession.Employee?.EmId ?? 0;
        return Task.Run(() =>
        {
            var succeeded = BusinessService.UpdateOrder(expected.OrderId, employeeId, edited.CustomerId, expected.OfficeId,
                expected.EmployeeId, edited.OrderDate, edited.DeliveryDate, expected.StateFlag, expected.ManagementFlag,
                expected.HiddenReason, remarks, lines, expected, out var message);
            return new OrderUpdateResult(succeeded, message);
        });
    }
}
