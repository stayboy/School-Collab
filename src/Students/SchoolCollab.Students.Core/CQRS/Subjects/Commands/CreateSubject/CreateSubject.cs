using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Commands.CreateSubject;

public sealed record CreateSubject(
    Guid CodedValueId,
    string Code,
    string Name,
    int DisplayOrder) : ICommand;