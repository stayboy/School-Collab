using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.CreateGradeLevel;

public sealed record CreateGradeLevel(
    Guid CodedValueId,
    int Level,
    string Name,
    int DisplayOrder) : ICommand;