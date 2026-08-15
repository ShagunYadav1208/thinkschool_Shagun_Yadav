using OrderApi.Services.Discounts;

namespace OrderApi.Tests.Unit;

public class DiscountRuleTests
{
    [Theory]
    [InlineData(400, 0)]
    [InlineData(500, 0)]
    [InlineData(750, 37.5)]
    [InlineData(1000, 50)]
    [InlineData(1500, 150)]
    public void BulkOrderDiscountRule_AppliesCorrectTier(
        decimal subtotal,
        decimal expectedDiscount)
    {
        var rule = new BulkOrderDiscountRule();

        var discount = rule.CalculateDiscount(
            new DiscountContext(subtotal, CouponCode: null));

        Assert.Equal(expectedDiscount, discount);
    }

    [Fact]
    public void Welcome10CouponDiscountRule_WithMatchingCoupon_ReturnsFlatDiscount()
    {
        var rule = new Welcome10CouponDiscountRule();

        var discount = rule.CalculateDiscount(
            new DiscountContext(Subtotal: 200m, CouponCode: "WELCOME10"));

        Assert.Equal(10m, discount);
    }

    [Fact]
    public void Welcome10CouponDiscountRule_WithoutMatchingCoupon_ReturnsZero()
    {
        var rule = new Welcome10CouponDiscountRule();

        var discount = rule.CalculateDiscount(
            new DiscountContext(Subtotal: 200m, CouponCode: "SAVE20"));

        Assert.Equal(0m, discount);
    }
}
