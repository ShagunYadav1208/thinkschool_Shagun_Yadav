namespace QuotesIntegrationApi.Services;

public interface ITokenService
{
    string CreateToken(string subject);
}
