namespace SchoolCollab.Students.Application.Components.Students;

/// <summary>
/// One topic+role assignment for a teacher, with the assignment dates
/// (section-card-lessons-adoption.md §9 dec 7). A null <see cref="EndDate"/>
/// means open-ended (the teacher teaches the topic indefinitely).
/// </summary>
public sealed record TeacherTopicAssignment(
    Guid TopicId,
    Guid? RoleCodedValueId = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null);
