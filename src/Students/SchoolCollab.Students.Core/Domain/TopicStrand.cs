using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Core.Domain;

public sealed class TopicStrand : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private TopicStrand() { }

    public Guid Id { get; private set; }

    // Multi-tenancy: inherits the topic's tenant (global-tenant-filter.md §3.2 Strict).
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }

    public Guid TopicId { get; private set; }
    public Topic Topic { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public int DisplayOrder { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public static TopicStrand Create(
        Guid topicId,
        string name,
        string? description,
        int displayOrder)
    {
        var now = DateTimeOffset.UtcNow;
        var strand = new TopicStrand
        {
            Id = Guid.NewGuid(),
            TopicId = topicId,
            Name = name.Trim(),
            Description = description?.Trim(),
            DisplayOrder = displayOrder,
            CreatedAt = now,
            UpdatedAt = now
        };

        strand._domainEvents.Add(new TopicStrandCreatedEvent(strand.Id, strand.Name, topicId));
        return strand;
    }

    public void Update(string name, string? description, int displayOrder)
    {
        Name = name.Trim();
        Description = description?.Trim();
        DisplayOrder = displayOrder;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new TopicStrandUpdatedEvent(Id, Name));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}