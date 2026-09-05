namespace Calametra.Domain.Abstractions;

/// <summary>Base class for entities identified by a <see cref="Guid"/>.</summary>
public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected Entity(Guid id) => Id = id;

    /// <summary>Required by EF Core materialisation. Not for application use.</summary>
    protected Entity()
    {
    }

    public Guid Id { get; protected init; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>An entity that records when it was created and last changed.</summary>
public abstract class AuditableEntity : Entity
{
    protected AuditableEntity(Guid id, DateTimeOffset createdAt)
        : base(id)
    {
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    protected AuditableEntity()
    {
    }

    public DateTimeOffset CreatedAt { get; protected set; }

    public DateTimeOffset UpdatedAt { get; protected set; }

    /// <summary>Time is always supplied by the caller. Entities never read the clock.</summary>
    protected void Touch(DateTimeOffset now) => UpdatedAt = now;
}
