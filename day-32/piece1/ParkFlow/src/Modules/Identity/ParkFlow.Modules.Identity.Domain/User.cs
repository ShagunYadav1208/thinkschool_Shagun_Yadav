using ParkFlow.BuildingBlocks.Domain;

namespace ParkFlow.Modules.Identity.Domain;

/// <summary>
/// A registered account. <see cref="Id"/> is the same value every other module knows as
/// "UserId" (Reservation.UserId, Vehicle.OwnerUserId, the JWT's "sub" claim) — this is the one
/// place that Guid now actually corresponds to a stored account with real credentials.
/// </summary>
public sealed class User : AggregateRoot<Guid>
{
    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    private User()
    {
        // EF Core materialization.
    }

    private User(Guid id, string email, DateTimeOffset createdAt) : base(id)
    {
        Email = email;
        NormalizedEmail = email.Trim().ToUpperInvariant();
        CreatedAt = createdAt;
    }

    public static User Register(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("An account must have an email address.", nameof(email));
        }

        return new User(Guid.NewGuid(), email.Trim(), DateTimeOffset.UtcNow);
    }

    // Set once, right after Register — split out so the application layer can hash the password
    // using this very instance (PasswordHasher<TUser> takes the user as a hashing parameter).
    public void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("A password hash is required.", nameof(passwordHash));
        }

        PasswordHash = passwordHash;
    }
}
