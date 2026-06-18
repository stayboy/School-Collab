using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Core.Domain;

public sealed class GradeLevel
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private GradeLevel() { }

    public Guid Id { get; private set; }
    public Guid CodedValueId { get; private set; }
    public int Level { get; private set; }
    public string Name { get; private set; } = default!;
    public int DisplayOrder { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public static GradeLevel Create(
        Guid codedValueId,
        int level,
        string name,
        int displayOrder)
    {
        var now = DateTimeOffset.UtcNow;
        var gradeLevel = new GradeLevel
        {
            Id = Guid.NewGuid(),
            CodedValueId = codedValueId,
            Level = level,
            Name = name.Trim(),
            DisplayOrder = displayOrder,
            CreatedAt = now,
            UpdatedAt = now
        };

        gradeLevel._domainEvents.Add(new GradeLevelCreatedEvent(gradeLevel.Id, gradeLevel.Name));
        return gradeLevel;
    }

    public void Update(int level, string name, int displayOrder)
    {
        Level = level;
        Name = name.Trim();
        DisplayOrder = displayOrder;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new GradeLevelUpdatedEvent(Id, Name));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}