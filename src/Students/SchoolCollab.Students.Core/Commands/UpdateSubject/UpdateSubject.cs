using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.UpdateSubject;

public sealed record UpdateSubject(
    Guid Id,
    string Name,
    int DisplayOrder) : ICommand;