using OrderApi.DTOs;

namespace OrderApi.Services;

public sealed class OrderValidationPipeline(IEnumerable<IOrderValidationRule> rules)
{
    private readonly IReadOnlyList<IOrderValidationRule> rules = rules.ToList();

    public void Validate(CreateOrderRequest request)
    {
        foreach (var rule in rules)
            rule.Validate(request);
    }
}
