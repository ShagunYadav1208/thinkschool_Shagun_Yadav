# Refresh-token rotation and reuse detection

`POST /api/auth/refresh` delegates to `RefreshTokenService.RefreshAsync`. It hashes the supplied refresh token before lookup, rejects missing, expired, or revoked tokens, and replaces a valid token with a new token in the same `FamilyId`.

When a previously replaced token is used again, the service logs a warning and revokes every still-active token in that family. The focused test, `RefreshingAReplacedToken_RevokesEveryActiveTokenInItsFamily`, proves this sequence:

1. Login creates the first refresh token.
2. Refresh rotates it, revoking the old token and creating its replacement.
3. Reusing the old token sets `ReuseDetected`.
4. Every token in the family is revoked, including the replacement.
5. Attempting to refresh with the replacement fails and requires a new login.
