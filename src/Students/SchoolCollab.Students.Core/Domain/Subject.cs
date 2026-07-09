using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.Domain;

public sealed class Subject : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private Subject() { }

    public Guid Id { get; private set; }

    // Multi-tenancy: each row belongs to a tenant (global-tenant-filter.md §3.2 Strict).
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }

    public Guid CodedValueId { get; private set; }
    // The following are kept for performance/indexing, but the source of truth
    // for metadata should be the CodedValue system + Tenant Overrides.
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

    /// <summary>
    /// Marks the subject for deletion. Call <see cref="CanDelete"/> first to
    /// verify no references exist.
    /// </summary>
    /// <exception cref="SubjectReferencedException">
    /// Thrown if grade-subject assignments or student-subject assignments reference this subject.
    /// </exception>
    public void Delete()
    {
        // Delete is a hard delete. The repository enforces referential integrity
        // by checking for GradeSubjectAssignments and StudentSubjectAssignments before
        // allowing the delete. See DeleteSubjectHandler.
        _domainEvents.Add(new SubjectDeletedEvent(Id, Code));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}