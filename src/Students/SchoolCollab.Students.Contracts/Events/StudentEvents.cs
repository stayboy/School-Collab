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
    DateOnly EnrolledOn,
    DateTimeOffset OccurredAt);

public record StudentTransferred(
    Guid StudentId,
    Guid PeriodId,
    Guid FromGradeLevelId,
    Guid ToGradeLevelId,
    DateTimeOffset OccurredAt);

public record StudentWithdrawn(
    Guid StudentId,
    Guid PeriodId,
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