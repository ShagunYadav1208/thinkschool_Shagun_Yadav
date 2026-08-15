namespace OrderApi.Services.Discounts;

public class BulkOrderDiscountRule : IDiscountRule
{
    private const decimal HighTierThreshold = 1000m;
    private const decimal HighTierRate = 0.10m;
    private const decimal LowTierThreshold = 500m;
    private const decimal LowTierRate = 0.05m;

    public decimal CalculateDiscount(DiscountContext context)
    {
        return context.Subtotal switch
        {
            > HighTierThreshold => context.Subtotal * HighTierRate,
            > LowTierThreshold => context.Subtotal * LowTierRate,
            _ => 0m
        };
    }
}
