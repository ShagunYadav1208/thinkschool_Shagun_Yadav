using OrderApi.DTOs;
using OrderApi.Services;
using OrderApi.Services.Rules;

namespace OrderApi.Tests;

public class OrderValidationTests
{
    // Test: validation rejects orders with negative quantity.
    [Fact]
    public void NegativeQuantityIsRejected()
    {
        var act = () => CreateService().Validate(RequestWithQuantity(-1));
        Assert.Throws<OrderValidationException>(act);
    }

    // Test: validation rejects orders with zero quantity.
    [Fact]
    public void ZeroQuantityIsRejected()
    {
        var act = () => CreateService().Validate(RequestWithQuantity(0));
        Assert.Throws<OrderValidationException>(act);
    }

    // Test: validation rejects orders with quantity above the maximum.
    [Fact]
    public void QuantityOverOneHundredIsRejected()
    {
        var act = () => CreateService().Validate(RequestWithQuantity(101));
        Assert.Throws<OrderValidationException>(act);
    }

    [Fact]
    public void ValidQuantityIsAccepted()
    {
        var act = () => CreateService().Validate(RequestWithQuantity(2));
        act();
    }

    private static OrderService CreateService() => new(new OrderValidationPipeline(
    [
        new RequestShapeRule(),
        new ItemQuantityRule(),
        new ShippingAddressRule()
    ]));

    private static CreateOrderRequest RequestWithQuantity(int quantity) => new()
    {
        CustomerName = "Shagun Yadav",
        CustomerEmail = "shagun@example.com",
        Items = [new CreateOrderItemRequest { ProductId = 1, Quantity = quantity }]
    };
}
