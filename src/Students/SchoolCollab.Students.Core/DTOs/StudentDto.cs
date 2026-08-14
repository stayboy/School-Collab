namespace SchoolCollab.Students.Core.DTOs;

public sealed record StudentDto(
    Guid Id,
    string StudentNumber,
    Guid? TitleCodedValueId,
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    Guid? GenderCodedValueId,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // Postgres xmin row version (IHasRowVersion). Echoed back to the client so the
    // all-inclusive edit can send it as ExpectedRowVersion for optimistic concurrency.
    uint RowVersion = 0,
    // Enriched client-side in StudentsApiClient (Age from DateOfBirth, GenderName
    // from the CodedValues module). Left null by the server projection so the
    // Students module stays decoupled from coded values. Defaults keep the 7
    // server projection sites source-compatible.
    int? Age = null,
    string? GenderName = null,
    GradeLevelDto? CurrentGrade = null);