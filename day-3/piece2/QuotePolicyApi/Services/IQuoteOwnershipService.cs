namespace QuotePolicyApi;

public interface IQuoteOwnershipService
{
    bool IsOwner(int quoteId, string userId);
}
