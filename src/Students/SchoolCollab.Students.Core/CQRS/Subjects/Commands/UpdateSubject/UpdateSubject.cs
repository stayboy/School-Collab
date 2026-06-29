using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Commands.UpdateSubject;

public sealed record UpdateSubject(
    Guid Id,
    string Name,
    int DisplayOrder) : ICommand;