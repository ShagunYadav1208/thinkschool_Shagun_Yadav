using System.IdentityModel.Tokens.Jwt;

namespace Day3Piece1.EntraAuth;

public static class EntraTokenRouting
{
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
