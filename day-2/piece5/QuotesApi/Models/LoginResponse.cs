namespace QuotesApi.Models;

public sealed record LoginResponse(string access_token, string refresh_token, int expires_in);
