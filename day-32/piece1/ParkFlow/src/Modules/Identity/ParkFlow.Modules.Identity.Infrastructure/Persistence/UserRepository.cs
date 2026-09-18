using Microsoft.EntityFrameworkCore;
using ParkFlow.Modules.Identity.Application.Abstractions;

namespace ParkFlow.Modules.Identity.Infrastructure.Persistence;

using Domain = ParkFlow.Modules.Identity.Domain.User;

public sealed class UserRepository(IdentityDbContext dbContext) : IUserRepository
{
    public Task<Domain?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        dbContext.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == Normalize(email), cancellationToken);

    public Task<bool> ExistsWithEmailAsync(string email, CancellationToken cancellationToken = default) =>
        dbContext.Users.AnyAsync(u => u.NormalizedEmail == Normalize(email), cancellationToken);

    public void Add(Domain user) => dbContext.Users.Add(user);

    private static string Normalize(string email) => email.Trim().ToUpperInvariant();
}
