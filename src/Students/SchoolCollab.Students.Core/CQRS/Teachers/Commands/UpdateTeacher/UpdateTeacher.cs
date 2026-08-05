using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.UpdateTeacher;

public sealed record UpdateTeacher(
    Guid Id,
    string FirstName,
    string LastName,
    string? DisplayName,
    string Email,
    string? ContactPhone,
    Guid? GenderCodedValueId = null,
    DateOnly? DateOfBirth = null,
    Guid? LevelOfEducationCodedValueId = null,
    Guid[]? QualificationCodedValueIds = null) : ICommand;
