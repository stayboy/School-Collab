using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.Domain;

public sealed class GradeLevel : IEntity, IAuditableEntity, IHasRowVersion
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private GradeLevel() { }

    public Guid Id { get; private set; }
    public Guid CodedValueId { get; private set; }
    // The following are kept for performance/indexing, but the source of truth
    // for metadata should be the CodedValue system + Tenant Overrides.
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

    /// <summary>
    /// Marks the grade level for deletion. Call <see cref="CanDelete"/> first to
    /// verify no references exist.
    /// </summary>
    /// <exception cref="GradeLevelReferencedException">
    /// Thrown if students or subjects are assigned to this grade level.
    /// </exception>
    public void Delete()
    {
        // Delete is a hard delete. The repository enforces referential integrity
        // by checking for StudentEnrollments and GradeSubjectAssignments before
        // allowing the delete. See DeleteGradeLevelHandler.
        _domainEvents.Add(new GradeLevelDeletedEvent(Id, Name));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}