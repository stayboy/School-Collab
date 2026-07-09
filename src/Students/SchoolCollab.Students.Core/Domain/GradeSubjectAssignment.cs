using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Core.Domain;

public sealed class GradeSubjectAssignment : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private GradeSubjectAssignment() { }

    public Guid Id { get; private set; }

    // Multi-tenancy: tenant-owned (global-tenant-filter.md §3.2 Strict).
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }

    public Guid GradeLevelId { get; private set; }
    public Guid SubjectId { get; private set; }
    public Guid PeriodId { get; private set; }
    public Guid? SubjectStrandId { get; private set; }
    public Guid? SubjectLessonId { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public static GradeSubjectAssignment Create(
        Guid gradeLevelId,
        Guid subjectId,
        Guid periodId,
        Guid? subjectStrandId = null,
        Guid? subjectLessonId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var assignment = new GradeSubjectAssignment
        {
            Id = Guid.NewGuid(),
            GradeLevelId = gradeLevelId,
            SubjectId = subjectId,
            PeriodId = periodId,
            SubjectStrandId = subjectStrandId,
            SubjectLessonId = subjectLessonId,
            CreatedAt = now,
            UpdatedAt = now
        };

        assignment._domainEvents.Add(new GradeSubjectAssignedEvent(assignment.Id, gradeLevelId, subjectId, periodId));
        return assignment;
    }

    public void UpdateTags(Guid? strandId, Guid? lessonId)
    {
        if (SubjectStrandId == strandId && SubjectLessonId == lessonId) return;
        
        SubjectStrandId = strandId;
        SubjectLessonId = lessonId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}