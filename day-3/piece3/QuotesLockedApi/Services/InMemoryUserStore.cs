namespace QuotesLockedApi;

public sealed class InMemoryUserStore : IUserStore
{
    private readonly User[] _users =
    [
        new("user-123", "writer@quotes.local", "WriterPassword123!", ["quotes.read", "quotes.write"]),
        new("user-456", "reader@quotes.local", "ReaderPassword123!", ["quotes.read"])
    ];

    public User? FindByEmail(string email) =>
        _users.SingleOrDefault(user => string.Equals(user.Email, email.Trim(), StringComparison.OrdinalIgnoreCase));

    public User? FindById(string id) =>
        _users.SingleOrDefault(user => string.Equals(user.Id, id, StringComparison.Ordinal));
}
