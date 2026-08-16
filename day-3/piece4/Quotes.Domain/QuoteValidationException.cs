namespace Quotes.Domain;

public sealed class QuoteValidationException(IReadOnlyList<QuoteError> errors)
    : Exception("Quote validation failed.")
{
    public IReadOnlyList<QuoteError> Errors { get; } = errors;
}
