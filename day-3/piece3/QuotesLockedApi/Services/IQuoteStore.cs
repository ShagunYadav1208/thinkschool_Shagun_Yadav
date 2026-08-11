namespace QuotesLockedApi;

public interface IQuoteStore
{
    IReadOnlyCollection<Quote> All();
    Quote Create(string text, string ownerId);
    Quote? Update(int id, string text);
    bool Delete(int id);
    bool IsOwner(int id, string userId);
}
