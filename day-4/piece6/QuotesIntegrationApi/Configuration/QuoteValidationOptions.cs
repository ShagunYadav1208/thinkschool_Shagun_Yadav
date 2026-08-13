namespace QuotesIntegrationApi.Configuration;

/// <summary>
/// Bound from the "QuoteValidation" section. Unlike <see cref="JwtOptions"/>, there's nothing
/// wrong with these values changing at runtime (raising the max text length doesn't need a
/// restart), which is exactly the case <see cref="Microsoft.Extensions.Options.IOptionsSnapshot{T}"/>
/// is for — see its use in the create-quote handler in Program.cs.
/// </summary>
public sealed record QuoteValidationOptions
{
    public int MaxAuthorLength { get; init; } = 100;

    public int MaxTextLength { get; init; } = 1000;
}
