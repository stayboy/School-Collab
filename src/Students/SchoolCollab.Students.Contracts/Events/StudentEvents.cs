namespace SchoolCollab.Students.Contracts.Events;

public record StudentCreated(
    Guid Id,
    string StudentNumber,
    string FirstName,
    string LastName,
    DateTimeOffset CreatedAt);

public record StudentUpdated(
    Guid Id,
    string StudentNumber,
    string FirstName,
    string LastName,
    DateTimeOffset UpdatedAt);

public record StudentDeleted(
    Guid Id,
    string StudentNumber,
    DateTimeOffset DeletedAt);

public record StudentEnrolled(
    Guid StudentId,
    Guid PeriodId,
    Guid GradeLevelId,
    Guid? StreamCodedValueId,
    DateOnly EnrolledOn,
    DateTimeOffset OccurredAt);

/// <summary>An active enrollment's grade/stream was corrected in place via
/// the Enroll-dialog upsert (same student + same active period). The
/// enrollment stays Active — this is not a transfer. Carries both the previous
/// and new grade so consumers can distinguish a no-op from a real change.</summary>
public record StudentEnrollmentUpdated(
    Guid StudentId,
    Guid PeriodId,
    Guid PreviousGradeLevelId,
    Guid NewGradeLevelId,
    Guid? NewStreamCodedValueId,
    DateTimeOffset OccurredAt);

public record StudentTransferred(
    Guid StudentId,
    Guid PeriodId,
    Guid FromGradeLevelId,
    Guid ToGradeLevelId,
    Guid? ToStreamCodedValueId,
    DateTimeOffset OccurredAt);

public record StudentWithdrawn(
    Guid StudentId,
    Guid PeriodId,
    DateTimeOffset OccurredAt);

/// <summary>A student&#39;s guardian link metadata (role / relationship /
/// emergency-contact flag) was updated in place via
/// PUT /students/{studentId}/guardians/{guardianId}. Emits a single
/// event instead of the unlink+relink double event (spec §3.2 / §5).
/// <c>Role</c> is the <c>GuardianRole</c> enum name as a string so the
/// contract stays decoupled from the Core domain assembly.
/// </summary>
public record StudentGuardianUpdated(
    Guid StudentId,
    Guid GuardianId,
    string Role,
    Guid? RelationshipCodedValueId,
    bool IsEmergencyContact,
    DateTimeOffset OccurredAt);

public record PeriodActivated(
    Guid Id,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    DateTimeOffset OccurredAt);

public record PeriodCompleted(
    Guid Id,
    string Name,
    DateTimeOffset OccurredAt);

public record StudentsPromoted(
    Guid FromPeriodId,
    Guid ToPeriodId,
    int StudentCount,
    DateTimeOffset OccurredAt);