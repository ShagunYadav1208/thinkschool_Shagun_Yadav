using Microsoft.AspNetCore.Identity;
using ParkFlow.BuildingBlocks.Application;
using ParkFlow.Modules.Identity.Application.Abstractions;

namespace ParkFlow.Modules.Identity.Application.Identity;

using Domain = ParkFlow.Modules.Identity.Domain.User;

public sealed class IdentityApplicationService(
    IUserRepository repository, IUnitOfWork unitOfWork, IPasswordHasher<Domain> passwordHasher)
{
    public async Task<Result<Domain>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        if (await repository.ExistsWithEmailAsync(request.Email, cancellationToken))
        {
            return Result.Failure<Domain>("An account with that email already exists.");
        }

        var user = Domain.Register(request.Email);
        user.SetPasswordHash(passwordHasher.HashPassword(user, request.Password));

        repository.Add(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(user);
    }

    // Deliberately the same generic message whether the email is unknown or the password is wrong
    // — telling the two apart would let a caller enumerate registered emails.
    public async Task<Result<Domain>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await repository.GetByEmailAsync(request.Email, cancellationToken);
        if (user is null)
        {
            return Result.Failure<Domain>("Invalid email or password.");
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            return Result.Failure<Domain>("Invalid email or password.");
        }

        return Result.Success(user);
    }
}
