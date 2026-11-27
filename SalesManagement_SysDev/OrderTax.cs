namespace SalesManagement_SysDev;

internal static class OrderTax
{
    public const decimal Rate = 0.1m;

    public static decimal Calculate(decimal subtotal) => Math.Round(subtotal * Rate, 0, MidpointRounding.AwayFromZero);
}
