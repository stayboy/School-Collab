namespace SchoolCollab.Students.Core.DTOs;

using SchoolCollab.Students.Core.Domain;

/// <summary>
/// A lesson projected from a <see cref="TopicStrand"/> that has a parent strand
/// (a strand with a parent is a lesson — strand-lesson-unification-plan.md).
/// </summary>
public sealed record TopicLessonDto(
    Guid Id,
    Guid TopicId,
    Guid? StrandId,
    string Name,
    string? Description,
    DateOnly? StartDate,
    DateOnly? EndDate,
    bool IsOpenEnded,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static TopicLessonDto FromStrand(TopicStrand s) => new(
        s.Id,
        s.TopicId,
        s.ParentStrandId,
        s.Name,
        s.Description,
        s.StartDate,
        s.EndDate,
        s.IsOpenEnded,
        s.DisplayOrder,
        s.CreatedAt,
        s.UpdatedAt);
}
