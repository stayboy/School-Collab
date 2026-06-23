namespace SchoolCollab.Students.Core.Domain.Events;

public sealed record StudentCreatedEvent(Guid StudentId, string StudentNumber) : IDomainEvent;
public sealed record StudentUpdatedEvent(Guid StudentId, string StudentNumber) : IDomainEvent;
public sealed record StudentDeletedEvent(Guid StudentId, string StudentNumber) : IDomainEvent;

public sealed record GradeLevelCreatedEvent(Guid GradeLevelId, string Name) : IDomainEvent;
public sealed record GradeLevelUpdatedEvent(Guid GradeLevelId, string Name) : IDomainEvent;

public sealed record SubjectCreatedEvent(Guid SubjectId, string Code) : IDomainEvent;
public sealed record SubjectUpdatedEvent(Guid SubjectId, string Code) : IDomainEvent;

public sealed record PeriodCreatedEvent(Guid PeriodId, string Name) : IDomainEvent;
public sealed record PeriodUpdatedEvent(Guid PeriodId, string Name) : IDomainEvent;
public sealed record PeriodActivatedEvent(Guid PeriodId, string Name) : IDomainEvent;
public sealed record PeriodCompletedEvent(Guid PeriodId, string Name) : IDomainEvent;

public sealed record StudentEnrolledEvent(Guid EnrollmentId, Guid StudentId, Guid PeriodId, Guid GradeLevelId) : IDomainEvent;
public sealed record StudentTransferredEvent(Guid EnrollmentId, Guid StudentId, Guid PeriodId, Guid NewGradeLevelId) : IDomainEvent;
public sealed record StudentWithdrawnEvent(Guid EnrollmentId, Guid StudentId, Guid PeriodId) : IDomainEvent;

public sealed record GradeSubjectAssignedEvent(Guid AssignmentId, Guid GradeLevelId, Guid SubjectId, Guid PeriodId) : IDomainEvent;

public sealed record StudentSubjectAssignedEvent(Guid AssignmentId, Guid StudentId, Guid SubjectId, Guid PeriodId) : IDomainEvent;

public sealed record StudentsPromotedEvent(Guid FromPeriodId, Guid ToPeriodId, int StudentCount) : IDomainEvent;

// --- Subject Strands ---
public sealed record SubjectStrandCreatedEvent(Guid StrandId, string Name, Guid SubjectId) : IDomainEvent;
public sealed record SubjectStrandUpdatedEvent(Guid StrandId, string Name) : IDomainEvent;

// --- Subject Lessons ---
public sealed record SubjectLessonCreatedEvent(Guid LessonId, string Name, Guid SubjectId) : IDomainEvent;
public sealed record SubjectLessonUpdatedEvent(Guid LessonId, string Name) : IDomainEvent;
public sealed record SubjectLessonStrandAssignedEvent(Guid LessonId, Guid StrandId) : IDomainEvent;
