namespace SchoolCollab.Students.Core.DTOs;

using SchoolCollab.Students.Core.Domain;

/// <summary>
/// A strand. A strand with a parent (<see cref="ParentStrandId"/> set) is a
/// lesson (strand-lesson-unification-plan.md).
/// </summary>
public sealed record TopicStrandDto(
    Guid Id,
    Guid TopicId,
    Guid? ParentStrandId,
    string Name,
    string? Description,
    DateOnly? StartDate,
    DateOnly? EndDate,
    bool IsLesson,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static TopicStrandDto FromStrand(TopicStrand s) => new(
        s.Id,
        s.TopicId,
        s.ParentStrandId,
        s.Name,
        s.Description,
        s.StartDate,
        s.EndDate,
        s.IsLesson,
        s.DisplayOrder,
        s.CreatedAt,
        s.UpdatedAt);
}
