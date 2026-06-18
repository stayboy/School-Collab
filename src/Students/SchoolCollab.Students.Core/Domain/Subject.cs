using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Core.Domain;

public sealed class Subject
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private Subject() { }

    public Guid Id { get; private set; }
    public Guid CodedValueId { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public int DisplayOrder { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public static Subject Create(
        Guid codedValueId,
        string code,
        string name,
        int displayOrder)
    {
        var now = DateTimeOffset.UtcNow;
        var subject = new Subject
        {
            Id = Guid.NewGuid(),
            CodedValueId = codedValueId,
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            DisplayOrder = displayOrder,
            CreatedAt = now,
            UpdatedAt = now
        };

        subject._domainEvents.Add(new SubjectCreatedEvent(subject.Id, subject.Code));
        return subject;
    }

    public void Update(string name, int displayOrder)
    {
        Name = name.Trim();
        DisplayOrder = displayOrder;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new SubjectUpdatedEvent(Id, Code));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}