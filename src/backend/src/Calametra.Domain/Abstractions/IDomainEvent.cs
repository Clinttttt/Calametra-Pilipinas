namespace Calametra.Domain.Abstractions;

/// <summary>Marker for something that happened inside the domain.</summary>
public interface IDomainEvent
{
    Guid EventId { get; }

    DateTimeOffset OccurredAt { get; }
}
