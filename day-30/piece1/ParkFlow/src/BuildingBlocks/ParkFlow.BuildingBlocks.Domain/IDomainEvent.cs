namespace ParkFlow.BuildingBlocks.Domain;

/// <summary>
/// Something that happened inside an aggregate. Raised in-process only — a module's Infrastructure
/// layer is what decides whether (and how) a domain event becomes an integration event on the
/// outbox for other modules to react to.
/// </summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredOn { get; }
}
