namespace ParkFlow.BuildingBlocks.Application;

/// <summary>
/// Something one module publishes for other modules to react to (e.g. ReservationCreated). This is
/// the contract that crosses module boundaries — modules never call each other's Application
/// services directly, only exchange these.
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredOn { get; }
}
