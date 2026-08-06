using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.CreateTeacher;

/// <summary>Creates a teacher (spec §4.12). Tenant is assigned in the handler.</summary>
public sealed record CreateTeacher(
    Guid? TitleCodedValueId,
    string FirstName,
    string LastName,
    string? DisplayName,
    Guid? GenderCodedValueId = null,
    DateOnly? DateOfBirth = null,
    Guid? LevelOfEducationCodedValueId = null,
    Guid[]? QualificationCodedValueIds = null) : ICommand;
