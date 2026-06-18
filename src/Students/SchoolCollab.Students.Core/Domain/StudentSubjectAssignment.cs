using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Core.Domain;

public sealed class StudentSubjectAssignment
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private StudentSubjectAssignment() { }

    public Guid Id { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid SubjectId { get; private set; }
    public Guid PeriodId { get; private set; }
    public bool IsOverride { get; private set; }
    public SubjectAssignmentSource SourceType { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public static StudentSubjectAssignment Create(
        Guid studentId,
        Guid subjectId,
        Guid periodId,
        bool isOverride,
        SubjectAssignmentSource sourceType)
    {
        var now = DateTimeOffset.UtcNow;
        var assignment = new StudentSubjectAssignment
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            SubjectId = subjectId,
            PeriodId = periodId,
            IsOverride = isOverride,
            SourceType = sourceType,
            CreatedAt = now,
            UpdatedAt = now
        };

        assignment._domainEvents.Add(new StudentSubjectAssignedEvent(assignment.Id, studentId, subjectId, periodId));
        return assignment;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}