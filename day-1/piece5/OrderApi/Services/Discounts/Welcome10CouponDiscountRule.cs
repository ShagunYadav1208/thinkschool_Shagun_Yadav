namespace OrderApi.Services.Discounts;

public class Welcome10CouponDiscountRule : IDiscountRule
{
    private const string CouponCode = "WELCOME10";
    private const decimal DiscountAmount = 10m;

    public decimal CalculateDiscount(DiscountContext context)
    {
        return context.CouponCode == CouponCode
            ? DiscountAmount
            : 0m;
    }
}
