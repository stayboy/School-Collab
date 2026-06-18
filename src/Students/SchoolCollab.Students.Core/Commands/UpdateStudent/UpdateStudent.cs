using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.UpdateStudent;

public sealed record UpdateStudent(
    Guid Id,
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    Guid? GenderCodedValueId,
    string ContactEmail,
    string? ContactPhone) : ICommand;