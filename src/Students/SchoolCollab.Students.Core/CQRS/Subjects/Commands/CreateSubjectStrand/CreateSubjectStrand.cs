using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Commands.CreateSubjectStrand;

public sealed record CreateSubjectStrand(
    Guid SubjectId,
    string Name,
    string? Description,
    int DisplayOrder) : ICommand;
