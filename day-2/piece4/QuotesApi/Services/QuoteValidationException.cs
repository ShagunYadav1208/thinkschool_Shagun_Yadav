using QuotesApi.Models;

namespace QuotesApi.Services;

public sealed class QuoteValidationException(IReadOnlyList<QuoteError> errors)
    : Exception("Quote validation failed.")
{
    public IReadOnlyList<QuoteError> Errors { get; } = errors;
}
