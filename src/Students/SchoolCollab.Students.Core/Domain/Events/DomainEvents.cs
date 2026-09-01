namespace SchoolCollab.Students.Core.Domain.Events;

public sealed record StudentCreatedEvent(Guid StudentId, string StudentNumber) : IDomainEvent;
public sealed record StudentUpdatedEvent(Guid StudentId, string StudentNumber) : IDomainEvent;
public sealed record StudentDeletedEvent(Guid StudentId, string StudentNumber) : IDomainEvent;

public sealed record GradeLevelCreatedEvent(Guid GradeLevelId, string Name) : IDomainEvent;
public sealed record GradeLevelUpdatedEvent(Guid GradeLevelId, string Name) : IDomainEvent;

public sealed record TopicCreatedEvent(Guid TopicId, string? Code) : IDomainEvent;
public sealed record TopicUpdatedEvent(Guid TopicId, string? Code) : IDomainEvent;

public sealed record PeriodCreatedEvent(Guid PeriodId, string Name) : IDomainEvent;
public sealed record PeriodUpdatedEvent(Guid PeriodId, string Name) : IDomainEvent;
public sealed record PeriodActivatedEvent(Guid PeriodId, string Name) : IDomainEvent;
public sealed record PeriodCompletedEvent(Guid PeriodId, string Name) : IDomainEvent;
public sealed record PeriodDeletedEvent(Guid PeriodId, string Name) : IDomainEvent;
public sealed record PeriodDeactivatedEvent(Guid PeriodId, string Name) : IDomainEvent;

public sealed record StudentEnrolledEvent(Guid EnrollmentId, Guid StudentId, Guid PeriodId, Guid GradeLevelId, Guid? StreamCodedValueId) : IDomainEvent;
public sealed record StudentTransferredEvent(Guid EnrollmentId, Guid StudentId, Guid PeriodId, Guid NewGradeLevelId, Guid? NewStreamCodedValueId) : IDomainEvent;

/// <summary>Raised when an ACTIVE enrollment's grade/stream is corrected in
/// place via the Enroll-dialog upsert (same student + same period). Unlike
/// <see cref="StudentTransferredEvent"/> the enrollment stays Active with no
/// ExitDate — this is a correction of the existing row, not a transfer.</summary>
public sealed record StudentEnrollmentUpdatedEvent(
    Guid EnrollmentId,
    Guid StudentId,
    Guid PeriodId,
    Guid PreviousGradeLevelId,
    Guid NewGradeLevelId,
    Guid? NewStreamCodedValueId) : IDomainEvent;
public sealed record StudentWithdrawnEvent(Guid EnrollmentId, Guid StudentId, Guid PeriodId) : IDomainEvent;

// --- Student ↔ Guardian links ---
/// <summary>Raised when a student&#39;s guardian link metadata is updated in
/// place (role / relationship / emergency-contact flag) via
/// <c>UpdateGuardianLink</c>. Emits a single event so the audit trail
/// records one mutation instead of the unlink+relink double event
/// (spec §3.2 / §5).</summary>
public sealed record StudentGuardianUpdatedEvent(
    Guid LinkId,
    Guid StudentId,
    Guid GuardianId,
    GuardianRole Role,
    Guid? RelationshipCodedValueId,
    bool IsEmergencyContact) : IDomainEvent;

public sealed record GradeTopicAssignedEvent(Guid AssignmentId, Guid GradeLevelId, Guid TopicId, DateOnly StartDate, DateOnly? EndDate) : IDomainEvent;
public sealed record ActivityGroupTopicAssignedEvent(Guid AssignmentId, Guid ActivityGroupId, Guid TopicId, DateOnly StartDate, DateOnly? EndDate) : IDomainEvent;

public sealed record StudentTopicAssignedEvent(Guid AssignmentId, Guid StudentId, Guid TopicId, Guid PeriodId) : IDomainEvent;

// --- Topic Strands (a strand with a parent is a lesson) ---
public sealed record TopicStrandCreatedEvent(Guid StrandId, string Name, Guid TopicId) : IDomainEvent;
public sealed record TopicStrandUpdatedEvent(Guid StrandId, string Name) : IDomainEvent;
