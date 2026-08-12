namespace Quotes.Domain;

public sealed class CreateQuoteRequestValidator
{
    public const int MaximumAuthorLength = 80;
    public const int MinimumTextLength = 10;
    public const int MaximumTextLength = 280;
    public const int MaximumTags = 5;

    public ValidationResult Validate(CreateQuoteRequest request)
    {
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(request.Author))
        {
            result.Add(nameof(request.Author), "Author is required.");
        }
        else if (request.Author.Trim().Length > MaximumAuthorLength)
        {
            result.Add(nameof(request.Author), "Author cannot exceed 80 characters.");
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            result.Add(nameof(request.Text), "Text is required.");
        }
        else
        {
            var text = request.Text.Trim();
            if (text.Length < MinimumTextLength)
            {
                result.Add(nameof(request.Text), "Text must be at least 10 characters.");
            }
            else if (text.Length > MaximumTextLength)
            {
                result.Add(nameof(request.Text), "Text cannot exceed 280 characters.");
            }
        }

        if (request.Tags is { Length: > MaximumTags })
        {
            result.Add(nameof(request.Tags), "A quote cannot have more than 5 tags.");
        }

        if (request.Tags?.Any(string.IsNullOrWhiteSpace) == true)
        {
            result.Add(nameof(request.Tags), "Tags cannot be blank.");
        }

        return result;
    }
}
