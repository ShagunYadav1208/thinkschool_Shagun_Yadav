namespace Quotes.Domain;

public interface IRefreshReuseNotifier
{
    void ReuseDetected(Guid familyId, string userId);
}
