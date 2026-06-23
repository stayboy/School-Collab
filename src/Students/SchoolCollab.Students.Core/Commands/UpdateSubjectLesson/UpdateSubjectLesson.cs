using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.UpdateSubjectLesson;

public sealed record UpdateSubjectLesson(
    Guid Id,
    string Name,
    string? Description,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int DisplayOrder) : ICommand;
