namespace Quotes.Domain;

public sealed record ValidationError(string Field, string Message);

public sealed class ValidationResult
{
    private readonly List<ValidationError> _errors = [];

    public bool IsValid => _errors.Count == 0;
    public IReadOnlyCollection<ValidationError> Errors => _errors.AsReadOnly();

    public void Add(string field, string message) =>
        _errors.Add(new ValidationError(field, message));
}
