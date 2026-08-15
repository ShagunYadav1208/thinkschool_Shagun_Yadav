namespace OrderApi.Services.Discounts;

public interface IDiscountRule
{
    decimal CalculateDiscount(DiscountContext context);
}
