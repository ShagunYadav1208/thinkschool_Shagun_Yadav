using System.IdentityModel.Tokens.Jwt;

namespace QuotesLockedApi;

/// <summary>
/// Issuer-based scheme selection for the "SmartBearer" policy scheme, pulled out of
/// <c>Program.cs</c> so it can be unit tested directly instead of only through a full
/// <c>WebApplicationFactory</c> HTTP round trip (which, for the Entra path, would otherwise
/// require a live tenant).
/// </summary>
public static class SmartBearerRouting
{
    public const string InternalJwtScheme = "InternalJwt";
    public const string EntraJwtScheme = "EntraJwt";

    public static string SelectScheme(string? authorizationHeader)
    {
        var token = GetBearerToken(authorizationHeader);
        var issuer = ReadIssuer(token);
        return IsEntraIssuer(issuer) ? EntraJwtScheme : InternalJwtScheme;
    }

    public static string? GetBearerToken(string? authorizationHeader)
    {
        const string prefix = "Bearer ";
        return authorizationHeader?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true
            ? authorizationHeader[prefix.Length..].Trim()
            : null;
    }

    public static string? ReadIssuer(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            return new JwtSecurityTokenHandler().ReadJwtToken(token).Issuer;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsEntraIssuer(string? issuer) =>
        issuer?.StartsWith("https://login.microsoftonline.com/", StringComparison.OrdinalIgnoreCase) == true
        || issuer?.StartsWith("https://sts.windows.net/", StringComparison.OrdinalIgnoreCase) == true;

    public static string[] GetAllowedEntraAudiences(string configuredAudience)
    {
        var alternateAudience = configuredAudience.StartsWith("api://", StringComparison.OrdinalIgnoreCase)
            ? configuredAudience["api://".Length..]
            : $"api://{configuredAudience}";

        return [configuredAudience, alternateAudience];
    }
}
