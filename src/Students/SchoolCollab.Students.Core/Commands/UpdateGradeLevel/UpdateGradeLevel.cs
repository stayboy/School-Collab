using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.UpdateGradeLevel;

public sealed record UpdateGradeLevel(
    Guid Id,
    int Level,
    string Name,
    int DisplayOrder) : ICommand;