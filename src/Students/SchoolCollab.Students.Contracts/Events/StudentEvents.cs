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
    Guid? GradeStrandCodedValueId,
    DateOnly EnrolledOn,
    DateTimeOffset OccurredAt);

public record StudentTransferred(
    Guid StudentId,
    Guid PeriodId,
    Guid FromGradeLevelId,
    Guid ToGradeLevelId,
    Guid? ToGradeStrandCodedValueId,
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