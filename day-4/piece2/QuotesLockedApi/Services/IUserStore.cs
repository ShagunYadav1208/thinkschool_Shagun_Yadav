namespace QuotesLockedApi;

public interface IUserStore
{
    User? FindByEmail(string email);
    User? FindById(string id);
}
