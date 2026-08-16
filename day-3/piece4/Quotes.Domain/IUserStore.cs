namespace Quotes.Domain;

public interface IUserStore
{
    User? FindById(string id);
}
