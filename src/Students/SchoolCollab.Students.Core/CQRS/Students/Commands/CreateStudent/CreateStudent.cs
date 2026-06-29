using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Students.Commands.CreateStudent;

public sealed record CreateStudent(
    string StudentNumber,
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    Guid? GenderCodedValueId,
    string ContactEmail,
    string? ContactPhone) : ICommand;