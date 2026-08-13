using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace QuotesLockedApi.Tests;

public sealed class SmartBearerRoutingTests
{
    [Fact]
    public void GetBearerToken_NullHeader_ReturnsNull()
    {
        Assert.Null(SmartBearerRouting.GetBearerToken(null));
    }

    [Fact]
    public void GetBearerToken_HeaderWithoutBearerPrefix_ReturnsNull()
    {
        Assert.Null(SmartBearerRouting.GetBearerToken("Basic dXNlcjpwYXNz"));
    }

    [Fact]
    public void GetBearerToken_ValidHeader_ReturnsTrimmedToken()
    {
        Assert.Equal("abc.def.ghi", SmartBearerRouting.GetBearerToken("bearer  abc.def.ghi "));
    }

    [Fact]
    public void ReadIssuer_NullOrWhitespaceToken_ReturnsNull()
    {
        Assert.Null(SmartBearerRouting.ReadIssuer(null));
        Assert.Null(SmartBearerRouting.ReadIssuer("   "));
    }

    [Fact]
    public void ReadIssuer_MalformedToken_ReturnsNullInsteadOfThrowing()
    {
        Assert.Null(SmartBearerRouting.ReadIssuer("not-a-real-jwt"));
    }

    [Fact]
    public void ReadIssuer_ValidJwt_ReturnsIssuer()
    {
        var token = CreateUnsignedJwt(issuer: "QuotesLockedApi.Internal");

        Assert.Equal("QuotesLockedApi.Internal", SmartBearerRouting.ReadIssuer(token));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("QuotesLockedApi.Internal", false)]
    [InlineData("https://login.microsoftonline.com/some-tenant/v2.0", true)]
    [InlineData("https://sts.windows.net/some-tenant/", true)]
    public void IsEntraIssuer_ClassifiesIssuerCorrectly(string? issuer, bool expected)
    {
        Assert.Equal(expected, SmartBearerRouting.IsEntraIssuer(issuer));
    }

    [Fact]
    public void GetAllowedEntraAudiences_WithApiPrefix_ReturnsBothForms()
    {
        var audiences = SmartBearerRouting.GetAllowedEntraAudiences("api://my-client-id");

        Assert.Equal(["api://my-client-id", "my-client-id"], audiences);
    }

    [Fact]
    public void GetAllowedEntraAudiences_WithoutApiPrefix_ReturnsBothForms()
    {
        var audiences = SmartBearerRouting.GetAllowedEntraAudiences("my-client-id");

        Assert.Equal(["my-client-id", "api://my-client-id"], audiences);
    }

    [Fact]
    public void SelectScheme_NoAuthorizationHeader_FallsBackToInternalJwtScheme()
    {
        Assert.Equal(SmartBearerRouting.InternalJwtScheme, SmartBearerRouting.SelectScheme(null));
    }

    [Fact]
    public void SelectScheme_InternalIssuerToken_ReturnsInternalJwtScheme()
    {
        var token = CreateUnsignedJwt(issuer: "QuotesLockedApi.Internal");

        Assert.Equal(SmartBearerRouting.InternalJwtScheme, SmartBearerRouting.SelectScheme($"Bearer {token}"));
    }

    [Fact]
    public void SelectScheme_EntraIssuerToken_ReturnsEntraJwtScheme()
    {
        var token = CreateUnsignedJwt(issuer: "https://login.microsoftonline.com/some-tenant/v2.0");

        Assert.Equal(SmartBearerRouting.EntraJwtScheme, SmartBearerRouting.SelectScheme($"Bearer {token}"));
    }

    // Scheme selection only needs to read the issuer claim, so an unsigned token is enough here —
    // signature validation happens later, inside whichever scheme this routes to.
    private static string CreateUnsignedJwt(string issuer)
    {
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: "any-audience",
            claims: [],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("unit-test-only-32-byte-minimum-key!")),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
