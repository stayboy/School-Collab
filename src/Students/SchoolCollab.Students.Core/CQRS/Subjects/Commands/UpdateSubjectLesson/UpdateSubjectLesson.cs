using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Commands.UpdateSubjectLesson;

public sealed record UpdateSubjectLesson(
    Guid Id,
    string Name,
    string? Description,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int DisplayOrder) : ICommand;
