using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Core.Domain;

public sealed class StudentTopicAssignment : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private StudentTopicAssignment() { }

    public Guid Id { get; private set; }

    // Multi-tenancy: inherits the student's tenant (global-tenant-filter.md §3.2 Strict).
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }

    public Guid StudentId { get; private set; }
    public Guid TopicId { get; private set; }
    public Guid PeriodId { get; private set; }

    // Creation stamp (period-hierarchy-terms-semesters.md FR-H13, Rev. 3): the
    // active sub-period (Term/Semester) of the active AcademicYear at creation,
    // or null in the gap state / a None-division year. Server-resolved by the
    // AssignStudentTopic handler — never a caller's free choice (§2.1).
    public Guid? SubPeriodId { get; private set; }

    public bool IsOverride { get; private set; }
    public SubjectAssignmentSource SourceType { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public static StudentTopicAssignment Create(
        Guid studentId,
        Guid topicId,
        Guid periodId,
        bool isOverride,
        SubjectAssignmentSource sourceType,
        Guid? subPeriodId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var assignment = new StudentTopicAssignment
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            TopicId = topicId,
            PeriodId = periodId,
            SubPeriodId = subPeriodId,
            IsOverride = isOverride,
            SourceType = sourceType,
            CreatedAt = now,
            UpdatedAt = now
        };

        assignment._domainEvents.Add(new StudentTopicAssignedEvent(assignment.Id, studentId, topicId, periodId));
        return assignment;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}