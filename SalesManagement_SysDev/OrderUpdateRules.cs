namespace SalesManagement_SysDev;

// T_OrderDetail.OrTotalPrice is decimal(10, 0). Keep the existing update rounding rule.
public static class OrderUpdateRules
{
    public const int RemarksMaxLength = 50;
    public const decimal MaximumLineAmount = 9_999_999_999m;

    // T_OrderDetail has no deletion flag. Retain removed rows as zero-quantity, zero-amount history.
    internal static bool IsRemovedLine(TOrderDetail line) => line.OrQuantity == 0 && line.OrTotalPrice == 0m;

    public static bool TrySubtotal(int quantity, decimal price, out decimal subtotal)
    {
        subtotal = 0;
        if (quantity <= 0 || price < 0) return false;
        try
        {
            subtotal = decimal.Round(quantity * price, 0, MidpointRounding.AwayFromZero);
            return subtotal <= MaximumLineAmount;
        }
        catch (OverflowException) { return false; }
    }

    // Compare stored amounts, rather than inferred rounded unit prices, to detect stale editors.
    internal static bool Matches(OrderDetailSnapshot expected, TOrder current)
    {
        var currentLines = current.TOrderDetails.Where(line => !IsRemovedLine(line)).ToList();
        return expected.OrderId == current.OrId && expected.CustomerId == current.ClId && expected.OfficeId == current.SoId
            && expected.EmployeeId == current.EmId && expected.OrderDate.Date == current.OrDate.Date
            && expected.DeliveryDate?.Date == current.OrDeliveryDate?.Date
            && expected.StateFlag == (current.OrStateFlag ?? 0) && expected.ManagementFlag == current.OrFlag
            && expected.CustomerContact == current.ClCharge && expected.HiddenReason == current.OrHidden
            && expected.Lines.Count == currentLines.Count
            && expected.Lines.All(line => currentLines.Any(detail => detail.OrDetailId == line.DetailId
                && detail.PrId == line.ProductId && detail.OrQuantity == line.Quantity && detail.OrTotalPrice == line.Subtotal));
    }
}
