using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.CreateSubject;

public sealed record CreateSubject(
    Guid CodedValueId,
    string Code,
    string Name,
    int DisplayOrder) : ICommand;