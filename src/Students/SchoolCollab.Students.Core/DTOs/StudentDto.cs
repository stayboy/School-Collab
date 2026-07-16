namespace SchoolCollab.Students.Core.DTOs;

public sealed record StudentDto(
    Guid Id,
    string StudentNumber,
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    Guid? GenderCodedValueId,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // Enriched client-side in StudentsApiClient (Age from DateOfBirth, GenderName
    // from the CodedValues module). Left null by the server projection so the
    // Students module stays decoupled from coded values. Defaults keep the 7
    // server projection sites source-compatible.
    int? Age = null,
    string? GenderName = null);