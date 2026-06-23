using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.UpdateSubjectStrand;

public sealed record UpdateSubjectStrand(
    Guid Id,
    string Name,
    string? Description,
    int DisplayOrder) : ICommand;
