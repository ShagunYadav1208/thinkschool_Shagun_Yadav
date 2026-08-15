using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Day3Piece1.EntraAuth;
using Microsoft.IdentityModel.Tokens;

namespace Day3Piece1.Tests;

public class EntraTokenRoutingTests
{
    [Theory]
    [InlineData("https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000/v2.0", true)]
    [InlineData("https://sts.windows.net/00000000-0000-0000-0000-000000000000/", true)]
    [InlineData("https://STS.WINDOWS.NET/00000000-0000-0000-0000-000000000000/", true)]
    [InlineData("ThinkSchool.Internal", false)]
    [InlineData(null, false)]
    public void IsEntraIssuer_ClassifiesKnownEntraIssuerPrefixes(string? issuer, bool expected)
    {
        Assert.Equal(expected, EntraTokenRouting.IsEntraIssuer(issuer));
    }

    [Theory]
    [InlineData("Bearer abc.def.ghi", "abc.def.ghi")]
    [InlineData("bearer abc.def.ghi", "abc.def.ghi")]
    [InlineData(null, null)]
    [InlineData("Basic abc.def.ghi", null)]
    [InlineData("", null)]
    public void GetBearerToken_ExtractsTokenFromAuthorizationHeader(string? header, string? expected)
    {
        Assert.Equal(expected, EntraTokenRouting.GetBearerToken(header));
    }

    [Fact]
    public void ReadIssuer_WithWellFormedToken_ReturnsTheIssuerClaim()
    {
        var token = CreateTestToken(issuer: "https://test-issuer.example.com/tenant/v2.0");

        var issuer = EntraTokenRouting.ReadIssuer(token);

        Assert.Equal("https://test-issuer.example.com/tenant/v2.0", issuer);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("only.two-parts")]
    public void ReadIssuer_WithMalformedInput_ReturnsNullInsteadOfThrowing(string? malformedToken)
    {
        var issuer = EntraTokenRouting.ReadIssuer(malformedToken);

        Assert.Null(issuer);
    }

    [Theory]
    [InlineData("api://11111111-1111-1111-1111-111111111111", "11111111-1111-1111-1111-111111111111")]
    [InlineData("22222222-2222-2222-2222-222222222222", "api://22222222-2222-2222-2222-222222222222")]
    public void GetAllowedEntraAudiences_ReturnsBothTheConfiguredAndAlternateForm(
        string configuredAudience,
        string expectedAlternate)
    {
        var audiences = EntraTokenRouting.GetAllowedEntraAudiences(configuredAudience);

        Assert.Contains(configuredAudience, audiences);
        Assert.Contains(expectedAlternate, audiences);
        Assert.Equal(2, audiences.Length);
    }

    private static string CreateTestToken(string issuer)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-only-signing-key-32-bytes-min!"));
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: "test-audience",
            claims: [new Claim(JwtRegisteredClaimNames.Sub, "test-subject")],
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
