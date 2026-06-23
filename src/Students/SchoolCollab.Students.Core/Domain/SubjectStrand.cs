using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Core.Domain;

public sealed class SubjectStrand : IEntity, IAuditableEntity, IHasRowVersion
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private SubjectStrand() { }

    public Guid Id { get; private set; }
    public Guid SubjectId { get; private set; }
    public Subject Subject { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public int DisplayOrder { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public static SubjectStrand Create(
        Guid subjectId,
        string name,
        string? description,
        int displayOrder)
    {
        var now = DateTimeOffset.UtcNow;
        var strand = new SubjectStrand
        {
            Id = Guid.NewGuid(),
            SubjectId = subjectId,
            Name = name.Trim(),
            Description = description?.Trim(),
            DisplayOrder = displayOrder,
            CreatedAt = now,
            UpdatedAt = now
        };

        strand._domainEvents.Add(new SubjectStrandCreatedEvent(strand.Id, strand.Name, subjectId));
        return strand;
    }

    public void Update(string name, string? description, int displayOrder)
    {
        Name = name.Trim();
        Description = description?.Trim();
        DisplayOrder = displayOrder;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new SubjectStrandUpdatedEvent(Id, Name));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}