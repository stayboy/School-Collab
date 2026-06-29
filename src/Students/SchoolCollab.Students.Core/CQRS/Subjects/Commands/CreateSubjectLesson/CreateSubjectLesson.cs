using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Commands.CreateSubjectLesson;

public sealed record CreateSubjectLesson(
    Guid SubjectId,
    string Name,
    string? Description,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int DisplayOrder) : ICommand;
