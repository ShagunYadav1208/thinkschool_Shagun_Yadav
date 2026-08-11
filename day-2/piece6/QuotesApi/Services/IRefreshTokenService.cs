using QuotesApi.Models;

namespace QuotesApi.Services;

public interface IRefreshTokenService
{
    Task<TokenResponse> IssueForLoginAsync(User user, CancellationToken cancellationToken);
    Task<RefreshResult> RefreshAsync(string rawRefreshToken, CancellationToken cancellationToken);
    Task<bool> RevokeAsync(string rawRefreshToken, CancellationToken cancellationToken);
}
