namespace SalesManagement_SysDev;

internal static class OrderTax
{
    public static decimal Calculate(decimal subtotal) => Math.Round(subtotal * 0.1m, 0, MidpointRounding.AwayFromZero);
}
