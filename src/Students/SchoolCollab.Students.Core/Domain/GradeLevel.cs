using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.Domain;

public sealed class GradeLevel : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private GradeLevel() { }

    public Guid Id { get; private set; }

    // Multi-tenancy: each row belongs to a tenant (global-tenant-filter.md §3.2 Strict).
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }

    public Guid CodedValueId { get; private set; }
    // The following are kept for performance/indexing, but the source of truth
    // for metadata should be the CodedValue system + Tenant Overrides.
    public int Level { get; private set; }
    public string Name { get; private set; } = default!;
    public int DisplayOrder { get; private set; }

    // Enrollment validation guard clauses (§2 of plan):
    // MinAge / MaxAge define the age range for students enrolling in this grade level.
    // AllowedGenderCodedValueId restricts enrollment to a specific gender; null = co-ed (no restriction).
    public int? MinAge { get; private set; }
    public int? MaxAge { get; private set; }
    public Guid? AllowedGenderCodedValueId { get; private set; }

    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public static GradeLevel Create(
        Guid codedValueId,
        int level,
        string name,
        int displayOrder,
        int? minAge = null,
        int? maxAge = null,
        Guid? allowedGenderCodedValueId = null)
    {
        if (minAge is not null && maxAge is not null && minAge > maxAge)
            throw new GradeLevelConstraintException(
                $"MinAge ({minAge}) cannot be greater than MaxAge ({maxAge}) for grade level '{name}'.");

        var now = DateTimeOffset.UtcNow;
        var gradeLevel = new GradeLevel
        {
            Id = Guid.NewGuid(),
            CodedValueId = codedValueId,
            Level = level,
            Name = name.Trim(),
            DisplayOrder = displayOrder,
            MinAge = minAge,
            MaxAge = maxAge,
            AllowedGenderCodedValueId = allowedGenderCodedValueId,
            CreatedAt = now,
            UpdatedAt = now
        };

        gradeLevel._domainEvents.Add(new GradeLevelCreatedEvent(gradeLevel.Id, gradeLevel.Name));
        return gradeLevel;
    }

    public void Update(
        int level,
        string name,
        int displayOrder,
        int? minAge = null,
        int? maxAge = null,
        Guid? allowedGenderCodedValueId = null)
    {
        if (minAge is not null && maxAge is not null && minAge > maxAge)
            throw new GradeLevelConstraintException(
                $"MinAge ({minAge}) cannot be greater than MaxAge ({maxAge}) for grade level '{Name}'.");

        Level = level;
        Name = name.Trim();
        DisplayOrder = displayOrder;
        MinAge = minAge;
        MaxAge = maxAge;
        AllowedGenderCodedValueId = allowedGenderCodedValueId;
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
        // by checking for StudentEnrollments and GradeTopicAssignments before
        // allowing the delete. See DeleteGradeLevelHandler.
        _domainEvents.Add(new GradeLevelDeletedEvent(Id, Name));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}