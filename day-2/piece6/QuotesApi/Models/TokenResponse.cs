namespace QuotesApi.Models;

public sealed record TokenResponse(string access_token, string refresh_token, int expires_in);
