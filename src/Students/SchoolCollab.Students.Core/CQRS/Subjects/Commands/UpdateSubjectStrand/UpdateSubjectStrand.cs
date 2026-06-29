using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Commands.UpdateSubjectStrand;

public sealed record UpdateSubjectStrand(
    Guid Id,
    string Name,
    string? Description,
    int DisplayOrder) : ICommand;
