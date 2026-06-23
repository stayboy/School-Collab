using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.CreateSubjectStrand;

public sealed record CreateSubjectStrand(
    Guid SubjectId,
    string Name,
    string? Description,
    int DisplayOrder) : ICommand;
