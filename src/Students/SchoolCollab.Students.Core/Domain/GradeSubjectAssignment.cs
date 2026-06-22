using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Core.Domain;

public sealed class GradeSubjectAssignment : IEntity, IAuditableEntity, IHasRowVersion
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private GradeSubjectAssignment() { }

    public Guid Id { get; private set; }
    public Guid GradeLevelId { get; private set; }
    public Guid SubjectId { get; private set; }
    public Guid PeriodId { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public static GradeSubjectAssignment Create(
        Guid gradeLevelId,
        Guid subjectId,
        Guid periodId)
    {
        var now = DateTimeOffset.UtcNow;
        var assignment = new GradeSubjectAssignment
        {
            Id = Guid.NewGuid(),
            GradeLevelId = gradeLevelId,
            SubjectId = subjectId,
            PeriodId = periodId,
            CreatedAt = now,
            UpdatedAt = now
        };

        assignment._domainEvents.Add(new GradeSubjectAssignedEvent(assignment.Id, gradeLevelId, subjectId, periodId));
        return assignment;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}