namespace ParkFlow.Modules.Identity.Application.Abstractions;

using Domain = ParkFlow.Modules.Identity.Domain.User;

public interface IUserRepository
{
    Task<Domain?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> ExistsWithEmailAsync(string email, CancellationToken cancellationToken = default);

    void Add(Domain user);
}
