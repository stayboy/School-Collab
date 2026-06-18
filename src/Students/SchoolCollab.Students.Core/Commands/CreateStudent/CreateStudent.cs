using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.CreateStudent;

public sealed record CreateStudent(
    string StudentNumber,
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    Guid? GenderCodedValueId,
    string ContactEmail,
    string? ContactPhone) : ICommand;